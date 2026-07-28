using System;
using Orpheus.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio
{
    internal struct OrpheusTransientExecutionState
    {
        internal AudioClip PendingClip;
        internal float PendingVolume;
        internal float PendingPitch;
        internal float PendingDuration;
        internal float BaseVolume;
        internal float FadeElapsed;
        internal int PendingEntryIndex;
        internal uint PendingGeneration;

        internal void SetPending(
            AudioClip clip,
            float volume,
            float pitch,
            float duration,
            int entryIndex,
            uint generation)
        {
            PendingClip = clip;
            PendingVolume = volume;
            PendingPitch = pitch;
            PendingDuration = duration;
            FadeElapsed = 0f;
            PendingEntryIndex = entryIndex;
            PendingGeneration = generation;
        }

        internal void ClearPending()
        {
            CancelPendingPreserveFade();
            FadeElapsed = 0f;
        }

        internal void CancelPendingPreserveFade()
        {
            PendingClip = null;
            PendingVolume = 0f;
            PendingPitch = 0f;
            PendingDuration = 0f;
            PendingEntryIndex = -1;
            PendingGeneration = 0u;
        }

        internal void Clear()
        {
            this = default;
            PendingEntryIndex = -1;
        }
    }

    public sealed partial class OrpheusAudioManager
    {
        public void Play(OrpheusAudioKey key)
        {
            if (!TryPassOneShotAdmissionPrefix(
                    key,
                    OrpheusPlaybackKind.OneShot2D,
                    default,
                    false,
                    out var admission,
                    out var entry,
                    out var entryIndex))
            {
                return;
            }

            admission.Pass(OrpheusOneShotAdmissionStage.Distance, true);
            var futureKeyCount = OrpheusOneShotPolicy.CountFutureKey(_transient2DSlots, key);
            var atPolyphonyCap = futureKeyCount >= entry.Policy.Voice.PolyphonyCap;
            var slotIndex = atPolyphonyCap
                ? OrpheusOneShotPolicy.FindSameKeyVictim(_transient2DSlots, key)
                : -1;
            if (!admission.Pass(
                    OrpheusOneShotAdmissionStage.Polyphony,
                    !atPolyphonyCap || slotIndex >= 0))
            {
                RejectOneShot(admission.Result);
                return;
            }

            if (!atPolyphonyCap)
            {
                slotIndex = OrpheusOneShotPolicy.FindAdmissionSlot(
                    _transient2DSlots,
                    entry.Policy.Category,
                    entry.Policy.Voice.Priority);
            }

            if (!admission.Pass(
                    OrpheusOneShotAdmissionStage.PoolCapacity,
                    slotIndex >= 0))
            {
                RejectOneShot(admission.Result);
                return;
            }

            StartAcceptedTransient2D(slotIndex, entryIndex, entry);
        }

        private bool TryPassOneShotAdmissionPrefix(
            OrpheusAudioKey key,
            OrpheusPlaybackKind expectedPlaybackKind,
            OrpheusPosition3 position,
            bool requiresFinitePosition,
            out OrpheusOneShotAdmission admission,
            out OrpheusCatalogEntry entry,
            out int entryIndex)
        {
            admission = default;
            entry = default;
            entryIndex = -1;
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return false;
            }

            admission.Pass(OrpheusOneShotAdmissionStage.MainThread, true);
            var available = _lifecycle == OrpheusAudioLifecycle.Running && _isBound &&
                            _sourceLeaseHeld && _mixerLeaseHeld;
            if (!admission.Pass(OrpheusOneShotAdmissionStage.Availability, available))
            {
                RejectOneShot(admission.Result);
                return false;
            }

            var keyFound = _catalogSnapshot.CoreIndex.TryGet(key, out entry, out entryIndex);
            if (!admission.Pass(OrpheusOneShotAdmissionStage.Key, keyFound))
            {
                RejectOneShot(admission.Result);
                return false;
            }

            if (!admission.Pass(
                    OrpheusOneShotAdmissionStage.PlaybackKind,
                    entry.Policy.PlaybackKind == expectedPlaybackKind))
            {
                RejectOneShot(admission.Result);
                return false;
            }

            if (!admission.Pass(
                    OrpheusOneShotAdmissionStage.Position,
                    !requiresFinitePosition || position.IsFinite))
            {
                RejectOneShot(admission.Result);
                return false;
            }

            if (!admission.Pass(
                    OrpheusOneShotAdmissionStage.Activation,
                    _activationState.ActivationReady))
            {
                RejectOneShot(admission.Result);
                return false;
            }

            if (!admission.Pass(
                    OrpheusOneShotAdmissionStage.Transport,
                    GetTransportState() == OrpheusTransportState.Active))
            {
                RejectOneShot(admission.Result);
                return false;
            }

            if (!admission.PassLoad(GetAggregateLoadState(entry)))
            {
                RejectOneShot(admission.Result);
                return false;
            }

            var lastAcceptedRealtime = _lastAcceptedRealtime[entryIndex];
            var cooldownReady = lastAcceptedRealtime < 0d ||
                                _lastRealtime - lastAcceptedRealtime >=
                                entry.Policy.Voice.CooldownSeconds;
            if (admission.Pass(OrpheusOneShotAdmissionStage.Cooldown, cooldownReady))
            {
                return true;
            }

            RejectOneShot(admission.Result);
            return false;
        }

        private void RejectOneShot(OrpheusOneShotAdmissionResult result)
        {
            switch (result)
            {
                case OrpheusOneShotAdmissionResult.UnavailableRejected:
                    SaturatingIncrement(ref _unavailableRejectedBits);
                    break;
                case OrpheusOneShotAdmissionResult.InvalidKeyRejected:
                    SaturatingIncrement(ref _invalidKeyRejectedBits);
                    break;
                case OrpheusOneShotAdmissionResult.PlaybackKindRejected:
                    SaturatingIncrement(ref _playbackKindRejectedBits);
                    break;
                case OrpheusOneShotAdmissionResult.InvalidPositionRejected:
                    SaturatingIncrement(ref _invalidPositionRejectedBits);
                    break;
                case OrpheusOneShotAdmissionResult.PreReadyRejected:
                    SaturatingIncrement(ref _preReadyRejectedBits);
                    break;
                case OrpheusOneShotAdmissionResult.SuspendedRejected:
                    SaturatingIncrement(ref _suspendedRejectedBits);
                    break;
                case OrpheusOneShotAdmissionResult.LoadNotReadyRejected:
                    SaturatingIncrement(ref _loadNotReadyRejectedBits);
                    break;
                case OrpheusOneShotAdmissionResult.LoadFailed:
                    SaturatingIncrement(ref _loadFailedBits);
                    break;
                case OrpheusOneShotAdmissionResult.CooldownRejected:
                    SaturatingIncrement(ref _cooldownRejectedBits);
                    break;
                case OrpheusOneShotAdmissionResult.DistanceRejected:
                    SaturatingIncrement(ref _distanceRejectedBits);
                    break;
                case OrpheusOneShotAdmissionResult.PolyphonyRejected:
                    SaturatingIncrement(ref _polyphonyRejectedBits);
                    break;
                case OrpheusOneShotAdmissionResult.PoolCapacityRejected:
                    SaturatingIncrement(ref _poolCapacityRejectedBits);
                    break;
            }
        }

        private void StartAcceptedTransient2D(
            int slotIndex,
            int entryIndex,
            OrpheusCatalogEntry entry)
        {
            if (!ValidateLiveCarrierOrFailClosed())
            {
                return;
            }

            try
            {
                var flattenedClipIndex = OrpheusTransientVariationPolicy.SampleClipIndex(
                    entryIndex,
                    entry.ClipRange.Offset,
                    entry.ClipRange.Count,
                    _shuffleOrder,
                    _shuffleCursors,
                    _shufflePreviousLast,
                    ref _random);
                var clip = _catalogSnapshot.GetClip(flattenedClipIndex);
                var volume = OrpheusTransientVariationPolicy.SampleScalar(
                    entry.Policy.Volume.Minimum,
                    entry.Policy.Volume.Maximum,
                    ref _random);
                var pitch = OrpheusTransientVariationPolicy.SampleScalar(
                    entry.Policy.Pitch.Minimum,
                    entry.Policy.Pitch.Maximum,
                    ref _random);
                var logicalDuration = clip.length / Math.Abs(pitch);
                if (float.IsNaN(logicalDuration) || float.IsInfinity(logicalDuration) ||
                    logicalDuration < 0f)
                {
                    FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                    return;
                }

                if (_transient2DSlots[slotIndex].State == OrpheusOneShotSlotState.PlayingOneShot)
                {
                    ScheduleTransient2DReplacement(
                        slotIndex,
                        entryIndex,
                        entry,
                        clip,
                        volume,
                        pitch,
                        logicalDuration);
                    return;
                }

                var route = _categoryRoutes.Get(entry.Policy.Category);
                if (route == null)
                {
                    FailClosed(OrpheusAudioDisableReason.MixerGroupReferenceInvalid);
                    return;
                }

                if (!TryGetTransient2DSourceOrFailClosed(slotIndex, out var source))
                {
                    return;
                }

                ConfigureTransient2DSource(source, route, clip, volume, pitch, entry);
                source.Play();
                _transient2DSlots[slotIndex].Start(
                    entry.Key,
                    entry.Policy.Category,
                    entry.Policy.Voice.Priority,
                    logicalDuration);
                _transient2DExecutionStates[slotIndex].BaseVolume = volume;
                _transient2DActiveCount++;
                _lastAcceptedRealtime[entryIndex] = _lastRealtime;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private static void ConfigureTransient2DSource(
            AudioSource source,
            AudioMixerGroup route,
            AudioClip clip,
            float volume,
            float pitch,
            OrpheusCatalogEntry entry)
        {
            OrpheusAudioSourceNormalizer.Normalize(source);
            source.clip = clip;
            source.loop = false;
            source.volume = volume;
            source.pitch = pitch;
            source.priority = entry.Policy.Voice.Priority;
            source.outputAudioMixerGroup = route;
            source.spatialBlend = 0f;
        }

        private void AdvanceTransient2D(float activeDelta)
        {
            if (!(activeDelta > 0f))
            {
                return;
            }

            for (var slotIndex = 0; slotIndex < _transient2DSlots.Length; slotIndex++)
            {
                if (_transient2DSlots[slotIndex].State ==
                    OrpheusOneShotSlotState.FadingOut)
                {
                    AdvanceTransient2DFade(slotIndex, activeDelta);
                    if (_lifecycle != OrpheusAudioLifecycle.Running)
                    {
                        return;
                    }

                    continue;
                }

                if (!_transient2DSlots[slotIndex].Advance(activeDelta))
                {
                    continue;
                }

                try
                {
                    if (!TryGetTransient2DSourceOrFailClosed(slotIndex, out var source))
                    {
                        return;
                    }

                    OrpheusAudioSourceNormalizer.Normalize(source);
                    _transient2DSlots[slotIndex].Clear();
                    _transient2DExecutionStates[slotIndex].Clear();
                    _transient2DActiveCount--;
                }
                catch (Exception exception) when (!IsCatastrophic(exception))
                {
                    FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                    return;
                }
            }
        }

        private void ScheduleTransient2DReplacement(
            int slotIndex,
            int entryIndex,
            OrpheusCatalogEntry entry,
            AudioClip clip,
            float volume,
            float pitch,
            float logicalDuration)
        {
            if (!_transient2DSlots[slotIndex].ScheduleReplacement(
                    entry.Key,
                    entry.Policy.Category,
                    entry.Policy.Voice.Priority))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                return;
            }

            _transient2DExecutionStates[slotIndex].SetPending(
                clip,
                volume,
                pitch,
                logicalDuration,
                entryIndex,
                _bootstrapGeneration);
            _fadingCount++;
            _pendingCount++;
            _lastAcceptedRealtime[entryIndex] = _lastRealtime;
            SaturatingIncrement(ref _stolenBits);
        }

        private void AdvanceTransient2DFade(int slotIndex, float activeDelta)
        {
            ref var executionState = ref _transient2DExecutionStates[slotIndex];
            var elapsed = executionState.FadeElapsed + activeDelta;
            executionState.FadeElapsed = elapsed;
            if (elapsed < OrpheusOneShotPolicy.FadeDurationSeconds)
            {
                try
                {
                    if (!TryGetTransient2DSourceOrFailClosed(slotIndex, out var source))
                    {
                        return;
                    }

                    source.volume = executionState.BaseVolume *
                                     (1f - (elapsed / OrpheusOneShotPolicy.FadeDurationSeconds));
                }
                catch (Exception exception) when (!IsCatastrophic(exception))
                {
                    FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                }

                return;
            }

            CompleteTransient2DReplacement(slotIndex);
        }

        private void CompleteTransient2DReplacement(int slotIndex)
        {
            try
            {
                if (!TryGetTransient2DSourceOrFailClosed(slotIndex, out var source))
                {
                    return;
                }

                ref var executionState = ref _transient2DExecutionStates[slotIndex];
                if (!_transient2DSlots[slotIndex].HasPending)
                {
                    CompleteTransient2DStop(slotIndex, source);
                    return;
                }

                var entryIndex = executionState.PendingEntryIndex;
                var pendingKey = _transient2DSlots[slotIndex].PendingKey;
                var entry = default(OrpheusCatalogEntry);
                var currentEntryIndex = -1;
                var valid = _lifecycle == OrpheusAudioLifecycle.Running &&
                            _isBound &&
                            _activationState.ActivationReady &&
                            GetTransportState() == OrpheusTransportState.Active &&
                            executionState.PendingGeneration == _bootstrapGeneration &&
                            _transient2DSlots[slotIndex].State ==
                            OrpheusOneShotSlotState.FadingOut &&
                            _catalogSnapshot.CoreIndex.TryGet(
                                pendingKey,
                                out entry,
                                out currentEntryIndex) &&
                            currentEntryIndex == entryIndex &&
                            GetAggregateLoadState(entry) == OrpheusClipLoadState.Loaded;
                if (!valid)
                {
                    CancelTransient2DReplacement(slotIndex, source);
                    return;
                }

                var route = _categoryRoutes.Get(entry.Policy.Category);
                if (route == null || !ReferenceEquals(route.audioMixer, _mixerIdentity))
                {
                    FailClosed(OrpheusAudioDisableReason.MixerGroupReferenceInvalid);
                    return;
                }

                ConfigureTransient2DSource(
                    source,
                    route,
                    executionState.PendingClip,
                    executionState.PendingVolume,
                    executionState.PendingPitch,
                    entry);
                source.Play();
                _transient2DSlots[slotIndex].StartPending(
                    executionState.PendingDuration);
                executionState.BaseVolume = executionState.PendingVolume;
                executionState.ClearPending();
                _fadingCount--;
                _pendingCount--;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private void CancelTransient2DReplacement(int slotIndex, AudioSource source)
        {
            OrpheusAudioSourceNormalizer.Normalize(source);
            _transient2DSlots[slotIndex].Clear();
            _transient2DExecutionStates[slotIndex].Clear();
            _transient2DActiveCount--;
            _fadingCount--;
            _pendingCount--;
        }

        private void CompleteTransient2DStop(int slotIndex, AudioSource source)
        {
            OrpheusAudioSourceNormalizer.Normalize(source);
            _transient2DSlots[slotIndex].Clear();
            _transient2DExecutionStates[slotIndex].Clear();
            _transient2DActiveCount--;
            _fadingCount--;
        }

        private AudioSource GetTransient2DSource(int slotIndex)
        {
            return _ownedSources[OrpheusAudioSourceBank.Transient2DOffset + slotIndex];
        }

        private bool TryGetTransient2DSourceOrFailClosed(
            int slotIndex,
            out AudioSource source)
        {
            source = GetTransient2DSource(slotIndex);
            if (source != null)
            {
                return true;
            }

            FailClosed(OrpheusAudioDisableReason.OwnedSourceDestroyed);
            return false;
        }

        private void ClearTransient2DSlots()
        {
            for (var slotIndex = 0; slotIndex < _transient2DSlots.Length; slotIndex++)
            {
                _transient2DSlots[slotIndex].Clear();
                _transient2DExecutionStates[slotIndex].Clear();
            }

            _transient2DActiveCount = 0;
        }
    }
}
