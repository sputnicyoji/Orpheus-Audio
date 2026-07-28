namespace Orpheus.Audio.Core
{
    internal enum OrpheusRecoveryAction : byte
    {
        None = 0,
        AcknowledgeSelfReset = 1,
        DeferExternalConfiguration = 2,
        RecoverExternalConfiguration = 3
    }

    internal readonly struct OrpheusRecoveryDecision
    {
        internal OrpheusRecoveryDecision(
            OrpheusRecoveryAction action,
            OrpheusRecoveryPendingReason retainedReasons)
        {
            Action = action;
            RetainedReasons = retainedReasons;
        }

        internal OrpheusRecoveryAction Action { get; }
        internal OrpheusRecoveryPendingReason RetainedReasons { get; }
    }

    internal static class OrpheusAudioRecoveryPolicy
    {
        private const byte ValidPendingMask =
            (byte)(OrpheusRecoveryPendingReason.ExternalConfiguration |
            OrpheusRecoveryPendingReason.SelfResetNotification);

        internal static bool IsValid(OrpheusRecoveryPendingReason reasons)
        {
            return ((byte)reasons & ~ValidPendingMask) == 0;
        }

        internal static OrpheusRecoveryPendingReason Coalesce(
            OrpheusRecoveryPendingReason current,
            OrpheusRecoveryPendingReason incoming)
        {
            if (!IsValid(current) || !IsValid(incoming))
            {
                return current;
            }

            return current | incoming;
        }

        internal static bool HasExternal(OrpheusRecoveryPendingReason reasons)
        {
            return IsValid(reasons) &&
                   (reasons & OrpheusRecoveryPendingReason.ExternalConfiguration) != 0;
        }

        internal static OrpheusRecoveryDecision EvaluatePending(
            OrpheusRecoveryPendingReason pendingReasons,
            OrpheusSuspensionReason suspensionReasons)
        {
            if (!IsValid(pendingReasons) ||
                !OrpheusAudioSuspensionPolicy.IsValidStoredMask(suspensionReasons))
            {
                return new OrpheusRecoveryDecision(
                    OrpheusRecoveryAction.None,
                    pendingReasons);
            }

            if (HasExternal(pendingReasons))
            {
                if (suspensionReasons != OrpheusSuspensionReason.None)
                {
                    return new OrpheusRecoveryDecision(
                        OrpheusRecoveryAction.DeferExternalConfiguration,
                        OrpheusRecoveryPendingReason.ExternalConfiguration);
                }

                return new OrpheusRecoveryDecision(
                    OrpheusRecoveryAction.RecoverExternalConfiguration,
                    OrpheusRecoveryPendingReason.None);
            }

            if ((pendingReasons & OrpheusRecoveryPendingReason.SelfResetNotification) != 0)
            {
                return new OrpheusRecoveryDecision(
                    OrpheusRecoveryAction.AcknowledgeSelfReset,
                    OrpheusRecoveryPendingReason.None);
            }

            return new OrpheusRecoveryDecision(
                OrpheusRecoveryAction.None,
                OrpheusRecoveryPendingReason.None);
        }

        internal static bool ShouldRequestAndroidManualRecovery(
            bool isAndroidPlayer,
            bool settingEnabled,
            OrpheusSuspensionReason clearedReason,
            bool reducerChanged,
            bool asserted)
        {
            if (!isAndroidPlayer || !settingEnabled || !reducerChanged || asserted)
            {
                return false;
            }

            return clearedReason == OrpheusSuspensionReason.FocusLost ||
                   clearedReason == OrpheusSuspensionReason.ApplicationPaused;
        }

        internal static uint AdvanceGeneration(uint current)
        {
            var next = unchecked(current + 1u);
            return next == 0u ? 1u : next;
        }

        internal static bool CanClaimRetry(
            uint recoveryGeneration,
            uint lastRetryGeneration)
        {
            return recoveryGeneration != 0u &&
                   recoveryGeneration != lastRetryGeneration;
        }

        internal static bool TryClaimRetry(
            uint recoveryGeneration,
            bool isDesired,
            bool isFailed,
            ref uint lastRetryGeneration)
        {
            if (!isDesired || !isFailed ||
                !CanClaimRetry(recoveryGeneration, lastRetryGeneration))
            {
                return false;
            }

            lastRetryGeneration = recoveryGeneration;
            return true;
        }
    }
}
