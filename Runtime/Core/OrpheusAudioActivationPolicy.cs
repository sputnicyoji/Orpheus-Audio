using System;

namespace Orpheus.Audio.Core
{
    internal struct OrpheusAudioActivationState
    {
        private bool _hostReady;
        private bool _bootstrapHydrated;

        internal bool HostReady => _hostReady;
        internal bool BootstrapHydrated => _bootstrapHydrated;
        internal bool ActivationReady => _hostReady && _bootstrapHydrated;

        internal bool CompleteHostReady()
        {
            if (_hostReady)
            {
                return false;
            }

            _hostReady = true;
            return true;
        }

        internal bool CompleteBootstrapHydration()
        {
            if (_bootstrapHydrated)
            {
                return false;
            }

            _bootstrapHydrated = true;
            return true;
        }

        internal OrpheusReadiness GetReadiness(OrpheusTransportState transportState)
        {
            var readiness = OrpheusReadiness.None;
            if (_hostReady)
            {
                readiness |= OrpheusReadiness.HostReady;
            }

            if (_bootstrapHydrated)
            {
                readiness |= OrpheusReadiness.BootstrapHydrated;
            }

            if (!ActivationReady)
            {
                return readiness;
            }

            readiness |= OrpheusReadiness.ActivationReady;
            if (transportState == OrpheusTransportState.Active)
            {
                readiness |= OrpheusReadiness.PlaybackReady;
            }

            return readiness;
        }
    }

    internal static class OrpheusAudioUserGainPolicy
    {
        internal const float MinimumDecibels = -80f;

        internal static bool AreValid(OrpheusAudioUserGains gains)
        {
            return IsValid(gains.Master) &&
                   IsValid(gains.Music) &&
                   IsValid(gains.SfxCombat) &&
                   IsValid(gains.SfxWorld) &&
                   IsValid(gains.SfxUi) &&
                   IsValid(gains.Ambience);
        }

        internal static bool IsValid(float linearGain)
        {
            return !float.IsNaN(linearGain) &&
                   !float.IsInfinity(linearGain) &&
                   linearGain >= 0f &&
                   linearGain <= 1f;
        }

        internal static float ToDecibels(float linearGain)
        {
            if (!IsValid(linearGain))
            {
                throw new ArgumentOutOfRangeException(nameof(linearGain));
            }

            return linearGain == 0f
                ? MinimumDecibels
                : (float)(20.0 * Math.Log10(linearGain));
        }

        internal static bool TryGet(
            OrpheusAudioUserGains gains,
            OrpheusBus bus,
            out float linearGain)
        {
            switch (bus)
            {
                case OrpheusBus.Master:
                    linearGain = gains.Master;
                    return true;
                case OrpheusBus.Music:
                    linearGain = gains.Music;
                    return true;
                case OrpheusBus.SfxCombat:
                    linearGain = gains.SfxCombat;
                    return true;
                case OrpheusBus.SfxWorld:
                    linearGain = gains.SfxWorld;
                    return true;
                case OrpheusBus.SfxUi:
                    linearGain = gains.SfxUi;
                    return true;
                case OrpheusBus.Ambience:
                    linearGain = gains.Ambience;
                    return true;
                default:
                    linearGain = 0f;
                    return false;
            }
        }

        internal static bool TrySet(
            OrpheusAudioUserGains gains,
            OrpheusBus bus,
            float linearGain,
            out OrpheusAudioUserGains updated)
        {
            if (!IsValid(linearGain))
            {
                updated = gains;
                return false;
            }

            switch (bus)
            {
                case OrpheusBus.Master:
                    updated = new OrpheusAudioUserGains(
                        linearGain,
                        gains.Music,
                        gains.SfxCombat,
                        gains.SfxWorld,
                        gains.SfxUi,
                        gains.Ambience);
                    return true;
                case OrpheusBus.Music:
                    updated = new OrpheusAudioUserGains(
                        gains.Master,
                        linearGain,
                        gains.SfxCombat,
                        gains.SfxWorld,
                        gains.SfxUi,
                        gains.Ambience);
                    return true;
                case OrpheusBus.SfxCombat:
                    updated = new OrpheusAudioUserGains(
                        gains.Master,
                        gains.Music,
                        linearGain,
                        gains.SfxWorld,
                        gains.SfxUi,
                        gains.Ambience);
                    return true;
                case OrpheusBus.SfxWorld:
                    updated = new OrpheusAudioUserGains(
                        gains.Master,
                        gains.Music,
                        gains.SfxCombat,
                        linearGain,
                        gains.SfxUi,
                        gains.Ambience);
                    return true;
                case OrpheusBus.SfxUi:
                    updated = new OrpheusAudioUserGains(
                        gains.Master,
                        gains.Music,
                        gains.SfxCombat,
                        gains.SfxWorld,
                        linearGain,
                        gains.Ambience);
                    return true;
                case OrpheusBus.Ambience:
                    updated = new OrpheusAudioUserGains(
                        gains.Master,
                        gains.Music,
                        gains.SfxCombat,
                        gains.SfxWorld,
                        gains.SfxUi,
                        linearGain);
                    return true;
                default:
                    updated = gains;
                    return false;
            }
        }
    }
}
