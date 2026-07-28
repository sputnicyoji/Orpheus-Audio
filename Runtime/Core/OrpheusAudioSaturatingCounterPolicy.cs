namespace Orpheus.Audio.Core
{
    internal static class OrpheusAudioSaturatingCounterPolicy
    {
        internal static ulong Increment(ulong current)
        {
            return current == ulong.MaxValue ? current : current + 1UL;
        }
    }
}
