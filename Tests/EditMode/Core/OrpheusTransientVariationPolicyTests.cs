using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusTransientVariationPolicyTests
    {
        [Test]
        public void Xorshift32_SeedOneMatchesGoldenSequence()
        {
            var random = new OrpheusXorshift32(1u);

            Assert.That(random.NextUInt(), Is.EqualTo(0x00042021u));
            Assert.That(random.NextUInt(), Is.EqualTo(0x04080601u));
            Assert.That(random.NextUInt(), Is.EqualTo(0x9DCCA8C5u));
            Assert.That(random.NextUInt(), Is.EqualTo(0x1255994Fu));
        }

        [Test]
        public void Xorshift32_ZeroSeedIsNormalizedAndDeterministic()
        {
            var first = new OrpheusXorshift32(0u);
            var second = new OrpheusXorshift32(0u);

            Assert.That(first.State, Is.Not.Zero);
            Assert.That(first.NextUInt(), Is.EqualTo(second.NextUInt()));
        }

        [Test]
        public void NextBounded_AlwaysStaysInsideExclusiveUpperBound()
        {
            var random = new OrpheusXorshift32(17u);

            for (var iteration = 0; iteration < 256; iteration++)
            {
                Assert.That(random.NextBounded(7u), Is.LessThan(7u));
            }
        }

        [Test]
        public void SampleClip_EachRoundIsPermutationAndBoundaryNeverRepeats()
        {
            var random = new OrpheusXorshift32(31u);
            var order = new byte[4];
            var cursors = new byte[] { byte.MaxValue };
            var previousLast = new byte[] { byte.MaxValue };
            var firstRound = new int[4];
            var secondRound = new int[4];

            for (var index = 0; index < 4; index++)
            {
                firstRound[index] = OrpheusTransientVariationPolicy.SampleClipIndex(
                    0,
                    0,
                    4,
                    order,
                    cursors,
                    previousLast,
                    ref random);
            }

            for (var index = 0; index < 4; index++)
            {
                secondRound[index] = OrpheusTransientVariationPolicy.SampleClipIndex(
                    0,
                    0,
                    4,
                    order,
                    cursors,
                    previousLast,
                    ref random);
            }

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, firstRound);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, secondRound);
            Assert.That(secondRound[0], Is.Not.EqualTo(firstRound[3]));
        }

        [Test]
        public void FixedClipAndScalarRangesDoNotConsumeRandomState()
        {
            var random = new OrpheusXorshift32(97u);
            var initialState = random.State;
            var order = new byte[1];
            var cursors = new byte[] { byte.MaxValue };
            var previousLast = new byte[] { byte.MaxValue };

            var clip = OrpheusTransientVariationPolicy.SampleClipIndex(
                0,
                12,
                1,
                order,
                cursors,
                previousLast,
                ref random);
            var scalar = OrpheusTransientVariationPolicy.SampleScalar(0.75f, 0.75f, ref random);

            Assert.That(clip, Is.EqualTo(12));
            Assert.That(scalar, Is.EqualTo(0.75f));
            Assert.That(random.State, Is.EqualTo(initialState));
        }

        [Test]
        public void IdenticalSeedAndStateProduceIdenticalVariationSequence()
        {
            var firstRandom = new OrpheusXorshift32(1234u);
            var secondRandom = new OrpheusXorshift32(1234u);
            var firstOrder = new byte[3];
            var secondOrder = new byte[3];
            var firstCursors = new byte[] { byte.MaxValue };
            var secondCursors = new byte[] { byte.MaxValue };
            var firstLast = new byte[] { byte.MaxValue };
            var secondLast = new byte[] { byte.MaxValue };

            for (var iteration = 0; iteration < 12; iteration++)
            {
                Assert.That(
                    OrpheusTransientVariationPolicy.SampleClipIndex(
                        0, 0, 3, firstOrder, firstCursors, firstLast, ref firstRandom),
                    Is.EqualTo(
                        OrpheusTransientVariationPolicy.SampleClipIndex(
                            0, 0, 3, secondOrder, secondCursors, secondLast, ref secondRandom)));
                Assert.That(
                    OrpheusTransientVariationPolicy.SampleScalar(0.5f, 1f, ref firstRandom),
                    Is.EqualTo(
                        OrpheusTransientVariationPolicy.SampleScalar(0.5f, 1f, ref secondRandom)));
            }
        }
    }
}
