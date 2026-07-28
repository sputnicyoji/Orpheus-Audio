using System;
using Orpheus.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio
{
    public sealed partial class OrpheusAudioManager
    {
        private OrpheusPersistentDirectorExecutionState _bgmDirector;

        private void InitializeBgmDirector()
        {
            _bgmDirector.Initialize();
        }

        private void ReconcileCommittedBgmIntent(OrpheusAudioKey desiredKey)
        {
            var view = new OrpheusPersistentDirectorView(
                _bgmDirector.Phase,
                GetBgmSourceKey(_bgmDirector.CurrentSourceIndex),
                _bgmDirector.LoadingKey,
                GetBgmSourceKey(_bgmDirector.TargetSourceIndex),
                _bgmDirector.QueuedKey,
                _bgmDirector.SourceZeroKey,
                _bgmDirector.SourceZeroGain,
                _bgmDirector.SourceOneKey,
                _bgmDirector.SourceOneGain,
                _bgmDirector.CrossfadeProgress);
            var decision = OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(view, desiredKey);
            if (decision.Action == OrpheusPersistentDirectorAction.None)
            {
                return;
            }

            AdvanceBgmGeneration();
            switch (decision.Action)
            {
                case OrpheusPersistentDirectorAction.BeginLoad:
                    BeginBgmLoading(desiredKey, false);
                    break;
                case OrpheusPersistentDirectorAction.CancelPendingKeepCurrent:
                    CancelBgmLoading();
                    _bgmDirector.QueuedKey = OrpheusAudioKey.Invalid;
                    _bgmDirector.Phase = HasBgmSources
                        ? OrpheusPersistentDirectorPhase.Holding
                        : OrpheusPersistentDirectorPhase.Idle;
                    break;
                case OrpheusPersistentDirectorAction.ContinueCrossfade:
                    CancelBgmLoading();
                    _bgmDirector.QueuedKey = OrpheusAudioKey.Invalid;
                    _bgmDirector.TransitionGeneration = _bgmDirector.Generation;
                    break;
                case OrpheusPersistentDirectorAction.ReverseCrossfade:
                    CancelBgmLoading();
                    _bgmDirector.QueuedKey = OrpheusAudioKey.Invalid;
                    SwapBgmTransitionRoles();
                    _bgmDirector.CrossfadeProgress = decision.CrossfadeProgress;
                    _bgmDirector.TransitionGeneration = _bgmDirector.Generation;
                    break;
                case OrpheusPersistentDirectorAction.QueueLatest:
                    BeginBgmLoading(decision.QueuedKey, true);
                    _bgmDirector.TransitionGeneration = _bgmDirector.Generation;
                    break;
                case OrpheusPersistentDirectorAction.Stop:
                    CancelBgmLoading();
                    _bgmDirector.QueuedKey = OrpheusAudioKey.Invalid;
                    BeginBgmStop();
                    break;
                case OrpheusPersistentDirectorAction.PreserveActive:
                    CancelBgmLoading();
                    _bgmDirector.QueuedKey = OrpheusAudioKey.Invalid;
                    BeginBgmNormalization(decision.SurvivorSourceIndex);
                    break;
                case OrpheusPersistentDirectorAction.PreserveSurvivorAndLoad:
                    BeginBgmNormalization(decision.SurvivorSourceIndex);
                    BeginBgmLoading(desiredKey, BgmActiveCount == 2);
                    break;
            }

            RequestBgmLoadFromIntent();
        }

        private void RetryBlockedBgmIntent()
        {
            var desiredKey = _profileState.Intent.BgmKey;
            if (!desiredKey.IsValid ||
                !TryConsumeProfilePersistentRetry(OrpheusPlaybackKind.Bgm, desiredKey))
            {
                return;
            }

            AdvanceBgmGeneration();
            if (_bgmDirector.Phase == OrpheusPersistentDirectorPhase.Crossfading ||
                _bgmDirector.Phase == OrpheusPersistentDirectorPhase.Stopping ||
                _bgmDirector.Phase == OrpheusPersistentDirectorPhase.Normalizing)
            {
                _bgmDirector.TransitionGeneration = _bgmDirector.Generation;
            }

            _bgmDirector.FailureReported = false;
            _bgmDirector.LoadRequestIssued = false;
            _bgmDirector.LoadingGeneration = _bgmDirector.Generation;
            if (_bgmDirector.LoadingKey != desiredKey)
            {
                BeginBgmLoading(desiredKey, _bgmDirector.Phase == OrpheusPersistentDirectorPhase.Crossfading);
            }
            else if (_bgmDirector.LoadingEntryIndex >= 0)
            {
                CancelLoadObservation(_bgmDirector.LoadingEntryIndex);
                _bgmDirector.LoadingEntryIndex = -1;
            }

            RequestBgmLoadFromIntent();
        }

        private void TickBgm(double realtime, float activeDelta)
        {
            AdvanceBgmTransition(activeDelta);
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            PollBgmLoading(realtime);
        }

        private void BeginBgmLoading(OrpheusAudioKey key, bool queued)
        {
            if (_bgmDirector.LoadingKey != key)
            {
                CancelBgmLoading();
                _bgmDirector.LoadingKey = key;
                _bgmDirector.LoadingGeneration = _bgmDirector.Generation;
                _bgmDirector.LoadRequestIssued = false;
                _bgmDirector.FailureReported = false;
            }

            _bgmDirector.QueuedKey = queued ? key : OrpheusAudioKey.Invalid;
            if (_bgmDirector.Phase != OrpheusPersistentDirectorPhase.Crossfading &&
                _bgmDirector.Phase != OrpheusPersistentDirectorPhase.Normalizing &&
                _bgmDirector.Phase != OrpheusPersistentDirectorPhase.Stopping)
            {
                _bgmDirector.Phase = OrpheusPersistentDirectorPhase.Loading;
            }
        }

        private void RequestBgmLoadFromIntent()
        {
            if (!_bgmDirector.LoadingKey.IsValid || _bgmDirector.LoadRequestIssued || _bgmFailedBlocked ||
                _profileState.Intent.BgmKey != _bgmDirector.LoadingKey)
            {
                return;
            }

            if (!_catalogSnapshot.CoreIndex.TryGet(
                    _bgmDirector.LoadingKey,
                    out var entry,
                    out var entryIndex))
            {
                MarkBgmLoadFailed();
                return;
            }

            _bgmDirector.LoadingEntryIndex = entryIndex;
            var realtime = _runtimeHost.ReadRealtime();
            if (_isBound && realtime < _lastRealtime)
            {
                realtime = _lastRealtime;
            }

            var aggregate = GetAggregateLoadState(entry);
            if (aggregate == OrpheusClipLoadState.Loaded)
            {
                CancelLoadObservation(entryIndex);
                return;
            }

            if (aggregate == OrpheusClipLoadState.Loading)
            {
                EnsureLoadObservation(entryIndex, realtime);
                _bgmDirector.LoadRequestIssued = true;
                return;
            }

            if (aggregate != OrpheusClipLoadState.Unloaded &&
                aggregate != OrpheusClipLoadState.Failed)
            {
                MarkBgmLoadFailed();
                return;
            }

            var requestAccepted = false;
            var requestFailed = false;
            var clipEnd = entry.ClipRange.Offset + entry.ClipRange.Count;
            for (var clipIndex = entry.ClipRange.Offset; clipIndex < clipEnd; clipIndex++)
            {
                if (!TryReadClipLoadState(clipIndex, out var state))
                {
                    requestFailed = true;
                    continue;
                }

                if (state == OrpheusClipLoadState.Loaded ||
                    state == OrpheusClipLoadState.Loading)
                {
                    continue;
                }

                if (TryRequestClipLoad(clipIndex))
                {
                    requestAccepted = true;
                }
                else
                {
                    requestFailed = true;
                }
            }

            _bgmDirector.LoadRequestIssued = true;
            if (requestAccepted)
            {
                RestartLoadObservation(entryIndex, realtime);
            }

            if (requestFailed || !requestAccepted)
            {
                MarkBgmLoadFailed();
            }
        }

        private void PollBgmLoading(double realtime)
        {
            if (!_bgmDirector.LoadingKey.IsValid || _profileState.Intent.BgmKey != _bgmDirector.LoadingKey)
            {
                return;
            }

            if (_bgmFailedBlocked)
            {
                return;
            }

            if (!_catalogSnapshot.CoreIndex.TryGet(
                    _bgmDirector.LoadingKey,
                    out var entry,
                    out var entryIndex))
            {
                MarkBgmLoadFailed();
                return;
            }

            _bgmDirector.LoadingEntryIndex = entryIndex;
            var aggregate = GetAggregateLoadState(entry);
            if (aggregate == OrpheusClipLoadState.Failed ||
                aggregate == OrpheusClipLoadState.Invalid)
            {
                if (_bgmDirector.LoadRequestIssued)
                {
                    MarkBgmLoadFailed();
                }
                else
                {
                    RequestBgmLoadFromIntent();
                }

                return;
            }

            if (aggregate == OrpheusClipLoadState.Unloaded)
            {
                RequestBgmLoadFromIntent();
                return;
            }

            if (aggregate == OrpheusClipLoadState.Loading)
            {
                EnsureLoadObservation(entryIndex, realtime);
                return;
            }

            if (_loadEntryActivePositions[entryIndex] >= 0)
            {
                CancelLoadObservation(entryIndex);
            }

            if (!IsPersistentPlaybackReady() ||
                _bgmDirector.Phase == OrpheusPersistentDirectorPhase.Crossfading ||
                _bgmDirector.Phase == OrpheusPersistentDirectorPhase.Stopping ||
                _bgmDirector.Phase == OrpheusPersistentDirectorPhase.Normalizing)
            {
                return;
            }

            TryStartLoadedBgm(entry);
        }

        private void TryStartLoadedBgm(OrpheusCatalogEntry entry)
        {
            var generation = _bgmDirector.LoadingGeneration;
            var key = _bgmDirector.LoadingKey;
            if (!OrpheusAudioPersistentDirectorPolicy.CanStart(
                    generation,
                    _bgmDirector.Generation,
                    key,
                    _profileState.Intent.BgmKey) ||
                _bgmFailedBlocked ||
                GetAggregateLoadState(entry) != OrpheusClipLoadState.Loaded)
            {
                return;
            }

            var sourceIndex = FindFreeBgmSourceIndex();
            if (sourceIndex < 0)
            {
                return;
            }

            var carrierReason = ValidateCarrier();
            if (carrierReason != OrpheusAudioDisableReason.None)
            {
                FailClosed(carrierReason);
                return;
            }

            try
            {
                var route = _categoryRoutes.Get(OrpheusCategory.Music);
                if (route == null)
                {
                    FailClosed(OrpheusAudioDisableReason.MixerGroupReferenceInvalid);
                    return;
                }

                var source = GetBgmSource(sourceIndex);
                if (source == null)
                {
                    FailClosed(OrpheusAudioDisableReason.OwnedSourceDestroyed);
                    return;
                }

                if (!OrpheusAudioPersistentDirectorPolicy.CanStart(
                        generation,
                        _bgmDirector.Generation,
                        key,
                        _profileState.Intent.BgmKey))
                {
                    return;
                }

                var clip = _catalogSnapshot.GetClip(entry.ClipRange.Offset);
                ConfigureBgmSource(
                    source,
                    route,
                    clip,
                    entry.Policy.Voice.Priority);
                source.Play();
                SetBgmSourceState(sourceIndex, key, entry.Policy.Volume.Minimum, 0f);
                CancelBgmLoading();
                _bgmDirector.QueuedKey = OrpheusAudioKey.Invalid;

                if (_bgmDirector.CurrentSourceIndex < 0)
                {
                    _bgmDirector.CurrentSourceIndex = sourceIndex;
                    _bgmDirector.TargetSourceIndex = -1;
                    BeginBgmNormalization(sourceIndex);
                }
                else
                {
                    _bgmDirector.TargetSourceIndex = sourceIndex;
                    _bgmDirector.Phase = OrpheusPersistentDirectorPhase.Crossfading;
                    _bgmDirector.CrossfadeProgress = 0f;
                    _bgmDirector.TransitionGeneration = _bgmDirector.Generation;
                    ApplyBgmCrossfadeGains();
                }
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private static void ConfigureBgmSource(
            AudioSource source,
            AudioMixerGroup route,
            AudioClip clip,
            byte priority)
        {
            OrpheusAudioSourceNormalizer.Normalize(source);
            source.clip = clip;
            source.loop = true;
            source.volume = 0f;
            source.pitch = 1f;
            source.priority = priority;
            source.outputAudioMixerGroup = route;
            source.spatialBlend = 0f;
        }

        private void AdvanceBgmTransition(float activeDelta)
        {
            if (!(activeDelta > 0f))
            {
                return;
            }

            if (_bgmDirector.Phase == OrpheusPersistentDirectorPhase.Crossfading ||
                _bgmDirector.Phase == OrpheusPersistentDirectorPhase.Stopping ||
                _bgmDirector.Phase == OrpheusPersistentDirectorPhase.Normalizing)
            {
                var carrierReason = ValidateCarrier();
                if (carrierReason != OrpheusAudioDisableReason.None)
                {
                    FailClosed(carrierReason);
                    return;
                }
            }

            switch (_bgmDirector.Phase)
            {
                case OrpheusPersistentDirectorPhase.Crossfading:
                    AdvanceBgmCrossfade(activeDelta);
                    break;
                case OrpheusPersistentDirectorPhase.Stopping:
                    AdvanceBgmStop(activeDelta);
                    break;
                case OrpheusPersistentDirectorPhase.Normalizing:
                    AdvanceBgmNormalization(activeDelta);
                    break;
            }
        }

        private void AdvanceBgmCrossfade(float activeDelta)
        {
            if (!OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    _bgmDirector.TransitionGeneration,
                    _bgmDirector.Generation))
            {
                return;
            }

            _bgmDirector.CrossfadeProgress = Clamp01(
                _bgmDirector.CrossfadeProgress + activeDelta / _bgmCrossfadeSeconds);
            ApplyBgmCrossfadeGains();
            if (_lifecycle != OrpheusAudioLifecycle.Running || _bgmDirector.CrossfadeProgress < 1f)
            {
                return;
            }

            var generation = _bgmDirector.TransitionGeneration;
            var releasedIndex = _bgmDirector.CurrentSourceIndex;
            var survivorIndex = _bgmDirector.TargetSourceIndex;
            if (!OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    generation,
                    _bgmDirector.Generation) ||
                _bgmDirector.Phase != OrpheusPersistentDirectorPhase.Crossfading ||
                releasedIndex < 0 || survivorIndex < 0)
            {
                return;
            }

            ReleaseBgmSource(releasedIndex, generation);
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            _bgmDirector.CurrentSourceIndex = survivorIndex;
            _bgmDirector.TargetSourceIndex = -1;
            SetBgmSourceGain(survivorIndex, 1f);
            _bgmDirector.CrossfadeProgress = 0f;
            _bgmDirector.Phase = _bgmDirector.LoadingKey.IsValid
                ? OrpheusPersistentDirectorPhase.Loading
                : OrpheusPersistentDirectorPhase.Holding;
        }

        private void ApplyBgmCrossfadeGains()
        {
            OrpheusAudioPersistentDirectorPolicy.GetEqualPowerGains(
                _bgmDirector.CrossfadeProgress,
                out var outgoing,
                out var incoming);
            SetBgmSourceGain(_bgmDirector.CurrentSourceIndex, outgoing);
            SetBgmSourceGain(_bgmDirector.TargetSourceIndex, incoming);
        }

        private void BeginBgmStop()
        {
            if (!HasBgmSources)
            {
                _bgmDirector.Phase = OrpheusPersistentDirectorPhase.Idle;
                return;
            }

            CaptureBgmTransitionStartGains();
            _bgmDirector.Phase = OrpheusPersistentDirectorPhase.Stopping;
            _bgmDirector.TransitionGeneration = _bgmDirector.Generation;
        }

        private void AdvanceBgmStop(float activeDelta)
        {
            if (!OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    _bgmDirector.TransitionGeneration,
                    _bgmDirector.Generation))
            {
                return;
            }

            _bgmDirector.TransitionElapsed += activeDelta;
            var progress = Clamp01(_bgmDirector.TransitionElapsed / _bgmCrossfadeSeconds);
            SetBgmSourceGain(0, _bgmDirector.TransitionStartGainZero * (1f - progress));
            SetBgmSourceGain(1, _bgmDirector.TransitionStartGainOne * (1f - progress));
            if (_lifecycle != OrpheusAudioLifecycle.Running || progress < 1f)
            {
                return;
            }

            var generation = _bgmDirector.TransitionGeneration;
            ReleaseBgmSource(0, generation);
            ReleaseBgmSource(1, generation);
            if (_lifecycle == OrpheusAudioLifecycle.Running &&
                OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    generation,
                    _bgmDirector.Generation))
            {
                _bgmDirector.CurrentSourceIndex = -1;
                _bgmDirector.TargetSourceIndex = -1;
                _bgmDirector.Phase = OrpheusPersistentDirectorPhase.Idle;
            }
        }

        private void BeginBgmNormalization(int survivorSourceIndex)
        {
            if (survivorSourceIndex < 0 || !GetBgmSourceKey(survivorSourceIndex).IsValid)
            {
                _bgmDirector.CurrentSourceIndex = -1;
                _bgmDirector.TargetSourceIndex = -1;
                _bgmDirector.Phase = _bgmDirector.LoadingKey.IsValid
                    ? OrpheusPersistentDirectorPhase.Loading
                    : OrpheusPersistentDirectorPhase.Idle;
                return;
            }

            _bgmDirector.CurrentSourceIndex = survivorSourceIndex;
            var otherIndex = 1 - survivorSourceIndex;
            _bgmDirector.TargetSourceIndex = GetBgmSourceKey(otherIndex).IsValid ? otherIndex : -1;
            CaptureBgmTransitionStartGains();
            _bgmDirector.Phase = OrpheusPersistentDirectorPhase.Normalizing;
            _bgmDirector.TransitionGeneration = _bgmDirector.Generation;
        }

        private void AdvanceBgmNormalization(float activeDelta)
        {
            if (!OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    _bgmDirector.TransitionGeneration,
                    _bgmDirector.Generation))
            {
                return;
            }

            _bgmDirector.TransitionElapsed += activeDelta;
            var progress = Clamp01(_bgmDirector.TransitionElapsed / _bgmCrossfadeSeconds);
            var survivorStart = GetBgmTransitionStartGain(_bgmDirector.CurrentSourceIndex);
            SetBgmSourceGain(
                _bgmDirector.CurrentSourceIndex,
                survivorStart + (1f - survivorStart) * progress);
            if (_bgmDirector.TargetSourceIndex >= 0)
            {
                var targetStart = GetBgmTransitionStartGain(_bgmDirector.TargetSourceIndex);
                SetBgmSourceGain(_bgmDirector.TargetSourceIndex, targetStart * (1f - progress));
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running || progress < 1f)
            {
                return;
            }

            var generation = _bgmDirector.TransitionGeneration;
            if (_bgmDirector.TargetSourceIndex >= 0)
            {
                ReleaseBgmSource(_bgmDirector.TargetSourceIndex, generation);
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running ||
                !OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    generation,
                    _bgmDirector.Generation))
            {
                return;
            }

            SetBgmSourceGain(_bgmDirector.CurrentSourceIndex, 1f);
            _bgmDirector.TargetSourceIndex = -1;
            _bgmDirector.Phase = _bgmDirector.LoadingKey.IsValid
                ? OrpheusPersistentDirectorPhase.Loading
                : OrpheusPersistentDirectorPhase.Holding;
        }

        private void CaptureBgmTransitionStartGains()
        {
            _bgmDirector.TransitionStartGainZero = _bgmDirector.SourceZeroGain;
            _bgmDirector.TransitionStartGainOne = _bgmDirector.SourceOneGain;
            _bgmDirector.TransitionElapsed = 0f;
        }

        private void SwapBgmTransitionRoles()
        {
            var oldCurrent = _bgmDirector.CurrentSourceIndex;
            _bgmDirector.CurrentSourceIndex = _bgmDirector.TargetSourceIndex;
            _bgmDirector.TargetSourceIndex = oldCurrent;
        }

        private void ReleaseBgmSource(int sourceIndex, uint generation)
        {
            if (sourceIndex < 0 ||
                !OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    generation,
                    _bgmDirector.Generation))
            {
                return;
            }

            try
            {
                var source = GetBgmSource(sourceIndex);
                if (source == null)
                {
                    FailClosed(OrpheusAudioDisableReason.OwnedSourceDestroyed);
                    return;
                }

                OrpheusAudioSourceNormalizer.Normalize(source);
                SetBgmSourceState(sourceIndex, OrpheusAudioKey.Invalid, 0f, 0f);
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private void SetBgmSourceGain(int sourceIndex, float gain)
        {
            if (sourceIndex < 0 || !GetBgmSourceKey(sourceIndex).IsValid)
            {
                return;
            }

            try
            {
                var source = GetBgmSource(sourceIndex);
                if (source == null)
                {
                    FailClosed(OrpheusAudioDisableReason.OwnedSourceDestroyed);
                    return;
                }

                var authoredVolume = sourceIndex == 0
                    ? _bgmDirector.SourceZeroAuthoredVolume
                    : _bgmDirector.SourceOneAuthoredVolume;
                source.volume = gain * authoredVolume;
                if (sourceIndex == 0)
                {
                    _bgmDirector.SourceZeroGain = gain;
                }
                else
                {
                    _bgmDirector.SourceOneGain = gain;
                }
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private void SetBgmSourceState(
            int sourceIndex,
            OrpheusAudioKey key,
            float authoredVolume,
            float gain)
        {
            if (sourceIndex == 0)
            {
                _bgmDirector.SourceZeroKey = key;
                _bgmDirector.SourceZeroAuthoredVolume = authoredVolume;
                _bgmDirector.SourceZeroGain = gain;
            }
            else
            {
                _bgmDirector.SourceOneKey = key;
                _bgmDirector.SourceOneAuthoredVolume = authoredVolume;
                _bgmDirector.SourceOneGain = gain;
            }
        }

        private void CancelBgmLoading()
        {
            if (_bgmDirector.LoadingEntryIndex >= 0)
            {
                CancelLoadObservation(_bgmDirector.LoadingEntryIndex);
            }

            _bgmDirector.LoadingKey = OrpheusAudioKey.Invalid;
            _bgmDirector.LoadingEntryIndex = -1;
            _bgmDirector.LoadingGeneration = 0u;
            _bgmDirector.LoadRequestIssued = false;
            _bgmDirector.FailureReported = false;
        }

        private void MarkBgmLoadFailed()
        {
            if (!_bgmDirector.FailureReported)
            {
                SaturatingIncrement(ref _loadFailedBits);
                _bgmDirector.FailureReported = true;
            }

            MarkProfilePersistentFailed(OrpheusPlaybackKind.Bgm, _bgmDirector.LoadingKey);
            if (_bgmDirector.LoadingEntryIndex >= 0)
            {
                CancelLoadObservation(_bgmDirector.LoadingEntryIndex);
            }
        }

        private void ClearBgmDirectorState(bool advanceGeneration = true)
        {
            _bgmDirector.Phase = OrpheusPersistentDirectorPhase.Idle;
            _bgmDirector.LoadingKey = OrpheusAudioKey.Invalid;
            _bgmDirector.QueuedKey = OrpheusAudioKey.Invalid;
            _bgmDirector.SourceZeroKey = OrpheusAudioKey.Invalid;
            _bgmDirector.SourceOneKey = OrpheusAudioKey.Invalid;
            _bgmDirector.SourceZeroGain = 0f;
            _bgmDirector.SourceOneGain = 0f;
            _bgmDirector.SourceZeroAuthoredVolume = 0f;
            _bgmDirector.SourceOneAuthoredVolume = 0f;
            _bgmDirector.TransitionStartGainZero = 0f;
            _bgmDirector.TransitionStartGainOne = 0f;
            _bgmDirector.TransitionElapsed = 0f;
            _bgmDirector.CrossfadeProgress = 0f;
            _bgmDirector.CurrentSourceIndex = -1;
            _bgmDirector.TargetSourceIndex = -1;
            _bgmDirector.LoadingEntryIndex = -1;
            _bgmDirector.LoadingGeneration = 0u;
            _bgmDirector.TransitionGeneration = 0u;
            _bgmDirector.LoadRequestIssued = false;
            _bgmDirector.FailureReported = false;
            if (advanceGeneration)
            {
                AdvanceBgmGeneration();
            }
        }

        private void AdvanceBgmGeneration()
        {
            _bgmDirector.Generation = unchecked(_bgmDirector.Generation + 1u);
            if (_bgmDirector.Generation == 0u)
            {
                _bgmDirector.Generation = 1u;
            }
        }

        private AudioSource GetBgmSource(int sourceIndex)
        {
            return _ownedSources[OrpheusAudioSourceBank.BgmOffset + sourceIndex];
        }

        private int FindFreeBgmSourceIndex()
        {
            if (!_bgmDirector.SourceZeroKey.IsValid)
            {
                return 0;
            }

            return !_bgmDirector.SourceOneKey.IsValid ? 1 : -1;
        }

        private OrpheusAudioKey GetBgmSourceKey(int sourceIndex)
        {
            if (sourceIndex == 0)
            {
                return _bgmDirector.SourceZeroKey;
            }

            return sourceIndex == 1 ? _bgmDirector.SourceOneKey : OrpheusAudioKey.Invalid;
        }

        private float GetBgmTransitionStartGain(int sourceIndex)
        {
            return sourceIndex == 0
                ? _bgmDirector.TransitionStartGainZero
                : _bgmDirector.TransitionStartGainOne;
        }

        private OrpheusAudioKey CurrentBgmKey => GetBgmSourceKey(_bgmDirector.CurrentSourceIndex);
        private OrpheusAudioKey TargetBgmKey => GetBgmSourceKey(_bgmDirector.TargetSourceIndex);
        private byte BgmActiveCount => (byte)(
            (_bgmDirector.SourceZeroKey.IsValid ? 1 : 0) +
            (_bgmDirector.SourceOneKey.IsValid ? 1 : 0));
        private bool HasBgmSources => _bgmDirector.SourceZeroKey.IsValid || _bgmDirector.SourceOneKey.IsValid;

        private static float Clamp01(float value)
        {
            if (!(value > 0f))
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }
    }
}
