namespace Orpheus.Audio.Core
{
    internal enum OrpheusGlobalLoopPhase : byte
    {
        Free = 0,
        Loading = 1,
        Playing = 2,
        Failed = 3,
        Stopping = 4
    }

    internal enum OrpheusGlobalLoopAction : byte
    {
        None = 0,
        ReserveAndLoad = 1,
        RetryLoad = 2,
        BeginStop = 3,
        Reactivate = 4,
        ClearUnstarted = 5,
        RejectFull = 6
    }

    internal readonly struct OrpheusGlobalLoopDecision
    {
        internal OrpheusGlobalLoopDecision(
            OrpheusGlobalLoopAction action,
            int entryIndex)
        {
            Action = action;
            EntryIndex = entryIndex;
        }

        internal OrpheusGlobalLoopAction Action { get; }
        internal int EntryIndex { get; }
    }

    internal readonly struct OrpheusGlobalLoopFadeDecision
    {
        internal OrpheusGlobalLoopFadeDecision(float gain, bool complete)
        {
            Gain = gain;
            Complete = complete;
        }

        internal float Gain { get; }
        internal bool Complete { get; }
    }

    internal struct OrpheusGlobalLoopEntryState
    {
        internal OrpheusAudioKey Key;
        internal OrpheusGlobalLoopPhase Phase;
        internal int CatalogEntryIndex;
        internal uint Generation;
        internal uint LoadGeneration;
        internal uint FadeGeneration;
        internal uint RecoveryRetryGeneration;
        internal float AuthoredVolume;
        internal float Gain;
        internal float FadeStartGain;
        internal float FadeElapsed;
        internal bool RequestedActive;
        internal bool LoadRequestIssued;
        internal bool FailureReported;

        internal void Initialize()
        {
            this = default;
            CatalogEntryIndex = -1;
            Generation = 1u;
        }
    }

    internal static class OrpheusAudioGlobalLoopPolicy
    {
        internal const int RegistryCapacity = 4;
        internal const float FadeDurationSeconds = 0.015f;

        internal static OrpheusGlobalLoopDecision EvaluatePlay(
            OrpheusGlobalLoopEntryState[] entries,
            OrpheusAudioKey key)
        {
            if (!key.IsValid)
            {
                return Decision(OrpheusGlobalLoopAction.None, -1);
            }

            var count = GetEntryCount(entries);
            var firstFreeIndex = -1;
            for (var entryIndex = 0; entryIndex < count; entryIndex++)
            {
                var entry = entries[entryIndex];
                if (entry.Phase == OrpheusGlobalLoopPhase.Free)
                {
                    if (firstFreeIndex < 0)
                    {
                        firstFreeIndex = entryIndex;
                    }

                    continue;
                }

                if (entry.Key != key)
                {
                    continue;
                }

                switch (entry.Phase)
                {
                    case OrpheusGlobalLoopPhase.Failed:
                        return Decision(OrpheusGlobalLoopAction.RetryLoad, entryIndex);
                    case OrpheusGlobalLoopPhase.Stopping:
                        return entry.RequestedActive
                            ? Decision(OrpheusGlobalLoopAction.None, entryIndex)
                            : Decision(OrpheusGlobalLoopAction.Reactivate, entryIndex);
                    default:
                        return Decision(OrpheusGlobalLoopAction.None, entryIndex);
                }
            }

            return firstFreeIndex >= 0
                ? Decision(OrpheusGlobalLoopAction.ReserveAndLoad, firstFreeIndex)
                : Decision(OrpheusGlobalLoopAction.RejectFull, -1);
        }

        internal static OrpheusGlobalLoopDecision EvaluateStop(
            OrpheusGlobalLoopEntryState[] entries,
            OrpheusAudioKey key)
        {
            if (!key.IsValid)
            {
                return Decision(OrpheusGlobalLoopAction.None, -1);
            }

            var count = GetEntryCount(entries);
            for (var entryIndex = 0; entryIndex < count; entryIndex++)
            {
                var entry = entries[entryIndex];
                if (entry.Phase == OrpheusGlobalLoopPhase.Free || entry.Key != key)
                {
                    continue;
                }

                switch (entry.Phase)
                {
                    case OrpheusGlobalLoopPhase.Loading:
                    case OrpheusGlobalLoopPhase.Failed:
                        return Decision(OrpheusGlobalLoopAction.ClearUnstarted, entryIndex);
                    case OrpheusGlobalLoopPhase.Playing:
                        return Decision(OrpheusGlobalLoopAction.BeginStop, entryIndex);
                    case OrpheusGlobalLoopPhase.Stopping:
                        return entry.RequestedActive
                            ? Decision(OrpheusGlobalLoopAction.BeginStop, entryIndex)
                            : Decision(OrpheusGlobalLoopAction.None, entryIndex);
                    default:
                        return Decision(OrpheusGlobalLoopAction.None, entryIndex);
                }
            }

            return Decision(OrpheusGlobalLoopAction.None, -1);
        }

        internal static uint AdvanceGeneration(uint currentGeneration)
        {
            var nextGeneration = unchecked(currentGeneration + 1u);
            return nextGeneration == 0u ? 1u : nextGeneration;
        }

        internal static bool CanStart(
            uint scheduledGeneration,
            uint currentGeneration,
            OrpheusAudioKey scheduledKey,
            OrpheusAudioKey currentKey,
            OrpheusGlobalLoopPhase phase,
            bool requestedActive,
            bool isLoaded,
            bool playbackReady)
        {
            return IsCurrentIdentity(
                       scheduledGeneration,
                       currentGeneration,
                       scheduledKey,
                       currentKey) &&
                   phase == OrpheusGlobalLoopPhase.Loading &&
                   requestedActive &&
                   isLoaded &&
                   playbackReady;
        }

        internal static bool CanRelease(
            uint scheduledGeneration,
            uint currentGeneration,
            OrpheusAudioKey scheduledKey,
            OrpheusAudioKey currentKey,
            OrpheusGlobalLoopPhase phase,
            bool requestedActive)
        {
            return IsCurrentIdentity(
                       scheduledGeneration,
                       currentGeneration,
                       scheduledKey,
                       currentKey) &&
                   phase == OrpheusGlobalLoopPhase.Stopping &&
                   !requestedActive;
        }

        internal static OrpheusGlobalLoopFadeDecision EvaluateFade(
            float startGain,
            bool requestedActive,
            float elapsedSeconds)
        {
            var progress = GetFadeProgress(elapsedSeconds);
            var clampedStartGain = Clamp01(startGain);
            var targetGain = requestedActive ? 1f : 0f;
            var gain = clampedStartGain + ((targetGain - clampedStartGain) * progress);
            return new OrpheusGlobalLoopFadeDecision(gain, progress >= 1f);
        }

        private static int GetEntryCount(OrpheusGlobalLoopEntryState[] entries)
        {
            return entries.Length < RegistryCapacity ? entries.Length : RegistryCapacity;
        }

        private static bool IsCurrentIdentity(
            uint scheduledGeneration,
            uint currentGeneration,
            OrpheusAudioKey scheduledKey,
            OrpheusAudioKey currentKey)
        {
            return scheduledGeneration != 0u &&
                   scheduledGeneration == currentGeneration &&
                   scheduledKey.IsValid &&
                   scheduledKey == currentKey;
        }

        private static float GetFadeProgress(float elapsedSeconds)
        {
            if (!(elapsedSeconds > 0f))
            {
                return 0f;
            }

            return elapsedSeconds >= FadeDurationSeconds
                ? 1f
                : elapsedSeconds / FadeDurationSeconds;
        }

        private static float Clamp01(float value)
        {
            if (!(value > 0f))
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }

        private static OrpheusGlobalLoopDecision Decision(
            OrpheusGlobalLoopAction action,
            int entryIndex)
        {
            return new OrpheusGlobalLoopDecision(action, entryIndex);
        }
    }
}
