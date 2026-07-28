using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio
{
    internal readonly struct OrpheusAudioCategoryRoutes
    {
        private readonly AudioMixerGroup _music;
        private readonly AudioMixerGroup _sfxCombat;
        private readonly AudioMixerGroup _sfxWorld;
        private readonly AudioMixerGroup _sfxUi;
        private readonly AudioMixerGroup _ambience;

        internal OrpheusAudioCategoryRoutes(
            AudioMixerGroup music,
            AudioMixerGroup sfxCombat,
            AudioMixerGroup sfxWorld,
            AudioMixerGroup sfxUi,
            AudioMixerGroup ambience)
        {
            _music = music;
            _sfxCombat = sfxCombat;
            _sfxWorld = sfxWorld;
            _sfxUi = sfxUi;
            _ambience = ambience;
        }

        internal AudioMixerGroup Get(Core.OrpheusCategory category)
        {
            switch (category)
            {
                case Core.OrpheusCategory.Music:
                    return _music;
                case Core.OrpheusCategory.SfxCombat:
                    return _sfxCombat;
                case Core.OrpheusCategory.SfxWorld:
                    return _sfxWorld;
                case Core.OrpheusCategory.SfxUi:
                    return _sfxUi;
                case Core.OrpheusCategory.Ambience:
                    return _ambience;
                default:
                    return null;
            }
        }
    }

    internal readonly struct OrpheusAudioMixerGroupSet
    {
        private readonly AudioMixerGroup _master;
        private readonly AudioMixerGroup _musicUser;
        private readonly AudioMixerGroup _musicState;
        private readonly AudioMixerGroup _sfxCombatUser;
        private readonly AudioMixerGroup _sfxCombatState;
        private readonly AudioMixerGroup _sfxWorldUser;
        private readonly AudioMixerGroup _sfxWorldState;
        private readonly AudioMixerGroup _sfxUiUser;
        private readonly AudioMixerGroup _sfxUiState;
        private readonly AudioMixerGroup _ambienceUser;
        private readonly AudioMixerGroup _ambienceState;

        internal OrpheusAudioMixerGroupSet(
            AudioMixerGroup master,
            AudioMixerGroup musicUser,
            AudioMixerGroup musicState,
            AudioMixerGroup sfxCombatUser,
            AudioMixerGroup sfxCombatState,
            AudioMixerGroup sfxWorldUser,
            AudioMixerGroup sfxWorldState,
            AudioMixerGroup sfxUiUser,
            AudioMixerGroup sfxUiState,
            AudioMixerGroup ambienceUser,
            AudioMixerGroup ambienceState)
        {
            _master = master;
            _musicUser = musicUser;
            _musicState = musicState;
            _sfxCombatUser = sfxCombatUser;
            _sfxCombatState = sfxCombatState;
            _sfxWorldUser = sfxWorldUser;
            _sfxWorldState = sfxWorldState;
            _sfxUiUser = sfxUiUser;
            _sfxUiState = sfxUiState;
            _ambienceUser = ambienceUser;
            _ambienceState = ambienceState;
        }

        internal bool HasValidMixerIdentity(object mixerIdentity)
        {
            return HasMixerIdentity(_master, mixerIdentity) &&
                   HasMixerIdentity(_musicUser, mixerIdentity) &&
                   HasMixerIdentity(_musicState, mixerIdentity) &&
                   HasMixerIdentity(_sfxCombatUser, mixerIdentity) &&
                   HasMixerIdentity(_sfxCombatState, mixerIdentity) &&
                   HasMixerIdentity(_sfxWorldUser, mixerIdentity) &&
                   HasMixerIdentity(_sfxWorldState, mixerIdentity) &&
                   HasMixerIdentity(_sfxUiUser, mixerIdentity) &&
                   HasMixerIdentity(_sfxUiState, mixerIdentity) &&
                   HasMixerIdentity(_ambienceUser, mixerIdentity) &&
                   HasMixerIdentity(_ambienceState, mixerIdentity);
        }

        private static bool HasMixerIdentity(AudioMixerGroup group, object mixerIdentity)
        {
            return group != null && ReferenceEquals(group.audioMixer, mixerIdentity);
        }
    }

    internal readonly struct OrpheusAudioSnapshotSet
    {
        private readonly AudioMixerSnapshot _peace;
        private readonly AudioMixerSnapshot _combat;
        private readonly AudioMixerSnapshot _menu;
        private readonly AudioMixerSnapshot _pause;

        internal OrpheusAudioSnapshotSet(
            AudioMixerSnapshot peace,
            AudioMixerSnapshot combat,
            AudioMixerSnapshot menu,
            AudioMixerSnapshot pause)
        {
            _peace = peace;
            _combat = combat;
            _menu = menu;
            _pause = pause;
        }

        internal AudioMixerSnapshot Get(Core.OrpheusEffectiveSnapshot snapshot)
        {
            switch (snapshot)
            {
                case Core.OrpheusEffectiveSnapshot.Peace:
                    return _peace;
                case Core.OrpheusEffectiveSnapshot.Combat:
                    return _combat;
                case Core.OrpheusEffectiveSnapshot.Menu:
                    return _menu;
                case Core.OrpheusEffectiveSnapshot.Pause:
                    return _pause;
                default:
                    return null;
            }
        }

        internal bool HasAllSnapshotReferences =>
            _peace != null && _combat != null && _menu != null && _pause != null;

        internal bool HasValidMixerIdentity(object mixerIdentity)
        {
            return HasSnapshotMixerIdentity(_peace, mixerIdentity) &&
                   HasSnapshotMixerIdentity(_combat, mixerIdentity) &&
                   HasSnapshotMixerIdentity(_menu, mixerIdentity) &&
                   HasSnapshotMixerIdentity(_pause, mixerIdentity);
        }

        private static bool HasSnapshotMixerIdentity(
            AudioMixerSnapshot snapshot,
            object mixerIdentity)
        {
            return snapshot != null && ReferenceEquals(snapshot.audioMixer, mixerIdentity);
        }
    }

    [CreateAssetMenu(fileName = "OrpheusAudioSettings", menuName = "Orpheus/Audio Settings")]
    public sealed class OrpheusAudioSettings : ScriptableObject
    {
        [SerializeField] private int _schemaVersion = Core.OrpheusAudioAuthoringSchema.Current;
        [SerializeField] private AudioMixer _mixer;
        [SerializeField] private AudioMixerGroup _master;
        [SerializeField] private AudioMixerGroup _musicUser;
        [SerializeField] private AudioMixerGroup _musicState;
        [SerializeField] private AudioMixerGroup _sfxCombatUser;
        [SerializeField] private AudioMixerGroup _sfxCombatState;
        [SerializeField] private AudioMixerGroup _sfxWorldUser;
        [SerializeField] private AudioMixerGroup _sfxWorldState;
        [SerializeField] private AudioMixerGroup _sfxUiUser;
        [SerializeField] private AudioMixerGroup _sfxUiState;
        [SerializeField] private AudioMixerGroup _ambienceUser;
        [SerializeField] private AudioMixerGroup _ambienceState;
        [SerializeField] private AudioMixerSnapshot _peace;
        [SerializeField] private AudioMixerSnapshot _combat;
        [SerializeField] private AudioMixerSnapshot _menu;
        [SerializeField] private AudioMixerSnapshot _pause;
        [SerializeField] private float _bgmCrossfadeSeconds =
            Core.OrpheusAudioDurationPolicy.DefaultBgmCrossfadeSeconds;
        [SerializeField] private float _profileAmbienceCrossfadeSeconds =
            Core.OrpheusAudioDurationPolicy.DefaultProfileAmbienceCrossfadeSeconds;
        [SerializeField] private float _snapshotTransitionSeconds =
            Core.OrpheusAudioDurationPolicy.DefaultSnapshotTransitionSeconds;
        [SerializeField] private bool _androidManualResetEnabled;

        internal int SchemaVersion => _schemaVersion;
        internal AudioMixer Mixer => _mixer;
        internal float BgmCrossfadeSeconds => _bgmCrossfadeSeconds;
        internal float ProfileAmbienceCrossfadeSeconds => _profileAmbienceCrossfadeSeconds;
        internal float SnapshotTransitionSeconds => _snapshotTransitionSeconds;
        internal bool AndroidManualResetEnabled => _androidManualResetEnabled;

        internal bool HasValidDurations =>
            Core.OrpheusAudioDurationPolicy.IsValid(_bgmCrossfadeSeconds) &&
            Core.OrpheusAudioDurationPolicy.IsValid(_profileAmbienceCrossfadeSeconds) &&
            Core.OrpheusAudioDurationPolicy.IsValid(_snapshotTransitionSeconds);

        internal OrpheusAudioSnapshotSet CaptureSnapshots()
        {
            return new OrpheusAudioSnapshotSet(_peace, _combat, _menu, _pause);
        }

        internal OrpheusAudioCategoryRoutes CaptureCategoryRoutes()
        {
            return new OrpheusAudioCategoryRoutes(
                _musicState,
                _sfxCombatState,
                _sfxWorldState,
                _sfxUiState,
                _ambienceState);
        }

        internal OrpheusAudioMixerGroupSet CaptureMixerGroups()
        {
            return new OrpheusAudioMixerGroupSet(
                _master,
                _musicUser,
                _musicState,
                _sfxCombatUser,
                _sfxCombatState,
                _sfxWorldUser,
                _sfxWorldState,
                _sfxUiUser,
                _sfxUiState,
                _ambienceUser,
                _ambienceState);
        }
    }
}
