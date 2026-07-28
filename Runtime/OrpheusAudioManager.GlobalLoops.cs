using System;
using Orpheus.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio
{
    public sealed partial class OrpheusAudioManager
    {
        public void PlayLoop(OrpheusAudioKey key)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            if (!_catalogSnapshot.CoreIndex.TryGet(key, out var catalogEntry, out var catalogEntryIndex))
            {
                SaturatingIncrement(ref _invalidKeyRejectedBits);
                return;
            }

            if (catalogEntry.Policy.PlaybackKind != OrpheusPlaybackKind.GlobalLoop2D)
            {
                SaturatingIncrement(ref _playbackKindRejectedBits);
                return;
            }

            var decision = OrpheusAudioGlobalLoopPolicy.EvaluatePlay(_globalLoops, key);
            if (decision.Action == OrpheusGlobalLoopAction.None)
            {
                return;
            }

            if (decision.Action == OrpheusGlobalLoopAction.RejectFull)
            {
                SaturatingIncrement(ref _loopRegistryFullBits);
                return;
            }

            ref var entry = ref _globalLoops[decision.EntryIndex];
            switch (decision.Action)
            {
                case OrpheusGlobalLoopAction.ReserveAndLoad:
                    ReserveGlobalLoop(
                        ref entry,
                        key,
                        catalogEntryIndex,
                        catalogEntry.Policy.Volume.Minimum);
                    RequestGlobalLoopLoad(decision.EntryIndex);
                    break;
                case OrpheusGlobalLoopAction.RetryLoad:
                    RetryGlobalLoopLoad(ref entry, catalogEntryIndex);
                    RequestGlobalLoopLoad(decision.EntryIndex);
                    break;
                case OrpheusGlobalLoopAction.Reactivate:
                    ReactivateGlobalLoop(ref entry);
                    break;
            }
        }

        public void StopLoop(OrpheusAudioKey key)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            if (!_catalogSnapshot.TryGetEntry(key, out var catalogEntry))
            {
                return;
            }

            if (catalogEntry.Policy.PlaybackKind != OrpheusPlaybackKind.GlobalLoop2D)
            {
                SaturatingIncrement(ref _playbackKindRejectedBits);
                return;
            }

            var decision = OrpheusAudioGlobalLoopPolicy.EvaluateStop(_globalLoops, key);
            ApplyGlobalLoopStopDecision(decision);
        }

        private byte GlobalLoopActiveCount
        {
            get
            {
                byte count = 0;
                for (var entryIndex = 0; entryIndex < _globalLoops.Length; entryIndex++)
                {
                    var phase = _globalLoops[entryIndex].Phase;
                    if (phase == OrpheusGlobalLoopPhase.Playing ||
                        phase == OrpheusGlobalLoopPhase.Stopping)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private static void ReserveGlobalLoop(
            ref OrpheusGlobalLoopEntryState entry,
            OrpheusAudioKey key,
            int catalogEntryIndex,
            float authoredVolume)
        {
            entry.Generation = OrpheusAudioGlobalLoopPolicy.AdvanceGeneration(entry.Generation);
            entry.Key = key;
            entry.Phase = OrpheusGlobalLoopPhase.Loading;
            entry.CatalogEntryIndex = catalogEntryIndex;
            entry.LoadGeneration = entry.Generation;
            entry.FadeGeneration = 0u;
            entry.AuthoredVolume = authoredVolume;
            entry.Gain = 0f;
            entry.FadeStartGain = 0f;
            entry.FadeElapsed = 0f;
            entry.RequestedActive = true;
            entry.LoadRequestIssued = false;
            entry.FailureReported = false;
        }

        private void RetryGlobalLoopLoad(
            ref OrpheusGlobalLoopEntryState entry,
            int catalogEntryIndex)
        {
            CancelLoadObservation(entry.CatalogEntryIndex);
            entry.Generation = OrpheusAudioGlobalLoopPolicy.AdvanceGeneration(entry.Generation);
            entry.Phase = OrpheusGlobalLoopPhase.Loading;
            entry.CatalogEntryIndex = catalogEntryIndex;
            entry.LoadGeneration = entry.Generation;
            entry.FadeGeneration = 0u;
            entry.RequestedActive = true;
            entry.LoadRequestIssued = false;
            entry.FailureReported = false;
        }

        private static void ReactivateGlobalLoop(ref OrpheusGlobalLoopEntryState entry)
        {
            entry.Generation = OrpheusAudioGlobalLoopPolicy.AdvanceGeneration(entry.Generation);
            entry.RequestedActive = true;
            entry.FadeGeneration = entry.Generation;
            entry.FadeStartGain = entry.Gain;
            entry.FadeElapsed = 0f;
        }

        private static void BeginGlobalLoopStop(ref OrpheusGlobalLoopEntryState entry)
        {
            entry.Generation = OrpheusAudioGlobalLoopPolicy.AdvanceGeneration(entry.Generation);
            entry.Phase = OrpheusGlobalLoopPhase.Stopping;
            entry.RequestedActive = false;
            entry.FadeGeneration = entry.Generation;
            entry.FadeStartGain = entry.Gain;
            entry.FadeElapsed = 0f;
        }

        private void ApplyGlobalLoopStopDecision(OrpheusGlobalLoopDecision decision)
        {
            if (decision.Action == OrpheusGlobalLoopAction.None)
            {
                return;
            }

            ref var entry = ref _globalLoops[decision.EntryIndex];
            switch (decision.Action)
            {
                case OrpheusGlobalLoopAction.ClearUnstarted:
                    CancelLoadObservation(entry.CatalogEntryIndex);
                    ClearGlobalLoopEntry(ref entry);
                    break;
                case OrpheusGlobalLoopAction.BeginStop:
                    BeginGlobalLoopStop(ref entry);
                    break;
            }
        }

        private void RequestGlobalLoopLoad(int slotIndex)
        {
            ref var loop = ref _globalLoops[slotIndex];
            if (loop.Phase != OrpheusGlobalLoopPhase.Loading ||
                !loop.RequestedActive || loop.LoadRequestIssued ||
                loop.LoadGeneration != loop.Generation)
            {
                return;
            }

            if (!_catalogSnapshot.CoreIndex.TryGet(loop.Key, out var catalogEntry, out var catalogEntryIndex) ||
                catalogEntryIndex != loop.CatalogEntryIndex)
            {
                MarkGlobalLoopLoadFailed(ref loop);
                return;
            }

            var realtime = _runtimeHost.ReadRealtime();
            if (_isBound && realtime < _lastRealtime)
            {
                realtime = _lastRealtime;
            }

            var aggregate = GetAggregateLoadState(catalogEntry);
            if (aggregate == OrpheusClipLoadState.Loaded)
            {
                CancelLoadObservation(catalogEntryIndex);
                return;
            }

            if (aggregate == OrpheusClipLoadState.Loading)
            {
                EnsureLoadObservation(catalogEntryIndex, realtime);
                loop.LoadRequestIssued = true;
                return;
            }

            if (aggregate != OrpheusClipLoadState.Unloaded &&
                aggregate != OrpheusClipLoadState.Failed)
            {
                MarkGlobalLoopLoadFailed(ref loop);
                return;
            }

            var requestAccepted = false;
            var requestFailed = false;
            var clipEnd = catalogEntry.ClipRange.Offset + catalogEntry.ClipRange.Count;
            for (var clipIndex = catalogEntry.ClipRange.Offset; clipIndex < clipEnd; clipIndex++)
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

            loop.LoadRequestIssued = true;
            if (requestAccepted)
            {
                RestartLoadObservation(catalogEntryIndex, realtime);
            }

            if (requestFailed || !requestAccepted)
            {
                MarkGlobalLoopLoadFailed(ref loop);
            }
        }

        private void TickGlobalLoops(double realtime, float activeDelta)
        {
            for (var slotIndex = 0; slotIndex < _globalLoops.Length; slotIndex++)
            {
                if (_globalLoops[slotIndex].Phase == OrpheusGlobalLoopPhase.Loading)
                {
                    PollGlobalLoopLoading(slotIndex, realtime);
                    if (_lifecycle != OrpheusAudioLifecycle.Running)
                    {
                        return;
                    }
                }
            }

            if (!(activeDelta > 0f) || !HasStoppingGlobalLoop())
            {
                return;
            }

            var carrierReason = ValidateCarrier();
            if (carrierReason != OrpheusAudioDisableReason.None)
            {
                FailClosed(carrierReason);
                return;
            }

            for (var slotIndex = 0; slotIndex < _globalLoops.Length; slotIndex++)
            {
                if (_globalLoops[slotIndex].Phase == OrpheusGlobalLoopPhase.Stopping)
                {
                    AdvanceGlobalLoopFade(slotIndex, activeDelta);
                    if (_lifecycle != OrpheusAudioLifecycle.Running)
                    {
                        return;
                    }
                }
            }
        }

        private void PollGlobalLoopLoading(int slotIndex, double realtime)
        {
            ref var loop = ref _globalLoops[slotIndex];
            var scheduledGeneration = loop.LoadGeneration;
            var scheduledKey = loop.Key;
            if (loop.Phase != OrpheusGlobalLoopPhase.Loading || !loop.RequestedActive)
            {
                return;
            }

            if (!_catalogSnapshot.CoreIndex.TryGet(scheduledKey, out var catalogEntry, out var catalogEntryIndex) ||
                catalogEntryIndex != loop.CatalogEntryIndex)
            {
                MarkGlobalLoopLoadFailed(ref loop);
                return;
            }

            var aggregate = GetAggregateLoadState(catalogEntry);
            if (aggregate == OrpheusClipLoadState.Failed ||
                aggregate == OrpheusClipLoadState.Invalid)
            {
                if (loop.LoadRequestIssued)
                {
                    MarkGlobalLoopLoadFailed(ref loop);
                }
                else
                {
                    RequestGlobalLoopLoad(slotIndex);
                }

                return;
            }

            if (aggregate == OrpheusClipLoadState.Unloaded)
            {
                RequestGlobalLoopLoad(slotIndex);
                return;
            }

            if (aggregate == OrpheusClipLoadState.Loading)
            {
                EnsureLoadObservation(catalogEntryIndex, realtime);
                return;
            }

            if (_loadEntryActivePositions[catalogEntryIndex] >= 0)
            {
                CancelLoadObservation(catalogEntryIndex);
            }

            var playbackReady = IsPersistentPlaybackReady();
            if (!OrpheusAudioGlobalLoopPolicy.CanStart(
                    scheduledGeneration,
                    loop.Generation,
                    scheduledKey,
                    loop.Key,
                    loop.Phase,
                    loop.RequestedActive,
                    true,
                    playbackReady))
            {
                return;
            }

            TryStartLoadedGlobalLoop(slotIndex, scheduledGeneration, scheduledKey, catalogEntry);
        }

        private void TryStartLoadedGlobalLoop(
            int slotIndex,
            uint scheduledGeneration,
            OrpheusAudioKey scheduledKey,
            OrpheusCatalogEntry catalogEntry)
        {
            var carrierReason = ValidateCarrier();
            if (carrierReason != OrpheusAudioDisableReason.None)
            {
                FailClosed(carrierReason);
                return;
            }

            try
            {
                var route = _categoryRoutes.Get(catalogEntry.Policy.Category);
                if (route == null)
                {
                    FailClosed(OrpheusAudioDisableReason.MixerGroupReferenceInvalid);
                    return;
                }

                ref var loop = ref _globalLoops[slotIndex];
                if (!OrpheusAudioGlobalLoopPolicy.CanStart(
                        scheduledGeneration,
                        loop.Generation,
                        scheduledKey,
                        loop.Key,
                        loop.Phase,
                        loop.RequestedActive,
                        GetAggregateLoadState(catalogEntry) == OrpheusClipLoadState.Loaded,
                            IsPersistentPlaybackReady()))
                {
                    return;
                }

                var source = GetGlobalLoopSource(slotIndex);
                if (source == null)
                {
                    FailClosed(OrpheusAudioDisableReason.OwnedSourceDestroyed);
                    return;
                }

                var clip = _catalogSnapshot.GetClip(catalogEntry.ClipRange.Offset);
                ConfigureGlobalLoopSource(
                    source,
                    route,
                    clip,
                    catalogEntry.Policy.Voice.Priority,
                    loop.AuthoredVolume);
                source.Play();
                loop.Phase = OrpheusGlobalLoopPhase.Playing;
                loop.Gain = 1f;
                loop.FadeStartGain = 1f;
                loop.FadeElapsed = 0f;
                loop.FadeGeneration = 0u;
                loop.LoadGeneration = 0u;
                loop.LoadRequestIssued = false;
                loop.FailureReported = false;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private static void ConfigureGlobalLoopSource(
            AudioSource source,
            AudioMixerGroup route,
            AudioClip clip,
            byte priority,
            float authoredVolume)
        {
            OrpheusAudioSourceNormalizer.Normalize(source);
            source.clip = clip;
            source.loop = true;
            source.volume = authoredVolume;
            source.pitch = 1f;
            source.priority = priority;
            source.outputAudioMixerGroup = route;
            source.spatialBlend = 0f;
        }

        private void MarkGlobalLoopLoadFailed(ref OrpheusGlobalLoopEntryState loop)
        {
            if (!loop.FailureReported)
            {
                SaturatingIncrement(ref _loadFailedBits);
                loop.FailureReported = true;
            }

            CancelLoadObservation(loop.CatalogEntryIndex);
            loop.Phase = OrpheusGlobalLoopPhase.Failed;
        }

        private bool HasStoppingGlobalLoop()
        {
            for (var slotIndex = 0; slotIndex < _globalLoops.Length; slotIndex++)
            {
                if (_globalLoops[slotIndex].Phase == OrpheusGlobalLoopPhase.Stopping)
                {
                    return true;
                }
            }

            return false;
        }

        private void AdvanceGlobalLoopFade(int slotIndex, float activeDelta)
        {
            ref var loop = ref _globalLoops[slotIndex];
            var scheduledGeneration = loop.FadeGeneration;
            var scheduledKey = loop.Key;
            loop.FadeElapsed += activeDelta;
            var fade = OrpheusAudioGlobalLoopPolicy.EvaluateFade(
                loop.FadeStartGain,
                loop.RequestedActive,
                loop.FadeElapsed);

            if (fade.Complete)
            {
                if (loop.RequestedActive)
                {
                    if (!IsCurrentGlobalLoopFade(
                            scheduledGeneration,
                            scheduledKey,
                            loop))
                    {
                        return;
                    }
                }
                else if (!OrpheusAudioGlobalLoopPolicy.CanRelease(
                             scheduledGeneration,
                             loop.Generation,
                             scheduledKey,
                             loop.Key,
                             loop.Phase,
                             loop.RequestedActive))
                {
                    return;
                }
            }
            else if (!IsCurrentGlobalLoopFade(
                         scheduledGeneration,
                         scheduledKey,
                         loop))
            {
                return;
            }

            try
            {
                var source = GetGlobalLoopSource(slotIndex);
                if (source == null)
                {
                    FailClosed(OrpheusAudioDisableReason.OwnedSourceDestroyed);
                    return;
                }

                source.volume = fade.Gain * loop.AuthoredVolume;
                loop.Gain = fade.Gain;
                if (!fade.Complete)
                {
                    return;
                }

                if (loop.RequestedActive)
                {
                    source.volume = loop.AuthoredVolume;
                    loop.Gain = 1f;
                    loop.Phase = OrpheusGlobalLoopPhase.Playing;
                    loop.FadeStartGain = 1f;
                    loop.FadeElapsed = 0f;
                    loop.FadeGeneration = 0u;
                    return;
                }

                source.volume = 0f;
                OrpheusAudioSourceNormalizer.Normalize(source);
                ClearGlobalLoopEntry(ref loop);
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private static bool IsCurrentGlobalLoopFade(
            uint scheduledGeneration,
            OrpheusAudioKey scheduledKey,
            OrpheusGlobalLoopEntryState entry)
        {
            return scheduledGeneration != 0u &&
                   scheduledGeneration == entry.Generation &&
                   scheduledKey.IsValid && scheduledKey == entry.Key &&
                   entry.Phase == OrpheusGlobalLoopPhase.Stopping;
        }

        private static void ClearGlobalLoopEntry(
            ref OrpheusGlobalLoopEntryState entry,
            bool advanceGeneration = true)
        {
            var generation = advanceGeneration
                ? OrpheusAudioGlobalLoopPolicy.AdvanceGeneration(entry.Generation)
                : entry.Generation;
            entry.Initialize();
            entry.Generation = generation;
        }

        private void ClearGlobalLoopRegistryState(bool advanceGenerations = true)
        {
            for (var slotIndex = 0; slotIndex < _globalLoops.Length; slotIndex++)
            {
                ClearGlobalLoopEntry(ref _globalLoops[slotIndex], advanceGenerations);
            }
        }

        private AudioSource GetGlobalLoopSource(int slotIndex)
        {
            return _ownedSources[OrpheusAudioSourceBank.GlobalLoopOffset + slotIndex];
        }
    }
}
