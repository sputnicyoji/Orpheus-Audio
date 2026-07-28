using System;
using Orpheus.Audio.Core;

namespace Orpheus.Audio
{
    public sealed partial class OrpheusAudioManager
    {
        internal void HandleRuntimeHostSuspensionInput(
            OrpheusAudioRuntimeHost runtimeHost,
            OrpheusSuspensionReason reason,
            bool asserted)
        {
            HandleRuntimeHostSuspensionInput(runtimeHost, reason, asserted, false);
        }

        internal void HandleRuntimeHostSuspensionInput(
            OrpheusAudioRuntimeHost runtimeHost,
            OrpheusSuspensionReason reason,
            bool asserted,
            bool isAndroidPlayer)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return;
            }

            if (!ReferenceEquals(_runtimeHost, runtimeHost) ||
                _lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            var decision = OrpheusAudioSuspensionPolicy.Reduce(
                _suspensionReasons,
                reason,
                asserted);
            if (OrpheusAudioRecoveryPolicy.ShouldRequestAndroidManualRecovery(
                    isAndroidPlayer,
                    _settings.AndroidManualResetEnabled,
                    reason,
                    decision.Changed,
                    asserted))
            {
                _androidManualRecoveryRequested = true;
            }

            ApplySuspensionDecision(decision);
        }

        private void SetListenerAvailable(bool available)
        {
            ApplySuspensionDecision(
                OrpheusAudioSuspensionPolicy.Reduce(
                    _suspensionReasons,
                    OrpheusSuspensionReason.ListenerMissing,
                    !available));
        }

        private void ApplySuspensionDecision(OrpheusSuspensionDecision decision)
        {
            if (!decision.Changed)
            {
                return;
            }

            _suspensionReasons = decision.Reasons;
            _wasTransportActive = false;
            if (_isRecovering)
            {
                return;
            }

            switch (decision.Transition)
            {
                case OrpheusSuspensionTransition.EnterSuspended:
                    EnterTransportSuspension();
                    break;
                case OrpheusSuspensionTransition.ExitSuspended:
                    ContinueAfterSuspensionCleared();
                    break;
            }
        }

        private void EnterTransportSuspension()
        {
            if (!_activationState.ActivationReady)
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
                StopAndClearTransientExecution();
                SuspendGlobalLoopSources();
                SuspendPersistentDirector(
                    ref _bgmDirector,
                    OrpheusAudioSourceBank.BgmOffset);
                SuspendPersistentDirector(
                    ref _profileAmbienceDirector,
                    OrpheusAudioSourceBank.ProfileAmbienceOffset);
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private void ResumeTransport()
        {
            if (!_activationState.ActivationReady)
            {
                return;
            }

            var carrierReason = ValidateCarrier();
            if (carrierReason != OrpheusAudioDisableReason.None)
            {
                FailClosed(carrierReason);
                return;
            }

            if (!ApplySnapshot(_profileState.EffectiveSnapshot, 0f))
            {
                return;
            }

            ReconcileCommittedBgmIntent(_profileState.Intent.BgmKey);
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            ReconcileCommittedProfileAmbienceIntent(
                _profileState.Intent.ProfileAmbienceKey);
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            try
            {
                ResumePersistentDirector(
                    ref _bgmDirector,
                    OrpheusAudioSourceBank.BgmOffset);
                ResumePersistentDirector(
                    ref _profileAmbienceDirector,
                    OrpheusAudioSourceBank.ProfileAmbienceOffset);
                ResumeGlobalLoopSources();
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
            }
        }

        private void StopAndClearTransientExecution()
        {
            for (var slotIndex = 0; slotIndex < _transient3DSlots.Length; slotIndex++)
            {
                if (_transient3DSlots[slotIndex].State != OrpheusOneShotSlotState.Free)
                {
                    OrpheusAudioSourceNormalizer.Normalize(GetTransient3DSource(slotIndex));
                }
            }

            for (var slotIndex = 0; slotIndex < _transient2DSlots.Length; slotIndex++)
            {
                if (_transient2DSlots[slotIndex].State != OrpheusOneShotSlotState.Free)
                {
                    OrpheusAudioSourceNormalizer.Normalize(GetTransient2DSource(slotIndex));
                }
            }

            ClearTransient3DSlots();
            ClearTransient2DSlots();
            _fadingCount = 0;
            _pendingCount = 0;
        }

        private void SuspendGlobalLoopSources()
        {
            for (var slotIndex = 0; slotIndex < _globalLoops.Length; slotIndex++)
            {
                var phase = _globalLoops[slotIndex].Phase;
                if (phase == OrpheusGlobalLoopPhase.Playing ||
                    phase == OrpheusGlobalLoopPhase.Stopping)
                {
                    GetGlobalLoopSource(slotIndex).Pause();
                }
            }
        }

        private void ResumeGlobalLoopSources()
        {
            for (var slotIndex = 0; slotIndex < _globalLoops.Length; slotIndex++)
            {
                var phase = _globalLoops[slotIndex].Phase;
                if (phase == OrpheusGlobalLoopPhase.Playing ||
                    phase == OrpheusGlobalLoopPhase.Stopping)
                {
                    GetGlobalLoopSource(slotIndex).UnPause();
                }
            }
        }

        private void SuspendPersistentDirector(
            ref OrpheusPersistentDirectorExecutionState state,
            int sourceOffset)
        {
            var hasSourceZero = state.SourceZeroKey.IsValid;
            var hasSourceOne = state.SourceOneKey.IsValid;
            var survivorIndex = SelectPersistentSurvivor(
                hasSourceZero,
                state.SourceZeroGain,
                hasSourceOne,
                state.SourceOneGain);

            if (hasSourceZero && survivorIndex != 0)
            {
                OrpheusAudioSourceNormalizer.Normalize(_ownedSources[sourceOffset]);
                ClearPersistentSourceState(ref state, 0);
            }

            if (hasSourceOne && survivorIndex != 1)
            {
                OrpheusAudioSourceNormalizer.Normalize(_ownedSources[sourceOffset + 1]);
                ClearPersistentSourceState(ref state, 1);
            }

            if (survivorIndex >= 0)
            {
                var authoredVolume = survivorIndex == 0
                    ? state.SourceZeroAuthoredVolume
                    : state.SourceOneAuthoredVolume;
                _ownedSources[sourceOffset + survivorIndex].volume = authoredVolume;
                if (survivorIndex == 0)
                {
                    state.SourceZeroGain = 1f;
                }
                else
                {
                    state.SourceOneGain = 1f;
                }
            }

            state.CurrentSourceIndex = survivorIndex;
            state.TargetSourceIndex = -1;
            state.QueuedKey = OrpheusAudioKey.Invalid;
            state.TransitionStartGainZero = 0f;
            state.TransitionStartGainOne = 0f;
            state.TransitionElapsed = 0f;
            state.CrossfadeProgress = 0f;
            state.TransitionGeneration = 0u;
            state.Phase = state.LoadingKey.IsValid
                ? OrpheusPersistentDirectorPhase.Loading
                : survivorIndex >= 0
                    ? OrpheusPersistentDirectorPhase.Holding
                    : OrpheusPersistentDirectorPhase.Idle;

            if (survivorIndex >= 0)
            {
                _ownedSources[sourceOffset + survivorIndex].Pause();
            }
        }

        private void ResumePersistentDirector(
            ref OrpheusPersistentDirectorExecutionState state,
            int sourceOffset)
        {
            var sourceIndex = state.CurrentSourceIndex;
            if (sourceIndex < 0)
            {
                return;
            }

            var key = sourceIndex == 0 ? state.SourceZeroKey : state.SourceOneKey;
            if (key.IsValid)
            {
                _ownedSources[sourceOffset + sourceIndex].UnPause();
            }
        }

        private static int SelectPersistentSurvivor(
            bool hasSourceZero,
            float sourceZeroGain,
            bool hasSourceOne,
            float sourceOneGain)
        {
            if (!hasSourceZero)
            {
                return hasSourceOne ? 1 : -1;
            }

            return !hasSourceOne
                ? 0
                : OrpheusAudioPersistentDirectorPolicy.SelectSurvivor(
                    sourceZeroGain,
                    sourceOneGain);
        }

        private static void ClearPersistentSourceState(
            ref OrpheusPersistentDirectorExecutionState state,
            int sourceIndex)
        {
            if (sourceIndex == 0)
            {
                state.SourceZeroKey = OrpheusAudioKey.Invalid;
                state.SourceZeroGain = 0f;
                state.SourceZeroAuthoredVolume = 0f;
                return;
            }

            state.SourceOneKey = OrpheusAudioKey.Invalid;
            state.SourceOneGain = 0f;
            state.SourceOneAuthoredVolume = 0f;
        }
    }
}
