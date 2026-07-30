using System;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Editor
{
    [Serializable]
    public struct OrpheusAudioModuleEventRecipe
    {
        [SerializeField] private string _symbol;
        [SerializeField] private OrpheusPlaybackKind _playbackKind;
        [SerializeField] private OrpheusCategory _category;
        [SerializeField] private OrpheusLoadPolicy _loadPolicy;
        [SerializeField] private AudioClip[] _clips;
        [SerializeField] private float _volumeMin;
        [SerializeField] private float _volumeMax;
        [SerializeField] private float _pitchMin;
        [SerializeField] private float _pitchMax;
        [SerializeField] private byte _priority;
        [SerializeField] private byte _polyphonyCap;
        [SerializeField] private float _cooldownSeconds;
        [SerializeField] private float _minimumDistance;
        [SerializeField] private float _maximumDistance;
        [SerializeField] private OrpheusRolloffMode _rolloffMode;
        [SerializeField] private string _profileHint;
        [SerializeField] private string _candidateContentBankId;

        internal OrpheusAudioModuleEventRecipeValue CaptureValue()
        {
            return new OrpheusAudioModuleEventRecipeValue(
                _symbol,
                _playbackKind,
                _category,
                _loadPolicy,
                _clips,
                _volumeMin,
                _volumeMax,
                _pitchMin,
                _pitchMax,
                _priority,
                _polyphonyCap,
                _cooldownSeconds,
                _minimumDistance,
                _maximumDistance,
                _rolloffMode,
                _profileHint,
                _candidateContentBankId);
        }
    }

    internal readonly struct OrpheusAudioModuleEventRecipeValue
    {
        private readonly AudioClip[] _clips;

        internal OrpheusAudioModuleEventRecipeValue(
            string symbol,
            OrpheusPlaybackKind playbackKind,
            OrpheusCategory category,
            OrpheusLoadPolicy loadPolicy,
            AudioClip[] clips,
            float volumeMin,
            float volumeMax,
            float pitchMin,
            float pitchMax,
            byte priority,
            byte polyphonyCap,
            float cooldownSeconds,
            float minimumDistance,
            float maximumDistance,
            OrpheusRolloffMode rolloffMode,
            string profileHint,
            string candidateContentBankId)
        {
            Symbol = symbol ?? string.Empty;
            PlaybackKind = playbackKind;
            Category = category;
            LoadPolicy = loadPolicy;
            _clips = clips == null ? null : (AudioClip[])clips.Clone();
            VolumeMin = volumeMin;
            VolumeMax = volumeMax;
            PitchMin = pitchMin;
            PitchMax = pitchMax;
            Priority = priority;
            PolyphonyCap = polyphonyCap;
            CooldownSeconds = cooldownSeconds;
            MinimumDistance = minimumDistance;
            MaximumDistance = maximumDistance;
            RolloffMode = rolloffMode;
            ProfileHint = profileHint ?? string.Empty;
            CandidateContentBankId = candidateContentBankId ?? string.Empty;
        }

        internal string Symbol { get; }

        internal OrpheusPlaybackKind PlaybackKind { get; }

        internal OrpheusCategory Category { get; }

        internal OrpheusLoadPolicy LoadPolicy { get; }

        internal bool HasClipStorage => _clips != null;

        internal int ClipCount => _clips == null ? 0 : _clips.Length;

        internal float VolumeMin { get; }

        internal float VolumeMax { get; }

        internal float PitchMin { get; }

        internal float PitchMax { get; }

        internal byte Priority { get; }

        internal byte PolyphonyCap { get; }

        internal float CooldownSeconds { get; }

        internal float MinimumDistance { get; }

        internal float MaximumDistance { get; }

        internal OrpheusRolloffMode RolloffMode { get; }

        internal string ProfileHint { get; }

        internal string CandidateContentBankId { get; }

        internal AudioClip GetClip(int index)
        {
            return _clips[index];
        }
    }

    [CreateAssetMenu(
        fileName = "OrpheusAudioModuleRecipe",
        menuName = "Orpheus/Audio Module Recipe")]
    public sealed class OrpheusAudioModuleRecipe : ScriptableObject
    {
        [SerializeField] private int _schemaVersion = 1;
        [SerializeField] private string _moduleId = string.Empty;
        [SerializeField] private OrpheusAudioModuleEventRecipe[] _events =
            Array.Empty<OrpheusAudioModuleEventRecipe>();

        internal OrpheusAudioModuleRecipeValue CaptureValue()
        {
            if (_events == null)
            {
                return new OrpheusAudioModuleRecipeValue(
                    _schemaVersion,
                    _moduleId,
                    null);
            }

            var events = new OrpheusAudioModuleEventRecipeValue[_events.Length];
            for (var index = 0; index < _events.Length; index++)
            {
                events[index] = _events[index].CaptureValue();
            }

            return new OrpheusAudioModuleRecipeValue(
                _schemaVersion,
                _moduleId,
                events);
        }
    }

    internal readonly struct OrpheusAudioModuleRecipeValue
    {
        private readonly OrpheusAudioModuleEventRecipeValue[] _events;

        internal OrpheusAudioModuleRecipeValue(
            int schemaVersion,
            string moduleId,
            OrpheusAudioModuleEventRecipeValue[] events)
        {
            SchemaVersion = schemaVersion;
            ModuleId = moduleId ?? string.Empty;
            _events = events == null
                ? null
                : (OrpheusAudioModuleEventRecipeValue[])events.Clone();
        }

        internal int SchemaVersion { get; }

        internal string ModuleId { get; }

        internal bool HasEventStorage => _events != null;

        internal int EventCount => _events == null ? 0 : _events.Length;

        internal OrpheusAudioModuleEventRecipeValue GetEvent(int index)
        {
            return _events[index];
        }
    }
}
