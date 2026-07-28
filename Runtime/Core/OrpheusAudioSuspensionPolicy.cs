namespace Orpheus.Audio.Core
{
    internal enum OrpheusSuspensionTransition : byte
    {
        None = 0,
        EnterSuspended = 1,
        RemainSuspended = 2,
        ExitSuspended = 3
    }

    internal readonly struct OrpheusSuspensionDecision
    {
        internal OrpheusSuspensionDecision(
            OrpheusSuspensionReason reasons,
            OrpheusSuspensionTransition transition,
            bool changed)
        {
            Reasons = reasons;
            Transition = transition;
            Changed = changed;
        }

        internal OrpheusSuspensionReason Reasons { get; }
        internal OrpheusSuspensionTransition Transition { get; }
        internal bool Changed { get; }
    }

    internal static class OrpheusAudioSuspensionPolicy
    {
        private const byte ValidStoredMask =
            (byte)(OrpheusSuspensionReason.FocusLost |
            OrpheusSuspensionReason.ApplicationPaused |
            OrpheusSuspensionReason.ListenerMissing);

        internal static OrpheusSuspensionReason CreateInitialReasons(
            bool hasFocus,
            bool hasListener)
        {
            var reasons = OrpheusSuspensionReason.None;
            if (!hasFocus)
            {
                reasons |= OrpheusSuspensionReason.FocusLost;
            }

            if (!hasListener)
            {
                reasons |= OrpheusSuspensionReason.ListenerMissing;
            }

            return reasons;
        }

        internal static OrpheusSuspensionDecision Reduce(
            OrpheusSuspensionReason current,
            OrpheusSuspensionReason reason,
            bool asserted)
        {
            if (!IsValidStoredMask(current) || !IsSingleReason(reason))
            {
                return Unchanged(current);
            }

            var next = asserted
                ? current | reason
                : current & ~reason;
            if (next == current)
            {
                return Unchanged(current);
            }

            OrpheusSuspensionTransition transition;
            if (current == OrpheusSuspensionReason.None)
            {
                transition = OrpheusSuspensionTransition.EnterSuspended;
            }
            else if (next == OrpheusSuspensionReason.None)
            {
                transition = OrpheusSuspensionTransition.ExitSuspended;
            }
            else
            {
                transition = OrpheusSuspensionTransition.RemainSuspended;
            }

            return new OrpheusSuspensionDecision(next, transition, true);
        }

        internal static bool IsValidStoredMask(OrpheusSuspensionReason reasons)
        {
            return ((byte)reasons & ~ValidStoredMask) == 0;
        }

        private static bool IsSingleReason(OrpheusSuspensionReason reason)
        {
            switch (reason)
            {
                case OrpheusSuspensionReason.FocusLost:
                case OrpheusSuspensionReason.ApplicationPaused:
                case OrpheusSuspensionReason.ListenerMissing:
                    return true;
                default:
                    return false;
            }
        }

        private static OrpheusSuspensionDecision Unchanged(
            OrpheusSuspensionReason reasons)
        {
            return new OrpheusSuspensionDecision(
                reasons,
                OrpheusSuspensionTransition.None,
                false);
        }
    }
}
