using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioActivationPolicyTests
    {
        [Test]
        public void ActivationState_DerivesReadinessFromIndependentBarriersAndTransport()
        {
            var state = new OrpheusAudioActivationState();

            Assert.That(state.HostReady, Is.False);
            Assert.That(state.BootstrapHydrated, Is.False);
            Assert.That(
                state.GetReadiness(OrpheusTransportState.Active),
                Is.EqualTo(OrpheusReadiness.None));

            state.CompleteHostReady();

            Assert.That(state.HostReady, Is.True);
            Assert.That(state.BootstrapHydrated, Is.False);
            Assert.That(
                state.GetReadiness(OrpheusTransportState.Active),
                Is.EqualTo(OrpheusReadiness.HostReady));

            state.CompleteBootstrapHydration();

            var activationReady = OrpheusReadiness.HostReady |
                                  OrpheusReadiness.BootstrapHydrated |
                                  OrpheusReadiness.ActivationReady;
            Assert.That(
                state.GetReadiness(OrpheusTransportState.Suspended),
                Is.EqualTo(activationReady));
            Assert.That(
                state.GetReadiness(OrpheusTransportState.Active),
                Is.EqualTo(activationReady | OrpheusReadiness.PlaybackReady));
        }

        [Test]
        public void ActivationState_BarriersAreOrderIndependentAndIdempotent()
        {
            var hostFirst = new OrpheusAudioActivationState();
            hostFirst.CompleteHostReady();
            hostFirst.CompleteHostReady();
            hostFirst.CompleteBootstrapHydration();
            hostFirst.CompleteBootstrapHydration();

            var bootstrapFirst = new OrpheusAudioActivationState();
            bootstrapFirst.CompleteBootstrapHydration();
            bootstrapFirst.CompleteBootstrapHydration();
            bootstrapFirst.CompleteHostReady();
            bootstrapFirst.CompleteHostReady();

            Assert.That(
                hostFirst.GetReadiness(OrpheusTransportState.Active),
                Is.EqualTo(bootstrapFirst.GetReadiness(OrpheusTransportState.Active)));
            Assert.That(hostFirst.HostReady, Is.True);
            Assert.That(hostFirst.BootstrapHydrated, Is.True);
        }

        [TestCase(0f, -80f)]
        [TestCase(1f, 0f)]
        [TestCase(0.5f, -6.0206f)]
        [TestCase(0.25f, -12.0412f)]
        public void UserGainPolicy_MapsValidLinearGainToDecibels(float linear, float expectedDecibels)
        {
            Assert.That(OrpheusAudioUserGainPolicy.IsValid(linear), Is.True);
            Assert.That(
                OrpheusAudioUserGainPolicy.ToDecibels(linear),
                Is.EqualTo(expectedDecibels).Within(0.0001f));
        }

        [TestCase(-0.0001f)]
        [TestCase(1.0001f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void UserGainPolicy_RejectsNonFiniteAndOutOfRangeValues(float invalid)
        {
            Assert.That(OrpheusAudioUserGainPolicy.IsValid(invalid), Is.False);
        }

        [Test]
        public void UserGainPolicy_ReadsAndUpdatesExactlyOneBusWithoutClamping()
        {
            var original = new OrpheusAudioUserGains(1f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f);

            Assert.That(
                OrpheusAudioUserGainPolicy.TrySet(
                    original,
                    OrpheusBus.SfxWorld,
                    0.25f,
                    out var updated),
                Is.True);
            Assert.That(
                OrpheusAudioUserGainPolicy.TryGet(updated, OrpheusBus.SfxWorld, out var changed),
                Is.True);
            Assert.That(changed, Is.EqualTo(0.25f));
            Assert.That(updated.Master, Is.EqualTo(original.Master));
            Assert.That(updated.Music, Is.EqualTo(original.Music));
            Assert.That(updated.SfxCombat, Is.EqualTo(original.SfxCombat));
            Assert.That(updated.SfxUi, Is.EqualTo(original.SfxUi));
            Assert.That(updated.Ambience, Is.EqualTo(original.Ambience));

            Assert.That(
                OrpheusAudioUserGainPolicy.TrySet(
                    updated,
                    OrpheusBus.SfxWorld,
                    1.01f,
                    out var rejected),
                Is.False);
            Assert.That(rejected.SfxWorld, Is.EqualTo(updated.SfxWorld));
            Assert.That(
                OrpheusAudioUserGainPolicy.TryGet(updated, OrpheusBus.Invalid, out var invalidBus),
                Is.False);
            Assert.That(invalidBus, Is.Zero);
            Assert.That(OrpheusAudioUserGainPolicy.AreValid(original), Is.True);
            Assert.That(
                OrpheusAudioUserGainPolicy.AreValid(
                    new OrpheusAudioUserGains(1f, 1f, float.NaN, 1f, 1f, 1f)),
                Is.False);
        }
    }
}
