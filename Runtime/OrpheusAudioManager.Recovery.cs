using System;
using Orpheus.Audio.Core;

namespace Orpheus.Audio
{
    public sealed partial class OrpheusAudioManager
    {
        internal void HandleRuntimeHostAudioConfiguration(
            OrpheusAudioRuntimeHost runtimeHost,
            OrpheusRecoveryPendingReason incomingReasons)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                return;
            }

            if (!ReferenceEquals(_runtimeHost, runtimeHost) ||
                _lifecycle != OrpheusAudioLifecycle.Running ||
                !OrpheusAudioRecoveryPolicy.IsValid(incomingReasons))
            {
                return;
            }

            _recoveryPendingReasons = OrpheusAudioRecoveryPolicy.Coalesce(
                _recoveryPendingReasons,
                incomingReasons);
            ProcessRecoveryPending();
        }

        private void ProcessRecoveryPending()
        {
            var pending = _recoveryPendingReasons;
            var decision = OrpheusAudioRecoveryPolicy.EvaluatePending(
                pending,
                _suspensionReasons);
            _recoveryPendingReasons = decision.RetainedReasons;

            switch (decision.Action)
            {
                case OrpheusRecoveryAction.AcknowledgeSelfReset:
                    break;
                case OrpheusRecoveryAction.DeferExternalConfiguration:
                    // External device/config recovery supersedes a pending Android manual Reset.
                    _androidManualRecoveryRequested = false;
                    break;
                case OrpheusRecoveryAction.RecoverExternalConfiguration:
                    _androidManualRecoveryRequested = false;
                    PerformCommonRecovery();
                    break;
            }
        }

        private void ContinueAfterSuspensionCleared()
        {
            var hostPending = _runtimeHost.PeekAudioConfigurationPending();
            if (OrpheusAudioRecoveryPolicy.HasExternal(hostPending))
            {
                // Retain the Host's pending mask bits; do not collapse to a literal External flag.
                _recoveryPendingReasons = OrpheusAudioRecoveryPolicy.Coalesce(
                    _recoveryPendingReasons,
                    hostPending);
                // External configuration pending after resume supersedes Android manual Reset.
                _androidManualRecoveryRequested = false;
                _wasTransportActive = false;
                return;
            }

            if (OrpheusAudioRecoveryPolicy.HasExternal(_recoveryPendingReasons))
            {
                ProcessRecoveryPending();
                return;
            }

            if (_androidManualRecoveryRequested)
            {
                AttemptAndroidManualRecovery();
                return;
            }

            ResumeTransport();
        }

        private void AttemptAndroidManualRecovery()
        {
            _isRecovering = true;
            _wasTransportActive = false;

            bool resetAccepted;
            _runtimeHost.BeginOwnedAudioReset(this);
            try
            {
                resetAccepted = _audioSystem.ResetCurrentConfiguration();
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                return;
            }
            finally
            {
                _runtimeHost.EndOwnedAudioReset(this);
            }

            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            _androidManualRecoveryRequested = false;
            if (!resetAccepted)
            {
                _isRecovering = false;
                SaturatingIncrement(ref _recoveryFailedBits);
                if (_suspensionReasons == OrpheusSuspensionReason.None)
                {
                    ContinueAfterSuspensionCleared();
                }

                return;
            }

            PerformCommonRecovery();
        }

        private void PerformCommonRecovery()
        {
            _recoveryPendingReasons = OrpheusRecoveryPendingReason.None;
            _recoveryGeneration = OrpheusAudioRecoveryPolicy.AdvanceGeneration(
                _recoveryGeneration);
            _isRecovering = true;
            _wasTransportActive = false;

            var carrierReason = ValidateCarrier();
            if (carrierReason != OrpheusAudioDisableReason.None)
            {
                FailClosed(carrierReason);
                return;
            }

            try
            {
                OrpheusAudioSourceNormalizer.NormalizeAll(_ownedSources);
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                return;
            }

            ClearTransientRecoveryExecution();

            var bgmWasFailed = _bgmFailedBlocked;
            var profileAmbienceWasFailed = _profileAmbienceFailedBlocked;
            PrepareBgmForRecovery();
            PrepareProfileAmbienceForRecovery();
            PrepareGlobalLoopsForRecovery();

            carrierReason = ValidateCarrier();
            if (carrierReason != OrpheusAudioDisableReason.None)
            {
                FailClosed(carrierReason);
                return;
            }

            RebuildBgmAfterRecovery(bgmWasFailed);
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            RebuildProfileAmbienceAfterRecovery(profileAmbienceWasFailed);
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            RebuildGlobalLoopsAfterRecovery();
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            if (!ApplyAllUserGains() ||
                !ApplySnapshot(_profileState.EffectiveSnapshot, 0f))
            {
                return;
            }

            _allowRecoveryPersistentStart = true;
            StartRecoveredPersistentExecution();
            _allowRecoveryPersistentStart = false;
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            _isRecovering = false;
            _wasTransportActive = false;
        }

        private void ClearTransientRecoveryExecution()
        {
            ClearTransient3DSlots();
            ClearTransient2DSlots();
            _fadingCount = 0;
            _pendingCount = 0;
        }

        private void PrepareBgmForRecovery()
        {
            if (_bgmDirector.LoadingEntryIndex >= 0)
            {
                CancelLoadObservation(_bgmDirector.LoadingEntryIndex);
            }

            ClearBgmDirectorState();
            _bgmRetryRequested = false;
        }

        private void PrepareProfileAmbienceForRecovery()
        {
            if (_profileAmbienceDirector.LoadingEntryIndex >= 0)
            {
                CancelLoadObservation(_profileAmbienceDirector.LoadingEntryIndex);
            }

            ClearProfileAmbienceDirectorState();
            _profileAmbienceRetryRequested = false;
        }

        private void PrepareGlobalLoopsForRecovery()
        {
            _failedGlobalLoopRecoveryMask = 0;
            for (var slotIndex = 0; slotIndex < _globalLoops.Length; slotIndex++)
            {
                ref var loop = ref _globalLoops[slotIndex];
                if (loop.CatalogEntryIndex >= 0)
                {
                    CancelLoadObservation(loop.CatalogEntryIndex);
                }

                if (!loop.RequestedActive)
                {
                    ClearGlobalLoopEntry(ref loop);
                    continue;
                }

                if (loop.Phase == OrpheusGlobalLoopPhase.Failed)
                {
                    _failedGlobalLoopRecoveryMask |= (byte)(1 << slotIndex);
                }

                var catalogEntryIndex = loop.CatalogEntryIndex;
                RetryGlobalLoopLoad(ref loop, catalogEntryIndex);
                loop.Gain = 0f;
                loop.FadeStartGain = 0f;
                loop.FadeElapsed = 0f;
            }
        }

        private void RebuildBgmAfterRecovery(bool wasFailed)
        {
            var desiredKey = _profileState.Intent.BgmKey;
            var retryClaimed = OrpheusAudioRecoveryPolicy.TryClaimRetry(
                _recoveryGeneration,
                desiredKey.IsValid,
                wasFailed,
                ref _bgmRecoveryRetryGeneration);
            _bgmFailedBlocked = wasFailed && !retryClaimed;
            ReconcileCommittedBgmIntent(desiredKey);
        }

        private void RebuildProfileAmbienceAfterRecovery(bool wasFailed)
        {
            var desiredKey = _profileState.Intent.ProfileAmbienceKey;
            var retryClaimed = OrpheusAudioRecoveryPolicy.TryClaimRetry(
                _recoveryGeneration,
                desiredKey.IsValid,
                wasFailed,
                ref _profileAmbienceRecoveryRetryGeneration);
            _profileAmbienceFailedBlocked = wasFailed && !retryClaimed;
            ReconcileCommittedProfileAmbienceIntent(desiredKey);
        }

        private void RebuildGlobalLoopsAfterRecovery()
        {
            for (var slotIndex = 0; slotIndex < _globalLoops.Length; slotIndex++)
            {
                ref var loop = ref _globalLoops[slotIndex];
                if (!loop.RequestedActive)
                {
                    continue;
                }

                var wasFailed = (_failedGlobalLoopRecoveryMask & (1 << slotIndex)) != 0;
                var retryClaimed = OrpheusAudioRecoveryPolicy.TryClaimRetry(
                    _recoveryGeneration,
                    true,
                    wasFailed,
                    ref loop.RecoveryRetryGeneration);
                if (wasFailed && !retryClaimed)
                {
                    loop.Phase = OrpheusGlobalLoopPhase.Failed;
                    continue;
                }

                RequestGlobalLoopLoad(slotIndex);
                if (_lifecycle != OrpheusAudioLifecycle.Running)
                {
                    return;
                }
            }
        }

        private bool IsPersistentPlaybackReady()
        {
            var transport = _isRecovering && _allowRecoveryPersistentStart &&
                            _suspensionReasons == OrpheusSuspensionReason.None
                ? OrpheusTransportState.Active
                : GetTransportState();
            var readiness = _activationState.GetReadiness(transport);
            return (readiness & OrpheusReadiness.PlaybackReady) != 0;
        }

        private void StartRecoveredPersistentExecution()
        {
            var realtime = _runtimeHost.ReadRealtime();
            if (_isBound && realtime < _lastRealtime)
            {
                realtime = _lastRealtime;
            }

            PollBgmLoading(realtime);
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

            PollProfileAmbienceLoading(realtime);
            if (_lifecycle != OrpheusAudioLifecycle.Running)
            {
                return;
            }

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
        }

        private void ClearRecoveryState(bool advanceGeneration = true)
        {
            if (advanceGeneration)
            {
                AdvanceRecoveryGeneration();
            }

            _recoveryPendingReasons = OrpheusRecoveryPendingReason.None;
            _isRecovering = false;
            _androidManualRecoveryRequested = false;
            _failedGlobalLoopRecoveryMask = 0;
            _allowRecoveryPersistentStart = false;
        }

        private void AdvanceRecoveryGeneration()
        {
            _recoveryGeneration = OrpheusAudioRecoveryPolicy.AdvanceGeneration(
                _recoveryGeneration);
        }
    }
}
