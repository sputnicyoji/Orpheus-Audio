using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusOneShotPolicyTests
    {
        [TestCase((byte)OrpheusOneShotAdmissionStage.MainThread, (byte)OrpheusOneShotAdmissionResult.WrongThreadRejected)]
        [TestCase((byte)OrpheusOneShotAdmissionStage.Availability, (byte)OrpheusOneShotAdmissionResult.UnavailableRejected)]
        [TestCase((byte)OrpheusOneShotAdmissionStage.Key, (byte)OrpheusOneShotAdmissionResult.InvalidKeyRejected)]
        [TestCase((byte)OrpheusOneShotAdmissionStage.PlaybackKind, (byte)OrpheusOneShotAdmissionResult.PlaybackKindRejected)]
        [TestCase((byte)OrpheusOneShotAdmissionStage.Position, (byte)OrpheusOneShotAdmissionResult.InvalidPositionRejected)]
        [TestCase((byte)OrpheusOneShotAdmissionStage.Activation, (byte)OrpheusOneShotAdmissionResult.PreReadyRejected)]
        [TestCase((byte)OrpheusOneShotAdmissionStage.Transport, (byte)OrpheusOneShotAdmissionResult.SuspendedRejected)]
        [TestCase((byte)OrpheusOneShotAdmissionStage.Cooldown, (byte)OrpheusOneShotAdmissionResult.CooldownRejected)]
        [TestCase((byte)OrpheusOneShotAdmissionStage.Distance, (byte)OrpheusOneShotAdmissionResult.DistanceRejected)]
        [TestCase((byte)OrpheusOneShotAdmissionStage.Polyphony, (byte)OrpheusOneShotAdmissionResult.PolyphonyRejected)]
        [TestCase((byte)OrpheusOneShotAdmissionStage.PoolCapacity, (byte)OrpheusOneShotAdmissionResult.PoolCapacityRejected)]
        public void Admission_StopsAtFirstRejectedStageAndCannotAdvanceLaterState(
            byte rejectedStageValue,
            byte expectedValue)
        {
            var rejectedStage = (OrpheusOneShotAdmissionStage)rejectedStageValue;
            var expected = (OrpheusOneShotAdmissionResult)expectedValue;
            var admission = new OrpheusOneShotAdmission();
            for (var rawStage = (byte)OrpheusOneShotAdmissionStage.MainThread;
                 rawStage < (byte)OrpheusOneShotAdmissionStage.Complete;
                 rawStage++)
            {
                var stage = (OrpheusOneShotAdmissionStage)rawStage;
                bool passed;
                if (stage == OrpheusOneShotAdmissionStage.Load)
                {
                    passed = admission.PassLoad(OrpheusClipLoadState.Loaded);
                }
                else
                {
                    passed = admission.Pass(stage, stage != rejectedStage);
                }

                if (stage == rejectedStage)
                {
                    Assert.That(passed, Is.False);
                    break;
                }

                Assert.That(passed, Is.True);
            }

            Assert.That(admission.Result, Is.EqualTo(expected));
            Assert.That(
                admission.Pass(OrpheusOneShotAdmissionStage.PoolCapacity, true),
                Is.False);
            Assert.That(admission.Result, Is.EqualTo(expected));
        }

        [TestCase(OrpheusClipLoadState.Unloaded, (byte)OrpheusOneShotAdmissionResult.LoadNotReadyRejected)]
        [TestCase(OrpheusClipLoadState.Loading, (byte)OrpheusOneShotAdmissionResult.LoadNotReadyRejected)]
        [TestCase(OrpheusClipLoadState.Failed, (byte)OrpheusOneShotAdmissionResult.LoadFailed)]
        [TestCase(OrpheusClipLoadState.Invalid, (byte)OrpheusOneShotAdmissionResult.RuntimeInvalid)]
        public void Admission_LoadStageMapsAggregateStateWithoutAdvancingLaterStages(
            OrpheusClipLoadState state,
            byte expectedValue)
        {
            var expected = (OrpheusOneShotAdmissionResult)expectedValue;
            var admission = AdvanceToLoad();

            Assert.That(admission.PassLoad(state), Is.False);
            Assert.That(admission.Result, Is.EqualTo(expected));
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.Cooldown, true), Is.False);
            Assert.That(admission.Result, Is.EqualTo(expected));
        }

        [Test]
        public void Admission_AllStagesPassInNormativeOrderBeforeAcceptance()
        {
            var admission = new OrpheusOneShotAdmission();
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.MainThread, true), Is.True);
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.Availability, true), Is.True);
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.Key, true), Is.True);
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.PlaybackKind, true), Is.True);
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.Position, true), Is.True);
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.Activation, true), Is.True);
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.Transport, true), Is.True);
            Assert.That(admission.PassLoad(OrpheusClipLoadState.Loaded), Is.True);
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.Cooldown, true), Is.True);
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.Distance, true), Is.True);
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.Polyphony, true), Is.True);
            Assert.That(admission.Pass(OrpheusOneShotAdmissionStage.PoolCapacity, true), Is.True);
            Assert.That(admission.Result, Is.EqualTo(OrpheusOneShotAdmissionResult.Accepted));
        }

        [Test]
        public void FindFirstFree_ReturnsLowestFreeSlotWithoutMutation()
        {
            var slots = new OrpheusOneShotSlot[4];
            slots[0].Start(new OrpheusAudioKey(10), 1f);
            slots[2].Start(new OrpheusAudioKey(12), 1f);

            Assert.That(OrpheusOneShotPolicy.FindFirstFree(slots), Is.EqualTo(1));
            Assert.That(slots[0].State, Is.EqualTo(OrpheusOneShotSlotState.PlayingOneShot));
            Assert.That(slots[1].State, Is.EqualTo(OrpheusOneShotSlotState.Free));
            Assert.That(slots[2].State, Is.EqualTo(OrpheusOneShotSlotState.PlayingOneShot));
        }

        [Test]
        public void FindFirstFree_ReturnsMinusOneWhenAllSlotsArePlaying()
        {
            var slots = new OrpheusOneShotSlot[2];
            slots[0].Start(new OrpheusAudioKey(1), 1f);
            slots[1].Start(new OrpheusAudioKey(2), 1f);

            Assert.That(OrpheusOneShotPolicy.FindFirstFree(slots), Is.EqualTo(-1));
        }

        [Test]
        public void CountFutureKey_CountsCurrentAndPendingAtMostOncePerSlot()
        {
            var key = new OrpheusAudioKey(9);
            var slots = new OrpheusOneShotSlot[3];
            slots[0].Start(key, OrpheusCategory.SfxCombat, 100, 1f);
            slots[0].ScheduleReplacement(key, OrpheusCategory.SfxCombat, 100);
            slots[1].Start(new OrpheusAudioKey(10), OrpheusCategory.SfxWorld, 100, 1f);
            slots[1].ScheduleReplacement(key, OrpheusCategory.SfxCombat, 100);

            Assert.That(OrpheusOneShotPolicy.CountFutureKey(slots, key), Is.EqualTo(2));
        }

        [Test]
        public void FindSameKeyVictim_RequiresExactMinimumAgeAndUsesOldestThenLowestIndex()
        {
            var key = new OrpheusAudioKey(9);
            var slots = new OrpheusOneShotSlot[4];
            slots[0].Start(key, OrpheusCategory.SfxCombat, 100, 1f);
            slots[0].Advance(0.049999f);
            slots[1].Start(key, OrpheusCategory.SfxCombat, 100, 1f);
            slots[1].Advance(0.05f);
            slots[2].Start(key, OrpheusCategory.SfxCombat, 100, 1f);
            slots[2].Advance(0.2f);
            slots[3].Start(key, OrpheusCategory.SfxCombat, 100, 1f);
            slots[3].Advance(0.2f);

            Assert.That(OrpheusOneShotPolicy.FindSameKeyVictim(slots, key), Is.EqualTo(2));

            slots[2].ScheduleReplacement(new OrpheusAudioKey(20), OrpheusCategory.SfxUi, 10);
            Assert.That(OrpheusOneShotPolicy.FindSameKeyVictim(slots, key), Is.EqualTo(3));
        }

        [Test]
        public void FindAdmissionSlot_ReservesTwoProjectedSlotsForUi()
        {
            var slots = new OrpheusOneShotSlot[4];
            slots[0].Start(new OrpheusAudioKey(1), OrpheusCategory.SfxCombat, 200, 1f);
            slots[1].Start(new OrpheusAudioKey(2), OrpheusCategory.SfxWorld, 200, 1f);

            Assert.That(
                OrpheusOneShotPolicy.FindAdmissionSlot(
                    slots,
                    OrpheusCategory.SfxCombat,
                    100),
                Is.EqualTo(-1));
            Assert.That(
                OrpheusOneShotPolicy.FindAdmissionSlot(
                    slots,
                    OrpheusCategory.SfxUi,
                    100),
                Is.EqualTo(2));
        }

        [Test]
        public void FindAdmissionSlot_RejectsNonUiWhenProjectedNonUiAlreadyExceedsCap()
        {
            var slots = new OrpheusOneShotSlot[4];
            StartAtAge(ref slots[0], 1, OrpheusCategory.SfxCombat, 200, 0.2f);
            StartAtAge(ref slots[1], 2, OrpheusCategory.SfxWorld, 200, 0.2f);
            StartAtAge(ref slots[2], 3, OrpheusCategory.SfxCombat, 200, 0.2f);

            Assert.That(OrpheusOneShotPolicy.CountProjectedNonUi(slots), Is.EqualTo(3));
            Assert.That(
                OrpheusOneShotPolicy.FindAdmissionSlot(
                    slots,
                    OrpheusCategory.SfxWorld,
                    100),
                Is.EqualTo(-1));
            Assert.That(
                OrpheusOneShotPolicy.FindAdmissionSlot(
                    slots,
                    OrpheusCategory.SfxUi,
                    100),
                Is.EqualTo(3));
        }

        [Test]
        public void FindAdmissionSlot_UsesPendingCategoryForProjectedReservation()
        {
            var slots = new OrpheusOneShotSlot[4];
            slots[0].Start(new OrpheusAudioKey(1), OrpheusCategory.SfxCombat, 200, 1f);
            slots[0].ScheduleReplacement(new OrpheusAudioKey(2), OrpheusCategory.SfxUi, 10);
            slots[1].Start(new OrpheusAudioKey(3), OrpheusCategory.SfxWorld, 200, 1f);

            Assert.That(OrpheusOneShotPolicy.CountProjectedNonUi(slots), Is.EqualTo(1));
            Assert.That(
                OrpheusOneShotPolicy.FindAdmissionSlot(
                    slots,
                    OrpheusCategory.SfxCombat,
                    100),
                Is.EqualTo(2));
        }

        [Test]
        public void FindAdmissionSlot_OrdinaryVictimUsesPriorityThenAgeThenIndex()
        {
            var slots = new OrpheusOneShotSlot[4];
            StartAtAge(ref slots[0], 1, OrpheusCategory.SfxCombat, 150, 0.2f);
            StartAtAge(ref slots[1], 2, OrpheusCategory.SfxWorld, 200, 0.1f);
            StartAtAge(ref slots[2], 3, OrpheusCategory.SfxUi, 200, 0.3f);
            StartAtAge(ref slots[3], 4, OrpheusCategory.SfxUi, 200, 0.3f);

            Assert.That(
                OrpheusOneShotPolicy.FindAdmissionSlot(
                    slots,
                    OrpheusCategory.SfxUi,
                    150),
                Is.EqualTo(2));
            Assert.That(
                OrpheusOneShotPolicy.FindAdmissionSlot(
                    slots,
                    OrpheusCategory.SfxCombat,
                    150),
                Is.EqualTo(1));
        }

        [Test]
        public void FindAdmissionSlot_RejectsTooYoungHigherPriorityAndFadingVictims()
        {
            var slots = new OrpheusOneShotSlot[3];
            StartAtAge(ref slots[0], 1, OrpheusCategory.SfxUi, 200, 0.049999f);
            StartAtAge(ref slots[1], 2, OrpheusCategory.SfxUi, 99, 0.5f);
            StartAtAge(ref slots[2], 3, OrpheusCategory.SfxUi, 200, 0.5f);
            slots[2].ScheduleReplacement(new OrpheusAudioKey(4), OrpheusCategory.SfxUi, 100);

            Assert.That(
                OrpheusOneShotPolicy.FindAdmissionSlot(
                    slots,
                    OrpheusCategory.SfxUi,
                    100),
                Is.EqualTo(-1));
        }

        [Test]
        public void RejectedSelection_DoesNotMutateCurrentOrPendingState()
        {
            var slots = new OrpheusOneShotSlot[2];
            StartAtAge(ref slots[0], 1, OrpheusCategory.SfxCombat, 50, 0.25f);
            StartAtAge(ref slots[1], 2, OrpheusCategory.SfxWorld, 50, 0.25f);
            slots[1].ScheduleReplacement(new OrpheusAudioKey(3), OrpheusCategory.SfxUi, 10);

            Assert.That(
                OrpheusOneShotPolicy.FindAdmissionSlot(
                    slots,
                    OrpheusCategory.SfxCombat,
                    100),
                Is.EqualTo(-1));
            Assert.That(slots[0].State, Is.EqualTo(OrpheusOneShotSlotState.PlayingOneShot));
            Assert.That(slots[0].Key, Is.EqualTo(new OrpheusAudioKey(1)));
            Assert.That(slots[0].ActiveElapsed, Is.EqualTo(0.25f));
            Assert.That(slots[1].State, Is.EqualTo(OrpheusOneShotSlotState.FadingOut));
            Assert.That(slots[1].Key, Is.EqualTo(new OrpheusAudioKey(2)));
            Assert.That(slots[1].PendingKey, Is.EqualTo(new OrpheusAudioKey(3)));
        }

        [Test]
        public void Slot_AdvancesOnlyByProvidedActiveDeltaAndCompletesAtExactBoundary()
        {
            var slot = new OrpheusOneShotSlot();
            slot.Start(new OrpheusAudioKey(7), 1.5f);

            Assert.That(slot.Advance(0f), Is.False);
            Assert.That(slot.ActiveElapsed, Is.Zero);
            Assert.That(slot.Advance(1f), Is.False);
            Assert.That(slot.ActiveElapsed, Is.EqualTo(1f));
            Assert.That(slot.Advance(0.5f), Is.True);
            Assert.That(slot.ActiveElapsed, Is.EqualTo(1.5f));
        }

        [Test]
        public void Slot_ClearRestoresCompleteFreeState()
        {
            var slot = new OrpheusOneShotSlot();
            slot.Start(new OrpheusAudioKey(23), 2f);
            slot.Advance(0.25f);

            slot.Clear();

            Assert.That(slot.State, Is.EqualTo(OrpheusOneShotSlotState.Free));
            Assert.That(slot.Key, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(slot.ActiveElapsed, Is.Zero);
            Assert.That(slot.LogicalDuration, Is.Zero);
            Assert.That(slot.Advance(10f), Is.False);
        }

        private static OrpheusOneShotAdmission AdvanceToLoad()
        {
            var admission = new OrpheusOneShotAdmission();
            admission.Pass(OrpheusOneShotAdmissionStage.MainThread, true);
            admission.Pass(OrpheusOneShotAdmissionStage.Availability, true);
            admission.Pass(OrpheusOneShotAdmissionStage.Key, true);
            admission.Pass(OrpheusOneShotAdmissionStage.PlaybackKind, true);
            admission.Pass(OrpheusOneShotAdmissionStage.Position, true);
            admission.Pass(OrpheusOneShotAdmissionStage.Activation, true);
            admission.Pass(OrpheusOneShotAdmissionStage.Transport, true);
            return admission;
        }

        private static void StartAtAge(
            ref OrpheusOneShotSlot slot,
            ushort key,
            OrpheusCategory category,
            byte priority,
            float age)
        {
            slot.Start(new OrpheusAudioKey(key), category, priority, 1f);
            slot.Advance(age);
        }
    }
}
