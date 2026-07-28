namespace Orpheus.Audio.Core
{
    internal static class OrpheusAudioDurationPolicy
    {
        internal const float MinimumSeconds = 0.015f;
        internal const float MaximumSeconds = 5f;
        internal const float DefaultBgmCrossfadeSeconds = 0.4f;
        internal const float DefaultProfileAmbienceCrossfadeSeconds = 0.4f;
        internal const float DefaultSnapshotTransitionSeconds = 0.6f;

        internal static bool IsValid(float seconds)
        {
            return !float.IsNaN(seconds) && !float.IsInfinity(seconds) &&
                   seconds >= MinimumSeconds && seconds <= MaximumSeconds;
        }
    }
}
