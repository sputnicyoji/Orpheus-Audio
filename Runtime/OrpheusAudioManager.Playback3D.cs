using System;
using Orpheus.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio
{
    public sealed partial class OrpheusAudioManager
    {
        public void PlayAt(OrpheusAudioKey key, Vector3 position)
        {
            var requestPosition = ToPosition3(position);
            if (!TryPassOneShotAdmissionPrefix(
                    key,
                    OrpheusPlaybackKind.OneShot3D,
                    requestPosition,
                    true,
                    out var admission,
                    out var entry,
                    out var entryIndex))
            {
                return;
            }

            var maximumDistanceSquared = OrpheusOneShotPolicy.CalculateMaximumDistanceSquared(
                entry.Policy.SpatialAttenuation.MaximumDistance);
            var spatialState = new OrpheusSpatialOneShotState(
                requestPosition,
                maximumDistanceSquared);
            var listenerPosition = ToPosition3(_cachedListenerPosition);
            var requestDistanceSquared = OrpheusOneShotPolicy.CalculateSquaredDistance(
                requestPosition,
                listenerPosition);
            if (!admission.Pass(
                    OrpheusOneShotAdmissionStage.Distance,
                    OrpheusOneShotPolicy.IsWithinMaximumDistance(
                        requestDistanceSquared,
                        maximumDistanceSquared)))
            {
                RejectOneShot(admission.Result);
                return;
            }

            OrpheusOneShotPolicy.Overwrite3DDistanceRatios(
                _transient3DSlots,
                listenerPosition,
                _transient3DDistanceRatios);
            var futureKeyCount = OrpheusOneShotPolicy.CountFutureKey(_transient3DSlots, key);
            var atPolyphonyCap = futureKeyCount >= entry.Policy.Voice.PolyphonyCap;
            var slotIndex = atPolyphonyCap
                ? OrpheusOneShotPolicy.FindSameKeyVictim(_transient3DSlots, key)
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
                slotIndex = OrpheusOneShotPolicy.Find3DAdmissionSlot(
                    _transient3DSlots,
                    entry.Policy.Voice.Priority,
                    _transient3DDistanceRatios);
            }

            if (!admission.Pass(
                    OrpheusOneShotAdmissionStage.PoolCapacity,
                    slotIndex >= 0))
            {
                RejectOneShot(admission.Result);
                return;
            }

            StartAcceptedTransient3D(
                slotIndex,
                entryIndex,
                entry,
                spatialState);
        }

        private void StartAcceptedTransient3D(
            int slotIndex,
            int entryIndex,
            OrpheusCatalogEntry entry,
            OrpheusSpatialOneShotState spatialState)
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

                if (_transient3DSlots[slotIndex].State == OrpheusOneShotSlotState.PlayingOneShot)
                {
                    ScheduleTransient3DReplacement(
                        slotIndex,
                        entryIndex,
                        entry,
                        clip,
                        volume,
                        pitch,
                        logicalDuration,
                        spatialState);
                    return;
                }

                var route = _categoryRoutes.Get(entry.Policy.Category);
                if (route == null)
                {
                    FailClosed(OrpheusAudioDisableReason.MixerGroupReferenceInvalid);
                    return;
                }

                if (!TryGetTransient3DSourceOrFailClosed(slotIndex, out var source))
                {
                    return;
                }

                if (!TryConfigureTransient3DSource(
                    source,
                    route,
                    clip,
                    volume,
                    pitch,
                    entry,
                    spatialState.Position))
                {
                    FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                    return;
                }

                source.Play();
                _transient3DSlots[slotIndex].Start(
                    entry.Key,
                    entry.Policy.Category,
                    entry.Policy.Voice.Priority,
                    logicalDuration,
                    spatialState);
                _transient3DExecutionStates[slotIndex].BaseVolume = volume;
                _transient3DActiveCount++;
                _lastAcceptedRealtime[entryIndex] = _lastRealtime;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private static bool TryConfigureTransient3DSource(
            AudioSource source,
            AudioMixerGroup route,
            AudioClip clip,
            float volume,
            float pitch,
            OrpheusCatalogEntry entry,
            OrpheusPosition3 position)
        {
            if (!TryToUnityRolloffMode(
                entry.Policy.SpatialAttenuation.RolloffMode,
                out var rolloffMode))
            {
                return false;
            }

            OrpheusAudioSourceNormalizer.Normalize(source);
            source.transform.position = new Vector3(position.X, position.Y, position.Z);
            source.clip = clip;
            source.outputAudioMixerGroup = route;
            source.volume = volume;
            source.pitch = pitch;
            source.priority = entry.Policy.Voice.Priority;
            source.spatialBlend = 1f;
            source.minDistance = entry.Policy.SpatialAttenuation.MinimumDistance;
            source.maxDistance = entry.Policy.SpatialAttenuation.MaximumDistance;
            source.rolloffMode = rolloffMode;
            return true;
        }

        private void AdvanceTransient3D(float activeDelta)
        {
            if (!(activeDelta > 0f))
            {
                return;
            }

            for (var slotIndex = 0; slotIndex < _transient3DSlots.Length; slotIndex++)
            {
                if (_transient3DSlots[slotIndex].State == OrpheusOneShotSlotState.FadingOut)
                {
                    AdvanceTransient3DFade(slotIndex, activeDelta);
                    if (_lifecycle != OrpheusAudioLifecycle.Running)
                    {
                        return;
                    }

                    continue;
                }

                if (!_transient3DSlots[slotIndex].Advance(activeDelta))
                {
                    continue;
                }

                try
                {
                    if (!TryGetTransient3DSourceOrFailClosed(slotIndex, out var source))
                    {
                        return;
                    }

                    OrpheusAudioSourceNormalizer.Normalize(source);
                    _transient3DSlots[slotIndex].Clear();
                    _transient3DExecutionStates[slotIndex].Clear();
                    _transient3DActiveCount--;
                }
                catch (Exception exception) when (!IsCatastrophic(exception))
                {
                    FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                    return;
                }
            }
        }

        private void ScheduleTransient3DReplacement(
            int slotIndex,
            int entryIndex,
            OrpheusCatalogEntry entry,
            AudioClip clip,
            float volume,
            float pitch,
            float logicalDuration,
            OrpheusSpatialOneShotState spatialState)
        {
            if (!_transient3DSlots[slotIndex].ScheduleReplacement(
                    entry.Key,
                    entry.Policy.Category,
                    entry.Policy.Voice.Priority,
                    spatialState))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                return;
            }

            _transient3DExecutionStates[slotIndex].SetPending(
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

        private void AdvanceTransient3DFade(int slotIndex, float activeDelta)
        {
            ref var executionState = ref _transient3DExecutionStates[slotIndex];
            var elapsed = executionState.FadeElapsed + activeDelta;
            executionState.FadeElapsed = elapsed;
            if (elapsed < OrpheusOneShotPolicy.FadeDurationSeconds)
            {
                try
                {
                    if (!TryGetTransient3DSourceOrFailClosed(slotIndex, out var source))
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

            CompleteTransient3DReplacement(slotIndex);
        }

        private void CompleteTransient3DReplacement(int slotIndex)
        {
            try
            {
                if (!TryGetTransient3DSourceOrFailClosed(slotIndex, out var source))
                {
                    return;
                }

                ref var executionState = ref _transient3DExecutionStates[slotIndex];
                if (!_transient3DSlots[slotIndex].HasPending)
                {
                    CompleteTransient3DStop(slotIndex, source);
                    return;
                }

                var slot = _transient3DSlots[slotIndex];
                var entryIndex = executionState.PendingEntryIndex;
                var pendingKey = slot.PendingKey;
                var entry = default(OrpheusCatalogEntry);
                var currentEntryIndex = -1;
                var valid = _lifecycle == OrpheusAudioLifecycle.Running &&
                            _isBound &&
                            _activationState.ActivationReady &&
                            GetTransportState() == OrpheusTransportState.Active &&
                            executionState.PendingGeneration == _bootstrapGeneration &&
                            slot.State == OrpheusOneShotSlotState.FadingOut &&
                            _catalogSnapshot.CoreIndex.TryGet(
                                pendingKey,
                                out entry,
                                out currentEntryIndex) &&
                            currentEntryIndex == entryIndex &&
                            GetAggregateLoadState(entry) == OrpheusClipLoadState.Loaded;
                if (!valid)
                {
                    CancelTransient3DReplacement(slotIndex, source);
                    return;
                }

                var route = _categoryRoutes.Get(entry.Policy.Category);
                if (route == null || !ReferenceEquals(route.audioMixer, _mixerIdentity))
                {
                    FailClosed(OrpheusAudioDisableReason.MixerGroupReferenceInvalid);
                    return;
                }

                var pendingPosition = slot.PendingPosition;
                if (!TryConfigureTransient3DSource(
                    source,
                    route,
                    executionState.PendingClip,
                    executionState.PendingVolume,
                    executionState.PendingPitch,
                    entry,
                    pendingPosition))
                {
                    FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                    return;
                }

                source.Play();
                _transient3DSlots[slotIndex].StartPending(executionState.PendingDuration);
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

        private void CancelTransient3DReplacement(int slotIndex, AudioSource source)
        {
            OrpheusAudioSourceNormalizer.Normalize(source);
            _transient3DSlots[slotIndex].Clear();
            _transient3DExecutionStates[slotIndex].Clear();
            _transient3DActiveCount--;
            _fadingCount--;
            _pendingCount--;
        }

        private void CompleteTransient3DStop(int slotIndex, AudioSource source)
        {
            OrpheusAudioSourceNormalizer.Normalize(source);
            _transient3DSlots[slotIndex].Clear();
            _transient3DExecutionStates[slotIndex].Clear();
            _transient3DActiveCount--;
            _fadingCount--;
        }

        private AudioSource GetTransient3DSource(int slotIndex)
        {
            return _ownedSources[slotIndex];
        }

        private bool TryGetTransient3DSourceOrFailClosed(
            int slotIndex,
            out AudioSource source)
        {
            source = GetTransient3DSource(slotIndex);
            if (source != null)
            {
                return true;
            }

            FailClosed(OrpheusAudioDisableReason.OwnedSourceDestroyed);
            return false;
        }

        private void ClearTransient3DSlots()
        {
            for (var slotIndex = 0; slotIndex < _transient3DSlots.Length; slotIndex++)
            {
                _transient3DSlots[slotIndex].Clear();
                _transient3DExecutionStates[slotIndex].Clear();
                _transient3DDistanceRatios[slotIndex] = 0d;
            }

            _transient3DActiveCount = 0;
        }

        private void RefreshTransient3DWorldPositions()
        {
            // PlayAt stores a fixed world position. Owned Sources may sit under a
            // moving SourceBank/Host transform, so LateTick re-applies world space
            // to keep the admission-time position stable for the voice lifetime.
            for (var slotIndex = 0; slotIndex < _transient3DSlots.Length; slotIndex++)
            {
                var slot = _transient3DSlots[slotIndex];
                if (slot.State == OrpheusOneShotSlotState.Free)
                {
                    continue;
                }

                try
                {
                    if (!TryGetTransient3DSourceOrFailClosed(slotIndex, out var source))
                    {
                        return;
                    }

                    var position = slot.Position;
                    source.transform.position = new Vector3(position.X, position.Y, position.Z);
                }
                catch (Exception exception) when (!IsCatastrophic(exception))
                {
                    FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                    return;
                }
            }
        }

        private static OrpheusPosition3 ToPosition3(Vector3 position)
        {
            return new OrpheusPosition3(position.x, position.y, position.z);
        }

        private static bool TryToUnityRolloffMode(
            OrpheusRolloffMode rolloffMode,
            out AudioRolloffMode unityRolloffMode)
        {
            switch (rolloffMode)
            {
                case OrpheusRolloffMode.Logarithmic:
                    unityRolloffMode = AudioRolloffMode.Logarithmic;
                    return true;
                case OrpheusRolloffMode.Linear:
                    unityRolloffMode = AudioRolloffMode.Linear;
                    return true;
                default:
                    unityRolloffMode = default;
                    return false;
            }
        }
    }
}
