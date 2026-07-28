using System;
using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioTransportSuspensionPolicyTests
    {
        private static readonly OrpheusSuspensionReason[] SingleReasons =
        {
            OrpheusSuspensionReason.FocusLost,
            OrpheusSuspensionReason.ApplicationPaused,
            OrpheusSuspensionReason.ListenerMissing
        };

        private static readonly byte[,] AssertedMasks =
        {
            { 1, 1, 3, 3, 5, 5, 7, 7 },
            { 2, 3, 2, 3, 6, 7, 6, 7 },
            { 4, 5, 6, 7, 4, 5, 6, 7 }
        };

        private static readonly byte[,] ClearedMasks =
        {
            { 0, 0, 2, 2, 4, 4, 6, 6 },
            { 0, 1, 0, 1, 4, 5, 4, 5 },
            { 0, 1, 2, 3, 0, 1, 2, 3 }
        };

        [TestCase(true, true, OrpheusSuspensionReason.None)]
        [TestCase(false, true, OrpheusSuspensionReason.FocusLost)]
        [TestCase(true, false, OrpheusSuspensionReason.ListenerMissing)]
        [TestCase(
            false,
            false,
            OrpheusSuspensionReason.FocusLost |
            OrpheusSuspensionReason.ListenerMissing)]
        public void InitialReasons_AreDerivedFromFocusAndListenerWhilePauseStartsFalse(
            bool hasFocus,
            bool hasListener,
            OrpheusSuspensionReason expected)
        {
            var actual = OrpheusAudioSuspensionPolicy.CreateInitialReasons(
                hasFocus,
                hasListener);

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(
                actual & OrpheusSuspensionReason.ApplicationPaused,
                Is.EqualTo(OrpheusSuspensionReason.None));
            Assert.That((byte)actual, Is.InRange((byte)0, (byte)7));
        }

        [Test]
        public void SetReason_IsIdempotentForEveryReasonAndAllEightMasks()
        {
            for (var reasonIndex = 0; reasonIndex < SingleReasons.Length; reasonIndex++)
            {
                var reason = SingleReasons[reasonIndex];
                for (var mask = 0; mask <= 7; mask++)
                {
                    var current = (OrpheusSuspensionReason)mask;
                    AssertUpdate(
                        current,
                        reason,
                        true,
                        (OrpheusSuspensionReason)AssertedMasks[reasonIndex, mask]);
                    AssertUpdate(
                        current,
                        reason,
                        false,
                        (OrpheusSuspensionReason)ClearedMasks[reasonIndex, mask]);
                }
            }
        }

        [TestCase(OrpheusSuspensionReason.None)]
        [TestCase(
            OrpheusSuspensionReason.FocusLost |
            OrpheusSuspensionReason.ApplicationPaused)]
        [TestCase(
            OrpheusSuspensionReason.FocusLost |
            OrpheusSuspensionReason.ListenerMissing)]
        [TestCase(
            OrpheusSuspensionReason.ApplicationPaused |
            OrpheusSuspensionReason.ListenerMissing)]
        [TestCase(
            OrpheusSuspensionReason.FocusLost |
            OrpheusSuspensionReason.ApplicationPaused |
            OrpheusSuspensionReason.ListenerMissing)]
        [TestCase((OrpheusSuspensionReason)8)]
        [TestCase((OrpheusSuspensionReason)255)]
        public void SetReason_InvalidOrCombinedReasonDoesNotMutate(
            OrpheusSuspensionReason invalidReason)
        {
            var current = OrpheusSuspensionReason.FocusLost |
                OrpheusSuspensionReason.ListenerMissing;

            AssertRejectedUpdate(current, invalidReason, true);
            AssertRejectedUpdate(current, invalidReason, false);
        }

        [TestCase((OrpheusSuspensionReason)8)]
        [TestCase((OrpheusSuspensionReason)9)]
        [TestCase((OrpheusSuspensionReason)255)]
        public void SetReason_InvalidStoredMaskDoesNotMutate(
            OrpheusSuspensionReason invalidCurrent)
        {
            AssertRejectedUpdate(
                invalidCurrent,
                OrpheusSuspensionReason.FocusLost,
                true);
            AssertRejectedUpdate(
                invalidCurrent,
                OrpheusSuspensionReason.FocusLost,
                false);
        }

        [Test]
        public void TransitionKind_ChangesOnlyAcrossZeroAndNonzeroBoundary()
        {
            var enter = OrpheusAudioSuspensionPolicy.Reduce(
                OrpheusSuspensionReason.None,
                OrpheusSuspensionReason.FocusLost,
                true);
            AssertDecision(
                enter,
                OrpheusSuspensionReason.FocusLost,
                OrpheusSuspensionTransition.EnterSuspended,
                true);

            var addPause = OrpheusAudioSuspensionPolicy.Reduce(
                enter.Reasons,
                OrpheusSuspensionReason.ApplicationPaused,
                true);
            AssertDecision(
                addPause,
                OrpheusSuspensionReason.FocusLost |
                OrpheusSuspensionReason.ApplicationPaused,
                OrpheusSuspensionTransition.RemainSuspended,
                true);

            var clearFocus = OrpheusAudioSuspensionPolicy.Reduce(
                addPause.Reasons,
                OrpheusSuspensionReason.FocusLost,
                false);
            AssertDecision(
                clearFocus,
                OrpheusSuspensionReason.ApplicationPaused,
                OrpheusSuspensionTransition.RemainSuspended,
                true);

            var exit = OrpheusAudioSuspensionPolicy.Reduce(
                clearFocus.Reasons,
                OrpheusSuspensionReason.ApplicationPaused,
                false);
            AssertDecision(
                exit,
                OrpheusSuspensionReason.None,
                OrpheusSuspensionTransition.ExitSuspended,
                true);

            var duplicateClear = OrpheusAudioSuspensionPolicy.Reduce(
                exit.Reasons,
                OrpheusSuspensionReason.ApplicationPaused,
                false);
            AssertDecision(
                duplicateClear,
                OrpheusSuspensionReason.None,
                OrpheusSuspensionTransition.None,
                false);
        }

        [Test]
        public void StoredMask_AcceptsOnlyDeclaredReasonCombinationsAndPartialClearRemainsNonzero()
        {
            Assert.That(
                OrpheusAudioSuspensionPolicy.IsValidStoredMask(
                    OrpheusSuspensionReason.None),
                Is.True);

            for (var mask = 1; mask <= 7; mask++)
            {
                var reasons = (OrpheusSuspensionReason)mask;
                Assert.That(
                    OrpheusAudioSuspensionPolicy.IsValidStoredMask(reasons),
                    Is.True,
                    mask.ToString());
            }

            Assert.That(
                OrpheusAudioSuspensionPolicy.IsValidStoredMask(
                    (OrpheusSuspensionReason)8),
                Is.False);
            Assert.That(
                OrpheusAudioSuspensionPolicy.IsValidStoredMask(
                    (OrpheusSuspensionReason)255),
                Is.False);

            var partialClear = OrpheusAudioSuspensionPolicy.Reduce(
                OrpheusSuspensionReason.FocusLost |
                OrpheusSuspensionReason.ApplicationPaused |
                OrpheusSuspensionReason.ListenerMissing,
                OrpheusSuspensionReason.FocusLost,
                false);
            Assert.That(
                partialClear.Reasons,
                Is.EqualTo(
                    OrpheusSuspensionReason.ApplicationPaused |
                    OrpheusSuspensionReason.ListenerMissing));
            Assert.That(partialClear.Reasons, Is.Not.EqualTo(OrpheusSuspensionReason.None));
        }

        [Test]
        public void ReducerHotPath_AllocatesZeroBytes()
        {
            var reasons = OrpheusSuspensionReason.None;
            reasons = OrpheusAudioSuspensionPolicy.Reduce(
                reasons,
                OrpheusSuspensionReason.FocusLost,
                true).Reasons;
            reasons = OrpheusAudioSuspensionPolicy.Reduce(
                reasons,
                OrpheusSuspensionReason.ApplicationPaused,
                true).Reasons;
            reasons = OrpheusAudioSuspensionPolicy.Reduce(
                reasons,
                OrpheusSuspensionReason.FocusLost,
                false).Reasons;
            reasons = OrpheusAudioSuspensionPolicy.Reduce(
                reasons,
                OrpheusSuspensionReason.ApplicationPaused,
                false).Reasons;

            var beforeEnter = GC.GetAllocatedBytesForCurrentThread();
            var enter = OrpheusAudioSuspensionPolicy.Reduce(
                reasons,
                OrpheusSuspensionReason.FocusLost,
                true);
            var enterBytes = GC.GetAllocatedBytesForCurrentThread() - beforeEnter;

            var beforeDuplicate = GC.GetAllocatedBytesForCurrentThread();
            var duplicate = OrpheusAudioSuspensionPolicy.Reduce(
                enter.Reasons,
                OrpheusSuspensionReason.FocusLost,
                true);
            var duplicateBytes =
                GC.GetAllocatedBytesForCurrentThread() - beforeDuplicate;

            var beforePartialClear = GC.GetAllocatedBytesForCurrentThread();
            var addPause = OrpheusAudioSuspensionPolicy.Reduce(
                duplicate.Reasons,
                OrpheusSuspensionReason.ApplicationPaused,
                true);
            var partialClear = OrpheusAudioSuspensionPolicy.Reduce(
                addPause.Reasons,
                OrpheusSuspensionReason.FocusLost,
                false);
            var partialClearBytes =
                GC.GetAllocatedBytesForCurrentThread() - beforePartialClear;

            var beforeExit = GC.GetAllocatedBytesForCurrentThread();
            var exit = OrpheusAudioSuspensionPolicy.Reduce(
                partialClear.Reasons,
                OrpheusSuspensionReason.ApplicationPaused,
                false);
            var exitBytes = GC.GetAllocatedBytesForCurrentThread() - beforeExit;

            Assert.That(enterBytes, Is.Zero);
            Assert.That(duplicateBytes, Is.Zero);
            Assert.That(partialClearBytes, Is.Zero);
            Assert.That(exitBytes, Is.Zero);
            Assert.That(exit.Reasons, Is.EqualTo(OrpheusSuspensionReason.None));
        }

        private static void AssertUpdate(
            OrpheusSuspensionReason current,
            OrpheusSuspensionReason reason,
            bool asserted,
            OrpheusSuspensionReason expected)
        {
            var first = OrpheusAudioSuspensionPolicy.Reduce(current, reason, asserted);
            var duplicate = OrpheusAudioSuspensionPolicy.Reduce(
                first.Reasons,
                reason,
                asserted);

            Assert.That(first.Reasons, Is.EqualTo(expected));
            Assert.That(first.Changed, Is.EqualTo(current != expected));
            Assert.That(duplicate.Reasons, Is.EqualTo(expected));
            Assert.That(duplicate.Changed, Is.False);
            Assert.That(duplicate.Transition, Is.EqualTo(OrpheusSuspensionTransition.None));
        }

        private static void AssertRejectedUpdate(
            OrpheusSuspensionReason current,
            OrpheusSuspensionReason reason,
            bool asserted)
        {
            OrpheusSuspensionDecision decision = default;

            Assert.DoesNotThrow(
                () => decision = OrpheusAudioSuspensionPolicy.Reduce(
                    current,
                    reason,
                    asserted));
            Assert.That(decision.Reasons, Is.EqualTo(current));
            Assert.That(decision.Changed, Is.False);
            Assert.That(decision.Transition, Is.EqualTo(OrpheusSuspensionTransition.None));
        }

        private static void AssertDecision(
            OrpheusSuspensionDecision decision,
            OrpheusSuspensionReason expectedReasons,
            OrpheusSuspensionTransition expectedTransition,
            bool expectedChanged)
        {
            Assert.That(decision.Reasons, Is.EqualTo(expectedReasons));
            Assert.That(decision.Transition, Is.EqualTo(expectedTransition));
            Assert.That(decision.Changed, Is.EqualTo(expectedChanged));
        }
    }
}
