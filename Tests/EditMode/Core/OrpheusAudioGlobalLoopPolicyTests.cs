using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioGlobalLoopPolicyTests
    {
        private static readonly OrpheusAudioKey KeyA = new OrpheusAudioKey(1);
        private static readonly OrpheusAudioKey KeyB = new OrpheusAudioKey(2);
        private static readonly OrpheusAudioKey KeyC = new OrpheusAudioKey(3);
        private static readonly OrpheusAudioKey KeyD = new OrpheusAudioKey(4);
        private static readonly OrpheusAudioKey KeyE = new OrpheusAudioKey(5);

        [Test]
        public void EvaluatePlay_NewKeySelectsLowestFreeEntry()
        {
            Assert.That(OrpheusAudioGlobalLoopPolicy.RegistryCapacity, Is.EqualTo(4));

            var entries = Entries(
                Entry(KeyA, OrpheusGlobalLoopPhase.Playing),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(KeyB, OrpheusGlobalLoopPhase.Failed),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free));

            var decision = OrpheusAudioGlobalLoopPolicy.EvaluatePlay(entries, KeyC);

            Assert.That(decision.Action, Is.EqualTo(OrpheusGlobalLoopAction.ReserveAndLoad));
            Assert.That(decision.EntryIndex, Is.EqualTo(1));
            Assert.That(entries[1].Phase, Is.EqualTo(OrpheusGlobalLoopPhase.Free));
            Assert.That(entries[1].Key, Is.EqualTo(OrpheusAudioKey.Invalid));
        }

        [TestCase((byte)OrpheusGlobalLoopPhase.Loading)]
        [TestCase((byte)OrpheusGlobalLoopPhase.Playing)]
        public void EvaluatePlay_SameLoadingOrPlayingKeyIsIdempotent(
            byte phaseValue)
        {
            var phase = (OrpheusGlobalLoopPhase)phaseValue;
            var entries = Entries(
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(KeyA, phase),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free));

            var decision = OrpheusAudioGlobalLoopPolicy.EvaluatePlay(entries, KeyA);

            Assert.That(decision.Action, Is.EqualTo(OrpheusGlobalLoopAction.None));
            Assert.That(decision.EntryIndex, Is.EqualTo(1));
        }

        [Test]
        public void EvaluatePlay_SameFailedKeyAuthorizesOneExplicitRetry()
        {
            var entries = Entries(
                Entry(KeyA, OrpheusGlobalLoopPhase.Failed),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free));

            var retry = OrpheusAudioGlobalLoopPolicy.EvaluatePlay(entries, KeyA);
            entries[0].Phase = OrpheusGlobalLoopPhase.Loading;
            var repeatedWhileLoading = OrpheusAudioGlobalLoopPolicy.EvaluatePlay(entries, KeyA);

            Assert.That(retry.Action, Is.EqualTo(OrpheusGlobalLoopAction.RetryLoad));
            Assert.That(retry.EntryIndex, Is.Zero);
            Assert.That(repeatedWhileLoading.Action, Is.EqualTo(OrpheusGlobalLoopAction.None));
            Assert.That(repeatedWhileLoading.EntryIndex, Is.Zero);
        }

        [Test]
        public void EvaluatePlay_AllOccupiedPhasesRejectWithoutMutation()
        {
            var entries = Entries(
                Entry(KeyA, OrpheusGlobalLoopPhase.Loading),
                Entry(KeyB, OrpheusGlobalLoopPhase.Failed),
                Entry(KeyC, OrpheusGlobalLoopPhase.Playing),
                Entry(KeyD, OrpheusGlobalLoopPhase.Stopping));

            var decision = OrpheusAudioGlobalLoopPolicy.EvaluatePlay(entries, KeyE);

            Assert.That(decision.Action, Is.EqualTo(OrpheusGlobalLoopAction.RejectFull));
            Assert.That(decision.EntryIndex, Is.EqualTo(-1));
            Assert.That(entries[0].Key, Is.EqualTo(KeyA));
            Assert.That(entries[1].Key, Is.EqualTo(KeyB));
            Assert.That(entries[2].Key, Is.EqualTo(KeyC));
            Assert.That(entries[3].Key, Is.EqualTo(KeyD));
        }

        [TestCase((byte)OrpheusGlobalLoopPhase.Loading)]
        [TestCase((byte)OrpheusGlobalLoopPhase.Failed)]
        public void EvaluateStop_UnstartedIntentClearsEntry(
            byte phaseValue)
        {
            var phase = (OrpheusGlobalLoopPhase)phaseValue;
            var entries = Entries(
                Entry(KeyA, phase),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free));

            var decision = OrpheusAudioGlobalLoopPolicy.EvaluateStop(entries, KeyA);

            Assert.That(decision.Action, Is.EqualTo(OrpheusGlobalLoopAction.ClearUnstarted));
            Assert.That(decision.EntryIndex, Is.Zero);
        }

        [Test]
        public void EvaluateStop_PlayingBeginsStopButStoppingIsIdempotent()
        {
            var entries = Entries(
                Entry(KeyA, OrpheusGlobalLoopPhase.Playing),
                Entry(KeyB, OrpheusGlobalLoopPhase.Stopping),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free));

            var begin = OrpheusAudioGlobalLoopPolicy.EvaluateStop(entries, KeyA);
            var repeated = OrpheusAudioGlobalLoopPolicy.EvaluateStop(entries, KeyB);

            Assert.That(begin.Action, Is.EqualTo(OrpheusGlobalLoopAction.BeginStop));
            Assert.That(begin.EntryIndex, Is.Zero);
            Assert.That(repeated.Action, Is.EqualTo(OrpheusGlobalLoopAction.None));
            Assert.That(repeated.EntryIndex, Is.EqualTo(1));
        }

        [Test]
        public void EvaluatePlay_StoppingSameKeyReactivatesWithoutChangingGain()
        {
            var stopping = Entry(KeyA, OrpheusGlobalLoopPhase.Stopping);
            stopping.Gain = 0.375f;
            var entries = Entries(
                stopping,
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free));

            var decision = OrpheusAudioGlobalLoopPolicy.EvaluatePlay(entries, KeyA);

            Assert.That(decision.Action, Is.EqualTo(OrpheusGlobalLoopAction.Reactivate));
            Assert.That(decision.EntryIndex, Is.Zero);
            Assert.That(entries[0].Gain, Is.EqualTo(0.375f));
        }

        [Test]
        public void EvaluatePlay_ReactivatingSameKeyIsIdempotent()
        {
            var reactivating = Entry(KeyA, OrpheusGlobalLoopPhase.Stopping);
            reactivating.RequestedActive = true;
            reactivating.Gain = 0.625f;
            var entries = Entries(
                reactivating,
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free));

            var decision = OrpheusAudioGlobalLoopPolicy.EvaluatePlay(entries, KeyA);

            Assert.That(decision.Action, Is.EqualTo(OrpheusGlobalLoopAction.None));
            Assert.That(decision.EntryIndex, Is.Zero);
            Assert.That(entries[0].Gain, Is.EqualTo(0.625f));
        }

        [Test]
        public void EvaluateStop_ReactivatingSameKeyBeginsNewStopFromCurrentState()
        {
            var reactivating = Entry(KeyA, OrpheusGlobalLoopPhase.Stopping);
            reactivating.RequestedActive = true;
            reactivating.Gain = 0.625f;
            var entries = Entries(
                reactivating,
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free));

            var decision = OrpheusAudioGlobalLoopPolicy.EvaluateStop(entries, KeyA);

            Assert.That(decision.Action, Is.EqualTo(OrpheusGlobalLoopAction.BeginStop));
            Assert.That(decision.EntryIndex, Is.Zero);
            Assert.That(entries[0].Gain, Is.EqualTo(0.625f));
        }

        [Test]
        public void EvaluatePlay_DifferentKeyNeverConsumesStoppingEntry()
        {
            var entries = Entries(
                Entry(KeyA, OrpheusGlobalLoopPhase.Stopping),
                Entry(KeyB, OrpheusGlobalLoopPhase.Playing),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(KeyC, OrpheusGlobalLoopPhase.Loading));

            var decision = OrpheusAudioGlobalLoopPolicy.EvaluatePlay(entries, KeyD);

            Assert.That(decision.Action, Is.EqualTo(OrpheusGlobalLoopAction.ReserveAndLoad));
            Assert.That(decision.EntryIndex, Is.EqualTo(2));
        }

        [Test]
        public void EvaluatePlay_InvalidKeyDoesNotReserveEntry()
        {
            var entries = Entries(
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free),
                Entry(OrpheusAudioKey.Invalid, OrpheusGlobalLoopPhase.Free));

            var decision = OrpheusAudioGlobalLoopPolicy.EvaluatePlay(
                entries,
                OrpheusAudioKey.Invalid);

            Assert.That(decision.Action, Is.EqualTo(OrpheusGlobalLoopAction.None));
            Assert.That(decision.EntryIndex, Is.EqualTo(-1));
        }

        [Test]
        public void Fade_UsesExactFifteenMillisecondLinearBoundary()
        {
            var before = OrpheusAudioGlobalLoopPolicy.EvaluateFade(0.8f, false, 0.014999f);
            var atBoundary = OrpheusAudioGlobalLoopPolicy.EvaluateFade(0.8f, false, 0.015f);
            var after = OrpheusAudioGlobalLoopPolicy.EvaluateFade(0.8f, false, 1f);

            Assert.That(before.Complete, Is.False);
            Assert.That(before.Gain, Is.GreaterThan(0f));
            Assert.That(atBoundary.Complete, Is.True);
            Assert.That(atBoundary.Gain, Is.Zero);
            Assert.That(after.Complete, Is.True);
            Assert.That(after.Gain, Is.Zero);
        }

        [Test]
        public void Fade_ReactivationStartsAtCurrentGainAndCompletesAtOne()
        {
            var unchanged = OrpheusAudioGlobalLoopPolicy.EvaluateFade(0.375f, true, 0f);
            var complete = OrpheusAudioGlobalLoopPolicy.EvaluateFade(0.375f, true, 0.015f);

            Assert.That(unchanged.Gain, Is.EqualTo(0.375f));
            Assert.That(unchanged.Complete, Is.False);
            Assert.That(complete.Gain, Is.EqualTo(1f));
            Assert.That(complete.Complete, Is.True);
        }

        [Test]
        public void AdvanceGeneration_WrapsPastZeroToOne()
        {
            Assert.That(OrpheusAudioGlobalLoopPolicy.AdvanceGeneration(0u), Is.EqualTo(1u));
            Assert.That(OrpheusAudioGlobalLoopPolicy.AdvanceGeneration(7u), Is.EqualTo(8u));
            Assert.That(
                OrpheusAudioGlobalLoopPolicy.AdvanceGeneration(uint.MaxValue),
                Is.EqualTo(1u));
        }

        [Test]
        public void CanStart_RequiresCurrentIdentityLoadingIntentAndReadiness()
        {
            Assert.That(CanStart(), Is.True);
            Assert.That(CanStart(scheduledGeneration: 6u), Is.False);
            Assert.That(CanStart(currentGeneration: 8u), Is.False);
            Assert.That(CanStart(scheduledGeneration: 0u, currentGeneration: 0u), Is.False);
            Assert.That(CanStart(scheduledKey: KeyB), Is.False);
            Assert.That(CanStart(currentKey: KeyB), Is.False);
            Assert.That(CanStart(phase: OrpheusGlobalLoopPhase.Failed), Is.False);
            Assert.That(CanStart(requestedActive: false), Is.False);
            Assert.That(CanStart(isLoaded: false), Is.False);
            Assert.That(CanStart(playbackReady: false), Is.False);
        }

        [Test]
        public void CanRelease_RequiresCurrentIdentityStoppingAndClearedIntent()
        {
            Assert.That(CanRelease(), Is.True);
            Assert.That(CanRelease(scheduledGeneration: 6u), Is.False);
            Assert.That(CanRelease(currentGeneration: 8u), Is.False);
            Assert.That(CanRelease(scheduledGeneration: 0u, currentGeneration: 0u), Is.False);
            Assert.That(CanRelease(scheduledKey: KeyB), Is.False);
            Assert.That(CanRelease(currentKey: KeyB), Is.False);
            Assert.That(CanRelease(phase: OrpheusGlobalLoopPhase.Playing), Is.False);
            Assert.That(CanRelease(requestedActive: true), Is.False);
        }

        [Test]
        public void EntryInitialize_ProducesReusableNonzeroGenerationState()
        {
            var entry = Entry(KeyA, OrpheusGlobalLoopPhase.Playing);
            entry.CatalogEntryIndex = 9;
            entry.Generation = uint.MaxValue;
            entry.Initialize();

            Assert.That(entry.Key, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(entry.Phase, Is.EqualTo(OrpheusGlobalLoopPhase.Free));
            Assert.That(entry.CatalogEntryIndex, Is.EqualTo(-1));
            Assert.That(entry.Generation, Is.EqualTo(1u));
            Assert.That(entry.RequestedActive, Is.False);
        }

        private static bool CanStart(
            uint scheduledGeneration = 7u,
            uint currentGeneration = 7u,
            OrpheusAudioKey scheduledKey = default,
            OrpheusAudioKey currentKey = default,
            OrpheusGlobalLoopPhase phase = OrpheusGlobalLoopPhase.Loading,
            bool requestedActive = true,
            bool isLoaded = true,
            bool playbackReady = true)
        {
            if (!scheduledKey.IsValid)
            {
                scheduledKey = KeyA;
            }

            if (!currentKey.IsValid)
            {
                currentKey = KeyA;
            }

            return OrpheusAudioGlobalLoopPolicy.CanStart(
                scheduledGeneration,
                currentGeneration,
                scheduledKey,
                currentKey,
                phase,
                requestedActive,
                isLoaded,
                playbackReady);
        }

        private static bool CanRelease(
            uint scheduledGeneration = 7u,
            uint currentGeneration = 7u,
            OrpheusAudioKey scheduledKey = default,
            OrpheusAudioKey currentKey = default,
            OrpheusGlobalLoopPhase phase = OrpheusGlobalLoopPhase.Stopping,
            bool requestedActive = false)
        {
            if (!scheduledKey.IsValid)
            {
                scheduledKey = KeyA;
            }

            if (!currentKey.IsValid)
            {
                currentKey = KeyA;
            }

            return OrpheusAudioGlobalLoopPolicy.CanRelease(
                scheduledGeneration,
                currentGeneration,
                scheduledKey,
                currentKey,
                phase,
                requestedActive);
        }

        private static OrpheusGlobalLoopEntryState Entry(
            OrpheusAudioKey key,
            OrpheusGlobalLoopPhase phase)
        {
            var entry = default(OrpheusGlobalLoopEntryState);
            entry.Initialize();
            entry.Key = key;
            entry.Phase = phase;
            entry.RequestedActive = phase != OrpheusGlobalLoopPhase.Free &&
                                    phase != OrpheusGlobalLoopPhase.Stopping;
            return entry;
        }

        private static OrpheusGlobalLoopEntryState[] Entries(
            OrpheusGlobalLoopEntryState zero,
            OrpheusGlobalLoopEntryState one,
            OrpheusGlobalLoopEntryState two,
            OrpheusGlobalLoopEntryState three)
        {
            return new[] { zero, one, two, three };
        }
    }
}
