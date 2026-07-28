using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioStopPolicyTests
    {
        private static readonly OrpheusAudioKey CurrentKey = new OrpheusAudioKey(100);
        private static readonly OrpheusAudioKey PendingKey = new OrpheusAudioKey(200);

        [TestCase(OrpheusBus.Master, true)]
        [TestCase(OrpheusBus.Music, true)]
        [TestCase(OrpheusBus.SfxCombat, true)]
        [TestCase(OrpheusBus.SfxWorld, true)]
        [TestCase(OrpheusBus.SfxUi, true)]
        [TestCase(OrpheusBus.Ambience, true)]
        [TestCase(OrpheusBus.Invalid, false)]
        [TestCase((OrpheusBus)7, false)]
        [TestCase((OrpheusBus)255, false)]
        public void IsValidBus_AcceptsOnlySixDeclaredValues(
            OrpheusBus bus,
            bool expected)
        {
            Assert.That(OrpheusAudioStopPolicy.IsValidBus(bus), Is.EqualTo(expected));
        }

        [Test]
        public void MatchesCategory_UsesExplicitBusinessMapping()
        {
            AssertMatchesOnly(OrpheusBus.Music, OrpheusCategory.Music);
            AssertMatchesOnly(OrpheusBus.SfxCombat, OrpheusCategory.SfxCombat);
            AssertMatchesOnly(OrpheusBus.SfxWorld, OrpheusCategory.SfxWorld);
            AssertMatchesOnly(OrpheusBus.SfxUi, OrpheusCategory.SfxUi);
            AssertMatchesOnly(OrpheusBus.Ambience, OrpheusCategory.Ambience);

            for (var index = 0; index < ValidCategories.Length; index++)
            {
                Assert.That(
                    OrpheusAudioStopPolicy.MatchesCategory(
                        OrpheusBus.Master,
                        ValidCategories[index]),
                    Is.True,
                    ValidCategories[index].ToString());
            }

            Assert.That((byte)OrpheusBus.Music, Is.Not.EqualTo((byte)OrpheusCategory.Music));
            Assert.That(
                OrpheusAudioStopPolicy.MatchesCategory(
                    OrpheusBus.Music,
                    OrpheusCategory.Music),
                Is.True);
        }

        [TestCase(OrpheusBus.Invalid)]
        [TestCase((OrpheusBus)7)]
        [TestCase((OrpheusBus)255)]
        public void MatchesCategory_InvalidBusMatchesNothing(OrpheusBus bus)
        {
            for (var index = 0; index < ValidCategories.Length; index++)
            {
                Assert.That(
                    OrpheusAudioStopPolicy.MatchesCategory(bus, ValidCategories[index]),
                    Is.False,
                    ValidCategories[index].ToString());
            }
        }

        [TestCase(OrpheusBus.Master)]
        [TestCase(OrpheusBus.Music)]
        [TestCase(OrpheusBus.SfxCombat)]
        [TestCase(OrpheusBus.SfxWorld)]
        [TestCase(OrpheusBus.SfxUi)]
        [TestCase(OrpheusBus.Ambience)]
        public void MatchesCategory_InvalidCategoryMatchesNoBus(OrpheusBus bus)
        {
            Assert.That(
                OrpheusAudioStopPolicy.MatchesCategory(bus, OrpheusCategory.Invalid),
                Is.False);
            Assert.That(
                OrpheusAudioStopPolicy.MatchesCategory(bus, (OrpheusCategory)6),
                Is.False);
            Assert.That(
                OrpheusAudioStopPolicy.MatchesCategory(bus, (OrpheusCategory)255),
                Is.False);
        }

        [Test]
        public void ProjectProfileIntent_ClearsOnlySelectedPersistentKeys()
        {
            var current = new OrpheusAudioProfileIntent(
                77u,
                OrpheusBaseState.Combat,
                CurrentKey,
                PendingKey);

            AssertProjected(
                current,
                OrpheusBus.Music,
                OrpheusAudioKey.Invalid,
                PendingKey);
            AssertProjected(
                current,
                OrpheusBus.Ambience,
                CurrentKey,
                OrpheusAudioKey.Invalid);
            AssertProjected(
                current,
                OrpheusBus.Master,
                OrpheusAudioKey.Invalid,
                OrpheusAudioKey.Invalid);
            AssertProjected(current, OrpheusBus.SfxCombat, CurrentKey, PendingKey);
            AssertProjected(current, OrpheusBus.SfxWorld, CurrentKey, PendingKey);
            AssertProjected(current, OrpheusBus.SfxUi, CurrentKey, PendingKey);
            AssertProjected(current, OrpheusBus.Invalid, CurrentKey, PendingKey);
            AssertProjected(current, (OrpheusBus)255, CurrentKey, PendingKey);
        }

        [Test]
        public void EvaluateTransient_UsesIndependentCurrentAndPendingCategories()
        {
            var playing = CreatePlaying(OrpheusCategory.SfxWorld);
            Assert.That(
                OrpheusAudioStopPolicy.EvaluateTransient(
                    OrpheusBus.SfxWorld,
                    playing),
                Is.EqualTo(OrpheusTransientStopAction.BeginFadeOut));
            Assert.That(
                OrpheusAudioStopPolicy.EvaluateTransient(
                    OrpheusBus.SfxCombat,
                    playing),
                Is.EqualTo(OrpheusTransientStopAction.None));

            var fading = CreateFading(
                OrpheusCategory.SfxWorld,
                OrpheusCategory.SfxCombat);
            Assert.That(
                OrpheusAudioStopPolicy.EvaluateTransient(
                    OrpheusBus.SfxCombat,
                    fading),
                Is.EqualTo(OrpheusTransientStopAction.CancelPending));
            Assert.That(
                OrpheusAudioStopPolicy.EvaluateTransient(
                    OrpheusBus.SfxWorld,
                    fading),
                Is.EqualTo(OrpheusTransientStopAction.None));
            Assert.That(
                OrpheusAudioStopPolicy.EvaluateTransient(
                    OrpheusBus.Master,
                    fading),
                Is.EqualTo(OrpheusTransientStopAction.CancelPending));
        }

        [Test]
        public void EvaluateTransient_FreeInvalidOrUnmatchedStateIsNoOp()
        {
            var free = default(OrpheusOneShotSlot);
            var playing = CreatePlaying(OrpheusCategory.Music);

            Assert.That(
                OrpheusAudioStopPolicy.EvaluateTransient(OrpheusBus.Master, free),
                Is.EqualTo(OrpheusTransientStopAction.None));
            Assert.That(
                OrpheusAudioStopPolicy.EvaluateTransient(OrpheusBus.Invalid, playing),
                Is.EqualTo(OrpheusTransientStopAction.None));
            Assert.That(
                OrpheusAudioStopPolicy.EvaluateTransient((OrpheusBus)255, playing),
                Is.EqualTo(OrpheusTransientStopAction.None));
        }

        [Test]
        public void Slot_BeginFadeOutCreatesNoPendingAndPreservesCurrentIdentity()
        {
            Assert.That(OrpheusOneShotPolicy.FadeDurationSeconds, Is.EqualTo(0.015f));
            var position = new OrpheusPosition3(1f, 2f, 3f);
            var slot = default(OrpheusOneShotSlot);
            slot.Start(
                CurrentKey,
                OrpheusCategory.SfxWorld,
                77,
                3f,
                new OrpheusSpatialOneShotState(position, 144d));
            slot.Advance(0.25f);

            Assert.That(slot.BeginFadeOut(), Is.True);

            Assert.That(slot.State, Is.EqualTo(OrpheusOneShotSlotState.FadingOut));
            Assert.That(slot.Key, Is.EqualTo(CurrentKey));
            Assert.That(slot.Category, Is.EqualTo(OrpheusCategory.SfxWorld));
            Assert.That(slot.Priority, Is.EqualTo(77));
            Assert.That(slot.ActiveElapsed, Is.EqualTo(0.25f));
            Assert.That(slot.LogicalDuration, Is.EqualTo(3f));
            Assert.That(slot.Position.X, Is.EqualTo(1f));
            Assert.That(slot.MaximumDistanceSquared, Is.EqualTo(144d));
            Assert.That(slot.HasPending, Is.False);
            Assert.That(slot.PendingKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(slot.BeginFadeOut(), Is.False);
        }

        [Test]
        public void Slot_BeginFadeOutDoesNothingWhenFree()
        {
            var slot = default(OrpheusOneShotSlot);

            Assert.That(slot.BeginFadeOut(), Is.False);
            Assert.That(slot.State, Is.EqualTo(OrpheusOneShotSlotState.Free));
        }

        [Test]
        public void Slot_CancelPendingClearsOnlyTargetIdentityAndIsIdempotent()
        {
            var currentPosition = new OrpheusPosition3(1f, 2f, 3f);
            var pendingPosition = new OrpheusPosition3(4f, 5f, 6f);
            var slot = default(OrpheusOneShotSlot);
            slot.Start(
                CurrentKey,
                OrpheusCategory.SfxWorld,
                77,
                3f,
                new OrpheusSpatialOneShotState(currentPosition, 144d));
            slot.Advance(0.25f);
            slot.ScheduleReplacement(
                PendingKey,
                OrpheusCategory.SfxCombat,
                12,
                new OrpheusSpatialOneShotState(pendingPosition, 400d));

            Assert.That(slot.CancelPending(), Is.True);

            Assert.That(slot.State, Is.EqualTo(OrpheusOneShotSlotState.FadingOut));
            Assert.That(slot.Key, Is.EqualTo(CurrentKey));
            Assert.That(slot.Category, Is.EqualTo(OrpheusCategory.SfxWorld));
            Assert.That(slot.Priority, Is.EqualTo(77));
            Assert.That(slot.ActiveElapsed, Is.EqualTo(0.25f));
            Assert.That(slot.LogicalDuration, Is.EqualTo(3f));
            Assert.That(slot.Position.X, Is.EqualTo(1f));
            Assert.That(slot.MaximumDistanceSquared, Is.EqualTo(144d));
            Assert.That(slot.HasPending, Is.False);
            Assert.That(slot.PendingKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(slot.PendingCategory, Is.EqualTo(OrpheusCategory.Invalid));
            Assert.That(slot.PendingPriority, Is.Zero);
            Assert.That(slot.PendingPosition.X, Is.Zero);
            Assert.That(slot.PendingMaximumDistanceSquared, Is.Zero);
            Assert.That(slot.CancelPending(), Is.False);
        }

        private static readonly OrpheusCategory[] ValidCategories =
        {
            OrpheusCategory.Music,
            OrpheusCategory.SfxCombat,
            OrpheusCategory.SfxWorld,
            OrpheusCategory.SfxUi,
            OrpheusCategory.Ambience
        };

        private static void AssertMatchesOnly(
            OrpheusBus bus,
            OrpheusCategory expectedCategory)
        {
            for (var index = 0; index < ValidCategories.Length; index++)
            {
                var category = ValidCategories[index];
                Assert.That(
                    OrpheusAudioStopPolicy.MatchesCategory(bus, category),
                    Is.EqualTo(category == expectedCategory),
                    category.ToString());
            }
        }

        private static void AssertProjected(
            OrpheusAudioProfileIntent current,
            OrpheusBus bus,
            OrpheusAudioKey expectedBgm,
            OrpheusAudioKey expectedAmbience)
        {
            var projected = OrpheusAudioStopPolicy.ProjectProfileIntent(current, bus);

            Assert.That(projected.ProfileId, Is.EqualTo(77u), bus.ToString());
            Assert.That(projected.BaseState, Is.EqualTo(OrpheusBaseState.Combat), bus.ToString());
            Assert.That(projected.BgmKey, Is.EqualTo(expectedBgm), bus.ToString());
            Assert.That(
                projected.ProfileAmbienceKey,
                Is.EqualTo(expectedAmbience),
                bus.ToString());
        }

        private static OrpheusOneShotSlot CreatePlaying(OrpheusCategory category)
        {
            var slot = default(OrpheusOneShotSlot);
            slot.Start(CurrentKey, category, 77, 3f);
            return slot;
        }

        private static OrpheusOneShotSlot CreateFading(
            OrpheusCategory currentCategory,
            OrpheusCategory pendingCategory)
        {
            var slot = CreatePlaying(currentCategory);
            slot.ScheduleReplacement(PendingKey, pendingCategory, 12);
            return slot;
        }
    }
}
