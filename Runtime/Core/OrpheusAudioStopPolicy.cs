namespace Orpheus.Audio.Core
{
    internal enum OrpheusTransientStopAction : byte
    {
        None = 0,
        BeginFadeOut = 1,
        CancelPending = 2
    }

    internal static class OrpheusAudioStopPolicy
    {
        internal static bool IsValidBus(OrpheusBus bus)
        {
            switch (bus)
            {
                case OrpheusBus.Master:
                case OrpheusBus.Music:
                case OrpheusBus.SfxCombat:
                case OrpheusBus.SfxWorld:
                case OrpheusBus.SfxUi:
                case OrpheusBus.Ambience:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool MatchesCategory(
            OrpheusBus bus,
            OrpheusCategory category)
        {
            if (!IsValidCategory(category))
            {
                return false;
            }

            switch (bus)
            {
                case OrpheusBus.Master:
                    return true;
                case OrpheusBus.Music:
                    return category == OrpheusCategory.Music;
                case OrpheusBus.SfxCombat:
                    return category == OrpheusCategory.SfxCombat;
                case OrpheusBus.SfxWorld:
                    return category == OrpheusCategory.SfxWorld;
                case OrpheusBus.SfxUi:
                    return category == OrpheusCategory.SfxUi;
                case OrpheusBus.Ambience:
                    return category == OrpheusCategory.Ambience;
                default:
                    return false;
            }
        }

        internal static OrpheusAudioProfileIntent ProjectProfileIntent(
            OrpheusAudioProfileIntent current,
            OrpheusBus bus)
        {
            var clearBgm = bus == OrpheusBus.Master || bus == OrpheusBus.Music;
            var clearProfileAmbience =
                bus == OrpheusBus.Master || bus == OrpheusBus.Ambience;
            return new OrpheusAudioProfileIntent(
                current.ProfileId,
                current.BaseState,
                clearBgm ? OrpheusAudioKey.Invalid : current.BgmKey,
                clearProfileAmbience
                    ? OrpheusAudioKey.Invalid
                    : current.ProfileAmbienceKey);
        }

        internal static OrpheusTransientStopAction EvaluateTransient(
            OrpheusBus bus,
            OrpheusOneShotSlot slot)
        {
            if (!IsValidBus(bus))
            {
                return OrpheusTransientStopAction.None;
            }

            if (slot.State == OrpheusOneShotSlotState.PlayingOneShot)
            {
                return MatchesCategory(bus, slot.Category)
                    ? OrpheusTransientStopAction.BeginFadeOut
                    : OrpheusTransientStopAction.None;
            }

            if (slot.State == OrpheusOneShotSlotState.FadingOut &&
                slot.HasPending &&
                MatchesCategory(bus, slot.PendingCategory))
            {
                return OrpheusTransientStopAction.CancelPending;
            }

            return OrpheusTransientStopAction.None;
        }

        private static bool IsValidCategory(OrpheusCategory category)
        {
            switch (category)
            {
                case OrpheusCategory.Music:
                case OrpheusCategory.SfxCombat:
                case OrpheusCategory.SfxWorld:
                case OrpheusCategory.SfxUi:
                case OrpheusCategory.Ambience:
                    return true;
                default:
                    return false;
            }
        }
    }
}
