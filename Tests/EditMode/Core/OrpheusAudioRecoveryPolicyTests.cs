using System;
using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioRecoveryPolicyTests
    {
        private const OrpheusSuspensionReason AllSuspensionReasons =
            OrpheusSuspensionReason.FocusLost |
            OrpheusSuspensionReason.ApplicationPaused |
            OrpheusSuspensionReason.ListenerMissing;

        [Test]
        public void Coalesce_ReducesEveryValidCombinationByBitwiseOr()
        {
            for (var current = 0; current <= 3; current++)
            {
                for (var incoming = 0; incoming <= 3; incoming++)
                {
                    var actual = OrpheusAudioRecoveryPolicy.Coalesce(
                        (OrpheusRecoveryPendingReason)current,
                        (OrpheusRecoveryPendingReason)incoming);

                    Assert.That(
                        actual,
                        Is.EqualTo((OrpheusRecoveryPendingReason)(current | incoming)));
                }
            }
        }

        [TestCase(4)]
        [TestCase(5)]
        [TestCase(255)]
        public void Coalesce_InvalidStoredBitsAreNotMutated(int invalidValue)
        {
            var invalid = (OrpheusRecoveryPendingReason)invalidValue;

            var actual = OrpheusAudioRecoveryPolicy.Coalesce(
                invalid,
                OrpheusRecoveryPendingReason.ExternalConfiguration);

            Assert.That(actual, Is.EqualTo(invalid));
        }

        [TestCase(4)]
        [TestCase(5)]
        [TestCase(255)]
        public void Coalesce_InvalidIncomingBitsAreIgnored(int invalidValue)
        {
            var current = OrpheusRecoveryPendingReason.SelfResetNotification;

            var actual = OrpheusAudioRecoveryPolicy.Coalesce(
                current,
                (OrpheusRecoveryPendingReason)invalidValue);

            Assert.That(actual, Is.EqualTo(current));
        }

        [Test]
        public void EvaluatePending_ExternalDominatesCombinedAndSelfOnlyAcknowledges()
        {
            AssertDecision(
                OrpheusAudioRecoveryPolicy.EvaluatePending(
                    OrpheusRecoveryPendingReason.None,
                    OrpheusSuspensionReason.None),
                OrpheusRecoveryAction.None,
                OrpheusRecoveryPendingReason.None);
            AssertDecision(
                OrpheusAudioRecoveryPolicy.EvaluatePending(
                    OrpheusRecoveryPendingReason.SelfResetNotification,
                    OrpheusSuspensionReason.None),
                OrpheusRecoveryAction.AcknowledgeSelfReset,
                OrpheusRecoveryPendingReason.None);
            AssertDecision(
                OrpheusAudioRecoveryPolicy.EvaluatePending(
                    OrpheusRecoveryPendingReason.ExternalConfiguration,
                    OrpheusSuspensionReason.None),
                OrpheusRecoveryAction.RecoverExternalConfiguration,
                OrpheusRecoveryPendingReason.None);
            AssertDecision(
                OrpheusAudioRecoveryPolicy.EvaluatePending(
                    OrpheusRecoveryPendingReason.ExternalConfiguration |
                    OrpheusRecoveryPendingReason.SelfResetNotification,
                    OrpheusSuspensionReason.None),
                OrpheusRecoveryAction.RecoverExternalConfiguration,
                OrpheusRecoveryPendingReason.None);
        }

        [Test]
        public void EvaluatePending_DefersExternalUntilEverySuspensionReasonClears()
        {
            for (var mask = 1; mask <= (byte)AllSuspensionReasons; mask++)
            {
                var reasons = (OrpheusSuspensionReason)mask;
                var decision = OrpheusAudioRecoveryPolicy.EvaluatePending(
                    OrpheusRecoveryPendingReason.ExternalConfiguration |
                    OrpheusRecoveryPendingReason.SelfResetNotification,
                    reasons);

                AssertDecision(
                    decision,
                    OrpheusRecoveryAction.DeferExternalConfiguration,
                    OrpheusRecoveryPendingReason.ExternalConfiguration);
            }

            AssertDecision(
                OrpheusAudioRecoveryPolicy.EvaluatePending(
                    OrpheusRecoveryPendingReason.ExternalConfiguration,
                    OrpheusSuspensionReason.None),
                OrpheusRecoveryAction.RecoverExternalConfiguration,
                OrpheusRecoveryPendingReason.None);
        }

        [TestCase(4)]
        [TestCase(5)]
        [TestCase(255)]
        public void EvaluatePending_InvalidPendingStateHasNoActionOrMutation(int invalidValue)
        {
            var invalid = (OrpheusRecoveryPendingReason)invalidValue;

            var decision = OrpheusAudioRecoveryPolicy.EvaluatePending(
                invalid,
                OrpheusSuspensionReason.None);

            AssertDecision(decision, OrpheusRecoveryAction.None, invalid);
        }

        [Test]
        public void AndroidManualRecovery_RequiresAcceptedForegroundReasonClear()
        {
            var clearableReasons = new[]
            {
                OrpheusSuspensionReason.FocusLost,
                OrpheusSuspensionReason.ApplicationPaused
            };

            foreach (var reason in clearableReasons)
            {
                for (var android = 0; android <= 1; android++)
                {
                    for (var enabled = 0; enabled <= 1; enabled++)
                    {
                        var actual = OrpheusAudioRecoveryPolicy
                            .ShouldRequestAndroidManualRecovery(
                                android != 0,
                                enabled != 0,
                                reason,
                                reducerChanged: true,
                                asserted: false);

                        Assert.That(actual, Is.EqualTo(android != 0 && enabled != 0));
                    }
                }
            }
        }

        [Test]
        public void AndroidManualRecovery_RejectsAssertDuplicateListenerAndInvalidClears()
        {
            Assert.That(
                OrpheusAudioRecoveryPolicy.ShouldRequestAndroidManualRecovery(
                    true,
                    true,
                    OrpheusSuspensionReason.FocusLost,
                    reducerChanged: true,
                    asserted: true),
                Is.False);
            Assert.That(
                OrpheusAudioRecoveryPolicy.ShouldRequestAndroidManualRecovery(
                    true,
                    true,
                    OrpheusSuspensionReason.ApplicationPaused,
                    reducerChanged: false,
                    asserted: false),
                Is.False);
            Assert.That(
                OrpheusAudioRecoveryPolicy.ShouldRequestAndroidManualRecovery(
                    true,
                    true,
                    OrpheusSuspensionReason.ListenerMissing,
                    reducerChanged: true,
                    asserted: false),
                Is.False);
            Assert.That(
                OrpheusAudioRecoveryPolicy.ShouldRequestAndroidManualRecovery(
                    true,
                    true,
                    OrpheusSuspensionReason.None,
                    reducerChanged: true,
                    asserted: false),
                Is.False);
            Assert.That(
                OrpheusAudioRecoveryPolicy.ShouldRequestAndroidManualRecovery(
                    true,
                    true,
                    OrpheusSuspensionReason.FocusLost |
                    OrpheusSuspensionReason.ApplicationPaused,
                    reducerChanged: true,
                    asserted: false),
                Is.False);
        }

        [Test]
        public void AdvanceGeneration_IsDeterministicAndSkipsZero()
        {
            Assert.That(OrpheusAudioRecoveryPolicy.AdvanceGeneration(0u), Is.EqualTo(1u));
            Assert.That(OrpheusAudioRecoveryPolicy.AdvanceGeneration(1u), Is.EqualTo(2u));
            Assert.That(
                OrpheusAudioRecoveryPolicy.AdvanceGeneration(uint.MaxValue),
                Is.EqualTo(1u));
        }

        [Test]
        public void RetryBudget_AllowsEachDesiredFailedRoleOncePerGeneration()
        {
            const uint generation = 9u;
            var bgm = 0u;
            var ambience = 0u;
            var loop0 = 0u;
            var loop1 = 0u;
            var loop2 = 0u;
            var loop3 = 0u;

            AssertClaim(ref bgm, generation, true);
            AssertClaim(ref ambience, generation, true);
            AssertClaim(ref loop0, generation, true);
            AssertClaim(ref loop1, generation, true);
            AssertClaim(ref loop2, generation, true);
            AssertClaim(ref loop3, generation, true);

            AssertClaim(ref bgm, generation, false);
            AssertClaim(ref ambience, generation, false);
            AssertClaim(ref loop0, generation, false);
            AssertClaim(ref loop1, generation, false);
            AssertClaim(ref loop2, generation, false);
            AssertClaim(ref loop3, generation, false);

            var next = OrpheusAudioRecoveryPolicy.AdvanceGeneration(generation);
            AssertClaim(ref bgm, next, true);
            AssertClaim(ref ambience, next, true);
            AssertClaim(ref loop0, next, true);
            AssertClaim(ref loop1, next, true);
            AssertClaim(ref loop2, next, true);
            AssertClaim(ref loop3, next, true);
        }

        [Test]
        public void RetryBudget_NonDesiredOrNonFailedRoleDoesNotConsumeBudget()
        {
            const uint generation = 17u;
            var lastRetryGeneration = 0u;

            Assert.That(
                OrpheusAudioRecoveryPolicy.TryClaimRetry(
                    generation,
                    isDesired: false,
                    isFailed: true,
                    ref lastRetryGeneration),
                Is.False);
            Assert.That(lastRetryGeneration, Is.Zero);
            Assert.That(
                OrpheusAudioRecoveryPolicy.TryClaimRetry(
                    generation,
                    isDesired: true,
                    isFailed: false,
                    ref lastRetryGeneration),
                Is.False);
            Assert.That(lastRetryGeneration, Is.Zero);
            Assert.That(
                OrpheusAudioRecoveryPolicy.TryClaimRetry(
                    generation,
                    isDesired: true,
                    isFailed: true,
                    ref lastRetryGeneration),
                Is.True);
            Assert.That(lastRetryGeneration, Is.EqualTo(generation));
        }

        [Test]
        public void RetryBudget_ZeroGenerationCannotBeClaimed()
        {
            var lastRetryGeneration = uint.MaxValue;

            Assert.That(
                OrpheusAudioRecoveryPolicy.TryClaimRetry(
                    0u,
                    isDesired: true,
                    isFailed: true,
                    ref lastRetryGeneration),
                Is.False);
            Assert.That(lastRetryGeneration, Is.EqualTo(uint.MaxValue));
        }

        [Test]
        public void RecoveryPolicy_HotOperationsAllocateZeroBytes()
        {
            var pending = OrpheusRecoveryPendingReason.None;
            var lastRetryGeneration = 0u;
            ExerciseHotOperations(ref pending, ref lastRetryGeneration);

            var before = GC.GetAllocatedBytesForCurrentThread();
            ExerciseHotOperations(ref pending, ref lastRetryGeneration);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        private static void ExerciseHotOperations(
            ref OrpheusRecoveryPendingReason pending,
            ref uint lastRetryGeneration)
        {
            pending = OrpheusAudioRecoveryPolicy.Coalesce(
                pending,
                OrpheusRecoveryPendingReason.ExternalConfiguration);
            pending = OrpheusAudioRecoveryPolicy.Coalesce(
                pending,
                OrpheusRecoveryPendingReason.SelfResetNotification);
            var suspended = OrpheusAudioRecoveryPolicy.EvaluatePending(
                pending,
                OrpheusSuspensionReason.FocusLost);
            var active = OrpheusAudioRecoveryPolicy.EvaluatePending(
                suspended.RetainedReasons,
                OrpheusSuspensionReason.None);
            var generation = OrpheusAudioRecoveryPolicy.AdvanceGeneration(
                lastRetryGeneration);
            OrpheusAudioRecoveryPolicy.TryClaimRetry(
                generation,
                isDesired: true,
                isFailed: true,
                ref lastRetryGeneration);
            OrpheusAudioRecoveryPolicy.ShouldRequestAndroidManualRecovery(
                true,
                true,
                OrpheusSuspensionReason.FocusLost,
                reducerChanged: true,
                asserted: false);

            pending = active.RetainedReasons;
        }

        private static void AssertClaim(
            ref uint lastRetryGeneration,
            uint generation,
            bool expected)
        {
            Assert.That(
                OrpheusAudioRecoveryPolicy.TryClaimRetry(
                    generation,
                    isDesired: true,
                    isFailed: true,
                    ref lastRetryGeneration),
                Is.EqualTo(expected));
        }

        private static void AssertDecision(
            OrpheusRecoveryDecision decision,
            OrpheusRecoveryAction expectedAction,
            OrpheusRecoveryPendingReason expectedRetainedReasons)
        {
            Assert.That(decision.Action, Is.EqualTo(expectedAction));
            Assert.That(decision.RetainedReasons, Is.EqualTo(expectedRetainedReasons));
        }
    }
}
