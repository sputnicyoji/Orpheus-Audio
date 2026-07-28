using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioProfilePolicyTests
    {
        [Test]
        public void ApplyProfile_ValidatesTheCompleteIntentBeforeProducingState()
        {
            var catalog = CreateCatalog(
                Entry(100, OrpheusPlaybackKind.Bgm, OrpheusCategory.Music, OrpheusLoadPolicy.PersistentStream, 0),
                Entry(200, OrpheusPlaybackKind.ProfileAmbience, OrpheusCategory.Ambience,
                    OrpheusLoadPolicy.PersistentStream, 1));
            var current = OrpheusAudioProfileState.Default;
            var valid = new OrpheusAudioProfileIntent(
                7,
                OrpheusBaseState.Combat,
                new OrpheusAudioKey(100),
                new OrpheusAudioKey(200));

            Assert.That(
                OrpheusAudioProfilePolicy.TryApplyProfile(
                    current, valid, catalog, false, false, out var accepted),
                Is.True);
            Assert.That(accepted.Commit, Is.True);
            Assert.That(accepted.State.Intent.ProfileId, Is.EqualTo(7));
            Assert.That(accepted.State.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Combat));

            var invalidValues = new[]
            {
                new OrpheusAudioProfileIntent(0, OrpheusBaseState.Combat,
                    new OrpheusAudioKey(100), new OrpheusAudioKey(200)),
                new OrpheusAudioProfileIntent(7, OrpheusBaseState.Invalid,
                    new OrpheusAudioKey(100), new OrpheusAudioKey(200)),
                new OrpheusAudioProfileIntent(7, OrpheusBaseState.Combat,
                    new OrpheusAudioKey(999), new OrpheusAudioKey(200)),
                new OrpheusAudioProfileIntent(7, OrpheusBaseState.Combat,
                    new OrpheusAudioKey(100), new OrpheusAudioKey(999))
            };

            for (var index = 0; index < invalidValues.Length; index++)
            {
                Assert.That(
                    OrpheusAudioProfilePolicy.TryApplyProfile(
                        accepted.State,
                        invalidValues[index],
                        catalog,
                        true,
                        true,
                        out var rejected),
                    Is.False,
                    index.ToString());
                Assert.That(rejected.Commit, Is.False, index.ToString());
                Assert.That(rejected.RetryBgm, Is.False, index.ToString());
                Assert.That(rejected.RetryProfileAmbience, Is.False, index.ToString());
            }
        }

        [Test]
        public void ApplyProfile_RejectsWrongPersistentContracts()
        {
            AssertRejectsBgm(OrpheusPlaybackKind.OneShot2D, OrpheusCategory.Music,
                OrpheusLoadPolicy.PersistentStream);
            AssertRejectsBgm(OrpheusPlaybackKind.Bgm, OrpheusCategory.Ambience,
                OrpheusLoadPolicy.PersistentStream);
            AssertRejectsBgm(OrpheusPlaybackKind.Bgm, OrpheusCategory.Music,
                OrpheusLoadPolicy.ExplicitTransient);
            AssertRejectsAmbience(OrpheusPlaybackKind.Bgm, OrpheusCategory.Ambience,
                OrpheusLoadPolicy.PersistentStream);
            AssertRejectsAmbience(OrpheusPlaybackKind.ProfileAmbience, OrpheusCategory.Music,
                OrpheusLoadPolicy.PersistentStream);
            AssertRejectsAmbience(OrpheusPlaybackKind.ProfileAmbience, OrpheusCategory.Ambience,
                OrpheusLoadPolicy.ExplicitTransient);
        }

        [Test]
        public void ApplyProfile_DistinguishesCommitNoOpAndCoalescedRetryBits()
        {
            var catalog = CreateCatalog(
                Entry(100, OrpheusPlaybackKind.Bgm, OrpheusCategory.Music, OrpheusLoadPolicy.PersistentStream, 0),
                Entry(200, OrpheusPlaybackKind.ProfileAmbience, OrpheusCategory.Ambience,
                    OrpheusLoadPolicy.PersistentStream, 1));
            var intent = new OrpheusAudioProfileIntent(
                1,
                OrpheusBaseState.Peace,
                new OrpheusAudioKey(100),
                new OrpheusAudioKey(200));
            Assert.That(
                OrpheusAudioProfilePolicy.TryApplyProfile(
                    OrpheusAudioProfileState.Default,
                    intent,
                    catalog,
                    false,
                    false,
                    out var first),
                Is.True);

            Assert.That(
                OrpheusAudioProfilePolicy.TryApplyProfile(
                    first.State, intent, catalog, false, false, out var noOp),
                Is.True);
            Assert.That(noOp.Commit, Is.False);
            Assert.That(noOp.RetryBgm, Is.False);
            Assert.That(noOp.RetryProfileAmbience, Is.False);

            Assert.That(
                OrpheusAudioProfilePolicy.TryApplyProfile(
                    first.State, intent, catalog, true, true, out var retry),
                Is.True);
            Assert.That(retry.Commit, Is.False);
            Assert.That(retry.RetryBgm, Is.True);
            Assert.That(retry.RetryProfileAmbience, Is.True);

            var changed = new OrpheusAudioProfileIntent(
                1,
                OrpheusBaseState.Combat,
                intent.BgmKey,
                intent.ProfileAmbienceKey);
            Assert.That(
                OrpheusAudioProfilePolicy.TryApplyProfile(
                    first.State, changed, catalog, true, true, out var update),
                Is.True);
            Assert.That(update.Commit, Is.True);
            Assert.That(update.RetryBgm, Is.False);
            Assert.That(update.RetryProfileAmbience, Is.False);
        }

        [Test]
        public void Overlay_ValidatesSelectorAndArbitratesPauseMenuThenBase()
        {
            var state = OrpheusAudioProfileState.Default;
            var catalog = CreateCatalog();
            Assert.That(
                OrpheusAudioProfilePolicy.TryApplyProfile(
                    state,
                    new OrpheusAudioProfileIntent(
                        1, OrpheusBaseState.Combat, OrpheusAudioKey.Invalid, OrpheusAudioKey.Invalid),
                    catalog,
                    false,
                    false,
                    out var combat),
                Is.True);
            state = combat.State;
            Assert.That(state.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Combat));

            Assert.That(OrpheusAudioProfilePolicy.TrySetOverlay(
                state, OrpheusOverlay.Menu, true, out var menu), Is.True);
            Assert.That(menu.Commit, Is.True);
            Assert.That(menu.State.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Menu));
            Assert.That(OrpheusAudioProfilePolicy.TrySetOverlay(
                menu.State, OrpheusOverlay.Pause, true, out var pause), Is.True);
            Assert.That(pause.State.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Pause));
            Assert.That(OrpheusAudioProfilePolicy.TrySetOverlay(
                pause.State, OrpheusOverlay.Pause, false, out var backToMenu), Is.True);
            Assert.That(backToMenu.State.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Menu));
            Assert.That(OrpheusAudioProfilePolicy.TrySetOverlay(
                backToMenu.State, OrpheusOverlay.Menu, false, out var backToCombat), Is.True);
            Assert.That(backToCombat.State.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Combat));

            AssertInvalidSelector(state, OrpheusOverlay.None);
            AssertInvalidSelector(state, OrpheusOverlay.Menu | OrpheusOverlay.Pause);
            AssertInvalidSelector(state, (OrpheusOverlay)4);
            AssertInvalidSelector(state, (OrpheusOverlay)255);
        }

        [TestCase(0.015f, true)]
        [TestCase(5f, true)]
        [TestCase(0.0149f, false)]
        [TestCase(5.0001f, false)]
        [TestCase(float.NaN, false)]
        [TestCase(float.PositiveInfinity, false)]
        [TestCase(float.NegativeInfinity, false)]
        public void DurationPolicy_UsesFiniteInclusiveBounds(float value, bool expected)
        {
            Assert.That(OrpheusAudioDurationPolicy.IsValid(value), Is.EqualTo(expected));
        }

        private static void AssertInvalidSelector(
            OrpheusAudioProfileState state,
            OrpheusOverlay selector)
        {
            Assert.That(
                OrpheusAudioProfilePolicy.TrySetOverlay(state, selector, true, out var rejected),
                Is.False);
            Assert.That(rejected.Commit, Is.False);
        }

        private static void AssertRejectsBgm(
            OrpheusPlaybackKind kind,
            OrpheusCategory category,
            OrpheusLoadPolicy loadPolicy)
        {
            var catalog = CreateCatalog(Entry(100, kind, category, loadPolicy, 0));
            var intent = new OrpheusAudioProfileIntent(
                1, OrpheusBaseState.Peace, new OrpheusAudioKey(100), OrpheusAudioKey.Invalid);
            Assert.That(
                OrpheusAudioProfilePolicy.TryApplyProfile(
                    OrpheusAudioProfileState.Default,
                    intent,
                    catalog,
                    false,
                    false,
                    out _),
                Is.False);
        }

        private static void AssertRejectsAmbience(
            OrpheusPlaybackKind kind,
            OrpheusCategory category,
            OrpheusLoadPolicy loadPolicy)
        {
            var catalog = CreateCatalog(Entry(200, kind, category, loadPolicy, 0));
            var intent = new OrpheusAudioProfileIntent(
                1, OrpheusBaseState.Peace, OrpheusAudioKey.Invalid, new OrpheusAudioKey(200));
            Assert.That(
                OrpheusAudioProfilePolicy.TryApplyProfile(
                    OrpheusAudioProfileState.Default,
                    intent,
                    catalog,
                    false,
                    false,
                    out _),
                Is.False);
        }

        private static OrpheusCatalogLookupIndex CreateCatalog(params OrpheusCatalogEntry[] entries)
        {
            Assert.That(
                OrpheusCatalogLookupIndex.TryCreate(
                    entries,
                    entries.Length,
                    out var catalog,
                    out var error),
                Is.True,
                error.ToString());
            return catalog;
        }

        private static OrpheusCatalogEntry Entry(
            ushort key,
            OrpheusPlaybackKind kind,
            OrpheusCategory category,
            OrpheusLoadPolicy loadPolicy,
            int offset)
        {
            return new OrpheusCatalogEntry(
                new OrpheusAudioKey(key),
                new OrpheusCatalogScalarPolicy(
                    kind,
                    category,
                    loadPolicy,
                    new OrpheusVolumeRange(1f, 1f),
                    new OrpheusPitchRange(1f, 1f),
                    new OrpheusVoicePolicy(128, 1, 0f),
                    new OrpheusSpatialAttenuation(1f, 10f, OrpheusRolloffMode.Logarithmic)),
                new OrpheusClipRange(offset, 1));
        }
    }
}
