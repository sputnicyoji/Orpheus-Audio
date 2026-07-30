using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioAuthoringPolicyTests
    {
        [Test]
        public void KeyStatus_UsesExactSerializedAbi()
        {
            Assert.That((byte)OrpheusAudioKeyStatus.Invalid, Is.EqualTo(0));
            Assert.That((byte)OrpheusAudioKeyStatus.Active, Is.EqualTo(1));
            Assert.That((byte)OrpheusAudioKeyStatus.Reserved, Is.EqualTo(2));
            Assert.That((byte)OrpheusAudioKeyStatus.Retired, Is.EqualTo(3));
        }

        [TestCase("Cue")]
        [TestCase("Cue_01")]
        [TestCase("_PrivateCue")]
        [TestCase("cue")]
        public void ManifestSymbol_AcceptsAsciiCSharpIdentifiers(string symbol)
        {
            Assert.That(OrpheusAudioManifestPolicy.IsLegalSymbol(symbol), Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1Cue")]
        [TestCase("Cue-01")]
        [TestCase("声音")]
        [TestCase("class")]
        [TestCase("await")]
        [TestCase("OrpheusAudioKeys")]
        [TestCase("ManifestSchemaVersion")]
        [TestCase("ManifestContentHash")]
        [TestCase("__arglist")]
        [TestCase("__makeref")]
        [TestCase("__reftype")]
        [TestCase("__refvalue")]
        [TestCase("Cue__Internal")]
        public void ManifestSymbol_RejectsIllegalOrReservedIdentifiers(string symbol)
        {
            Assert.That(OrpheusAudioManifestPolicy.IsLegalSymbol(symbol), Is.False);
        }

        [Test]
        public void ManifestEntry_ReportsEveryIndependentMismatch()
        {
            var value = new OrpheusAudioKeyManifestEntryValue(
                0,
                "class",
                OrpheusAudioKeyStatus.Invalid);

            Assert.That(
                OrpheusAudioManifestPolicy.Evaluate(value),
                Is.EqualTo(
                    OrpheusAudioManifestEntryMismatch.Id |
                    OrpheusAudioManifestEntryMismatch.Symbol |
                    OrpheusAudioManifestEntryMismatch.Status));
        }

        [TestCase(OrpheusPlaybackKind.OneShot2D, OrpheusCategory.SfxUi, OrpheusLoadPolicy.BootstrapTransient, 4)]
        [TestCase(OrpheusPlaybackKind.OneShot3D, OrpheusCategory.Ambience, OrpheusLoadPolicy.ExplicitTransient, 12)]
        [TestCase(OrpheusPlaybackKind.GlobalLoop2D, OrpheusCategory.SfxWorld, OrpheusLoadPolicy.PersistentStream, 1)]
        [TestCase(OrpheusPlaybackKind.Bgm, OrpheusCategory.Music, OrpheusLoadPolicy.PersistentStream, 1)]
        [TestCase(OrpheusPlaybackKind.ProfileAmbience, OrpheusCategory.Ambience, OrpheusLoadPolicy.PersistentStream, 1)]
        public void EventPolicy_AcceptsEveryPlaybackKindMatrix(
            object playbackKindValue,
            object categoryValue,
            object loadPolicyValue,
            byte polyphony)
        {
            var playbackKind = (OrpheusPlaybackKind)playbackKindValue;
            var category = (OrpheusCategory)categoryValue;
            var loadPolicy = (OrpheusLoadPolicy)loadPolicyValue;
            var persistent = playbackKind >= OrpheusPlaybackKind.GlobalLoop2D;
            var values = CreateValues(
                playbackKind,
                category,
                loadPolicy,
                persistent ? 1 : 2,
                persistent ? 0.75f : 0.5f,
                persistent ? 0.75f : 1f,
                persistent ? 1f : 0.8f,
                persistent ? 1f : 1.2f,
                polyphony,
                persistent ? 0f : 0.25f,
                0f,
                100f,
                OrpheusRolloffMode.Linear);

            Assert.That(
                OrpheusAudioEventPolicy.Evaluate(values),
                Is.EqualTo(OrpheusAudioEventPolicyMismatch.None));
        }

        [TestCase(0f, 1f, true)]
        [TestCase(-0.01f, 1f, false)]
        [TestCase(0f, 1.01f, false)]
        [TestCase(0.8f, 0.2f, false)]
        public void EventPolicy_EnforcesInclusiveVolumeBounds(
            float minimum,
            float maximum,
            bool valid)
        {
            var values = CreateValues(
                OrpheusPlaybackKind.OneShot2D,
                OrpheusCategory.SfxUi,
                OrpheusLoadPolicy.BootstrapTransient,
                1,
                minimum,
                maximum,
                1f,
                1f,
                1,
                0f,
                0f,
                100f,
                OrpheusRolloffMode.Linear);

            AssertMismatch(
                OrpheusAudioEventPolicy.Evaluate(values),
                OrpheusAudioEventPolicyMismatch.VolumeRange,
                valid);
        }

        [TestCase(0.5f, 2f, true)]
        [TestCase(0.49f, 1f, false)]
        [TestCase(1f, 2.01f, false)]
        [TestCase(1.5f, 1f, false)]
        public void EventPolicy_EnforcesInclusivePitchBounds(
            float minimum,
            float maximum,
            bool valid)
        {
            var values = CreateValues(
                OrpheusPlaybackKind.OneShot2D,
                OrpheusCategory.SfxUi,
                OrpheusLoadPolicy.BootstrapTransient,
                1,
                1f,
                1f,
                minimum,
                maximum,
                1,
                0f,
                0f,
                100f,
                OrpheusRolloffMode.Linear);

            AssertMismatch(
                OrpheusAudioEventPolicy.Evaluate(values),
                OrpheusAudioEventPolicyMismatch.PitchRange,
                valid);
        }

        [TestCase(OrpheusPlaybackKind.OneShot2D, 1, true)]
        [TestCase(OrpheusPlaybackKind.OneShot2D, 4, true)]
        [TestCase(OrpheusPlaybackKind.OneShot2D, 0, false)]
        [TestCase(OrpheusPlaybackKind.OneShot2D, 5, false)]
        [TestCase(OrpheusPlaybackKind.OneShot3D, 1, true)]
        [TestCase(OrpheusPlaybackKind.OneShot3D, 12, true)]
        [TestCase(OrpheusPlaybackKind.OneShot3D, 0, false)]
        [TestCase(OrpheusPlaybackKind.OneShot3D, 13, false)]
        public void EventPolicy_EnforcesTransientPolyphonyBounds(
            object playbackKindValue,
            byte polyphony,
            bool valid)
        {
            var playbackKind = (OrpheusPlaybackKind)playbackKindValue;
            var values = CreateValues(
                playbackKind,
                OrpheusCategory.SfxWorld,
                OrpheusLoadPolicy.BootstrapTransient,
                1,
                1f,
                1f,
                1f,
                1f,
                polyphony,
                0f,
                0f,
                100f,
                OrpheusRolloffMode.Linear);

            AssertMismatch(
                OrpheusAudioEventPolicy.Evaluate(values),
                OrpheusAudioEventPolicyMismatch.Polyphony,
                valid);
        }

        [TestCase(0f, true)]
        [TestCase(0.1f, true)]
        [TestCase(-0.01f, false)]
        public void EventPolicy_EnforcesFiniteNonNegativeCooldown(float cooldown, bool valid)
        {
            var values = CreateValues(
                OrpheusPlaybackKind.OneShot2D,
                OrpheusCategory.SfxUi,
                OrpheusLoadPolicy.BootstrapTransient,
                1,
                1f,
                1f,
                1f,
                1f,
                1,
                cooldown,
                0f,
                100f,
                OrpheusRolloffMode.Linear);

            AssertMismatch(
                OrpheusAudioEventPolicy.Evaluate(values),
                OrpheusAudioEventPolicyMismatch.Cooldown,
                valid);
        }

        [TestCase(0f, 0.01f, true)]
        [TestCase(-0.01f, 1f, false)]
        [TestCase(1f, 1f, false)]
        [TestCase(2f, 1f, false)]
        public void EventPolicy_EnforcesThreeDimensionalDistanceBounds(
            float minimum,
            float maximum,
            bool valid)
        {
            var values = CreateValues(
                OrpheusPlaybackKind.OneShot3D,
                OrpheusCategory.SfxWorld,
                OrpheusLoadPolicy.BootstrapTransient,
                1,
                1f,
                1f,
                1f,
                1f,
                1,
                0f,
                minimum,
                maximum,
                OrpheusRolloffMode.Logarithmic);

            AssertMismatch(
                OrpheusAudioEventPolicy.Evaluate(values),
                OrpheusAudioEventPolicyMismatch.DistanceRange,
                valid);
        }

        [Test]
        public void EventPolicy_RejectsNonFiniteCooldownAndDistance()
        {
            var cooldown = CreateValues(
                OrpheusPlaybackKind.OneShot2D,
                OrpheusCategory.SfxUi,
                OrpheusLoadPolicy.BootstrapTransient,
                1,
                1f,
                1f,
                1f,
                1f,
                1,
                float.PositiveInfinity,
                0f,
                100f,
                OrpheusRolloffMode.Linear);
            var distance = CreateValues(
                OrpheusPlaybackKind.OneShot3D,
                OrpheusCategory.SfxWorld,
                OrpheusLoadPolicy.BootstrapTransient,
                1,
                1f,
                1f,
                1f,
                1f,
                1,
                0f,
                float.NaN,
                float.PositiveInfinity,
                OrpheusRolloffMode.Linear);

            AssertMismatch(
                OrpheusAudioEventPolicy.Evaluate(cooldown),
                OrpheusAudioEventPolicyMismatch.Cooldown,
                false);
            AssertMismatch(
                OrpheusAudioEventPolicy.Evaluate(distance),
                OrpheusAudioEventPolicyMismatch.DistanceRange,
                false);
        }

        [TestCase(OrpheusPlaybackKind.OneShot3D, OrpheusCategory.Music)]
        [TestCase(OrpheusPlaybackKind.GlobalLoop2D, OrpheusCategory.Music)]
        [TestCase(OrpheusPlaybackKind.Bgm, OrpheusCategory.Ambience)]
        [TestCase(OrpheusPlaybackKind.ProfileAmbience, OrpheusCategory.Music)]
        public void EventPolicy_RejectsIllegalCategoryMatrix(
            object playbackKindValue,
            object categoryValue)
        {
            var playbackKind = (OrpheusPlaybackKind)playbackKindValue;
            var category = (OrpheusCategory)categoryValue;
            var persistent = playbackKind >= OrpheusPlaybackKind.GlobalLoop2D;
            var values = CreateValues(
                playbackKind,
                category,
                persistent ? OrpheusLoadPolicy.PersistentStream : OrpheusLoadPolicy.BootstrapTransient,
                1,
                1f,
                1f,
                1f,
                1f,
                1,
                0f,
                1f,
                100f,
                OrpheusRolloffMode.Logarithmic);

            Assert.That(
                OrpheusAudioEventPolicy.Evaluate(values) &
                OrpheusAudioEventPolicyMismatch.CategoryForPlaybackKind,
                Is.Not.EqualTo(OrpheusAudioEventPolicyMismatch.None));
        }

        [Test]
        public void EventPolicy_CollectsEveryIndependentScalarFailure()
        {
            var values = new OrpheusAudioEventPolicyValues(
                0,
                0,
                OrpheusPlaybackKind.OneShot3D,
                OrpheusCategory.Music,
                OrpheusLoadPolicy.PersistentStream,
                17,
                0.8f,
                0.2f,
                2f,
                0.5f,
                0,
                -1f,
                float.NaN,
                float.PositiveInfinity,
                OrpheusRolloffMode.Invalid);

            var mismatch = OrpheusAudioEventPolicy.Evaluate(values);

            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.SchemaVersion), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.Key), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.ClipCount), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.VolumeRange), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.PitchRange), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.Cooldown), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.CategoryForPlaybackKind), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.Polyphony), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.LoadPolicyForPlaybackKind), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.DistanceRange), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.RolloffMode), Is.True);
        }

        [Test]
        public void EventPolicy_EnforcesEveryPersistentInvariant()
        {
            var values = CreateValues(
                OrpheusPlaybackKind.Bgm,
                OrpheusCategory.Music,
                OrpheusLoadPolicy.ExplicitTransient,
                2,
                0.5f,
                0.75f,
                0.9f,
                1.1f,
                2,
                0.1f,
                1f,
                100f,
                OrpheusRolloffMode.Logarithmic);

            var mismatch = OrpheusAudioEventPolicy.Evaluate(values);

            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.Polyphony), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.LoadPolicyForPlaybackKind), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.PersistentClipCount), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.PersistentVolume), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.PersistentPitch), Is.True);
            Assert.That(mismatch.HasFlag(OrpheusAudioEventPolicyMismatch.PersistentCooldown), Is.True);
        }

        [TestCase(0, false)]
        [TestCase(1, true)]
        [TestCase(8, true)]
        [TestCase(9, true)]
        [TestCase(15, true)]
        [TestCase(16, true)]
        [TestCase(17, false)]
        public void EventPolicy_BoundsTransientClipCountToSixteen(int clipCount, bool valid)
        {
            var values = CreateValues(
                OrpheusPlaybackKind.OneShot2D,
                OrpheusCategory.SfxUi,
                OrpheusLoadPolicy.BootstrapTransient,
                clipCount,
                0.5f,
                1f,
                1f,
                1f,
                1,
                0f,
                1f,
                100f,
                OrpheusRolloffMode.Logarithmic);

            AssertMismatch(
                OrpheusAudioEventPolicy.Evaluate(values),
                OrpheusAudioEventPolicyMismatch.ClipCount,
                valid);
        }

        [TestCase(16, true)]
        [TestCase(17, false)]
        public void EventPolicy_AppliesTheClipCeilingToOneShot3D(int clipCount, bool valid)
        {
            var values = CreateValues(
                OrpheusPlaybackKind.OneShot3D,
                OrpheusCategory.SfxWorld,
                OrpheusLoadPolicy.ExplicitTransient,
                clipCount,
                0.5f,
                1f,
                1f,
                1f,
                12,
                0f,
                1f,
                100f,
                OrpheusRolloffMode.Linear);

            AssertMismatch(
                OrpheusAudioEventPolicy.Evaluate(values),
                OrpheusAudioEventPolicyMismatch.ClipCount,
                valid);
        }

        [Test]
        public void EventPolicy_StillRejectsMultiClipPersistentEventsInsideTheRaisedCeiling()
        {
            var values = CreateValues(
                OrpheusPlaybackKind.Bgm,
                OrpheusCategory.Music,
                OrpheusLoadPolicy.PersistentStream,
                16,
                0.6f,
                0.6f,
                1f,
                1f,
                1,
                0f,
                1f,
                100f,
                OrpheusRolloffMode.Logarithmic);

            var mismatch = OrpheusAudioEventPolicy.Evaluate(values);

            AssertMismatch(mismatch, OrpheusAudioEventPolicyMismatch.ClipCount, true);
            AssertMismatch(mismatch, OrpheusAudioEventPolicyMismatch.PersistentClipCount, false);
        }

        private static OrpheusAudioEventPolicyValues CreateValues(
            OrpheusPlaybackKind playbackKind,
            OrpheusCategory category,
            OrpheusLoadPolicy loadPolicy,
            int clipCount,
            float volumeMinimum,
            float volumeMaximum,
            float pitchMinimum,
            float pitchMaximum,
            byte polyphony,
            float cooldown,
            float minimumDistance,
            float maximumDistance,
            OrpheusRolloffMode rolloffMode)
        {
            return new OrpheusAudioEventPolicyValues(
                OrpheusAudioAuthoringSchema.Current,
                100,
                playbackKind,
                category,
                loadPolicy,
                clipCount,
                volumeMinimum,
                volumeMaximum,
                pitchMinimum,
                pitchMaximum,
                polyphony,
                cooldown,
                minimumDistance,
                maximumDistance,
                rolloffMode);
        }

        private static void AssertMismatch(
            OrpheusAudioEventPolicyMismatch mismatch,
            OrpheusAudioEventPolicyMismatch flag,
            bool valid)
        {
            Assert.That(
                (mismatch & flag) == OrpheusAudioEventPolicyMismatch.None,
                Is.EqualTo(valid));
        }
    }
}
