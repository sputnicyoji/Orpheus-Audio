using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusSpatialOneShotPolicyTests
    {
        [TestCase(float.NaN, 0f, 0f)]
        [TestCase(float.PositiveInfinity, 0f, 0f)]
        [TestCase(float.NegativeInfinity, 0f, 0f)]
        [TestCase(0f, float.NaN, 0f)]
        [TestCase(0f, float.PositiveInfinity, 0f)]
        [TestCase(0f, float.NegativeInfinity, 0f)]
        [TestCase(0f, 0f, float.NaN)]
        [TestCase(0f, 0f, float.PositiveInfinity)]
        [TestCase(0f, 0f, float.NegativeInfinity)]
        public void Position3_RejectsEveryNonFiniteComponent(float x, float y, float z)
        {
            Assert.That(new OrpheusPosition3(x, y, z).IsFinite, Is.False);
            Assert.That(new OrpheusPosition3(1f, -2f, 3f).IsFinite, Is.True);
        }

        [Test]
        public void DistancePolicy_UsesDoublePrecisionSquaredDistanceAndAcceptsBoundary()
        {
            var source = new OrpheusPosition3(3f, 4f, 12f);
            var listener = new OrpheusPosition3(0f, 0f, 0f);
            var squaredDistance = OrpheusOneShotPolicy.CalculateSquaredDistance(
                source,
                listener);
            var maximumSquared = OrpheusOneShotPolicy.CalculateMaximumDistanceSquared(13f);

            Assert.That(squaredDistance, Is.EqualTo(169d));
            Assert.That(maximumSquared, Is.EqualTo(169d));
            Assert.That(
                OrpheusOneShotPolicy.IsWithinMaximumDistance(
                    squaredDistance,
                    maximumSquared),
                Is.True);
            Assert.That(
                OrpheusOneShotPolicy.IsWithinMaximumDistance(
                    squaredDistance + 0.0001d,
                    maximumSquared),
                Is.False);
        }

        [Test]
        public void DistanceRatio_IsSquaredAndNeverClamped()
        {
            Assert.That(
                OrpheusOneShotPolicy.CalculateDistanceRatio(400d, 100d),
                Is.EqualTo(4d));
        }

        [Test]
        public void Find3DAdmissionSlot_PrefersLowestFreeAndOverwritesWholeBuffer()
        {
            var slots = new OrpheusOneShotSlot[4];
            StartSpatialAtAge(ref slots[0], 1, 200, 0.1f, 1f, 10f);
            StartSpatialAtAge(ref slots[2], 2, 200, 0.1f, 2f, 10f);
            var ratios = new[] { -1d, -1d, -1d, -1d };

            var selected = OrpheusOneShotPolicy.Find3DAdmissionSlot(
                slots,
                100,
                new OrpheusPosition3(0f, 0f, 0f),
                ratios);

            Assert.That(selected, Is.EqualTo(1));
            Assert.That(ratios, Is.EqualTo(new[] { 0.01d, 0d, 0.04d, 0d }));
        }

        [Test]
        public void Find3DAdmissionSlot_UsesPriorityThenDistanceThenAgeThenIndex()
        {
            var listener = new OrpheusPosition3(0f, 0f, 0f);
            var ratios = new double[4];
            var slots = new OrpheusOneShotSlot[4];

            StartSpatialAtAge(ref slots[0], 1, 201, 0.05f, 1f, 10f);
            StartSpatialAtAge(ref slots[1], 2, 200, 0.5f, 9f, 10f);
            StartSpatialAtAge(ref slots[2], 3, 200, 0.4f, 8f, 10f);
            StartSpatialAtAge(ref slots[3], 4, 200, 0.4f, 8f, 10f);
            Assert.That(
                OrpheusOneShotPolicy.Find3DAdmissionSlot(
                    slots,
                    100,
                    listener,
                    ratios),
                Is.EqualTo(0),
                "Priority must dominate every later comparator.");

            slots[0].Clear();
            StartSpatialAtAge(ref slots[0], 1, 200, 0.05f, 1f, 10f);
            Assert.That(
                OrpheusOneShotPolicy.Find3DAdmissionSlot(
                    slots,
                    100,
                    listener,
                    ratios),
                Is.EqualTo(1),
                "Distance must dominate age.");

            slots[1].Clear();
            StartSpatialAtAge(ref slots[1], 2, 200, 0.3f, 8f, 10f);
            Assert.That(
                OrpheusOneShotPolicy.Find3DAdmissionSlot(
                    slots,
                    100,
                    listener,
                    ratios),
                Is.EqualTo(2),
                "Age must dominate slot index.");

            slots[1].Clear();
            StartSpatialAtAge(ref slots[1], 2, 200, 0.4f, 8f, 10f);
            Assert.That(
                OrpheusOneShotPolicy.Find3DAdmissionSlot(
                    slots,
                    100,
                    listener,
                    ratios),
                Is.EqualTo(1),
                "Smaller slot index must break an exact tie.");
        }

        [Test]
        public void Find3DAdmissionSlot_EnforcesExactAgePriorityAndStateEligibility()
        {
            var slots = new OrpheusOneShotSlot[4];
            StartSpatialAtAge(ref slots[0], 1, 200, 0.049999f, 1f, 10f);
            StartSpatialAtAge(ref slots[1], 2, 99, 0.5f, 2f, 10f);
            StartSpatialAtAge(ref slots[2], 3, 100, 0.05f, 3f, 10f);
            StartSpatialAtAge(ref slots[3], 4, 200, 0.5f, 4f, 10f);
            slots[3].ScheduleReplacement(
                new OrpheusAudioKey(5),
                OrpheusCategory.SfxWorld,
                100,
                new OrpheusSpatialOneShotState(
                    new OrpheusPosition3(5f, 0f, 0f),
                    100d));

            Assert.That(
                OrpheusOneShotPolicy.Find3DAdmissionSlot(
                    slots,
                    100,
                    new OrpheusPosition3(0f, 0f, 0f),
                    new double[4]),
                Is.EqualTo(2));
        }

        [Test]
        public void Find3DAdmissionSlot_RecomputesVictimDistanceAfterListenerMovement()
        {
            var slots = new OrpheusOneShotSlot[2];
            StartSpatialAtAge(ref slots[0], 1, 200, 0.1f, 0f, 10f);
            StartSpatialAtAge(ref slots[1], 2, 200, 0.1f, 10f, 10f);
            var ratios = new double[2];

            Assert.That(
                OrpheusOneShotPolicy.Find3DAdmissionSlot(
                    slots,
                    100,
                    new OrpheusPosition3(0f, 0f, 0f),
                    ratios),
                Is.EqualTo(1));
            Assert.That(
                OrpheusOneShotPolicy.Find3DAdmissionSlot(
                    slots,
                    100,
                    new OrpheusPosition3(10f, 0f, 0f),
                    ratios),
                Is.EqualTo(0));
        }

        [Test]
        public void Slot_StartPendingTransfersSpatialIdentityAtomically()
        {
            var slot = new OrpheusOneShotSlot();
            slot.Start(
                new OrpheusAudioKey(1),
                OrpheusCategory.SfxCombat,
                200,
                1f,
                new OrpheusSpatialOneShotState(
                    new OrpheusPosition3(1f, 2f, 3f),
                    100d));
            slot.ScheduleReplacement(
                new OrpheusAudioKey(2),
                OrpheusCategory.SfxWorld,
                100,
                new OrpheusSpatialOneShotState(
                    new OrpheusPosition3(4f, 5f, 6f),
                    400d));

            slot.StartPending(2f);

            Assert.That(slot.Key, Is.EqualTo(new OrpheusAudioKey(2)));
            Assert.That(slot.Position, Is.EqualTo(new OrpheusPosition3(4f, 5f, 6f)));
            Assert.That(slot.MaximumDistanceSquared, Is.EqualTo(400d));
            Assert.That(slot.HasPending, Is.False);
        }

        [Test]
        public void Rejected3DSelection_DoesNotMutateCurrentOrPendingSpatialState()
        {
            var current = new OrpheusPosition3(1f, 2f, 3f);
            var pending = new OrpheusPosition3(4f, 5f, 6f);
            var slots = new OrpheusOneShotSlot[1];
            slots[0].Start(
                new OrpheusAudioKey(1),
                OrpheusCategory.SfxCombat,
                50,
                1f,
                new OrpheusSpatialOneShotState(current, 100d));
            slots[0].Advance(0.5f);
            slots[0].ScheduleReplacement(
                new OrpheusAudioKey(2),
                OrpheusCategory.SfxWorld,
                10,
                new OrpheusSpatialOneShotState(pending, 400d));

            Assert.That(
                OrpheusOneShotPolicy.Find3DAdmissionSlot(
                    slots,
                    100,
                    new OrpheusPosition3(0f, 0f, 0f),
                    new double[1]),
                Is.EqualTo(-1));
            Assert.That(slots[0].Position, Is.EqualTo(current));
            Assert.That(slots[0].MaximumDistanceSquared, Is.EqualTo(100d));
            Assert.That(slots[0].PendingPosition, Is.EqualTo(pending));
            Assert.That(slots[0].PendingMaximumDistanceSquared, Is.EqualTo(400d));
        }

        private static void StartSpatialAtAge(
            ref OrpheusOneShotSlot slot,
            ushort key,
            byte priority,
            float age,
            float positionX,
            float maximumDistance)
        {
            slot.Start(
                new OrpheusAudioKey(key),
                OrpheusCategory.SfxWorld,
                priority,
                1f,
                new OrpheusSpatialOneShotState(
                    new OrpheusPosition3(positionX, 0f, 0f),
                    OrpheusOneShotPolicy.CalculateMaximumDistanceSquared(maximumDistance)));
            slot.Advance(age);
        }
    }
}
