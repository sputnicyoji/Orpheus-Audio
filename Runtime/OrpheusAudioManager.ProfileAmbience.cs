using System;
using Orpheus.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio
{
    public sealed partial class OrpheusAudioManager
    {
        private OrpheusPersistentDirectorExecutionState _profileAmbienceDirector;

        private void InitializeProfileAmbienceDirector()
        {
            _profileAmbienceDirector.Initialize();
        }

        private void ReconcileCommittedProfileAmbienceIntent(OrpheusAudioKey desiredKey)
        {
            var view = new OrpheusPersistentDirectorView(
                _profileAmbienceDirector.Phase,
                GetProfileAmbienceSourceKey(_profileAmbienceDirector.CurrentSourceIndex),
                _profileAmbienceDirector.LoadingKey,
                GetProfileAmbienceSourceKey(_profileAmbienceDirector.TargetSourceIndex),
                _profileAmbienceDirector.QueuedKey,
                _profileAmbienceDirector.SourceZeroKey,
                _profileAmbienceDirector.SourceZeroGain,
                _profileAmbienceDirector.SourceOneKey,
                _profileAmbienceDirector.SourceOneGain,
                _profileAmbienceDirector.CrossfadeProgress);
            var decision = OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(view, desiredKey);
            if (decision.Action == OrpheusPersistentDirectorAction.None)
            {
                return;
            }

            AdvanceProfileAmbienceGeneration();
            switch (decision.Action)
            {
                case OrpheusPersistentDirectorAction.BeginLoad:
                    BeginProfileAmbienceLoading(desiredKey, false);
                    break;
                case OrpheusPersistentDirectorAction.CancelPendingKeepCurrent:
                    CancelProfileAmbienceLoading();
                    _profileAmbienceDirector.QueuedKey = OrpheusAudioKey.Invalid;
                    _profileAmbienceDirector.Phase = HasProfileAmbienceSources
                        ? OrpheusPersistentDirectorPhase.Holding
                        : OrpheusPersistentDirectorPhase.Idle;
                    break;
                case OrpheusPersistentDirectorAction.ContinueCrossfade:
                    CancelProfileAmbienceLoading();
                    _profileAmbienceDirector.QueuedKey = OrpheusAudioKey.Invalid;
                    _profileAmbienceDirector.TransitionGeneration = _profileAmbienceDirector.Generation;
                    break;
                case OrpheusPersistentDirectorAction.ReverseCrossfade:
                    CancelProfileAmbienceLoading();
                    _profileAmbienceDirector.QueuedKey = OrpheusAudioKey.Invalid;
                    SwapProfileAmbienceTransitionRoles();
                    _profileAmbienceDirector.CrossfadeProgress = decision.CrossfadeProgress;
                    _profileAmbienceDirector.TransitionGeneration = _profileAmbienceDirector.Generation;
                    break;
                case OrpheusPersistentDirectorAction.QueueLatest:
                    BeginProfileAmbienceLoading(decision.QueuedKey, true);
                    _profileAmbienceDirector.TransitionGeneration = _profileAmbienceDirector.Generation;
                    break;
                case OrpheusPersistentDirectorAction.Stop:
                    CancelProfileAmbienceLoading();
                    _profileAmbienceDirector.QueuedKey = OrpheusAudioKey.Invalid;
                    BeginProfileAmbienceStop();
                    break;
                case OrpheusPersistentDirectorAction.PreserveActive:
                    CancelProfileAmbienceLoading();
                    _profileAmbienceDirector.QueuedKey = OrpheusAudioKey.Invalid;
                    BeginProfileAmbienceNormalization(decision.SurvivorSourceIndex);
                    break;
                case OrpheusPersistentDirectorAction.PreserveSurvivorAndLoad:
                    BeginProfileAmbienceNormalization(decision.SurvivorSourceIndex);
                    BeginProfileAmbienceLoading(desiredKey, ProfileAmbienceActiveCount == 2);
                    break;
            }

            RequestProfileAmbienceLoadFromIntent();
        }

        private void RetryBlockedProfileAmbienceIntent()
        {
            var desiredKey = _profileState.Intent.ProfileAmbienceKey;
            if (!desiredKey.IsValid ||
                !TryConsumeProfilePersistentRetry(OrpheusPlaybackKind.ProfileAmbience, desiredKey))
            {
                return;
            }

            AdvanceProfileAmbienceGeneration();
            if (_profileAmbienceDirector.Phase == OrpheusPersistentDirectorPhase.Crossfading ||
                _profileAmbienceDirector.Phase == OrpheusPersistentDirectorPhase.Stopping ||
                _profileAmbienceDirector.Phase == OrpheusPersistentDirectorPhase.Normalizing)
            {
                _profileAmbienceDirector.TransitionGeneration = _profileAmbienceDirector.Generation;
            }

            _profileAmbienceDirector.FailureReported = false;
            _profileAmbienceDirector.LoadRequestIssued = false;
            _profileAmbienceDirector.LoadingGeneration = _profileAmbienceDirector.Generation;
            if (_profileAmbienceDirector.LoadingKey != desiredKey)
            {
                BeginProfileAmbienceLoading(desiredKey, _profileAmbienceDirector.Phase == OrpheusPersistentDirectorPhase.Crossfading);
            }
            else if (_profileAmbienceDirector.LoadingEntryIndex >= 0)
            {
                CancelLoadObservation(_profileAmbienceDirector.LoadingEntryIndex);
                _profileAmbienceDirector.LoadingEntryIndex = -1;
            }

            RequestProfileAmbienceLoadFromIntent();
        }

        private void TickProfileAmbience(double realtime, float activeDelta)
        {
            AdvanceProfileAmbienceTransition(activeDelta);
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            PollProfileAmbienceLoading(realtime);
        }

        private void BeginProfileAmbienceLoading(OrpheusAudioKey key, bool queued)
        {
            if (_profileAmbienceDirector.LoadingKey != key)
            {
                CancelProfileAmbienceLoading();
                _profileAmbienceDirector.LoadingKey = key;
                _profileAmbienceDirector.LoadingGeneration = _profileAmbienceDirector.Generation;
                _profileAmbienceDirector.LoadRequestIssued = false;
                _profileAmbienceDirector.FailureReported = false;
            }

            _profileAmbienceDirector.QueuedKey = queued ? key : OrpheusAudioKey.Invalid;
            if (_profileAmbienceDirector.Phase != OrpheusPersistentDirectorPhase.Crossfading &&
                _profileAmbienceDirector.Phase != OrpheusPersistentDirectorPhase.Normalizing &&
                _profileAmbienceDirector.Phase != OrpheusPersistentDirectorPhase.Stopping)
            {
                _profileAmbienceDirector.Phase = OrpheusPersistentDirectorPhase.Loading;
            }
        }

        private void RequestProfileAmbienceLoadFromIntent()
        {
            if (!_profileAmbienceDirector.LoadingKey.IsValid || _profileAmbienceDirector.LoadRequestIssued || _profileAmbienceFailedBlocked ||
                _profileState.Intent.ProfileAmbienceKey != _profileAmbienceDirector.LoadingKey)
            {
                return;
            }

            if (!_catalogSnapshot.CoreIndex.TryGet(
                    _profileAmbienceDirector.LoadingKey,
                    out var entry,
                    out var entryIndex))
            {
                MarkProfileAmbienceLoadFailed();
                return;
            }

            _profileAmbienceDirector.LoadingEntryIndex = entryIndex;
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
                _profileAmbienceDirector.LoadRequestIssued = true;
                return;
            }

            if (aggregate != OrpheusClipLoadState.Unloaded &&
                aggregate != OrpheusClipLoadState.Failed)
            {
                MarkProfileAmbienceLoadFailed();
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

            _profileAmbienceDirector.LoadRequestIssued = true;
            if (requestAccepted)
            {
                RestartLoadObservation(entryIndex, realtime);
            }

            if (requestFailed || !requestAccepted)
            {
                MarkProfileAmbienceLoadFailed();
            }
        }

        private void PollProfileAmbienceLoading(double realtime)
        {
            if (!_profileAmbienceDirector.LoadingKey.IsValid || _profileState.Intent.ProfileAmbienceKey != _profileAmbienceDirector.LoadingKey)
            {
                return;
            }

            if (_profileAmbienceFailedBlocked)
            {
                return;
            }

            if (!_catalogSnapshot.CoreIndex.TryGet(
                    _profileAmbienceDirector.LoadingKey,
                    out var entry,
                    out var entryIndex))
            {
                MarkProfileAmbienceLoadFailed();
                return;
            }

            _profileAmbienceDirector.LoadingEntryIndex = entryIndex;
            var aggregate = GetAggregateLoadState(entry);
            if (aggregate == OrpheusClipLoadState.Failed ||
                aggregate == OrpheusClipLoadState.Invalid)
            {
                if (_profileAmbienceDirector.LoadRequestIssued)
                {
                    MarkProfileAmbienceLoadFailed();
                }
                else
                {
                    RequestProfileAmbienceLoadFromIntent();
                }

                return;
            }

            if (aggregate == OrpheusClipLoadState.Unloaded)
            {
                RequestProfileAmbienceLoadFromIntent();
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
                _profileAmbienceDirector.Phase == OrpheusPersistentDirectorPhase.Crossfading ||
                _profileAmbienceDirector.Phase == OrpheusPersistentDirectorPhase.Stopping ||
                _profileAmbienceDirector.Phase == OrpheusPersistentDirectorPhase.Normalizing)
            {
                return;
            }

            TryStartLoadedProfileAmbience(entry);
        }

        private void TryStartLoadedProfileAmbience(OrpheusCatalogEntry entry)
        {
            var generation = _profileAmbienceDirector.LoadingGeneration;
            var key = _profileAmbienceDirector.LoadingKey;
            if (!OrpheusAudioPersistentDirectorPolicy.CanStart(
                    generation,
                    _profileAmbienceDirector.Generation,
                    key,
                    _profileState.Intent.ProfileAmbienceKey) ||
                _profileAmbienceFailedBlocked ||
                GetAggregateLoadState(entry) != OrpheusClipLoadState.Loaded)
            {
                return;
            }

            var sourceIndex = FindFreeProfileAmbienceSourceIndex();
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
                var route = _categoryRoutes.Get(OrpheusCategory.Ambience);
                if (route == null)
                {
                    FailClosed(OrpheusAudioDisableReason.MixerGroupReferenceInvalid);
                    return;
                }

                var source = GetProfileAmbienceSource(sourceIndex);
                if (source == null)
                {
                    FailClosed(OrpheusAudioDisableReason.OwnedSourceDestroyed);
                    return;
                }

                if (!OrpheusAudioPersistentDirectorPolicy.CanStart(
                        generation,
                        _profileAmbienceDirector.Generation,
                        key,
                        _profileState.Intent.ProfileAmbienceKey))
                {
                    return;
                }

                var clip = _catalogSnapshot.GetClip(entry.ClipRange.Offset);
                ConfigureProfileAmbienceSource(
                    source,
                    route,
                    clip,
                    entry.Policy.Voice.Priority);
                source.Play();
                SetProfileAmbienceSourceState(sourceIndex, key, entry.Policy.Volume.Minimum, 0f);
                CancelProfileAmbienceLoading();
                _profileAmbienceDirector.QueuedKey = OrpheusAudioKey.Invalid;

                if (_profileAmbienceDirector.CurrentSourceIndex < 0)
                {
                    _profileAmbienceDirector.CurrentSourceIndex = sourceIndex;
                    _profileAmbienceDirector.TargetSourceIndex = -1;
                    BeginProfileAmbienceNormalization(sourceIndex);
                }
                else
                {
                    _profileAmbienceDirector.TargetSourceIndex = sourceIndex;
                    _profileAmbienceDirector.Phase = OrpheusPersistentDirectorPhase.Crossfading;
                    _profileAmbienceDirector.CrossfadeProgress = 0f;
                    _profileAmbienceDirector.TransitionGeneration = _profileAmbienceDirector.Generation;
                    ApplyProfileAmbienceCrossfadeGains();
                }
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private static void ConfigureProfileAmbienceSource(
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

        private void AdvanceProfileAmbienceTransition(float activeDelta)
        {
            if (!(activeDelta > 0f))
            {
                return;
            }

            if (_profileAmbienceDirector.Phase == OrpheusPersistentDirectorPhase.Crossfading ||
                _profileAmbienceDirector.Phase == OrpheusPersistentDirectorPhase.Stopping ||
                _profileAmbienceDirector.Phase == OrpheusPersistentDirectorPhase.Normalizing)
            {
                var carrierReason = ValidateCarrier();
                if (carrierReason != OrpheusAudioDisableReason.None)
                {
                    FailClosed(carrierReason);
                    return;
                }
            }

            switch (_profileAmbienceDirector.Phase)
            {
                case OrpheusPersistentDirectorPhase.Crossfading:
                    AdvanceProfileAmbienceCrossfade(activeDelta);
                    break;
                case OrpheusPersistentDirectorPhase.Stopping:
                    AdvanceProfileAmbienceStop(activeDelta);
                    break;
                case OrpheusPersistentDirectorPhase.Normalizing:
                    AdvanceProfileAmbienceNormalization(activeDelta);
                    break;
            }
        }

        private void AdvanceProfileAmbienceCrossfade(float activeDelta)
        {
            if (!OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    _profileAmbienceDirector.TransitionGeneration,
                    _profileAmbienceDirector.Generation))
            {
                return;
            }

            _profileAmbienceDirector.CrossfadeProgress = Clamp01(
                _profileAmbienceDirector.CrossfadeProgress + activeDelta / _profileAmbienceCrossfadeSeconds);
            ApplyProfileAmbienceCrossfadeGains();
            if (_lifecycle != OrpheusAudioLifecycle.Running || _profileAmbienceDirector.CrossfadeProgress < 1f)
            {
                return;
            }

            var generation = _profileAmbienceDirector.TransitionGeneration;
            var releasedIndex = _profileAmbienceDirector.CurrentSourceIndex;
            var survivorIndex = _profileAmbienceDirector.TargetSourceIndex;
            if (!OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    generation,
                    _profileAmbienceDirector.Generation) ||
                _profileAmbienceDirector.Phase != OrpheusPersistentDirectorPhase.Crossfading ||
                releasedIndex < 0 || survivorIndex < 0)
            {
                return;
            }

            ReleaseProfileAmbienceSource(releasedIndex, generation);
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            _profileAmbienceDirector.CurrentSourceIndex = survivorIndex;
            _profileAmbienceDirector.TargetSourceIndex = -1;
            SetProfileAmbienceSourceGain(survivorIndex, 1f);
            _profileAmbienceDirector.CrossfadeProgress = 0f;
            _profileAmbienceDirector.Phase = _profileAmbienceDirector.LoadingKey.IsValid
                ? OrpheusPersistentDirectorPhase.Loading
                : OrpheusPersistentDirectorPhase.Holding;
        }

        private void ApplyProfileAmbienceCrossfadeGains()
        {
            OrpheusAudioPersistentDirectorPolicy.GetEqualPowerGains(
                _profileAmbienceDirector.CrossfadeProgress,
                out var outgoing,
                out var incoming);
            SetProfileAmbienceSourceGain(_profileAmbienceDirector.CurrentSourceIndex, outgoing);
            SetProfileAmbienceSourceGain(_profileAmbienceDirector.TargetSourceIndex, incoming);
        }

        private void BeginProfileAmbienceStop()
        {
            if (!HasProfileAmbienceSources)
            {
                _profileAmbienceDirector.Phase = OrpheusPersistentDirectorPhase.Idle;
                return;
            }

            CaptureProfileAmbienceTransitionStartGains();
            _profileAmbienceDirector.Phase = OrpheusPersistentDirectorPhase.Stopping;
            _profileAmbienceDirector.TransitionGeneration = _profileAmbienceDirector.Generation;
        }

        private void AdvanceProfileAmbienceStop(float activeDelta)
        {
            if (!OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    _profileAmbienceDirector.TransitionGeneration,
                    _profileAmbienceDirector.Generation))
            {
                return;
            }

            _profileAmbienceDirector.TransitionElapsed += activeDelta;
            var progress = Clamp01(_profileAmbienceDirector.TransitionElapsed / _profileAmbienceCrossfadeSeconds);
            SetProfileAmbienceSourceGain(0, _profileAmbienceDirector.TransitionStartGainZero * (1f - progress));
            SetProfileAmbienceSourceGain(1, _profileAmbienceDirector.TransitionStartGainOne * (1f - progress));
            if (_lifecycle != OrpheusAudioLifecycle.Running || progress < 1f)
            {
                return;
            }

            var generation = _profileAmbienceDirector.TransitionGeneration;
            ReleaseProfileAmbienceSource(0, generation);
            ReleaseProfileAmbienceSource(1, generation);
            if (_lifecycle == OrpheusAudioLifecycle.Running &&
                OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    generation,
                    _profileAmbienceDirector.Generation))
            {
                _profileAmbienceDirector.CurrentSourceIndex = -1;
                _profileAmbienceDirector.TargetSourceIndex = -1;
                _profileAmbienceDirector.Phase = OrpheusPersistentDirectorPhase.Idle;
            }
        }

        private void BeginProfileAmbienceNormalization(int survivorSourceIndex)
        {
            if (survivorSourceIndex < 0 || !GetProfileAmbienceSourceKey(survivorSourceIndex).IsValid)
            {
                _profileAmbienceDirector.CurrentSourceIndex = -1;
                _profileAmbienceDirector.TargetSourceIndex = -1;
                _profileAmbienceDirector.Phase = _profileAmbienceDirector.LoadingKey.IsValid
                    ? OrpheusPersistentDirectorPhase.Loading
                    : OrpheusPersistentDirectorPhase.Idle;
                return;
            }

            _profileAmbienceDirector.CurrentSourceIndex = survivorSourceIndex;
            var otherIndex = 1 - survivorSourceIndex;
            _profileAmbienceDirector.TargetSourceIndex = GetProfileAmbienceSourceKey(otherIndex).IsValid ? otherIndex : -1;
            CaptureProfileAmbienceTransitionStartGains();
            _profileAmbienceDirector.Phase = OrpheusPersistentDirectorPhase.Normalizing;
            _profileAmbienceDirector.TransitionGeneration = _profileAmbienceDirector.Generation;
        }

        private void AdvanceProfileAmbienceNormalization(float activeDelta)
        {
            if (!OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    _profileAmbienceDirector.TransitionGeneration,
                    _profileAmbienceDirector.Generation))
            {
                return;
            }

            _profileAmbienceDirector.TransitionElapsed += activeDelta;
            var progress = Clamp01(_profileAmbienceDirector.TransitionElapsed / _profileAmbienceCrossfadeSeconds);
            var survivorStart = GetProfileAmbienceTransitionStartGain(_profileAmbienceDirector.CurrentSourceIndex);
            SetProfileAmbienceSourceGain(
                _profileAmbienceDirector.CurrentSourceIndex,
                survivorStart + (1f - survivorStart) * progress);
            if (_profileAmbienceDirector.TargetSourceIndex >= 0)
            {
                var targetStart = GetProfileAmbienceTransitionStartGain(_profileAmbienceDirector.TargetSourceIndex);
                SetProfileAmbienceSourceGain(_profileAmbienceDirector.TargetSourceIndex, targetStart * (1f - progress));
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running || progress < 1f)
            {
                return;
            }

            var generation = _profileAmbienceDirector.TransitionGeneration;
            if (_profileAmbienceDirector.TargetSourceIndex >= 0)
            {
                ReleaseProfileAmbienceSource(_profileAmbienceDirector.TargetSourceIndex, generation);
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running ||
                !OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    generation,
                    _profileAmbienceDirector.Generation))
            {
                return;
            }

            SetProfileAmbienceSourceGain(_profileAmbienceDirector.CurrentSourceIndex, 1f);
            _profileAmbienceDirector.TargetSourceIndex = -1;
            _profileAmbienceDirector.Phase = _profileAmbienceDirector.LoadingKey.IsValid
                ? OrpheusPersistentDirectorPhase.Loading
                : OrpheusPersistentDirectorPhase.Holding;
        }

        private void CaptureProfileAmbienceTransitionStartGains()
        {
            _profileAmbienceDirector.TransitionStartGainZero = _profileAmbienceDirector.SourceZeroGain;
            _profileAmbienceDirector.TransitionStartGainOne = _profileAmbienceDirector.SourceOneGain;
            _profileAmbienceDirector.TransitionElapsed = 0f;
        }

        private void SwapProfileAmbienceTransitionRoles()
        {
            var oldCurrent = _profileAmbienceDirector.CurrentSourceIndex;
            _profileAmbienceDirector.CurrentSourceIndex = _profileAmbienceDirector.TargetSourceIndex;
            _profileAmbienceDirector.TargetSourceIndex = oldCurrent;
        }

        private void ReleaseProfileAmbienceSource(int sourceIndex, uint generation)
        {
            if (sourceIndex < 0 ||
                !OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(
                    generation,
                    _profileAmbienceDirector.Generation))
            {
                return;
            }

            try
            {
                var source = GetProfileAmbienceSource(sourceIndex);
                if (source == null)
                {
                    FailClosed(OrpheusAudioDisableReason.OwnedSourceDestroyed);
                    return;
                }

                OrpheusAudioSourceNormalizer.Normalize(source);
                SetProfileAmbienceSourceState(sourceIndex, OrpheusAudioKey.Invalid, 0f, 0f);
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private void SetProfileAmbienceSourceGain(int sourceIndex, float gain)
        {
            if (sourceIndex < 0 || !GetProfileAmbienceSourceKey(sourceIndex).IsValid)
            {
                return;
            }

            try
            {
                var source = GetProfileAmbienceSource(sourceIndex);
                if (source == null)
                {
                    FailClosed(OrpheusAudioDisableReason.OwnedSourceDestroyed);
                    return;
                }

                var authoredVolume = sourceIndex == 0
                    ? _profileAmbienceDirector.SourceZeroAuthoredVolume
                    : _profileAmbienceDirector.SourceOneAuthoredVolume;
                source.volume = gain * authoredVolume;
                if (sourceIndex == 0)
                {
                    _profileAmbienceDirector.SourceZeroGain = gain;
                }
                else
                {
                    _profileAmbienceDirector.SourceOneGain = gain;
                }
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private void SetProfileAmbienceSourceState(
            int sourceIndex,
            OrpheusAudioKey key,
            float authoredVolume,
            float gain)
        {
            if (sourceIndex == 0)
            {
                _profileAmbienceDirector.SourceZeroKey = key;
                _profileAmbienceDirector.SourceZeroAuthoredVolume = authoredVolume;
                _profileAmbienceDirector.SourceZeroGain = gain;
            }
            else
            {
                _profileAmbienceDirector.SourceOneKey = key;
                _profileAmbienceDirector.SourceOneAuthoredVolume = authoredVolume;
                _profileAmbienceDirector.SourceOneGain = gain;
            }
        }

        private void CancelProfileAmbienceLoading()
        {
            if (_profileAmbienceDirector.LoadingEntryIndex >= 0)
            {
                CancelLoadObservation(_profileAmbienceDirector.LoadingEntryIndex);
            }

            _profileAmbienceDirector.LoadingKey = OrpheusAudioKey.Invalid;
            _profileAmbienceDirector.LoadingEntryIndex = -1;
            _profileAmbienceDirector.LoadingGeneration = 0u;
            _profileAmbienceDirector.LoadRequestIssued = false;
            _profileAmbienceDirector.FailureReported = false;
        }

        private void MarkProfileAmbienceLoadFailed()
        {
            if (!_profileAmbienceDirector.FailureReported)
            {
                SaturatingIncrement(ref _loadFailedBits);
                _profileAmbienceDirector.FailureReported = true;
            }

            MarkProfilePersistentFailed(OrpheusPlaybackKind.ProfileAmbience, _profileAmbienceDirector.LoadingKey);
            if (_profileAmbienceDirector.LoadingEntryIndex >= 0)
            {
                CancelLoadObservation(_profileAmbienceDirector.LoadingEntryIndex);
            }
        }

        private void ClearProfileAmbienceDirectorState(bool advanceGeneration = true)
        {
            _profileAmbienceDirector.Phase = OrpheusPersistentDirectorPhase.Idle;
            _profileAmbienceDirector.LoadingKey = OrpheusAudioKey.Invalid;
            _profileAmbienceDirector.QueuedKey = OrpheusAudioKey.Invalid;
            _profileAmbienceDirector.SourceZeroKey = OrpheusAudioKey.Invalid;
            _profileAmbienceDirector.SourceOneKey = OrpheusAudioKey.Invalid;
            _profileAmbienceDirector.SourceZeroGain = 0f;
            _profileAmbienceDirector.SourceOneGain = 0f;
            _profileAmbienceDirector.SourceZeroAuthoredVolume = 0f;
            _profileAmbienceDirector.SourceOneAuthoredVolume = 0f;
            _profileAmbienceDirector.TransitionStartGainZero = 0f;
            _profileAmbienceDirector.TransitionStartGainOne = 0f;
            _profileAmbienceDirector.TransitionElapsed = 0f;
            _profileAmbienceDirector.CrossfadeProgress = 0f;
            _profileAmbienceDirector.CurrentSourceIndex = -1;
            _profileAmbienceDirector.TargetSourceIndex = -1;
            _profileAmbienceDirector.LoadingEntryIndex = -1;
            _profileAmbienceDirector.LoadingGeneration = 0u;
            _profileAmbienceDirector.TransitionGeneration = 0u;
            _profileAmbienceDirector.LoadRequestIssued = false;
            _profileAmbienceDirector.FailureReported = false;
            if (advanceGeneration)
            {
                AdvanceProfileAmbienceGeneration();
            }
        }

        private void AdvanceProfileAmbienceGeneration()
        {
            _profileAmbienceDirector.Generation = unchecked(_profileAmbienceDirector.Generation + 1u);
            if (_profileAmbienceDirector.Generation == 0u)
            {
                _profileAmbienceDirector.Generation = 1u;
            }
        }

        private AudioSource GetProfileAmbienceSource(int sourceIndex)
        {
            return _ownedSources[OrpheusAudioSourceBank.ProfileAmbienceOffset + sourceIndex];
        }

        private int FindFreeProfileAmbienceSourceIndex()
        {
            if (!_profileAmbienceDirector.SourceZeroKey.IsValid)
            {
                return 0;
            }

            return !_profileAmbienceDirector.SourceOneKey.IsValid ? 1 : -1;
        }

        private OrpheusAudioKey GetProfileAmbienceSourceKey(int sourceIndex)
        {
            if (sourceIndex == 0)
            {
                return _profileAmbienceDirector.SourceZeroKey;
            }

            return sourceIndex == 1 ? _profileAmbienceDirector.SourceOneKey : OrpheusAudioKey.Invalid;
        }

        private float GetProfileAmbienceTransitionStartGain(int sourceIndex)
        {
            return sourceIndex == 0
                ? _profileAmbienceDirector.TransitionStartGainZero
                : _profileAmbienceDirector.TransitionStartGainOne;
        }

        private OrpheusAudioKey CurrentProfileAmbienceKey => GetProfileAmbienceSourceKey(_profileAmbienceDirector.CurrentSourceIndex);
        private OrpheusAudioKey TargetProfileAmbienceKey => GetProfileAmbienceSourceKey(_profileAmbienceDirector.TargetSourceIndex);
        private byte ProfileAmbienceActiveCount => (byte)(
            (_profileAmbienceDirector.SourceZeroKey.IsValid ? 1 : 0) +
            (_profileAmbienceDirector.SourceOneKey.IsValid ? 1 : 0));
        private bool HasProfileAmbienceSources => _profileAmbienceDirector.SourceZeroKey.IsValid || _profileAmbienceDirector.SourceOneKey.IsValid;

    }
}
