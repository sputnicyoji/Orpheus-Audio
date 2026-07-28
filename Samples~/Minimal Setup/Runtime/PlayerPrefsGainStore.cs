using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Samples.MinimalSetup
{
    public static class PlayerPrefsGainStore
    {
        public const string MasterKey = "Orpheus.MinimalSetup.Gain.Master";
        public const string MusicKey = "Orpheus.MinimalSetup.Gain.Music";
        public const string SfxCombatKey = "Orpheus.MinimalSetup.Gain.SfxCombat";
        public const string SfxWorldKey = "Orpheus.MinimalSetup.Gain.SfxWorld";
        public const string SfxUiKey = "Orpheus.MinimalSetup.Gain.SfxUi";
        public const string AmbienceKey = "Orpheus.MinimalSetup.Gain.Ambience";

        private const float DefaultGain = 1f;

        public static OrpheusAudioUserGains Load()
        {
            return new OrpheusAudioUserGains(
                LoadGain(MasterKey),
                LoadGain(MusicKey),
                LoadGain(SfxCombatKey),
                LoadGain(SfxWorldKey),
                LoadGain(SfxUiKey),
                LoadGain(AmbienceKey));
        }

        public static bool Save(OrpheusAudioUserGains gains)
        {
            if (!IsValid(gains.Master) ||
                !IsValid(gains.Music) ||
                !IsValid(gains.SfxCombat) ||
                !IsValid(gains.SfxWorld) ||
                !IsValid(gains.SfxUi) ||
                !IsValid(gains.Ambience))
            {
                return false;
            }

            PlayerPrefs.SetFloat(MasterKey, gains.Master);
            PlayerPrefs.SetFloat(MusicKey, gains.Music);
            PlayerPrefs.SetFloat(SfxCombatKey, gains.SfxCombat);
            PlayerPrefs.SetFloat(SfxWorldKey, gains.SfxWorld);
            PlayerPrefs.SetFloat(SfxUiKey, gains.SfxUi);
            PlayerPrefs.SetFloat(AmbienceKey, gains.Ambience);
            PlayerPrefs.Save();
            return true;
        }

        private static float LoadGain(string key)
        {
            var gain = PlayerPrefs.GetFloat(key, DefaultGain);
            return IsValid(gain) ? gain : DefaultGain;
        }

        private static bool IsValid(float gain)
        {
            return !float.IsNaN(gain) && !float.IsInfinity(gain) && gain >= 0f && gain <= 1f;
        }
    }
}
