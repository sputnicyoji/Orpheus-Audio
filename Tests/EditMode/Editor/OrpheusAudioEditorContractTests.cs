using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioEditorContractTests
    {
        [Test]
        public void ValidationProfile_HasExactlyTheNormativeSerializedFields()
        {
            var fields = typeof(OrpheusAudioValidationProfile)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(field => field.IsDefined(typeof(SerializeField), false))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(fields, Is.EqualTo(new[]
            {
                "_catalog",
                "_enabled",
                "_keyManifest",
                "_listenerScenes",
                "_runtimeHostPrefab",
                "_schemaVersion",
                "_settings"
            }));
        }

        [Test]
        public void ValidationProfile_ListenerSceneSlotsAreStronglyTyped()
        {
            var field = typeof(OrpheusAudioValidationProfile).GetField(
                "_listenerScenes",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(SceneAsset[])));
        }

        [Test]
        public void Settings_HasExactlyTheNormativeSerializedFields()
        {
            var fields = typeof(OrpheusAudioSettings)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(field => field.IsDefined(typeof(SerializeField), false))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(fields, Is.EqualTo(new[]
            {
                "_ambienceState",
                "_ambienceUser",
                "_androidManualResetEnabled",
                "_bgmCrossfadeSeconds",
                "_combat",
                "_master",
                "_menu",
                "_mixer",
                "_musicState",
                "_musicUser",
                "_pause",
                "_peace",
                "_profileAmbienceCrossfadeSeconds",
                "_schemaVersion",
                "_sfxCombatState",
                "_sfxCombatUser",
                "_sfxUiState",
                "_sfxUiUser",
                "_sfxWorldState",
                "_sfxWorldUser",
                "_snapshotTransitionSeconds"
            }));
        }

        [Test]
        public void Settings_DefaultsMatchTheNormativeDurationsAndAndroidPolicy()
        {
            var settings = ScriptableObject.CreateInstance<OrpheusAudioSettings>();

            Assert.That(settings.BgmCrossfadeSeconds, Is.EqualTo(0.4f));
            Assert.That(settings.ProfileAmbienceCrossfadeSeconds, Is.EqualTo(0.4f));
            Assert.That(settings.SnapshotTransitionSeconds, Is.EqualTo(0.6f));
            Assert.That(settings.AndroidManualResetEnabled, Is.False);

            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void AudioEvent_HasOnlyTheNormativeSerializedConcepts()
        {
            var fields = typeof(OrpheusAudioEvent)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(field => field.IsDefined(typeof(SerializeField), false))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(fields, Is.EqualTo(new[]
            {
                "_category",
                "_clips",
                "_cooldownSeconds",
                "_key",
                "_loadPolicy",
                "_maximumDistance",
                "_minimumDistance",
                "_pitchMax",
                "_pitchMin",
                "_playbackKind",
                "_polyphonyCap",
                "_priority",
                "_rolloffMode",
                "_schemaVersion",
                "_volumeMax",
                "_volumeMin"
            }));
        }

        [Test]
        public void EveryAuthoringAsset_DefaultsToCurrentSchema()
        {
            var settings = ScriptableObject.CreateInstance<OrpheusAudioSettings>();
            var audioEvent = ScriptableObject.CreateInstance<OrpheusAudioEvent>();
            var catalog = ScriptableObject.CreateInstance<OrpheusAudioCatalog>();
            var manifest = ScriptableObject.CreateInstance<OrpheusAudioKeyManifest>();
            var profile = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();

            Assert.That(settings.SchemaVersion, Is.EqualTo(OrpheusAudioAuthoringSchema.Current));
            Assert.That(audioEvent.SchemaVersion, Is.EqualTo(OrpheusAudioAuthoringSchema.Current));
            Assert.That(catalog.SchemaVersion, Is.EqualTo(OrpheusAudioAuthoringSchema.Current));
            Assert.That(manifest.SchemaVersion, Is.EqualTo(OrpheusAudioAuthoringSchema.Current));
            Assert.That(profile.SchemaVersion, Is.EqualTo(OrpheusAudioAuthoringSchema.Current));

            UnityEngine.Object.DestroyImmediate(settings);
            UnityEngine.Object.DestroyImmediate(audioEvent);
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(manifest);
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void TypedProjection_IsOrderIndependentAndEmitsOnlyActiveKeys()
        {
            var first = CreateManifest(
                CreateEntry(300, "RetiredCue", OrpheusAudioKeyStatus.Retired),
                CreateEntry(100, "ActiveCue", OrpheusAudioKeyStatus.Active),
                CreateEntry(200, "ReservedCue", OrpheusAudioKeyStatus.Reserved));
            var reordered = CreateManifest(
                CreateEntry(200, "ReservedCue", OrpheusAudioKeyStatus.Reserved),
                CreateEntry(300, "RetiredCue", OrpheusAudioKeyStatus.Retired),
                CreateEntry(100, "ActiveCue", OrpheusAudioKeyStatus.Active));

            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    first, out var firstSource, out var firstHash),
                Is.True);
            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    reordered, out var reorderedSource, out var reorderedHash),
                Is.True);

            Assert.That(firstHash, Is.EqualTo(reorderedHash));
            Assert.That(
                firstHash,
                Is.EqualTo("f7798edf317a94266e763edbc86cd5b8571a581478af4b7ba9a71d5737d15730"));
            Assert.That(firstHash.Length, Is.EqualTo(64));
            Assert.That(firstSource, Is.EqualTo(reorderedSource));
            Assert.That(firstSource, Does.Contain("ManifestSchemaVersion = 1"));
            Assert.That(firstSource, Does.Contain("OrpheusAudioKey ActiveCue"));
            Assert.That(firstSource, Does.Not.Contain("OrpheusAudioKey ReservedCue"));
            Assert.That(firstSource, Does.Not.Contain("OrpheusAudioKey RetiredCue"));

            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(reordered);
        }

        [Test]
        public void TypedProjection_RejectsInvalidOrDuplicateManifestEntries()
        {
            var invalid = CreateManifest(
                CreateEntry(0, "class", OrpheusAudioKeyStatus.Invalid));
            var duplicateId = CreateManifest(
                CreateEntry(100, "First", OrpheusAudioKeyStatus.Active),
                CreateEntry(100, "Second", OrpheusAudioKeyStatus.Active));
            var duplicateSymbol = CreateManifest(
                CreateEntry(100, "Same", OrpheusAudioKeyStatus.Active),
                CreateEntry(200, "Same", OrpheusAudioKeyStatus.Active));

            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    invalid, out _, out _),
                Is.False);
            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    duplicateId, out _, out _),
                Is.False);
            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    duplicateSymbol, out _, out _),
                Is.False);

            UnityEngine.Object.DestroyImmediate(invalid);
            UnityEngine.Object.DestroyImmediate(duplicateId);
            UnityEngine.Object.DestroyImmediate(duplicateSymbol);
        }

        [TestCase("OrpheusAudioKeys")]
        [TestCase("ManifestSchemaVersion")]
        [TestCase("ManifestContentHash")]
        [TestCase("__arglist")]
        [TestCase("__makeref")]
        [TestCase("__reftype")]
        [TestCase("__refvalue")]
        [TestCase("Cue__Internal")]
        public void TypedProjection_RejectsGeneratedCodeReservedNames(string symbol)
        {
            var manifest = CreateManifest(
                CreateEntry(100, symbol, OrpheusAudioKeyStatus.Active));

            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    manifest, out _, out _),
                Is.False);

            UnityEngine.Object.DestroyImmediate(manifest);
        }

        [Test]
        public void ManifestSymbolRenameChangesProjectionWithoutChangingNumericIdentity()
        {
            var before = CreateManifest(
                CreateEntry(64000, "BeforeRename", OrpheusAudioKeyStatus.Active));
            var after = CreateManifest(
                CreateEntry(64000, "AfterRename", OrpheusAudioKeyStatus.Active));

            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    before, out var beforeSource, out var beforeHash),
                Is.True);
            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    after, out var afterSource, out var afterHash),
                Is.True);

            Assert.That(beforeSource, Does.Contain("BeforeRename = new OrpheusAudioKey(64000)"));
            Assert.That(afterSource, Does.Contain("AfterRename = new OrpheusAudioKey(64000)"));
            Assert.That(afterHash, Is.Not.EqualTo(beforeHash));

            UnityEngine.Object.DestroyImmediate(before);
            UnityEngine.Object.DestroyImmediate(after);
        }

        internal static OrpheusAudioKeyManifest CreateManifest(
            params OrpheusAudioKeyManifestEntry[] entries)
        {
            var manifest = ScriptableObject.CreateInstance<OrpheusAudioKeyManifest>();
            SetField(manifest, "_entries", entries);
            return manifest;
        }

        internal static OrpheusAudioKeyManifestEntry CreateEntry(
            ushort id,
            string symbol,
            OrpheusAudioKeyStatus status)
        {
            object boxed = default(OrpheusAudioKeyManifestEntry);
            SetField(boxed, "_id", id);
            SetField(boxed, "_symbol", symbol);
            SetField(boxed, "_status", status);
            return (OrpheusAudioKeyManifestEntry)boxed;
        }

        internal static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }
    }
}
