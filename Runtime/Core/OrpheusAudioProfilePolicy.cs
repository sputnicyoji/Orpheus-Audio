namespace Orpheus.Audio.Core
{
    internal readonly struct OrpheusAudioProfileState
    {
        internal OrpheusAudioProfileState(
            OrpheusAudioProfileIntent intent,
            OrpheusOverlay overlay,
            OrpheusEffectiveSnapshot effectiveSnapshot)
        {
            Intent = intent;
            Overlay = overlay;
            EffectiveSnapshot = effectiveSnapshot;
        }

        internal OrpheusAudioProfileIntent Intent { get; }
        internal OrpheusOverlay Overlay { get; }
        internal OrpheusEffectiveSnapshot EffectiveSnapshot { get; }

        internal static OrpheusAudioProfileState Default =>
            new OrpheusAudioProfileState(
                new OrpheusAudioProfileIntent(
                    0,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid),
                OrpheusOverlay.None,
                OrpheusEffectiveSnapshot.Peace);
    }

    internal readonly struct OrpheusAudioProfileDecision
    {
        internal OrpheusAudioProfileDecision(
            OrpheusAudioProfileState state,
            bool commit,
            bool retryBgm,
            bool retryProfileAmbience)
        {
            State = state;
            Commit = commit;
            RetryBgm = retryBgm;
            RetryProfileAmbience = retryProfileAmbience;
        }

        internal OrpheusAudioProfileState State { get; }
        internal bool Commit { get; }
        internal bool RetryBgm { get; }
        internal bool RetryProfileAmbience { get; }
    }

    internal static class OrpheusAudioProfilePolicy
    {
        internal static bool TryApplyProfile(
            OrpheusAudioProfileState current,
            OrpheusAudioProfileIntent submitted,
            OrpheusCatalogLookupIndex catalog,
            bool bgmFailedBlocked,
            bool profileAmbienceFailedBlocked,
            out OrpheusAudioProfileDecision decision)
        {
            decision = default;
            if (catalog == null || submitted.ProfileId == 0 ||
                !IsValidBaseState(submitted.BaseState) ||
                !IsValidPersistentKey(
                    catalog,
                    submitted.BgmKey,
                    OrpheusPlaybackKind.Bgm,
                    OrpheusCategory.Music) ||
                !IsValidPersistentKey(
                    catalog,
                    submitted.ProfileAmbienceKey,
                    OrpheusPlaybackKind.ProfileAmbience,
                    OrpheusCategory.Ambience))
            {
                return false;
            }

            if (AreEqual(current.Intent, submitted))
            {
                decision = new OrpheusAudioProfileDecision(
                    current,
                    false,
                    bgmFailedBlocked && submitted.BgmKey.IsValid,
                    profileAmbienceFailedBlocked && submitted.ProfileAmbienceKey.IsValid);
                return true;
            }

            var nextSnapshot = Resolve(submitted.BaseState, current.Overlay);
            decision = new OrpheusAudioProfileDecision(
                new OrpheusAudioProfileState(submitted, current.Overlay, nextSnapshot),
                true,
                false,
                false);
            return true;
        }

        internal static bool TrySetOverlay(
            OrpheusAudioProfileState current,
            OrpheusOverlay selector,
            bool enabled,
            out OrpheusAudioProfileDecision decision)
        {
            decision = default;
            if (selector != OrpheusOverlay.Menu && selector != OrpheusOverlay.Pause)
            {
                return false;
            }

            var hasSelector = (current.Overlay & selector) != 0;
            if (hasSelector == enabled)
            {
                decision = new OrpheusAudioProfileDecision(current, false, false, false);
                return true;
            }

            var nextOverlay = enabled
                ? current.Overlay | selector
                : current.Overlay & ~selector;
            decision = new OrpheusAudioProfileDecision(
                new OrpheusAudioProfileState(
                    current.Intent,
                    nextOverlay,
                    Resolve(current.Intent.BaseState, nextOverlay)),
                true,
                false,
                false);
            return true;
        }

        internal static OrpheusEffectiveSnapshot Resolve(
            OrpheusBaseState baseState,
            OrpheusOverlay overlay)
        {
            if ((overlay & OrpheusOverlay.Pause) != 0)
            {
                return OrpheusEffectiveSnapshot.Pause;
            }

            if ((overlay & OrpheusOverlay.Menu) != 0)
            {
                return OrpheusEffectiveSnapshot.Menu;
            }

            return baseState == OrpheusBaseState.Combat
                ? OrpheusEffectiveSnapshot.Combat
                : OrpheusEffectiveSnapshot.Peace;
        }

        private static bool IsValidBaseState(OrpheusBaseState baseState)
        {
            return baseState == OrpheusBaseState.Peace || baseState == OrpheusBaseState.Combat;
        }

        private static bool IsValidPersistentKey(
            OrpheusCatalogLookupIndex catalog,
            OrpheusAudioKey key,
            OrpheusPlaybackKind requiredKind,
            OrpheusCategory requiredCategory)
        {
            if (!key.IsValid)
            {
                return true;
            }

            if (!catalog.TryGet(key, out var entry))
            {
                return false;
            }

            var policy = entry.Policy;
            return policy.PlaybackKind == requiredKind &&
                   policy.Category == requiredCategory &&
                   policy.LoadPolicy == OrpheusLoadPolicy.PersistentStream;
        }

        private static bool AreEqual(
            OrpheusAudioProfileIntent left,
            OrpheusAudioProfileIntent right)
        {
            return left.ProfileId == right.ProfileId &&
                   left.BaseState == right.BaseState &&
                   left.BgmKey == right.BgmKey &&
                   left.ProfileAmbienceKey == right.ProfileAmbienceKey;
        }
    }

}
