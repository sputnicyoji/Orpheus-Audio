namespace Orpheus.Audio.Core
{
    internal interface IOrpheusAudioClipReadiness
    {
        OrpheusClipLoadState GetLoadState(int flattenedClipIndex);
        bool RequestLoad(int flattenedClipIndex);
    }
}
