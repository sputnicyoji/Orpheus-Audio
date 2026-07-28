namespace Orpheus.Audio.Core
{
    internal readonly struct OrpheusClipSelection
    {
        public OrpheusClipSelection(OrpheusAudioKey key, int selectedClipIndex)
        {
            Key = key;
            SelectedClipIndex = selectedClipIndex;
        }

        public OrpheusAudioKey Key { get; }

        public int SelectedClipIndex { get; }
    }
}
