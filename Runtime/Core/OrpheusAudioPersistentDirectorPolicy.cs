using System;

namespace Orpheus.Audio.Core
{
    internal enum OrpheusPersistentDirectorPhase : byte
    {
        Idle = 0,
        Holding = 1,
        Loading = 2,
        Crossfading = 3,
        Stopping = 4,
        Normalizing = 5
    }

    internal enum OrpheusPersistentDirectorAction : byte
    {
        None = 0,
        BeginLoad = 1,
        CancelPendingKeepCurrent = 2,
        ContinueCrossfade = 3,
        ReverseCrossfade = 4,
        QueueLatest = 5,
        Stop = 6,
        PreserveActive = 7,
        PreserveSurvivorAndLoad = 8
    }

    internal readonly struct OrpheusPersistentDirectorView
    {
        internal OrpheusPersistentDirectorView(
            OrpheusPersistentDirectorPhase phase,
            OrpheusAudioKey currentKey,
            OrpheusAudioKey loadingKey,
            OrpheusAudioKey targetKey,
            OrpheusAudioKey queuedKey,
            OrpheusAudioKey sourceZeroKey,
            float sourceZeroGain,
            OrpheusAudioKey sourceOneKey,
            float sourceOneGain,
            float crossfadeProgress)
        {
            Phase = phase;
            CurrentKey = currentKey;
            LoadingKey = loadingKey;
            TargetKey = targetKey;
            QueuedKey = queuedKey;
            SourceZeroKey = sourceZeroKey;
            SourceZeroGain = sourceZeroGain;
            SourceOneKey = sourceOneKey;
            SourceOneGain = sourceOneGain;
            CrossfadeProgress = crossfadeProgress;
        }

        internal OrpheusPersistentDirectorPhase Phase { get; }
        internal OrpheusAudioKey CurrentKey { get; }
        internal OrpheusAudioKey LoadingKey { get; }
        internal OrpheusAudioKey TargetKey { get; }
        internal OrpheusAudioKey QueuedKey { get; }
        internal OrpheusAudioKey SourceZeroKey { get; }
        internal float SourceZeroGain { get; }
        internal OrpheusAudioKey SourceOneKey { get; }
        internal float SourceOneGain { get; }
        internal float CrossfadeProgress { get; }
    }

    internal readonly struct OrpheusPersistentDirectorDecision
    {
        internal OrpheusPersistentDirectorDecision(
            OrpheusPersistentDirectorAction action,
            OrpheusAudioKey key,
            OrpheusAudioKey queuedKey,
            int survivorSourceIndex,
            float crossfadeProgress)
        {
            Action = action;
            Key = key;
            QueuedKey = queuedKey;
            SurvivorSourceIndex = survivorSourceIndex;
            CrossfadeProgress = crossfadeProgress;
        }

        internal OrpheusPersistentDirectorAction Action { get; }
        internal OrpheusAudioKey Key { get; }
        internal OrpheusAudioKey QueuedKey { get; }
        internal int SurvivorSourceIndex { get; }
        internal float CrossfadeProgress { get; }
    }

    internal struct OrpheusPersistentDirectorExecutionState
    {
        internal OrpheusPersistentDirectorPhase Phase;
        internal OrpheusAudioKey LoadingKey;
        internal OrpheusAudioKey QueuedKey;
        internal OrpheusAudioKey SourceZeroKey;
        internal OrpheusAudioKey SourceOneKey;
        internal float SourceZeroGain;
        internal float SourceOneGain;
        internal float SourceZeroAuthoredVolume;
        internal float SourceOneAuthoredVolume;
        internal float TransitionStartGainZero;
        internal float TransitionStartGainOne;
        internal float TransitionElapsed;
        internal float CrossfadeProgress;
        internal int CurrentSourceIndex;
        internal int TargetSourceIndex;
        internal int LoadingEntryIndex;
        internal uint Generation;
        internal uint LoadingGeneration;
        internal uint TransitionGeneration;
        internal bool LoadRequestIssued;
        internal bool FailureReported;

        internal void Initialize()
        {
            this = default;
            CurrentSourceIndex = -1;
            TargetSourceIndex = -1;
            LoadingEntryIndex = -1;
            Generation = 1u;
        }
    }

    internal static class OrpheusAudioPersistentDirectorPolicy
    {
        internal static OrpheusPersistentDirectorDecision EvaluateIntent(
            OrpheusPersistentDirectorView view,
            OrpheusAudioKey desiredKey)
        {
            if (!desiredKey.IsValid)
            {
                if (view.Phase == OrpheusPersistentDirectorPhase.Idle ||
                    (view.Phase == OrpheusPersistentDirectorPhase.Stopping &&
                     !view.LoadingKey.IsValid &&
                     !view.QueuedKey.IsValid))
                {
                    return Decision(OrpheusPersistentDirectorAction.None, desiredKey);
                }

                return Decision(OrpheusPersistentDirectorAction.Stop, desiredKey);
            }

            if (view.Phase == OrpheusPersistentDirectorPhase.Crossfading)
            {
                return EvaluateCrossfade(view, desiredKey);
            }

            if (view.Phase == OrpheusPersistentDirectorPhase.Stopping ||
                view.Phase == OrpheusPersistentDirectorPhase.Normalizing)
            {
                return EvaluateCleanup(view, desiredKey);
            }

            if (desiredKey == view.LoadingKey)
            {
                return Decision(OrpheusPersistentDirectorAction.None, desiredKey);
            }

            if (desiredKey == view.CurrentKey)
            {
                return view.LoadingKey.IsValid
                    ? Decision(
                        OrpheusPersistentDirectorAction.CancelPendingKeepCurrent,
                        desiredKey)
                    : Decision(OrpheusPersistentDirectorAction.None, desiredKey);
            }

            return Decision(OrpheusPersistentDirectorAction.BeginLoad, desiredKey);
        }

        internal static void GetEqualPowerGains(
            float progress,
            out float outgoingGain,
            out float incomingGain)
        {
            var clamped = Clamp01(progress);
            var angle = clamped * (Math.PI * 0.5d);
            outgoingGain = (float)Math.Cos(angle);
            incomingGain = (float)Math.Sin(angle);
        }

        internal static float ReverseProgress(float progress)
        {
            return 1f - Clamp01(progress);
        }

        internal static int SelectSurvivor(float sourceZeroGain, float sourceOneGain)
        {
            return sourceOneGain > sourceZeroGain ? 1 : 0;
        }

        internal static bool IsCurrentGeneration(
            uint scheduledGeneration,
            uint currentGeneration)
        {
            return scheduledGeneration != 0 && scheduledGeneration == currentGeneration;
        }

        internal static bool CanStart(
            uint scheduledGeneration,
            uint currentGeneration,
            OrpheusAudioKey scheduledKey,
            OrpheusAudioKey latestDesiredKey)
        {
            return scheduledKey.IsValid &&
                   scheduledKey == latestDesiredKey &&
                   IsCurrentGeneration(scheduledGeneration, currentGeneration);
        }

        private static OrpheusPersistentDirectorDecision EvaluateCrossfade(
            OrpheusPersistentDirectorView view,
            OrpheusAudioKey desiredKey)
        {
            if (desiredKey == view.CurrentKey)
            {
                return new OrpheusPersistentDirectorDecision(
                    OrpheusPersistentDirectorAction.ReverseCrossfade,
                    desiredKey,
                    OrpheusAudioKey.Invalid,
                    -1,
                    ReverseProgress(view.CrossfadeProgress));
            }

            if (desiredKey == view.TargetKey)
            {
                return view.QueuedKey.IsValid
                    ? new OrpheusPersistentDirectorDecision(
                        OrpheusPersistentDirectorAction.ContinueCrossfade,
                        desiredKey,
                        OrpheusAudioKey.Invalid,
                        -1,
                        Clamp01(view.CrossfadeProgress))
                    : Decision(OrpheusPersistentDirectorAction.None, desiredKey);
            }

            if (desiredKey == view.QueuedKey)
            {
                return Decision(OrpheusPersistentDirectorAction.None, desiredKey);
            }

            return new OrpheusPersistentDirectorDecision(
                OrpheusPersistentDirectorAction.QueueLatest,
                desiredKey,
                desiredKey,
                -1,
                Clamp01(view.CrossfadeProgress));
        }

        private static OrpheusPersistentDirectorDecision EvaluateCleanup(
            OrpheusPersistentDirectorView view,
            OrpheusAudioKey desiredKey)
        {
            if (desiredKey == view.SourceZeroKey)
            {
                return new OrpheusPersistentDirectorDecision(
                    OrpheusPersistentDirectorAction.PreserveActive,
                    desiredKey,
                    OrpheusAudioKey.Invalid,
                    0,
                    0f);
            }

            if (desiredKey == view.SourceOneKey)
            {
                return new OrpheusPersistentDirectorDecision(
                    OrpheusPersistentDirectorAction.PreserveActive,
                    desiredKey,
                    OrpheusAudioKey.Invalid,
                    1,
                    0f);
            }

            var survivor = SelectOccupiedSurvivor(view);
            return new OrpheusPersistentDirectorDecision(
                OrpheusPersistentDirectorAction.PreserveSurvivorAndLoad,
                desiredKey,
                OrpheusAudioKey.Invalid,
                survivor,
                0f);
        }

        private static int SelectOccupiedSurvivor(OrpheusPersistentDirectorView view)
        {
            if (!view.SourceZeroKey.IsValid)
            {
                return view.SourceOneKey.IsValid ? 1 : -1;
            }

            if (!view.SourceOneKey.IsValid)
            {
                return 0;
            }

            return SelectSurvivor(view.SourceZeroGain, view.SourceOneGain);
        }

        private static OrpheusPersistentDirectorDecision Decision(
            OrpheusPersistentDirectorAction action,
            OrpheusAudioKey key)
        {
            return new OrpheusPersistentDirectorDecision(
                action,
                key,
                OrpheusAudioKey.Invalid,
                -1,
                0f);
        }

        private static float Clamp01(float value)
        {
            if (!(value > 0f))
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }
    }
}
