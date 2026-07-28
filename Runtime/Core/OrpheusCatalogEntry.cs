namespace Orpheus.Audio.Core
{
    internal enum OrpheusRolloffMode : byte
    {
        Invalid = 0,
        Logarithmic = 1,
        Linear = 2
    }

    internal readonly struct OrpheusVolumeRange
    {
        public OrpheusVolumeRange(float minimum, float maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        public float Minimum { get; }

        public float Maximum { get; }
    }

    internal readonly struct OrpheusPitchRange
    {
        public OrpheusPitchRange(float minimum, float maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        public float Minimum { get; }

        public float Maximum { get; }
    }

    internal readonly struct OrpheusVoicePolicy
    {
        public OrpheusVoicePolicy(byte priority, byte polyphonyCap, float cooldownSeconds)
        {
            Priority = priority;
            PolyphonyCap = polyphonyCap;
            CooldownSeconds = cooldownSeconds;
        }

        public byte Priority { get; }

        public byte PolyphonyCap { get; }

        public float CooldownSeconds { get; }
    }

    internal readonly struct OrpheusSpatialAttenuation
    {
        public OrpheusSpatialAttenuation(
            float minimumDistance,
            float maximumDistance,
            OrpheusRolloffMode rolloffMode)
        {
            MinimumDistance = minimumDistance;
            MaximumDistance = maximumDistance;
            RolloffMode = rolloffMode;
        }

        public float MinimumDistance { get; }

        public float MaximumDistance { get; }

        public OrpheusRolloffMode RolloffMode { get; }
    }

    internal readonly struct OrpheusCatalogScalarPolicy
    {
        public OrpheusCatalogScalarPolicy(
            OrpheusPlaybackKind playbackKind,
            OrpheusCategory category,
            OrpheusLoadPolicy loadPolicy,
            OrpheusVolumeRange volume,
            OrpheusPitchRange pitch,
            OrpheusVoicePolicy voice,
            OrpheusSpatialAttenuation spatialAttenuation)
        {
            PlaybackKind = playbackKind;
            Category = category;
            LoadPolicy = loadPolicy;
            Volume = volume;
            Pitch = pitch;
            Voice = voice;
            SpatialAttenuation = spatialAttenuation;
        }

        public OrpheusPlaybackKind PlaybackKind { get; }

        public OrpheusCategory Category { get; }

        public OrpheusLoadPolicy LoadPolicy { get; }

        public OrpheusVolumeRange Volume { get; }

        public OrpheusPitchRange Pitch { get; }

        public OrpheusVoicePolicy Voice { get; }

        public OrpheusSpatialAttenuation SpatialAttenuation { get; }
    }

    internal readonly struct OrpheusClipRange
    {
        public OrpheusClipRange(int offset, int count)
        {
            Offset = offset;
            Count = count;
        }

        public int Offset { get; }

        public int Count { get; }
    }

    internal readonly struct OrpheusCatalogEntry
    {
        public OrpheusCatalogEntry(
            OrpheusAudioKey key,
            OrpheusCatalogScalarPolicy policy,
            OrpheusClipRange clipRange)
        {
            Key = key;
            Policy = policy;
            ClipRange = clipRange;
        }

        public OrpheusAudioKey Key { get; }

        public OrpheusCatalogScalarPolicy Policy { get; }

        public OrpheusClipRange ClipRange { get; }
    }
}
