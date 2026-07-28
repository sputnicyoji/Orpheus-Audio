using Orpheus.Audio.Core;

namespace Orpheus.Audio
{
    public sealed partial class OrpheusAudioManager
    {
        public void StopAll(OrpheusBus bus)
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

            if (!OrpheusAudioStopPolicy.IsValidBus(bus))
            {
                SaturatingIncrement(ref _invalidValueRejectedBits);
                return;
            }

            ClearMatchingProfileIntent(bus);
            StopMatchingGlobalLoops(bus);
            StopMatchingTransients(bus, _transient3DSlots, _transient3DExecutionStates);
            StopMatchingTransients(bus, _transient2DSlots, _transient2DExecutionStates);
        }

        private void ClearMatchingProfileIntent(OrpheusBus bus)
        {
            var currentIntent = _profileState.Intent;
            var projectedIntent = OrpheusAudioStopPolicy.ProjectProfileIntent(currentIntent, bus);
            var bgmChanged = currentIntent.BgmKey != projectedIntent.BgmKey;
            var profileAmbienceChanged =
                currentIntent.ProfileAmbienceKey != projectedIntent.ProfileAmbienceKey;
            if (!bgmChanged && !profileAmbienceChanged)
            {
                return;
            }

            var projectedState = new OrpheusAudioProfileState(
                projectedIntent,
                _profileState.Overlay,
                _profileState.EffectiveSnapshot);
            _profileState = projectedState;
            _lastCommittedProfileState = projectedState;

            if (bgmChanged)
            {
                _bgmFailedBlocked = false;
                _bgmRetryRequested = false;
                ReconcileCommittedBgmIntent(projectedIntent.BgmKey);
            }

            if (profileAmbienceChanged)
            {
                _profileAmbienceFailedBlocked = false;
                _profileAmbienceRetryRequested = false;
                ReconcileCommittedProfileAmbienceIntent(projectedIntent.ProfileAmbienceKey);
            }
        }

        private void StopMatchingGlobalLoops(OrpheusBus bus)
        {
            for (var slotIndex = 0; slotIndex < _globalLoops.Length; slotIndex++)
            {
                var loop = _globalLoops[slotIndex];
                if (loop.Phase == OrpheusGlobalLoopPhase.Free ||
                    !_catalogSnapshot.CoreIndex.TryGet(loop.Key, out var catalogEntry) ||
                    !OrpheusAudioStopPolicy.MatchesCategory(bus, catalogEntry.Policy.Category))
                {
                    continue;
                }

                var decision = OrpheusAudioGlobalLoopPolicy.EvaluateStop(_globalLoops, loop.Key);
                ApplyGlobalLoopStopDecision(decision);
            }
        }

        private void StopMatchingTransients(
            OrpheusBus bus,
            OrpheusOneShotSlot[] slots,
            OrpheusTransientExecutionState[] executionStates)
        {
            for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                var action = OrpheusAudioStopPolicy.EvaluateTransient(bus, slots[slotIndex]);
                switch (action)
                {
                    case OrpheusTransientStopAction.BeginFadeOut:
                        if (slots[slotIndex].BeginFadeOut())
                        {
                            executionStates[slotIndex].CancelPendingPreserveFade();
                            executionStates[slotIndex].FadeElapsed = 0f;
                            _fadingCount++;
                        }

                        break;
                    case OrpheusTransientStopAction.CancelPending:
                        if (slots[slotIndex].CancelPending())
                        {
                            executionStates[slotIndex].CancelPendingPreserveFade();
                            _pendingCount--;
                        }

                        break;
                }
            }
        }
    }
}
