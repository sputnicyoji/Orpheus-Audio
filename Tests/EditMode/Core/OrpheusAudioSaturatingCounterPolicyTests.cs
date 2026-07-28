using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioSaturatingCounterPolicyTests
    {
        [TestCase(0UL, 1UL)]
        [TestCase(1UL, 2UL)]
        [TestCase(ulong.MaxValue - 1UL, ulong.MaxValue)]
        [TestCase(ulong.MaxValue, ulong.MaxValue)]
        public void Increment_ClampsAtUlongMaximum(ulong current, ulong expected)
        {
            Assert.That(
                OrpheusAudioSaturatingCounterPolicy.Increment(current),
                Is.EqualTo(expected));
        }
    }
}
