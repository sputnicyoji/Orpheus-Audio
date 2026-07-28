using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio
{
    [CreateAssetMenu(fileName = "AE_New", menuName = "Orpheus/Audio Event")]
    public sealed class OrpheusAudioEvent : ScriptableObject
    {
        [SerializeField] private int _schemaVersion = OrpheusAudioAuthoringSchema.Current;
        [SerializeField] private ushort _key;
        [SerializeField] private OrpheusPlaybackKind _playbackKind;
        [SerializeField] private OrpheusCategory _category;
        [SerializeField] private OrpheusLoadPolicy _loadPolicy;
        [SerializeField] private AudioClip[] _clips = new AudioClip[0];
        [SerializeField] private float _volumeMin = 1f;
        [SerializeField] private float _volumeMax = 1f;
        [SerializeField] private float _pitchMin = 1f;
        [SerializeField] private float _pitchMax = 1f;
        [SerializeField] private byte _priority = 128;
        [SerializeField] private byte _polyphonyCap = 1;
        [SerializeField] private float _cooldownSeconds;
        [SerializeField] private float _minimumDistance = 1f;
        [SerializeField] private float _maximumDistance = 500f;
        [SerializeField] private OrpheusRolloffMode _rolloffMode = OrpheusRolloffMode.Logarithmic;

        internal int SchemaVersion => _schemaVersion;
        internal OrpheusAudioKey Key => new OrpheusAudioKey(_key);
        internal OrpheusPlaybackKind PlaybackKind => _playbackKind;
        internal OrpheusLoadPolicy LoadPolicy => _loadPolicy;
        internal int ClipCount => _clips == null ? 0 : _clips.Length;

        internal bool HasValidScalarShape()
        {
            return OrpheusAudioEventPolicy.Evaluate(CapturePolicyValues()) ==
                   OrpheusAudioEventPolicyMismatch.None;
        }

        internal OrpheusAudioEventPolicyValues CapturePolicyValues()
        {
            return new OrpheusAudioEventPolicyValues(
                _schemaVersion,
                _key,
                _playbackKind,
                _category,
                _loadPolicy,
                ClipCount,
                _volumeMin,
                _volumeMax,
                _pitchMin,
                _pitchMax,
                _polyphonyCap,
                _cooldownSeconds,
                _minimumDistance,
                _maximumDistance,
                _rolloffMode);
        }

        internal AudioClip GetClip(int index)
        {
            return _clips[index];
        }

        internal bool HasUniqueClipReferences()
        {
            for (var clipIndex = 0; clipIndex < _clips.Length; clipIndex++)
            {
                var clip = _clips[clipIndex];
                if (clip == null)
                {
                    continue;
                }

                for (var previousClipIndex = 0; previousClipIndex < clipIndex; previousClipIndex++)
                {
                    if (ReferenceEquals(_clips[previousClipIndex], clip))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal OrpheusCatalogEntry BuildEntry(int clipOffset)
        {
            var policy = new OrpheusCatalogScalarPolicy(
                _playbackKind,
                _category,
                _loadPolicy,
                new OrpheusVolumeRange(_volumeMin, _volumeMax),
                new OrpheusPitchRange(_pitchMin, _pitchMax),
                new OrpheusVoicePolicy(_priority, _polyphonyCap, _cooldownSeconds),
                new OrpheusSpatialAttenuation(_minimumDistance, _maximumDistance, _rolloffMode));
            return new OrpheusCatalogEntry(Key, policy, new OrpheusClipRange(clipOffset, ClipCount));
        }

    }
}
