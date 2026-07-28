using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioPlayableSampleContractTests
    {
        private const string PackageRoot = "Packages/com.orpheus.audio";
        private const string SampleRoot =
            PackageRoot + "/Samples~/Playable One Shot";
        private const string ImportedRoot =
            "Assets/OrpheusPlayableOneShotContract";
        private const string ProjectionRoot =
            "Library/OrpheusPlayableOneShotContract";

        private static readonly string[] RequiredFiles =
        {
            "README.md",
            "Audio/OrpheusPlayableOneShot.mixer",
            "Audio/Clips/PlayableOneShot.wav",
            "Audio/Events/AE_PlayableOneShot.asset",
            "Prefabs/OrpheusRuntimeHost.prefab",
            "Runtime/Orpheus.Audio.Samples.PlayableOneShot.asmdef",
            "Runtime/PlayableOneShotCompositionRoot.cs",
            "Runtime/PlayableOneShotSceneBinding.cs",
            "Scenes/PlayableOneShot.unity",
            "Settings/OrpheusAudioSettings.asset",
            "Settings/PlayableOneShotCatalog.asset",
            "Settings/PlayableOneShotKeyManifest.asset",
            "Settings/ValidationProfile.asset",
            "Tests/Orpheus.Audio.Samples.PlayableOneShot.Tests.asmdef",
            "Tests/OrpheusAudioPlayableOneShotSmokeTests.cs"
        };

        [Serializable]
        private sealed class PackageManifest
        {
            public PackageSample[] samples;
        }

        [Serializable]
        private sealed class PackageSample
        {
            public string displayName;
            public string path;
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(ImportedRoot))
            {
                AssetDatabase.DeleteAsset(ImportedRoot);
            }

            if (Directory.Exists(ProjectionRoot))
            {
                Directory.Delete(ProjectionRoot, true);
            }
        }

        [Test]
        public void Package_DeclaresEmptyAndPlayableSamples()
        {
            var json = File.ReadAllText(PackageRoot + "/package.json");
            var manifest = JsonUtility.FromJson<PackageManifest>(json);

            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.samples, Has.Length.EqualTo(2));
            Assert.That(
                manifest.samples.Select(sample => sample.displayName),
                Is.EqualTo(new[] { "Minimal Setup", "Playable One Shot" }));
            Assert.That(
                manifest.samples.Select(sample => sample.path),
                Is.EqualTo(new[]
                {
                    "Samples~/Minimal Setup",
                    "Samples~/Playable One Shot"
                }));
        }

        [Test]
        public void PlayableOneShot_HasRequiredFilesAndStableMetas()
        {
            var actualFiles = Directory.GetFiles(
                    SampleRoot, "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(
                    ".meta", StringComparison.OrdinalIgnoreCase))
                .Select(path => path.Substring(SampleRoot.Length + 1)
                    .Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.That(
                actualFiles,
                Is.EqualTo(RequiredFiles.OrderBy(
                    path => path, StringComparer.Ordinal)));

            foreach (var relativePath in RequiredFiles)
            {
                AssertStableMeta(SampleRoot + "/" + relativePath + ".meta");
            }

            var scene = File.ReadAllText(
                SampleRoot + "/Scenes/PlayableOneShot.unity");
            Assert.That(scene, Does.Contain("m_Name: Main Camera"));
            Assert.That(scene, Does.Contain("--- !u!20 "));
            Assert.That(scene, Does.Contain("m_Name: Directional Light"));
            Assert.That(scene, Does.Contain("--- !u!108 "));
        }

        [Test]
        public void PlayableOneShot_ImportsAndPassesSharedValidation()
        {
            CopyDirectory(SampleRoot, ImportedRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var profile = AssetDatabase.LoadAssetAtPath<OrpheusAudioValidationProfile>(
                ImportedRoot + "/Settings/ValidationProfile.asset");
            var manifest = AssetDatabase.LoadAssetAtPath<OrpheusAudioKeyManifest>(
                ImportedRoot + "/Settings/PlayableOneShotKeyManifest.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<OrpheusAudioCatalog>(
                ImportedRoot + "/Settings/PlayableOneShotCatalog.asset");

            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.Enabled, Is.True);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.EntryCount, Is.EqualTo(1));
            Assert.That(manifest.GetEntry(0).Id, Is.EqualTo(100));
            Assert.That(
                manifest.GetEntry(0).Status,
                Is.EqualTo(OrpheusAudioKeyStatus.Active));

            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.EventCount, Is.EqualTo(1));
            var audioEvent = catalog.GetEvent(0);
            Assert.That(audioEvent.Key.Value, Is.EqualTo(100));
            Assert.That(
                audioEvent.PlaybackKind,
                Is.EqualTo(OrpheusPlaybackKind.OneShot2D));
            Assert.That(
                audioEvent.LoadPolicy,
                Is.EqualTo(OrpheusLoadPolicy.BootstrapTransient));
            var clip = audioEvent.GetClip(0);
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.channels, Is.EqualTo(1));
            Assert.That(clip.frequency, Is.EqualTo(16000));
            Assert.That(clip.length, Is.EqualTo(0.75f).Within(0.01f));
            var samples = new float[clip.samples * clip.channels];
            Assert.That(clip.GetData(samples, 0), Is.True);
            var peak = 0f;
            var squareSum = 0d;
            for (var index = 0; index < samples.Length; index++)
            {
                var absolute = Math.Abs(samples[index]);
                if (absolute > peak)
                {
                    peak = absolute;
                }
                squareSum += samples[index] * samples[index];
            }
            var rootMeanSquare = Math.Sqrt(squareSum / samples.Length);
            Assert.That(peak, Is.GreaterThan(0.2f));
            Assert.That(rootMeanSquare, Is.GreaterThan(0.1d));
            Assert.That(Math.Abs(samples[0]), Is.LessThan(0.001f));
            Assert.That(
                Math.Abs(samples[samples.Length - 1]),
                Is.LessThan(0.001f));

            var clipPath = AssetDatabase.GetAssetPath(clip);
            var importer = AssetImporter.GetAtPath(clipPath) as AudioImporter;
            Assert.That(importer, Is.Not.Null, clipPath);
            Assert.That(
                OrpheusAudioValidator.EvaluateImporterPolicy(
                    importer,
                    audioEvent.PlaybackKind,
                    audioEvent.LoadPolicy,
                    BuildTargetGroup.Standalone),
                Is.EqualTo(OrpheusAudioImportPolicyMismatch.None));

            var paths = new OrpheusAudioTypedKeyProjectionPaths(ProjectionRoot);
            Directory.CreateDirectory(ProjectionRoot);
            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    manifest, out var source, out _),
                Is.True);
            File.WriteAllText(
                paths.AssemblyPath,
                OrpheusAudioTypedKeyProjection.ExpectedAssembly,
                new UTF8Encoding(false));
            File.WriteAllText(paths.SourcePath, source, new UTF8Encoding(false));

            var errors = OrpheusAudioValidator.ValidateProfile(
                profile,
                BuildTargetGroup.Standalone,
                paths);
            Assert.That(errors, Is.Empty);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (var child in Directory.GetDirectories(source))
            {
                CopyDirectory(
                    child,
                    Path.Combine(destination, Path.GetFileName(child)));
            }
        }

        private static void AssertStableMeta(string path)
        {
            Assert.That(File.Exists(path), Is.True, path);
            var guidLine = File.ReadLines(path)
                .SingleOrDefault(line => line.StartsWith(
                    "guid: ", StringComparison.Ordinal));
            Assert.That(guidLine, Does.Match("^guid: [0-9a-f]{32}$"), path);
        }
    }
}
