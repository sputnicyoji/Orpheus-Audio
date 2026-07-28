using System;
using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioPersistentDirectorPolicyTests
    {
        private static readonly OrpheusAudioKey KeyA = new OrpheusAudioKey(1);
        private static readonly OrpheusAudioKey KeyB = new OrpheusAudioKey(2);
        private static readonly OrpheusAudioKey KeyC = new OrpheusAudioKey(3);
        private static readonly OrpheusAudioKey KeyD = new OrpheusAudioKey(4);

        [TestCase(0f, 1f, 0f)]
        [TestCase(0.5f, 0.70710677f, 0.70710677f)]
        [TestCase(1f, 0f, 1f)]
        public void EqualPowerGains_UseClampedCosineAndSine(
            float progress,
            float expectedOutgoing,
            float expectedIncoming)
        {
            OrpheusAudioPersistentDirectorPolicy.GetEqualPowerGains(
                progress,
                out var outgoing,
                out var incoming);

            Assert.That(outgoing, Is.EqualTo(expectedOutgoing).Within(0.000001f));
            Assert.That(incoming, Is.EqualTo(expectedIncoming).Within(0.000001f));
        }

        [Test]
        public void EqualPowerGains_MidpointMeetsContractTolerance()
        {
            OrpheusAudioPersistentDirectorPolicy.GetEqualPowerGains(
                0.5f,
                out var outgoing,
                out var incoming);
            var expected = (float)Math.Sqrt(0.5d);

            Assert.That(Math.Abs(outgoing - expected), Is.LessThanOrEqualTo(0.001f));
            Assert.That(Math.Abs(incoming - expected), Is.LessThanOrEqualTo(0.001f));
        }

        [Test]
        public void ReverseProgress_SwapsRolesWithoutChangingEitherGain()
        {
            const float progress = 0.3f;
            OrpheusAudioPersistentDirectorPolicy.GetEqualPowerGains(
                progress,
                out var oldOutgoing,
                out var oldIncoming);

            var reversedProgress = OrpheusAudioPersistentDirectorPolicy.ReverseProgress(progress);
            OrpheusAudioPersistentDirectorPolicy.GetEqualPowerGains(
                reversedProgress,
                out var newOutgoing,
                out var newIncoming);

            Assert.That(newOutgoing, Is.EqualTo(oldIncoming).Within(0.000001f));
            Assert.That(newIncoming, Is.EqualTo(oldOutgoing).Within(0.000001f));
        }

        [Test]
        public void EvaluateIntent_CurrentRequestedWhileDifferentKeyLoads_CancelsPending()
        {
            var view = View(
                OrpheusPersistentDirectorPhase.Loading,
                current: KeyA,
                loading: KeyB,
                sourceZeroKey: KeyA,
                sourceZeroGain: 1f);

            var decision = OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(view, KeyA);

            Assert.That(
                decision.Action,
                Is.EqualTo(OrpheusPersistentDirectorAction.CancelPendingKeepCurrent));
            Assert.That(decision.Key, Is.EqualTo(KeyA));
            Assert.That(decision.QueuedKey, Is.EqualTo(OrpheusAudioKey.Invalid));
        }

        [Test]
        public void EvaluateIntent_CurrentRequestedDuringCrossfade_ReversesAndClearsQueue()
        {
            var view = Crossfading(progress: 0.25f, queued: KeyC);

            var decision = OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(view, KeyA);

            Assert.That(decision.Action, Is.EqualTo(OrpheusPersistentDirectorAction.ReverseCrossfade));
            Assert.That(decision.Key, Is.EqualTo(KeyA));
            Assert.That(decision.QueuedKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(decision.CrossfadeProgress, Is.EqualTo(0.75f));
        }

        [Test]
        public void EvaluateIntent_TargetRequestedDuringCrossfade_ContinuesAndClearsQueue()
        {
            var view = Crossfading(progress: 0.25f, queued: KeyC);

            var decision = OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(view, KeyB);

            Assert.That(decision.Action, Is.EqualTo(OrpheusPersistentDirectorAction.ContinueCrossfade));
            Assert.That(decision.Key, Is.EqualTo(KeyB));
            Assert.That(decision.QueuedKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(decision.CrossfadeProgress, Is.EqualTo(0.25f));
        }

        [Test]
        public void EvaluateIntent_ThirdTargetDuringCrossfade_UsesSingleLatestWinsQueue()
        {
            var first = OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(
                Crossfading(progress: 0.25f),
                KeyC);
            var replacement = OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(
                Crossfading(progress: 0.25f, queued: KeyC),
                KeyD);

            Assert.That(first.Action, Is.EqualTo(OrpheusPersistentDirectorAction.QueueLatest));
            Assert.That(first.QueuedKey, Is.EqualTo(KeyC));
            Assert.That(replacement.Action, Is.EqualTo(OrpheusPersistentDirectorAction.QueueLatest));
            Assert.That(replacement.QueuedKey, Is.EqualTo(KeyD));
        }

        [Test]
        public void EvaluateIntent_RepeatingCurrentLoadingTargetOrQueued_IsIdempotent()
        {
            Assert.That(
                OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(
                    View(
                        OrpheusPersistentDirectorPhase.Holding,
                        current: KeyA,
                        sourceZeroKey: KeyA,
                        sourceZeroGain: 1f),
                    KeyA).Action,
                Is.EqualTo(OrpheusPersistentDirectorAction.None));
            Assert.That(
                OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(
                    View(
                        OrpheusPersistentDirectorPhase.Loading,
                        current: KeyA,
                        loading: KeyB,
                        sourceZeroKey: KeyA,
                        sourceZeroGain: 1f),
                    KeyB).Action,
                Is.EqualTo(OrpheusPersistentDirectorAction.None));
            Assert.That(
                OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(
                    Crossfading(progress: 0.25f),
                    KeyB).Action,
                Is.EqualTo(OrpheusPersistentDirectorAction.None));
            Assert.That(
                OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(
                    Crossfading(progress: 0.25f, queued: KeyC),
                    KeyC).Action,
                Is.EqualTo(OrpheusPersistentDirectorAction.None));
        }

        [Test]
        public void EvaluateIntent_InvalidDesiredStopsAndClearsPendingRoles()
        {
            var decision = OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(
                Crossfading(progress: 0.25f, queued: KeyC),
                OrpheusAudioKey.Invalid);

            Assert.That(decision.Action, Is.EqualTo(OrpheusPersistentDirectorAction.Stop));
            Assert.That(decision.Key, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(decision.QueuedKey, Is.EqualTo(OrpheusAudioKey.Invalid));
        }

        [Test]
        public void EvaluateIntent_MatchingActiveSourceDuringCleanup_PreservesWithoutRestart()
        {
            AssertMatchingActiveSourceIsPreserved(OrpheusPersistentDirectorPhase.Stopping);
            AssertMatchingActiveSourceIsPreserved(OrpheusPersistentDirectorPhase.Normalizing);
        }

        private static void AssertMatchingActiveSourceIsPreserved(
            OrpheusPersistentDirectorPhase phase)
        {
            var view = View(
                phase,
                current: KeyA,
                target: KeyB,
                sourceZeroKey: KeyA,
                sourceZeroGain: 0.25f,
                sourceOneKey: KeyB,
                sourceOneGain: 0.75f);

            var decision = OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(view, KeyA);

            Assert.That(decision.Action, Is.EqualTo(OrpheusPersistentDirectorAction.PreserveActive));
            Assert.That(decision.SurvivorSourceIndex, Is.Zero);
            Assert.That(decision.Key, Is.EqualTo(KeyA));
        }

        [TestCase(0.75f, 0.25f, 0)]
        [TestCase(0.25f, 0.75f, 1)]
        [TestCase(0.5f, 0.5f, 0)]
        public void EvaluateIntent_DifferentDesiredDuringCleanup_SelectsDeterministicSurvivor(
            float sourceZeroGain,
            float sourceOneGain,
            int expectedSurvivor)
        {
            var view = View(
                OrpheusPersistentDirectorPhase.Normalizing,
                current: KeyA,
                target: KeyB,
                sourceZeroKey: KeyA,
                sourceZeroGain: sourceZeroGain,
                sourceOneKey: KeyB,
                sourceOneGain: sourceOneGain);

            var decision = OrpheusAudioPersistentDirectorPolicy.EvaluateIntent(view, KeyC);

            Assert.That(
                decision.Action,
                Is.EqualTo(OrpheusPersistentDirectorAction.PreserveSurvivorAndLoad));
            Assert.That(decision.SurvivorSourceIndex, Is.EqualTo(expectedSurvivor));
            Assert.That(decision.Key, Is.EqualTo(KeyC));
        }

        [Test]
        public void GenerationGuards_RejectStaleOrSupersededDelayedActions()
        {
            Assert.That(
                OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(7, 7),
                Is.True);
            Assert.That(
                OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(0, 0),
                Is.False);
            Assert.That(
                OrpheusAudioPersistentDirectorPolicy.IsCurrentGeneration(6, 7),
                Is.False);
            Assert.That(
                OrpheusAudioPersistentDirectorPolicy.CanStart(7, 7, KeyB, KeyB),
                Is.True);
            Assert.That(
                OrpheusAudioPersistentDirectorPolicy.CanStart(6, 7, KeyB, KeyB),
                Is.False);
            Assert.That(
                OrpheusAudioPersistentDirectorPolicy.CanStart(7, 7, KeyB, KeyC),
                Is.False);
        }

        private static OrpheusPersistentDirectorView Crossfading(
            float progress,
            OrpheusAudioKey queued = default)
        {
            return View(
                OrpheusPersistentDirectorPhase.Crossfading,
                current: KeyA,
                target: KeyB,
                queued: queued,
                sourceZeroKey: KeyA,
                sourceZeroGain: 0.75f,
                sourceOneKey: KeyB,
                sourceOneGain: 0.25f,
                progress: progress);
        }

        private static OrpheusPersistentDirectorView View(
            OrpheusPersistentDirectorPhase phase,
            OrpheusAudioKey current = default,
            OrpheusAudioKey loading = default,
            OrpheusAudioKey target = default,
            OrpheusAudioKey queued = default,
            OrpheusAudioKey sourceZeroKey = default,
            float sourceZeroGain = 0f,
            OrpheusAudioKey sourceOneKey = default,
            float sourceOneGain = 0f,
            float progress = 0f)
        {
            return new OrpheusPersistentDirectorView(
                phase,
                current,
                loading,
                target,
                queued,
                sourceZeroKey,
                sourceZeroGain,
                sourceOneKey,
                sourceOneGain,
                progress);
        }
    }
}
