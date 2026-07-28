using System;
using System.Linq;
using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusCatalogLookupIndexTests
    {
        [Test]
        public void TryCreate_SortsKeysAndPreservesAuthoredClipRanges()
        {
            var authored = new[]
            {
                CreateEntry(300, 0, 2),
                CreateEntry(100, 2, 1),
                CreateEntry(200, 3, 2)
            };

            var created = OrpheusCatalogLookupIndex.TryCreate(
                authored,
                5,
                out var index,
                out var error);

            Assert.That(created, Is.True);
            Assert.That(error, Is.EqualTo(OrpheusCatalogBuildError.None));
            Assert.That(index.Count, Is.EqualTo(3));
            Assert.That(index.TryGet(new OrpheusAudioKey(100), out var first), Is.True);
            Assert.That(first.ClipRange.Offset, Is.EqualTo(2));
            Assert.That(first.ClipRange.Count, Is.EqualTo(1));
            Assert.That(index.TryGet(new OrpheusAudioKey(200), out var second), Is.True);
            Assert.That(second.ClipRange.Offset, Is.EqualTo(3));
            Assert.That(second.ClipRange.Count, Is.EqualTo(2));
            Assert.That(index.TryGet(new OrpheusAudioKey(300), out var third), Is.True);
            Assert.That(third.ClipRange.Offset, Is.EqualTo(0));
            Assert.That(third.ClipRange.Count, Is.EqualTo(2));
        }

        [Test]
        public void TryCreate_CopiesInputAndRetainsCompleteScalarPolicy()
        {
            var expected = new OrpheusCatalogEntry(
                new OrpheusAudioKey(42),
                new OrpheusCatalogScalarPolicy(
                    OrpheusPlaybackKind.OneShot3D,
                    OrpheusCategory.SfxWorld,
                    OrpheusLoadPolicy.ExplicitTransient,
                    new OrpheusVolumeRange(0.25f, 0.75f),
                    new OrpheusPitchRange(0.8f, 1.2f),
                    new OrpheusVoicePolicy(17, 6, 0.125f),
                    new OrpheusSpatialAttenuation(3.5f, 80f, OrpheusRolloffMode.Linear)),
                new OrpheusClipRange(0, 2));
            var authored = new[] { expected };

            Assert.That(
                OrpheusCatalogLookupIndex.TryCreate(authored, 2, out var index, out _),
                Is.True);

            authored[0] = CreateEntry(99, 0, 2);

            Assert.That(index.TryGet(new OrpheusAudioKey(42), out var actual), Is.True);
            Assert.That(actual.Key, Is.EqualTo(expected.Key));
            Assert.That(actual.Policy.PlaybackKind, Is.EqualTo(expected.Policy.PlaybackKind));
            Assert.That(actual.Policy.Category, Is.EqualTo(expected.Policy.Category));
            Assert.That(actual.Policy.LoadPolicy, Is.EqualTo(expected.Policy.LoadPolicy));
            Assert.That(actual.Policy.Volume.Minimum, Is.EqualTo(expected.Policy.Volume.Minimum));
            Assert.That(actual.Policy.Volume.Maximum, Is.EqualTo(expected.Policy.Volume.Maximum));
            Assert.That(actual.Policy.Pitch.Minimum, Is.EqualTo(expected.Policy.Pitch.Minimum));
            Assert.That(actual.Policy.Pitch.Maximum, Is.EqualTo(expected.Policy.Pitch.Maximum));
            Assert.That(actual.Policy.Voice.Priority, Is.EqualTo(expected.Policy.Voice.Priority));
            Assert.That(actual.Policy.Voice.PolyphonyCap, Is.EqualTo(expected.Policy.Voice.PolyphonyCap));
            Assert.That(actual.Policy.Voice.CooldownSeconds, Is.EqualTo(expected.Policy.Voice.CooldownSeconds));
            Assert.That(
                actual.Policy.SpatialAttenuation.MinimumDistance,
                Is.EqualTo(expected.Policy.SpatialAttenuation.MinimumDistance));
            Assert.That(
                actual.Policy.SpatialAttenuation.MaximumDistance,
                Is.EqualTo(expected.Policy.SpatialAttenuation.MaximumDistance));
            Assert.That(
                actual.Policy.SpatialAttenuation.RolloffMode,
                Is.EqualTo(expected.Policy.SpatialAttenuation.RolloffMode));
            Assert.That(actual.ClipRange.Offset, Is.EqualTo(expected.ClipRange.Offset));
            Assert.That(actual.ClipRange.Count, Is.EqualTo(expected.ClipRange.Count));
            Assert.That(index.TryGet(new OrpheusAudioKey(99), out _), Is.False);
        }

        [Test]
        public void TryCreate_AcceptsAnEmptyCatalog()
        {
            var created = OrpheusCatalogLookupIndex.TryCreate(
                Array.Empty<OrpheusCatalogEntry>(),
                0,
                out var index,
                out var error);

            Assert.That(created, Is.True);
            Assert.That(error, Is.EqualTo(OrpheusCatalogBuildError.None));
            Assert.That(index.Count, Is.Zero);
        }

        [Test]
        public void TryCreate_RejectsNullInput()
        {
            AssertBuildFailure(null, 0, OrpheusCatalogBuildError.InvalidArguments);
        }

        [Test]
        public void TryCreate_RejectsInvalidAndDuplicateKeys()
        {
            AssertBuildFailure(
                new[] { CreateEntry(0, 0, 1) },
                1,
                OrpheusCatalogBuildError.InvalidKey);
            AssertBuildFailure(
                new[] { CreateEntry(7, 0, 1), CreateEntry(7, 1, 1) },
                2,
                OrpheusCatalogBuildError.DuplicateKey);
        }

        [Test]
        public void TryCreate_RejectsNegativeOverflowingAndInconsistentClipRanges()
        {
            AssertBuildFailure(
                Array.Empty<OrpheusCatalogEntry>(),
                -1,
                OrpheusCatalogBuildError.InvalidClipRange);
            AssertBuildFailure(
                new[] { CreateEntry(1, -1, 1) },
                1,
                OrpheusCatalogBuildError.InvalidClipRange);
            AssertBuildFailure(
                new[] { CreateEntry(1, 0, 0) },
                0,
                OrpheusCatalogBuildError.InvalidClipRange);
            AssertBuildFailure(
                new[] { CreateEntry(1, 1, 1) },
                2,
                OrpheusCatalogBuildError.InvalidClipRange);
            AssertBuildFailure(
                new[]
                {
                    CreateEntry(1, 0, int.MaxValue),
                    CreateEntry(2, int.MaxValue, 1)
                },
                int.MaxValue,
                OrpheusCatalogBuildError.InvalidClipRange);
            AssertBuildFailure(
                new[] { CreateEntry(1, 0, 1) },
                2,
                OrpheusCatalogBuildError.InvalidClipRange);
        }

        [Test]
        public void TryGet_ResolvesFirstMiddleLastAndReturnsDefaultForFailures()
        {
            var authored = new[]
            {
                CreateEntry(40, 0, 1),
                CreateEntry(10, 1, 1),
                CreateEntry(30, 2, 1),
                CreateEntry(20, 3, 1)
            };
            Assert.That(
                OrpheusCatalogLookupIndex.TryCreate(authored, 4, out var index, out _),
                Is.True);

            Assert.That(index.TryGet(new OrpheusAudioKey(10), out var first), Is.True);
            Assert.That(first.Key.Value, Is.EqualTo(10));
            Assert.That(index.TryGet(new OrpheusAudioKey(30), out var middle), Is.True);
            Assert.That(middle.Key.Value, Is.EqualTo(30));
            Assert.That(index.TryGet(new OrpheusAudioKey(40), out var last), Is.True);
            Assert.That(last.Key.Value, Is.EqualTo(40));
            Assert.That(index.TryGet(new OrpheusAudioKey(25), out var missing), Is.False);
            Assert.That(missing, Is.EqualTo(default(OrpheusCatalogEntry)));
            Assert.That(index.TryGet(OrpheusAudioKey.Invalid, out var invalid), Is.False);
            Assert.That(invalid, Is.EqualTo(default(OrpheusCatalogEntry)));
            Assert.That(index.Count, Is.EqualTo(4));
        }

        [Test]
        public void TryGetRaw_ResolvesUshortIdentityWithoutTypedRequestKey()
        {
            var authored = new[]
            {
                CreateEntry(ushort.MaxValue, 0, 1),
                CreateEntry(200, 1, 1)
            };
            Assert.That(
                OrpheusCatalogLookupIndex.TryCreate(authored, 2, out var index, out _),
                Is.True);

            Assert.That(index.TryGetRaw(200, out var found, out var foundIndex), Is.True);
            Assert.That(found.Key.Value, Is.EqualTo(200));
            Assert.That(foundIndex, Is.Zero);
            Assert.That(index.TryGetRaw(201, out _, out var missingIndex), Is.False);
            Assert.That(missingIndex, Is.EqualTo(-1));
            Assert.That(
                index.TryGetRaw(ushort.MaxValue, out var maximum, out var maximumIndex),
                Is.True);
            Assert.That(maximum.Key.Value, Is.EqualTo(ushort.MaxValue));
            Assert.That(maximumIndex, Is.EqualTo(1));
        }

        [Test]
        public void TryGet_HandlesSyntheticCatalogWith1024Keys()
        {
            const int count = 1024;
            var authored = new OrpheusCatalogEntry[count];
            for (var authoredIndex = 0; authoredIndex < count; authoredIndex++)
            {
                var key = (ushort)(count - authoredIndex);
                authored[authoredIndex] = CreateEntry(key, authoredIndex, 1);
            }

            Assert.That(
                OrpheusCatalogLookupIndex.TryCreate(authored, count, out var index, out _),
                Is.True);
            Assert.That(index.TryGet(new OrpheusAudioKey(1), out var first), Is.True);
            Assert.That(first.ClipRange.Offset, Is.EqualTo(1023));
            Assert.That(index.TryGet(new OrpheusAudioKey(512), out var middle), Is.True);
            Assert.That(middle.ClipRange.Offset, Is.EqualTo(512));
            Assert.That(index.TryGet(new OrpheusAudioKey(1024), out var last), Is.True);
            Assert.That(last.ClipRange.Offset, Is.EqualTo(0));
            Assert.That(index.TryGet(new OrpheusAudioKey(2048), out _), Is.False);
        }

        [Test]
        public void TryGet_DoesNotAllocate()
        {
            var authored = new OrpheusCatalogEntry[32];
            for (var i = 0; i < authored.Length; i++)
            {
                authored[i] = CreateEntry((ushort)(i + 1), i, 1);
            }

            Assert.That(
                OrpheusCatalogLookupIndex.TryCreate(authored, authored.Length, out var index, out _),
                Is.True);
            index.TryGet(new OrpheusAudioKey(17), out _);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1024; i++)
            {
                index.TryGet(new OrpheusAudioKey(17), out _);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void CoreAssemblyAndClipSelectionContainNoUnityObjectSurface()
        {
            var referencedAssemblies = typeof(OrpheusCatalogLookupIndex)
                .Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .ToArray();
            var selectionProperties = typeof(OrpheusClipSelection)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name)
                .ToArray();

            Assert.That(
                referencedAssemblies.Any(
                    name => name.StartsWith("UnityEngine", StringComparison.Ordinal)),
                Is.False);
            Assert.That(selectionProperties, Is.EqualTo(new[] { "Key", "SelectedClipIndex" }));
        }

        private static OrpheusCatalogEntry CreateEntry(ushort key, int clipOffset, int clipCount)
        {
            return new OrpheusCatalogEntry(
                new OrpheusAudioKey(key),
                new OrpheusCatalogScalarPolicy(
                    OrpheusPlaybackKind.OneShot2D,
                    OrpheusCategory.SfxUi,
                    OrpheusLoadPolicy.BootstrapTransient,
                    new OrpheusVolumeRange(1f, 1f),
                    new OrpheusPitchRange(1f, 1f),
                    new OrpheusVoicePolicy(128, 1, 0f),
                    new OrpheusSpatialAttenuation(0f, 0f, OrpheusRolloffMode.Invalid)),
                new OrpheusClipRange(clipOffset, clipCount));
        }

        private static void AssertBuildFailure(
            OrpheusCatalogEntry[] authored,
            int flattenedClipCount,
            OrpheusCatalogBuildError expectedError)
        {
            var created = OrpheusCatalogLookupIndex.TryCreate(
                authored,
                flattenedClipCount,
                out var index,
                out var error);

            Assert.That(created, Is.False);
            Assert.That(index, Is.Null);
            Assert.That(error, Is.EqualTo(expectedError));
        }
    }
}
