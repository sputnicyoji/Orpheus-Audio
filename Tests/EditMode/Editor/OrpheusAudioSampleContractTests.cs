using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioSampleContractTests
    {
        private const string PackageRoot = "Packages/com.orpheus.audio";
        private const string SampleParent = PackageRoot + "/Samples~";
        private const string SampleRoot = SampleParent + "/Minimal Setup";
        private const string ProjectionRoot = "Library/OrpheusIssue19ContractTests";
        private const string ImportedSampleRoot = "Assets/OrpheusIssue19SampleContract";
        private const string FixtureRoot =
            PackageRoot + "/Tests/Fixtures/NonEmptyHost/Editor/Resources/OrpheusIssue19";

        private static readonly string[] RequiredSampleFiles =
        {
            "README.md",
            "Audio/OrpheusMinimalSetup.mixer",
            "Prefabs/OrpheusRuntimeHost.prefab",
            "Runtime/MinimalCompositionRoot.cs",
            "Runtime/MinimalSceneBinding.cs",
            "Runtime/Orpheus.Audio.Samples.MinimalSetup.asmdef",
            "Runtime/PlayerPrefsGainStore.cs",
            "Scenes/MinimalSetup.unity",
            "Settings/EmptyCatalog.asset",
            "Settings/EmptyKeyManifest.asset",
            "Settings/OrpheusAudioSettings.asset",
            "Settings/ValidationProfile.asset",
            "Tests/Orpheus.Audio.Samples.MinimalSetup.Tests.asmdef",
            "Tests/OrpheusAudioMinimalSetupSmokeTests.cs"
        };

        private static readonly string[] RequiredSampleFolders =
        {
            SampleRoot,
            SampleRoot + "/Audio",
            SampleRoot + "/Prefabs",
            SampleRoot + "/Runtime",
            SampleRoot + "/Scenes",
            SampleRoot + "/Settings",
            SampleRoot + "/Tests"
        };

        private static readonly string[] ReadmeTopics =
        {
            "Installation",
            "Ownership",
            "Lifecycle",
            "Mixer Duplication",
            "Bootstrap",
            "Scene Binding",
            "Gain Persistence",
            "xLua Integration"
        };

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(ImportedSampleRoot))
            {
                AssetDatabase.DeleteAsset(ImportedSampleRoot);
            }

            if (Directory.Exists(ProjectionRoot))
            {
                Directory.Delete(ProjectionRoot, true);
            }
        }

        [Test]
        public void MinimalSetup_HasTheExactRequiredFileAndMetaShape()
        {
            Assert.That(Directory.Exists(SampleRoot), Is.True, SampleRoot);
            var actualFiles = Directory.GetFiles(SampleRoot, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .Select(ToSampleRelativePath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                actualFiles,
                Is.EqualTo(RequiredSampleFiles.OrderBy(path => path, StringComparer.Ordinal)));

            foreach (var relativePath in RequiredSampleFiles)
            {
                AssertStableMeta(SampleRoot + "/" + relativePath + ".meta");
            }

            foreach (var folder in RequiredSampleFolders)
            {
                Assert.That(Directory.Exists(folder), Is.True, folder);
                AssertStableMeta(folder + ".meta");
            }

            // Unity never imports a folder ending in "~"; a .meta beside it makes every
            // consumer log a missing-folder warning on package resolution.
            Assert.That(Directory.Exists(SampleParent), Is.True, SampleParent);
            Assert.That(File.Exists(SampleParent + ".meta"), Is.False, SampleParent + ".meta");
        }

        [Test]
        public void MinimalSetupReadme_ContainsTheRequiredTopics()
        {
            var headings = File.ReadAllLines(SampleRoot + "/README.md")
                .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
                .Select(line => line.Substring(3).Trim())
                .ToArray();

            Assert.That(headings, Does.Contain(ReadmeTopics[0]));
            for (var i = 1; i < ReadmeTopics.Length; i++)
            {
                Assert.That(headings, Does.Contain(ReadmeTopics[i]));
            }
        }

        [Test]
        public void RuntimeAndSample_DoNotCrossTheirOwnershipBoundaries()
        {
            var runtime = ReadSources(PackageRoot + "/Runtime");
            AssertNoMatch(runtime, @"\bPlayerPrefs\b", "Runtime must not own gain persistence.");
            AssertNoMatch(runtime, @"\bDontDestroyOnLoad\b", "Runtime must not own Scene survival.");
            AssertNoMatch(runtime, @"\bAudioListener\s*\.\s*pause\b", "Only Owned Sources may pause.");
            AssertNoMatch(runtime, @"\bxLua\b", "Production Runtime has no xLua dependency.");

            var sample = ReadSources(SampleRoot + "/Runtime");
            AssertNoMatch(sample, @"\bDontDestroyOnLoad\b", "The sample is scene-scoped.");
            AssertNoMatch(sample, @"\bAudioListener\s*\.\s*pause\b", "The sample must not globally pause audio.");
            AssertNoMatch(sample, @"\.\s*Tick\s*\(", "Runtime Host exclusively owns Tick.");
            AssertNoMatch(sample, @"\.\s*LateTick\s*\(", "Runtime Host exclusively owns LateTick.");
            AssertNoMatch(
                sample,
                @"\bstatic\s+[^;\r\n]+\bInstance\b",
                "The sample must not introduce a singleton instance.");
        }

        [Test]
        public void RuntimeHost_IsTheOnlyProductionTickAndLateTickCaller()
        {
            AssertOnlyRuntimeHostCalls("Tick");
            AssertOnlyRuntimeHostCalls("LateTick");
        }

        [Test]
        public void EmptyMinimalSetup_ContainsNoAudioEvents()
        {
            var eventGuids = AssetDatabase.FindAssets(
                "t:" + nameof(OrpheusAudioEvent),
                new[] { SampleRoot });

            Assert.That(eventGuids, Is.Empty);
        }

        [Test]
        public void EmptyMinimalSetupAssets_PassTheSharedValidator()
        {
            ImportStaticSampleAssets();
            var profiles = AssetDatabase.FindAssets(
                    "t:" + nameof(OrpheusAudioValidationProfile),
                    new[] { ImportedSampleRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<OrpheusAudioValidationProfile>)
                .Where(profile => profile != null && profile.Enabled)
                .ToArray();

            Assert.That(profiles, Has.Length.EqualTo(1));
            Assert.That(
                AssetDatabase.GetAssetPath(profiles[0]),
                Is.EqualTo(ImportedSampleRoot + "/Settings/ValidationProfile.asset"));

            var profile = profiles[0];
            Assert.That(profile.Catalog, Is.Not.Null);
            Assert.That(profile.Catalog.EventCount, Is.Zero);
            Assert.That(profile.KeyManifest, Is.Not.Null);
            Assert.That(profile.KeyManifest.EntryCount, Is.Zero);
            Assert.That(profile.ListenerSceneCount, Is.EqualTo(1));

            WriteProjection(profile.KeyManifest, out var paths);

            var errors = OrpheusAudioValidator.ValidateProfile(
                profile,
                BuildTargetGroup.Standalone,
                paths);

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void MinimalSetupSettingsMixerAndSourceBank_MatchTheNormativeContract()
        {
            ImportStaticSampleAssets();
            var settings = AssetDatabase.LoadAssetAtPath<OrpheusAudioSettings>(
                ImportedSampleRoot + "/Settings/OrpheusAudioSettings.asset");
            var runtimeHostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ImportedSampleRoot + "/Prefabs/OrpheusRuntimeHost.prefab");
            Assert.That(settings, Is.Not.Null);
            Assert.That(runtimeHostPrefab, Is.Not.Null);

            Assert.That(settings.SchemaVersion, Is.EqualTo(1));
            Assert.That(settings.BgmCrossfadeSeconds, Is.EqualTo(0.4f));
            Assert.That(settings.ProfileAmbienceCrossfadeSeconds, Is.EqualTo(0.4f));
            Assert.That(settings.SnapshotTransitionSeconds, Is.EqualTo(0.6f));
            Assert.That(settings.AndroidManualResetEnabled, Is.False);
            Assert.That(settings.Mixer, Is.Not.Null);
            Assert.That(settings.Mixer.updateMode, Is.EqualTo(AudioMixerUpdateMode.UnscaledTime));

            Assert.That(
                OrpheusAudioIntegrationValidation.TryCaptureMixerModel(
                    settings.Mixer, out var mixerModel),
                Is.True);
            var stateGroupIndices = new[] { 2, 4, 6, 8, 10 };
            var expected = new[,]
            {
                { 0f, 0f, 0f, 0f, 0f },
                { -3f, 0f, -2f, 0f, -6f },
                { -3f, -6f, -6f, 0f, -12f },
                { -8f, -80f, -80f, 0f, -8f }
            };
            for (var snapshotIndex = 0; snapshotIndex < expected.GetLength(0); snapshotIndex++)
            {
                var snapshot = mixerModel.Snapshots[snapshotIndex];
                Assert.That(snapshot, Is.Not.Null, "snapshot " + snapshotIndex);
                for (var stateIndex = 0; stateIndex < stateGroupIndices.Length; stateIndex++)
                {
                    var parameter = mixerModel.Groups[stateGroupIndices[stateIndex]].VolumeParameter;
                    var valueIndex = Array.IndexOf(snapshot.Parameters, parameter);
                    Assert.That(valueIndex, Is.GreaterThanOrEqualTo(0), parameter);
                    Assert.That(
                        snapshot.Values[valueIndex],
                        Is.EqualTo(expected[snapshotIndex, stateIndex]),
                        "snapshot " + snapshotIndex + " state " + stateIndex);
                }
            }

            Assert.That(
                runtimeHostPrefab.GetComponentsInChildren<OrpheusAudioRuntimeHost>(true),
                Has.Length.EqualTo(1));
            Assert.That(
                runtimeHostPrefab.GetComponentsInChildren<OrpheusAudioSourceBank>(true),
                Has.Length.EqualTo(1));
            Assert.That(
                runtimeHostPrefab.GetComponentsInChildren<AudioSource>(true),
                Has.Length.EqualTo(24));
        }

        [Test]
        public void NonEmptyFixtureAssets_CoverEverySupportedKindPolicyPairAndPassValidation()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<OrpheusAudioCatalog>(
                FixtureRoot + "/FixtureCatalog.asset");
            var manifest = AssetDatabase.LoadAssetAtPath<OrpheusAudioKeyManifest>(
                FixtureRoot + "/FixtureKeyManifest.asset");
            var settings = AssetDatabase.LoadAssetAtPath<OrpheusAudioSettings>(
                FixtureRoot + "/FixtureSettings.asset");
            var runtimeHostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                FixtureRoot + "/FixtureRuntimeHost.prefab");
            var listenerScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                FixtureRoot + "/FixtureHost.unity");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(settings, Is.Not.Null);
            Assert.That(runtimeHostPrefab, Is.Not.Null);
            Assert.That(listenerScene, Is.Not.Null);

            var expected = new[]
            {
                new FixtureEntry(100, OrpheusPlaybackKind.OneShot2D,
                    OrpheusLoadPolicy.BootstrapTransient),
                new FixtureEntry(101, OrpheusPlaybackKind.OneShot2D,
                    OrpheusLoadPolicy.ExplicitTransient),
                new FixtureEntry(102, OrpheusPlaybackKind.OneShot3D,
                    OrpheusLoadPolicy.BootstrapTransient),
                new FixtureEntry(103, OrpheusPlaybackKind.OneShot3D,
                    OrpheusLoadPolicy.ExplicitTransient),
                new FixtureEntry(104, OrpheusPlaybackKind.GlobalLoop2D,
                    OrpheusLoadPolicy.PersistentStream),
                new FixtureEntry(105, OrpheusPlaybackKind.Bgm,
                    OrpheusLoadPolicy.PersistentStream),
                new FixtureEntry(106, OrpheusPlaybackKind.ProfileAmbience,
                    OrpheusLoadPolicy.PersistentStream)
            };

            Assert.That(catalog.EventCount, Is.EqualTo(expected.Length));
            Assert.That(manifest.EntryCount, Is.EqualTo(expected.Length));
            for (var index = 0; index < expected.Length; index++)
            {
                var audioEvent = catalog.GetEvent(index);
                Assert.That(audioEvent, Is.Not.Null, "event " + index);
                Assert.That(audioEvent.Key.Value, Is.EqualTo(expected[index].Key));
                Assert.That(audioEvent.PlaybackKind, Is.EqualTo(expected[index].PlaybackKind));
                Assert.That(audioEvent.LoadPolicy, Is.EqualTo(expected[index].LoadPolicy));
                Assert.That(audioEvent.ClipCount, Is.EqualTo(1));

                var clipPath = AssetDatabase.GetAssetPath(audioEvent.GetClip(0));
                var importer = AssetImporter.GetAtPath(clipPath) as AudioImporter;
                Assert.That(importer, Is.Not.Null, clipPath);
                Assert.That(
                    OrpheusAudioValidator.EvaluateImporterPolicy(
                        importer,
                        expected[index].PlaybackKind,
                        expected[index].LoadPolicy,
                        BuildTargetGroup.Standalone),
                    Is.EqualTo(OrpheusAudioImportPolicyMismatch.None),
                    clipPath);

                var manifestEntry = manifest.GetEntry(index);
                Assert.That(manifestEntry.Id, Is.EqualTo(expected[index].Key));
                Assert.That(manifestEntry.Status, Is.EqualTo(OrpheusAudioKeyStatus.Active));
            }

            Assert.That(
                catalog.TryBuildSnapshot(out _, out var error, out _, out _),
                Is.True,
                error.ToString());

            var profile = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            try
            {
                var serialized = new SerializedObject(profile);
                serialized.FindProperty("_enabled").boolValue = true;
                serialized.FindProperty("_settings").objectReferenceValue = settings;
                serialized.FindProperty("_catalog").objectReferenceValue = catalog;
                serialized.FindProperty("_keyManifest").objectReferenceValue = manifest;
                serialized.FindProperty("_runtimeHostPrefab").objectReferenceValue = runtimeHostPrefab;
                var scenes = serialized.FindProperty("_listenerScenes");
                scenes.arraySize = 1;
                scenes.GetArrayElementAtIndex(0).objectReferenceValue = listenerScene;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                WriteProjection(manifest, out var paths);
                var errors = OrpheusAudioValidator.ValidateProfile(
                    profile,
                    BuildTargetGroup.Standalone,
                    paths);
                Assert.That(errors, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void NonEmptyFixtureHost_UsesOnlyExplicitSceneBindingForListenerOwnership()
        {
            var fixtureHostPath = PackageRoot +
                "/Tests/Fixtures/NonEmptyHost/OrpheusAudioFixtureHost.cs";
            var fixtureBindingPath = PackageRoot +
                "/Tests/Fixtures/NonEmptyHost/OrpheusAudioFixtureSceneBinding.cs";
            var fixtureHostSource = File.ReadAllText(fixtureHostPath);

            Assert.That(File.Exists(fixtureBindingPath), Is.True, fixtureBindingPath);
            Assert.That(fixtureHostSource, Does.Not.Contain("FindObjectsOfType"));
            Assert.That(fixtureHostSource, Does.Contain("_sceneBinding.BindListener(manager)"));
            Assert.That(fixtureHostSource, Does.Contain("_sceneBinding.RemoveListener(manager)"));
            Assert.That(fixtureHostSource, Does.Not.Match(@"manager\.BindListener\s*\("));
            Assert.That(fixtureHostSource, Does.Not.Match(@"manager\.RemoveListener\s*\("));
        }

        [TestCase(OrpheusPlaybackKind.OneShot2D, OrpheusLoadPolicy.PersistentStream)]
        [TestCase(OrpheusPlaybackKind.OneShot3D, OrpheusLoadPolicy.PersistentStream)]
        [TestCase(OrpheusPlaybackKind.GlobalLoop2D, OrpheusLoadPolicy.BootstrapTransient)]
        [TestCase(OrpheusPlaybackKind.GlobalLoop2D, OrpheusLoadPolicy.ExplicitTransient)]
        [TestCase(OrpheusPlaybackKind.Bgm, OrpheusLoadPolicy.BootstrapTransient)]
        [TestCase(OrpheusPlaybackKind.Bgm, OrpheusLoadPolicy.ExplicitTransient)]
        [TestCase(OrpheusPlaybackKind.ProfileAmbience, OrpheusLoadPolicy.BootstrapTransient)]
        [TestCase(OrpheusPlaybackKind.ProfileAmbience, OrpheusLoadPolicy.ExplicitTransient)]
        public void NonEmptyFixtureValidator_RejectsEveryUnsupportedKindPolicyPair(
            object playbackKindValue,
            object loadPolicyValue)
        {
            var playbackKind = (OrpheusPlaybackKind)playbackKindValue;
            var loadPolicy = (OrpheusLoadPolicy)loadPolicyValue;
            var catalog = AssetDatabase.LoadAssetAtPath<OrpheusAudioCatalog>(
                FixtureRoot + "/FixtureCatalog.asset");
            var manifest = AssetDatabase.LoadAssetAtPath<OrpheusAudioKeyManifest>(
                FixtureRoot + "/FixtureKeyManifest.asset");
            var settings = AssetDatabase.LoadAssetAtPath<OrpheusAudioSettings>(
                FixtureRoot + "/FixtureSettings.asset");
            var runtimeHostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                FixtureRoot + "/FixtureRuntimeHost.prefab");
            var listenerScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                FixtureRoot + "/FixtureHost.unity");
            var audioEvent = Enumerable.Range(0, catalog.EventCount)
                .Select(catalog.GetEvent)
                .First(candidate => candidate.PlaybackKind == playbackKind);
            var eventPath = AssetDatabase.GetAssetPath(audioEvent);
            var originalLoadPolicy = audioEvent.LoadPolicy;
            var profile = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();

            try
            {
                var eventObject = new SerializedObject(audioEvent);
                eventObject.FindProperty("_loadPolicy").enumValueIndex = (int)loadPolicy;
                eventObject.ApplyModifiedPropertiesWithoutUndo();

                var serialized = new SerializedObject(profile);
                serialized.FindProperty("_enabled").boolValue = true;
                serialized.FindProperty("_settings").objectReferenceValue = settings;
                serialized.FindProperty("_catalog").objectReferenceValue = catalog;
                serialized.FindProperty("_keyManifest").objectReferenceValue = manifest;
                serialized.FindProperty("_runtimeHostPrefab").objectReferenceValue = runtimeHostPrefab;
                var scenes = serialized.FindProperty("_listenerScenes");
                scenes.arraySize = 1;
                scenes.GetArrayElementAtIndex(0).objectReferenceValue = listenerScene;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                WriteProjection(manifest, out var paths);
                var errors = OrpheusAudioValidator.ValidateProfile(
                    profile,
                    BuildTargetGroup.Standalone,
                    paths);
                var expectedDetail =
                    (uint)OrpheusAudioEventPolicyMismatch.LoadPolicyForPlaybackKind;

                Assert.That(errors.Any(error =>
                    error.Code == OrpheusAudioValidationErrorCode.InvalidEventPolicy &&
                    error.AssetPath == eventPath &&
                    error.Detail == expectedDetail), Is.True);
            }
            finally
            {
                var eventObject = new SerializedObject(audioEvent);
                eventObject.FindProperty("_loadPolicy").enumValueIndex =
                    (int)originalLoadPolicy;
                eventObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.ClearDirty(audioEvent);
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        private static void ImportStaticSampleAssets()
        {
            if (AssetDatabase.IsValidFolder(ImportedSampleRoot))
            {
                AssetDatabase.DeleteAsset(ImportedSampleRoot);
            }

            Directory.CreateDirectory(ImportedSampleRoot);
            foreach (var folderName in new[] { "Audio", "Prefabs", "Scenes", "Settings" })
            {
                CopyDirectory(
                    SampleRoot + "/" + folderName,
                    ImportedSampleRoot + "/" + folderName);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, destination + "/" + Path.GetFileName(file), true);
            }

            foreach (var child in Directory.GetDirectories(source))
            {
                CopyDirectory(child, destination + "/" + Path.GetFileName(child));
            }
        }

        private static void WriteProjection(
            OrpheusAudioKeyManifest manifest,
            out OrpheusAudioTypedKeyProjectionPaths paths)
        {
            paths = new OrpheusAudioTypedKeyProjectionPaths(ProjectionRoot);
            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    manifest, out var source, out _),
                Is.True);
            Directory.CreateDirectory(ProjectionRoot);
            File.WriteAllText(paths.AssemblyPath, OrpheusAudioTypedKeyProjection.ExpectedAssembly);
            File.WriteAllText(paths.SourcePath, source, new UTF8Encoding(false));
        }

        private static string ToSampleRelativePath(string path)
        {
            return path.Substring(SampleRoot.Length + 1).Replace('\\', '/');
        }

        private static void AssertStableMeta(string path)
        {
            Assert.That(File.Exists(path), Is.True, path);
            Assert.That(
                Regex.IsMatch(
                    File.ReadAllText(path),
                    @"^guid: [0-9a-f]{32}\s*$",
                    RegexOptions.Multiline | RegexOptions.CultureInvariant),
                Is.True,
                path + " must contain a stable Unity GUID.");
        }

        private static IReadOnlyList<SourceText> ReadSources(string root)
        {
            return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new SourceText(path.Replace('\\', '/'), File.ReadAllText(path)))
                .ToArray();
        }

        private static void AssertNoMatch(
            IReadOnlyList<SourceText> sources,
            string pattern,
            string message)
        {
            var matches = sources
                .Where(source => Regex.IsMatch(
                    source.Text,
                    pattern,
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
                .Select(source => source.Path)
                .ToArray();

            Assert.That(matches, Is.Empty, message);
        }

        private static void AssertOnlyRuntimeHostCalls(string methodName)
        {
            var pattern = @"\.\s*" + Regex.Escape(methodName) + @"\s*\(";
            var callers = ReadSources(PackageRoot + "/Runtime")
                .SelectMany(source => Regex.Matches(
                        source.Text,
                        pattern,
                        RegexOptions.CultureInvariant)
                    .Cast<Match>()
                    .Select(_ => source.Path))
                .ToArray();

            Assert.That(callers, Has.Length.EqualTo(1));
            Assert.That(
                callers[0],
                Does.EndWith("/Runtime/OrpheusAudioRuntimeHost.cs"));
        }

        private readonly struct SourceText
        {
            internal SourceText(string path, string text)
            {
                Path = path;
                Text = text;
            }

            internal string Path { get; }
            internal string Text { get; }
        }

        private readonly struct FixtureEntry
        {
            internal FixtureEntry(
                ushort key,
                OrpheusPlaybackKind playbackKind,
                OrpheusLoadPolicy loadPolicy)
            {
                Key = key;
                PlaybackKind = playbackKind;
                LoadPolicy = loadPolicy;
            }

            internal ushort Key { get; }
            internal OrpheusPlaybackKind PlaybackKind { get; }
            internal OrpheusLoadPolicy LoadPolicy { get; }
        }
    }
}
