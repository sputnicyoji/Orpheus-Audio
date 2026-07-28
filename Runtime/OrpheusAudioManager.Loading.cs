using System;
using Orpheus.Audio.Core;

namespace Orpheus.Audio
{
    public sealed partial class OrpheusAudioManager
    {
        public OrpheusAudioPrepareResult Prepare(OrpheusAudioKey key)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return OrpheusAudioPrepareResult.Invalid;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return OrpheusAudioPrepareResult.Invalid;
            }

            if (!_catalogSnapshot.CoreIndex.TryGet(key, out var entry, out var entryIndex))
            {
                return OrpheusAudioPrepareResult.RejectedInvalidKey;
            }

            if (entry.Policy.LoadPolicy != OrpheusLoadPolicy.ExplicitTransient)
            {
                return OrpheusAudioPrepareResult.RejectedPolicy;
            }

            var intentRealtime = _runtimeHost.ReadRealtime();
            if (_isBound && intentRealtime < _lastRealtime)
            {
                intentRealtime = _lastRealtime;
            }
            var aggregation = default(OrpheusClipLoadAggregation);
            var requestAttempted = false;
            var requestAccepted = false;
            var requestFailed = false;
            var loadingObserved = false;
            var clipEnd = entry.ClipRange.Offset + entry.ClipRange.Count;

            for (var clipIndex = entry.ClipRange.Offset; clipIndex < clipEnd; clipIndex++)
            {
                if (!TryReadClipLoadState(clipIndex, out var state))
                {
                    requestFailed = true;
                    aggregation.Include(OrpheusClipLoadState.Invalid);
                    continue;
                }

                aggregation.Include(state);
                if (state == OrpheusClipLoadState.Loading)
                {
                    loadingObserved = true;
                }

                if (state != OrpheusClipLoadState.Unloaded &&
                    state != OrpheusClipLoadState.Failed)
                {
                    if (state == OrpheusClipLoadState.Invalid)
                    {
                        requestFailed = true;
                    }

                    continue;
                }

                requestAttempted = true;
                if (TryRequestClipLoad(clipIndex))
                {
                    requestAccepted = true;
                }
                else
                {
                    requestFailed = true;
                }
            }

            if (requestAccepted)
            {
                RestartLoadObservation(entryIndex, intentRealtime);
            }
            else if (loadingObserved)
            {
                EnsureLoadObservation(entryIndex, intentRealtime);
            }

            if (requestFailed)
            {
                return OrpheusAudioPrepareResult.FailedToRequest;
            }

            if (requestAttempted)
            {
                return OrpheusAudioPrepareResult.LoadRequested;
            }

            var aggregateState = aggregation.Result;
            if (aggregateState == OrpheusClipLoadState.Loading)
            {
                EnsureLoadObservation(entryIndex, intentRealtime);
                return OrpheusAudioPrepareResult.AlreadyLoading;
            }

            return aggregateState == OrpheusClipLoadState.Loaded
                ? OrpheusAudioPrepareResult.AlreadyLoaded
                : OrpheusAudioPrepareResult.FailedToRequest;
        }

        public OrpheusClipLoadState GetLoadState(OrpheusAudioKey key)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return OrpheusClipLoadState.Invalid;
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running ||
                !_catalogSnapshot.TryGetEntry(key, out var entry))
            {
                return OrpheusClipLoadState.Invalid;
            }

            return GetAggregateLoadState(entry);
        }

        internal void PreloadBootstrapTransient(double realtime)
        {
            var coreIndex = _catalogSnapshot.CoreIndex;
            for (var entryIndex = 0; entryIndex < coreIndex.Count; entryIndex++)
            {
                var entry = coreIndex.GetAt(entryIndex);
                if (entry.Policy.LoadPolicy != OrpheusLoadPolicy.BootstrapTransient)
                {
                    continue;
                }

                var requestAccepted = false;
                var loadingObserved = false;
                var clipEnd = entry.ClipRange.Offset + entry.ClipRange.Count;
                for (var clipIndex = entry.ClipRange.Offset; clipIndex < clipEnd; clipIndex++)
                {
                    if (!TryReadClipLoadState(clipIndex, out var state))
                    {
                        continue;
                    }

                    if (state == OrpheusClipLoadState.Loaded)
                    {
                        continue;
                    }

                    if (state == OrpheusClipLoadState.Loading)
                    {
                        loadingObserved = true;
                    }

                    if (TryRequestClipLoad(clipIndex))
                    {
                        requestAccepted = true;
                    }
                }

                if (requestAccepted)
                {
                    RestartLoadObservation(entryIndex, realtime);
                }
                else if (loadingObserved)
                {
                    EnsureLoadObservation(entryIndex, realtime);
                }
            }
        }

        private OrpheusClipLoadState GetAggregateLoadState(OrpheusCatalogEntry entry)
        {
            var aggregation = default(OrpheusClipLoadAggregation);
            var clipEnd = entry.ClipRange.Offset + entry.ClipRange.Count;
            for (var clipIndex = entry.ClipRange.Offset; clipIndex < clipEnd; clipIndex++)
            {
                if (!TryReadClipLoadState(clipIndex, out var state))
                {
                    return OrpheusClipLoadState.Invalid;
                }

                aggregation.Include(state);
            }

            return aggregation.Result;
        }

        private void TickLoadObservations(double realtime)
        {
            var activePosition = 0;
            while (activePosition < _activeLoadObservationCount)
            {
                var entryIndex = _activeLoadEntryIndices[activePosition];
                var entry = _catalogSnapshot.CoreIndex.GetAt(entryIndex);
                var aggregateState = GetAggregateLoadState(entry);
                if (_loadStallTrackers[entryIndex].Observe(aggregateState, realtime))
                {
                    SaturatingIncrement(ref _loadStalledBits);
                }

                if (_loadStallTrackers[entryIndex].IsTracking)
                {
                    activePosition++;
                }
                else
                {
                    RemoveLoadObservation(entryIndex, activePosition);
                }
            }
        }

        private bool TryReadClipLoadState(int clipIndex, out OrpheusClipLoadState state)
        {
            try
            {
                state = _clipReadiness.GetLoadState(clipIndex);
                return true;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                SaturatingIncrement(ref _unexpectedExceptionBits);
                state = OrpheusClipLoadState.Invalid;
                return false;
            }
        }

        private bool TryRequestClipLoad(int clipIndex)
        {
            try
            {
                return _clipReadiness.RequestLoad(clipIndex);
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                SaturatingIncrement(ref _unexpectedExceptionBits);
                return false;
            }
        }

        private void RestartLoadObservation(int entryIndex, double realtime)
        {
            var activePosition = _loadEntryActivePositions[entryIndex];
            if (activePosition >= 0)
            {
                _loadStallTrackers[entryIndex].BeginGeneration(realtime);
                return;
            }

            AddLoadObservation(entryIndex, realtime);
        }

        private void EnsureLoadObservation(int entryIndex, double realtime)
        {
            if (_loadEntryActivePositions[entryIndex] >= 0)
            {
                return;
            }

            AddLoadObservation(entryIndex, realtime);
        }

        private void AddLoadObservation(int entryIndex, double realtime)
        {
            _loadStallTrackers[entryIndex].BeginGeneration(realtime);
            var activePosition = _activeLoadObservationCount;
            _activeLoadEntryIndices[activePosition] = entryIndex;
            _loadEntryActivePositions[entryIndex] = activePosition;
            _activeLoadObservationCount++;
        }

        private void RemoveLoadObservation(int entryIndex, int activePosition)
        {
            var lastPosition = _activeLoadObservationCount - 1;
            var movedEntryIndex = _activeLoadEntryIndices[lastPosition];
            _activeLoadObservationCount = lastPosition;
            _loadEntryActivePositions[entryIndex] = -1;
            _activeLoadEntryIndices[lastPosition] = 0;

            if (activePosition == lastPosition)
            {
                return;
            }

            _activeLoadEntryIndices[activePosition] = movedEntryIndex;
            _loadEntryActivePositions[movedEntryIndex] = activePosition;
        }

        private void CancelLoadObservation(int entryIndex)
        {
            if ((uint)entryIndex >= (uint)_loadEntryActivePositions.Length)
            {
                return;
            }

            var activePosition = _loadEntryActivePositions[entryIndex];
            if (activePosition < 0)
            {
                return;
            }

            _loadStallTrackers[entryIndex] = default;
            RemoveLoadObservation(entryIndex, activePosition);
        }

        private void ClearLoadObservations()
        {
            for (var activePosition = 0;
                 activePosition < _activeLoadObservationCount;
                 activePosition++)
            {
                var entryIndex = _activeLoadEntryIndices[activePosition];
                _loadStallTrackers[entryIndex] = default;
                _loadEntryActivePositions[entryIndex] = -1;
                _activeLoadEntryIndices[activePosition] = 0;
            }

            _activeLoadObservationCount = 0;
        }
    }
}
