using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioValidatorTests
    {
        private const string TemporaryRoot = "Assets/OrpheusIssue05Tests";
        private static readonly OrpheusAudioTypedKeyProjectionPaths ProjectionPaths =
            new OrpheusAudioTypedKeyProjectionPaths(TemporaryRoot + "/Generated");

        [SetUp]
        public void SetUp()
        {
            DeleteTemporaryAssets();
            CreateTemporaryFolder();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTemporaryAssets();
            AssetDatabase.Refresh();
        }

        [Test]
        public void DisabledProfile_IsIgnoredBeforeReadingInvalidReferences()
        {
            var profile = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            OrpheusAudioEditorContractTests.SetField(profile, "_schemaVersion", 0);

            var errors = ValidateProfile(profile);

            Assert.That(errors, Is.Empty);
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void ProfileErrors_UseDeterministicSchemaThenReferenceOrder()
        {
            var profile = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            OrpheusAudioEditorContractTests.SetField(profile, "_enabled", true);
            OrpheusAudioEditorContractTests.SetField(profile, "_schemaVersion", 0);

            var errors = ValidateProfile(profile);

            Assert.That(
                errors.Select(error => error.Code).ToArray(),
                Is.EqualTo(new[]
                {
                    OrpheusAudioValidationErrorCode.InvalidProfileSchema,
                    OrpheusAudioValidationErrorCode.MissingSettings,
                    OrpheusAudioValidationErrorCode.MissingCatalog,
                    OrpheusAudioValidationErrorCode.MissingKeyManifest,
                    OrpheusAudioValidationErrorCode.MissingRuntimeHostPrefab
                }));
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void ManifestValidation_CollectsIndependentErrorsInEntryOrder()
        {
            var settings = ScriptableObject.CreateInstance<OrpheusAudioSettings>();
            var catalog = ScriptableObject.CreateInstance<OrpheusAudioCatalog>();
            var manifest = OrpheusAudioEditorContractTests.CreateManifest(
                OrpheusAudioEditorContractTests.CreateEntry(
                    0, "class", OrpheusAudioKeyStatus.Invalid),
                OrpheusAudioEditorContractTests.CreateEntry(
                    0, "class", OrpheusAudioKeyStatus.Invalid));
            var profile = CreateProfile(true, settings, catalog, manifest);

            var errors = ValidateProfile(profile);

            Assert.That(
                errors.Take(8).Select(error => error.Code).ToArray(),
                Is.EqualTo(new[]
                {
                    OrpheusAudioValidationErrorCode.InvalidManifestId,
                    OrpheusAudioValidationErrorCode.InvalidManifestSymbol,
                    OrpheusAudioValidationErrorCode.InvalidManifestStatus,
                    OrpheusAudioValidationErrorCode.InvalidManifestId,
                    OrpheusAudioValidationErrorCode.InvalidManifestSymbol,
                    OrpheusAudioValidationErrorCode.InvalidManifestStatus,
                    OrpheusAudioValidationErrorCode.DuplicateManifestId,
                    OrpheusAudioValidationErrorCode.DuplicateManifestSymbol
                }));

            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(manifest);
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void ValidProfile_TraversesOnlyItsCatalogAndPassesWithoutMutation()
        {
            var clip = CreateImportedClip("ValidCue.wav");
            ConfigureImporter(clip, AudioClipLoadType.DecompressOnLoad, true, false, false);
            var audioEvent = CreateEventAsset("AE_ValidCue.asset", 100, clip);
            var strayEvent = CreateEventAsset("AE_Stray.asset", 999, clip);
            var catalog = CreateAsset<OrpheusAudioCatalog>("Catalog.asset");
            OrpheusAudioEditorContractTests.SetField(catalog, "_events", new[] { audioEvent });
            var settings = CreateAsset<OrpheusAudioSettings>("Settings.asset");
            var manifest = CreateAsset<OrpheusAudioKeyManifest>("Manifest.asset");
            OrpheusAudioEditorContractTests.SetField(
                manifest,
                "_entries",
                new[]
                {
                    OrpheusAudioEditorContractTests.CreateEntry(
                        100, "ValidCue", OrpheusAudioKeyStatus.Active)
                });
            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            var profile = CreateProfile(true, settings, catalog, manifest);
            WriteCurrentProjection(manifest);
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
            var before = CaptureImporter(importer);

            var errors = ValidateProfile(profile);

            Assert.That(errors, Is.Empty);
            Assert.That(CaptureImporter(importer), Is.EqualTo(before));
            Assert.That(strayEvent, Is.Not.Null);
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void TransactionValidation_SkipsOnlySelectedUncommittedAuthoringState()
        {
            var clip = CreateImportedClip("TransactionCue.wav");
            ConfigureImporter(
                clip,
                AudioClipLoadType.DecompressOnLoad,
                true,
                false,
                false);
            var audioEvent =
                CreateEventAsset("AE_TransactionCue.asset", 100, clip);
            var catalog = CreateAsset<OrpheusAudioCatalog>(
                "TransactionCatalog.asset");
            OrpheusAudioEditorContractTests.SetField(
                catalog,
                "_events",
                new[] { audioEvent });
            var settings =
                CreateAsset<OrpheusAudioSettings>("TransactionSettings.asset");
            var manifest =
                CreateAsset<OrpheusAudioKeyManifest>("TransactionManifest.asset");
            OrpheusAudioEditorContractTests.SetField(
                manifest,
                "_entries",
                new[]
                {
                    OrpheusAudioEditorContractTests.CreateEntry(
                        100,
                        "TransactionCue",
                        OrpheusAudioKeyStatus.Active)
                });
            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            WriteCurrentProjection(manifest);

            var authoring =
                CreateAsset<OrpheusAudioAuthoringProfile>("Authoring.asset");
            OrpheusAudioEditorContractTests.SetField(
                authoring,
                "_keyManifest",
                manifest);
            OrpheusAudioEditorContractTests.SetField(
                authoring,
                "_catalog",
                catalog);
            OrpheusAudioEditorContractTests.SetField(
                authoring,
                "_generatedRoot",
                TemporaryRoot + "/Generated");
            var profile = CreateProfile(true, settings, catalog, manifest);
            AssetDatabase.CreateAsset(
                profile,
                TemporaryRoot + "/Validation.asset");
            OrpheusAudioEditorContractTests.SetField(
                profile,
                "_authoringProfile",
                authoring);
            OrpheusAudioEditorContractTests.SetField(
                profile,
                "_authoringEnrollmentGuid",
                AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(authoring)).ToLowerInvariant());
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var transactionErrors = OrpheusAudioValidator.ValidateProfile(
                profile,
                BuildTargetGroup.Standalone,
                ProjectionPaths,
                new[] { profile },
                false);
            var committedErrors = OrpheusAudioValidator.ValidateProfile(
                profile,
                BuildTargetGroup.Standalone,
                ProjectionPaths,
                new[] { profile });

            Assert.That(transactionErrors, Is.Empty);
            Assert.That(
                committedErrors.Select(error => error.Code),
                Does.Contain(
                    OrpheusAudioValidationErrorCode
                        .GeneratedCatalogMismatch));
        }

        [Test]
        public void ProjectionMissingAndStaleBothBlockValidation()
        {
            var settings = ScriptableObject.CreateInstance<OrpheusAudioSettings>();
            var catalog = ScriptableObject.CreateInstance<OrpheusAudioCatalog>();
            var manifest = OrpheusAudioEditorContractTests.CreateManifest();
            var profile = CreateProfile(true, settings, catalog, manifest);

            var missing = ValidateProfile(profile);
            Assert.That(
                missing.Select(error => error.Code),
                Does.Contain(OrpheusAudioValidationErrorCode.MissingTypedKeyProjection));

            Directory.CreateDirectory(ProjectionPaths.OutputDirectory);
            File.WriteAllText(ProjectionPaths.AssemblyPath, "{}");
            File.WriteAllText(ProjectionPaths.SourcePath, "// stale");
            var stale = ValidateProfile(profile);
            Assert.That(
                stale.Select(error => error.Code),
                Does.Contain(OrpheusAudioValidationErrorCode.StaleTypedKeyProjection));

            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(manifest);
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(settings);
        }

        [Test]
        public void ProjectionComparisonRejectsExtraAndMissingGeneratedKeys()
        {
            var manifest = OrpheusAudioEditorContractTests.CreateManifest(
                OrpheusAudioEditorContractTests.CreateEntry(
                    100, "ProjectedCue", OrpheusAudioKeyStatus.Active));
            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    manifest, out var source, out _),
                Is.True);
            Directory.CreateDirectory(ProjectionPaths.OutputDirectory);
            File.WriteAllText(
                ProjectionPaths.AssemblyPath,
                OrpheusAudioTypedKeyProjection.ExpectedAssembly);
            File.WriteAllText(ProjectionPaths.SourcePath, source);
            Assert.That(OrpheusAudioTypedKeyProjection.IsCurrent(manifest, ProjectionPaths), Is.True);

            var member =
                "        public static readonly OrpheusAudioKey ProjectedCue = " +
                "new OrpheusAudioKey(100);\n";
            File.WriteAllText(
                ProjectionPaths.SourcePath,
                source.Replace(member, string.Empty));
            Assert.That(OrpheusAudioTypedKeyProjection.IsCurrent(manifest, ProjectionPaths), Is.False);

            File.WriteAllText(
                ProjectionPaths.SourcePath,
                source.Replace(
                    "    }\n}\n",
                    "        public static readonly OrpheusAudioKey ExtraCue = " +
                    "new OrpheusAudioKey(999);\n    }\n}\n"));
            Assert.That(OrpheusAudioTypedKeyProjection.IsCurrent(manifest, ProjectionPaths), Is.False);

            UnityEngine.Object.DestroyImmediate(manifest);
        }

        [Test]
        public void CatalogMembershipRejectsReservedRetiredWrongNameAndMissingActiveKeys()
        {
            var clip = CreateImportedClip("Membership.wav");
            ConfigureImporter(clip, AudioClipLoadType.DecompressOnLoad, true, false, false);
            var active = CreateEventAsset("WrongName.asset", 100, clip);
            var reserved = CreateEventAsset("AE_ReservedCue.asset", 200, clip);
            var retired = CreateEventAsset("AE_RetiredCue.asset", 300, clip);
            var catalog = CreateAsset<OrpheusAudioCatalog>("MembershipCatalog.asset");
            OrpheusAudioEditorContractTests.SetField(
                catalog, "_events", new[] { active, reserved, retired });
            var settings = CreateAsset<OrpheusAudioSettings>("MembershipSettings.asset");
            var manifest = CreateAsset<OrpheusAudioKeyManifest>("MembershipManifest.asset");
            OrpheusAudioEditorContractTests.SetField(
                manifest,
                "_entries",
                new[]
                {
                    OrpheusAudioEditorContractTests.CreateEntry(
                        100, "ActiveCue", OrpheusAudioKeyStatus.Active),
                    OrpheusAudioEditorContractTests.CreateEntry(
                        200, "ReservedCue", OrpheusAudioKeyStatus.Reserved),
                    OrpheusAudioEditorContractTests.CreateEntry(
                        300, "RetiredCue", OrpheusAudioKeyStatus.Retired),
                    OrpheusAudioEditorContractTests.CreateEntry(
                        400, "MissingCue", OrpheusAudioKeyStatus.Active)
                });
            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            var profile = CreateProfile(true, settings, catalog, manifest);
            WriteCurrentProjection(manifest);

            var errors = ValidateProfile(profile);
            var membershipErrors = errors
                .Where(error =>
                    error.Code == OrpheusAudioValidationErrorCode.InvalidEventAssetName ||
                    error.Code == OrpheusAudioValidationErrorCode.EventKeyNotActive ||
                    error.Code == OrpheusAudioValidationErrorCode.MissingActiveEvent)
                .Select(error => error.Code)
                .ToArray();

            Assert.That(membershipErrors, Is.EqualTo(new[]
            {
                OrpheusAudioValidationErrorCode.InvalidEventAssetName,
                OrpheusAudioValidationErrorCode.EventKeyNotActive,
                OrpheusAudioValidationErrorCode.EventKeyNotActive,
                OrpheusAudioValidationErrorCode.MissingActiveEvent
            }));
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void NullCatalogStorageStillReportsEveryMissingActiveEvent()
        {
            var catalog = CreateAsset<OrpheusAudioCatalog>("NullCatalog.asset");
            OrpheusAudioEditorContractTests.SetField(catalog, "_events", null);
            var settings = CreateAsset<OrpheusAudioSettings>("NullCatalogSettings.asset");
            var manifest = CreateAsset<OrpheusAudioKeyManifest>("NullCatalogManifest.asset");
            OrpheusAudioEditorContractTests.SetField(
                manifest,
                "_entries",
                new[]
                {
                    OrpheusAudioEditorContractTests.CreateEntry(
                        100, "MissingCue", OrpheusAudioKeyStatus.Active)
                });
            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            var profile = CreateProfile(true, settings, catalog, manifest);
            WriteCurrentProjection(manifest);

            var relevant = ValidateProfile(profile)
                .Where(error => error.Code == OrpheusAudioValidationErrorCode.NullEvent ||
                                error.Code == OrpheusAudioValidationErrorCode.MissingActiveEvent)
                .Select(error => error.Code)
                .ToArray();

            Assert.That(relevant, Is.EqualTo(new[]
            {
                OrpheusAudioValidationErrorCode.NullEvent,
                OrpheusAudioValidationErrorCode.MissingActiveEvent
            }));
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void DuplicateActiveEventDoesNotProduceFalseMissingActiveEvent()
        {
            var clip = CreateImportedClip("Duplicate.wav");
            ConfigureImporter(clip, AudioClipLoadType.DecompressOnLoad, true, false, false);
            var first = CreateEventAsset("AE_DuplicateCue.asset", 100, clip);
            var second = CreateEventAsset("AE_DuplicateCueCopy.asset", 100, clip);
            var catalog = CreateAsset<OrpheusAudioCatalog>("DuplicateCatalog.asset");
            OrpheusAudioEditorContractTests.SetField(catalog, "_events", new[] { first, second });
            var settings = CreateAsset<OrpheusAudioSettings>("DuplicateSettings.asset");
            var manifest = CreateAsset<OrpheusAudioKeyManifest>("DuplicateManifest.asset");
            OrpheusAudioEditorContractTests.SetField(
                manifest,
                "_entries",
                new[]
                {
                    OrpheusAudioEditorContractTests.CreateEntry(
                        100, "DuplicateCue", OrpheusAudioKeyStatus.Active)
                });
            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            var profile = CreateProfile(true, settings, catalog, manifest);
            WriteCurrentProjection(manifest);

            var errors = ValidateProfile(profile);

            Assert.That(
                errors.Count(error => error.Code == OrpheusAudioValidationErrorCode.DuplicateEventKey),
                Is.EqualTo(1));
            Assert.That(
                errors.Any(error => error.Code == OrpheusAudioValidationErrorCode.MissingActiveEvent),
                Is.False);
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void EnabledProfilesMustShareOneManifestAssetIdentity()
        {
            var firstManifest = FindEnabledManifestOutsideTemporaryRoot() ??
                                CreateAsset<OrpheusAudioKeyManifest>("A_Manifest.asset");
            var secondManifest = CreateAsset<OrpheusAudioKeyManifest>("B_Manifest.asset");
            var first = CreateAsset<OrpheusAudioValidationProfile>("A_Profile.asset");
            var second = CreateAsset<OrpheusAudioValidationProfile>("B_Profile.asset");
            OrpheusAudioEditorContractTests.SetField(first, "_enabled", true);
            OrpheusAudioEditorContractTests.SetField(first, "_keyManifest", firstManifest);
            OrpheusAudioEditorContractTests.SetField(second, "_enabled", true);
            OrpheusAudioEditorContractTests.SetField(second, "_keyManifest", secondManifest);
            EditorUtility.SetDirty(first);
            EditorUtility.SetDirty(second);
            AssetDatabase.SaveAssets();

            var conflicts = OrpheusAudioValidationProfileDiscovery.ValidateEnabled(
                    BuildTarget.StandaloneWindows64)
                .Where(error => error.Code == OrpheusAudioValidationErrorCode.MultipleKeyManifests &&
                                error.ProfilePath.StartsWith(TemporaryRoot, StringComparison.Ordinal))
                .ToArray();

            Assert.That(conflicts.Length, Is.EqualTo(1));
            Assert.That(conflicts[0].ProfilePath, Does.EndWith("B_Profile.asset"));
        }

        private static OrpheusAudioKeyManifest FindEnabledManifestOutsideTemporaryRoot()
        {
            var guids = AssetDatabase.FindAssets("t:OrpheusAudioValidationProfile");
            Array.Sort(guids, StringComparer.Ordinal);
            for (var index = 0; index < guids.Length; index++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[index]);
                if (path.StartsWith(TemporaryRoot, StringComparison.Ordinal))
                {
                    continue;
                }

                var profile = AssetDatabase.LoadAssetAtPath<OrpheusAudioValidationProfile>(path);
                if (profile != null && profile.Enabled && profile.KeyManifest != null)
                {
                    return profile.KeyManifest;
                }
            }

            return null;
        }

        [Test]
        public void BuildPreprocessorThrowsForInvalidEnabledProfile()
        {
            var profile = CreateAsset<OrpheusAudioValidationProfile>("InvalidBuildProfile.asset");
            OrpheusAudioEditorContractTests.SetField(profile, "_enabled", true);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            Assert.Throws<BuildFailedException>(
                () => OrpheusAudioBuildPreprocessor.ValidateOrThrow(
                    BuildTarget.StandaloneWindows64));
        }

        [Test]
        public void DiscoverySortsProfilesAndBuildValidationSkipsDisabledProfiles()
        {
            CreateTemporaryFolder();
            var disabled = CreateAsset<OrpheusAudioValidationProfile>("B_Disabled.asset");
            var enabled = CreateAsset<OrpheusAudioValidationProfile>("A_Enabled.asset");
            OrpheusAudioEditorContractTests.SetField(disabled, "_enabled", false);
            OrpheusAudioEditorContractTests.SetField(enabled, "_enabled", true);
            EditorUtility.SetDirty(disabled);
            EditorUtility.SetDirty(enabled);
            AssetDatabase.SaveAssets();

            var discovered = OrpheusAudioValidationProfileDiscovery.Discover()
                .Where(profile => AssetDatabase.GetAssetPath(profile).StartsWith(
                    TemporaryRoot, StringComparison.Ordinal))
                .ToArray();
            var errors = OrpheusAudioValidationProfileDiscovery.ValidateEnabled(
                BuildTarget.StandaloneWindows64)
                .Where(error => error.ProfilePath.StartsWith(
                    TemporaryRoot, StringComparison.Ordinal))
                .ToArray();

            Assert.That(discovered, Is.EqualTo(new[] { enabled, disabled }));
            Assert.That(errors.Select(error => error.Code),
                Is.EqualTo(new[]
                {
                    OrpheusAudioValidationErrorCode.MissingSettings,
                    OrpheusAudioValidationErrorCode.MissingCatalog,
                    OrpheusAudioValidationErrorCode.MissingKeyManifest,
                    OrpheusAudioValidationErrorCode.MissingRuntimeHostPrefab
                }));
        }

        [Test]
        public void VerificationHostCardinalityRequiresExactlyOneEnabledProfile()
        {
            var first = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            var second = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            var profiles = new[] { first, second };
            Assert.That(
                OrpheusAudioValidationProfileDiscovery.HasExactlyOneEnabledProfile(profiles),
                Is.False);

            OrpheusAudioEditorContractTests.SetField(first, "_enabled", true);
            Assert.That(
                OrpheusAudioValidationProfileDiscovery.HasExactlyOneEnabledProfile(profiles),
                Is.True);

            OrpheusAudioEditorContractTests.SetField(second, "_enabled", true);
            Assert.That(
                OrpheusAudioValidationProfileDiscovery.HasExactlyOneEnabledProfile(profiles),
                Is.False);

            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }

        [Test]
        public void EveryEnabledProfileIsEvaluatedIndependentlyInPathOrder()
        {
            var second = CreateAsset<OrpheusAudioValidationProfile>("B_Profile.asset");
            var first = CreateAsset<OrpheusAudioValidationProfile>("A_Profile.asset");
            OrpheusAudioEditorContractTests.SetField(first, "_enabled", true);
            OrpheusAudioEditorContractTests.SetField(second, "_enabled", true);
            EditorUtility.SetDirty(first);
            EditorUtility.SetDirty(second);
            AssetDatabase.SaveAssets();

            var errors = OrpheusAudioValidationProfileDiscovery.ValidateEnabled(
                    BuildTarget.StandaloneWindows64)
                .Where(error => error.ProfilePath.StartsWith(
                    TemporaryRoot, StringComparison.Ordinal))
                .ToArray();

            Assert.That(errors.Length, Is.EqualTo(8));
            Assert.That(errors.Take(4).Select(error => error.ProfilePath).Distinct().Single(),
                Does.EndWith("A_Profile.asset"));
            Assert.That(errors.Skip(4).Select(error => error.ProfilePath).Distinct().Single(),
                Does.EndWith("B_Profile.asset"));
        }

        [Test]
        [TestCase(
            OrpheusPlaybackKind.OneShot2D,
            OrpheusLoadPolicy.BootstrapTransient,
            AudioClipLoadType.DecompressOnLoad,
            true,
            false)]
        [TestCase(
            OrpheusPlaybackKind.OneShot2D,
            OrpheusLoadPolicy.ExplicitTransient,
            AudioClipLoadType.DecompressOnLoad,
            false,
            true)]
        [TestCase(
            OrpheusPlaybackKind.Bgm,
            OrpheusLoadPolicy.PersistentStream,
            AudioClipLoadType.Streaming,
            false,
            true)]
        public void ImporterTranslationCoversEveryLoadPolicyWithoutMutation(
            object playbackKindValue,
            object loadPolicyValue,
            AudioClipLoadType loadType,
            bool preloadAudioData,
            bool loadInBackground)
        {
            var playbackKind = (OrpheusPlaybackKind)playbackKindValue;
            var loadPolicy = (OrpheusLoadPolicy)loadPolicyValue;
            var clip = CreateImportedClip("Importer.wav");
            ConfigureImporter(clip, loadType, preloadAudioData, loadInBackground, false);
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
            var before = CaptureImporter(importer);

            var valid = OrpheusAudioValidator.EvaluateImporterPolicy(
                importer,
                playbackKind,
                loadPolicy,
                BuildTargetGroup.Standalone);

            Assert.That(valid, Is.EqualTo(OrpheusAudioImportPolicyMismatch.None));
            Assert.That(CaptureImporter(importer), Is.EqualTo(before));
        }

        [Test]
        public void ImporterTranslationUsesTargetGroupOverrideAndReportsForceToMono()
        {
            var clip = CreateImportedClip("ImporterOverride.wav");
            ConfigureImporter(clip, AudioClipLoadType.DecompressOnLoad, true, true, false);
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
            var sample = importer.defaultSampleSettings;
            sample.loadType = AudioClipLoadType.Streaming;
            sample.preloadAudioData = false;
            importer.SetOverrideSampleSettings(BuildTargetGroup.Standalone, sample);
            importer.SaveAndReimport();
            var before = CaptureImporter(importer);

            var persistent = OrpheusAudioValidator.EvaluateImporterPolicy(
                importer,
                OrpheusPlaybackKind.Bgm,
                OrpheusLoadPolicy.PersistentStream,
                BuildTargetGroup.Standalone);
            Assert.That(persistent, Is.EqualTo(OrpheusAudioImportPolicyMismatch.None));
            Assert.That(CaptureImporter(importer), Is.EqualTo(before));

            ConfigureImporter(
                clip, AudioClipLoadType.DecompressOnLoad, true, false, false);
            importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
            var oneShot3D = OrpheusAudioValidator.EvaluateImporterPolicy(
                importer,
                OrpheusPlaybackKind.OneShot3D,
                OrpheusLoadPolicy.BootstrapTransient,
                BuildTargetGroup.Standalone);
            Assert.That(
                oneShot3D & OrpheusAudioImportPolicyMismatch.ForceToMono,
                Is.Not.EqualTo(OrpheusAudioImportPolicyMismatch.None));
        }

        private OrpheusAudioValidationProfile CreateProfile(
            bool enabled,
            OrpheusAudioSettings settings,
            OrpheusAudioCatalog catalog,
            OrpheusAudioKeyManifest manifest)
        {
            var runtimeHost = OrpheusAudioEditorTestAssets.GetOrCreateValidRuntimeHostPrefab(
                TemporaryRoot + "/ValidRuntimeHost.prefab");
            var listenerScene = OrpheusAudioEditorTestAssets.CopyEmptyScene(
                TemporaryRoot + "/ValidListener.unity");
            OrpheusAudioEditorTestAssets.ConfigureValidSettings(settings);
            var profile = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            OrpheusAudioEditorContractTests.SetField(profile, "_enabled", enabled);
            OrpheusAudioEditorContractTests.SetField(profile, "_settings", settings);
            OrpheusAudioEditorContractTests.SetField(profile, "_catalog", catalog);
            OrpheusAudioEditorContractTests.SetField(profile, "_keyManifest", manifest);
            OrpheusAudioEditorContractTests.SetField(profile, "_runtimeHostPrefab", runtimeHost);
            OrpheusAudioEditorContractTests.SetField(profile, "_listenerScenes", new[] { listenerScene });
            return profile;
        }

        private static List<OrpheusAudioValidationError> ValidateProfile(
            OrpheusAudioValidationProfile profile)
        {
            return OrpheusAudioValidator.ValidateProfile(
                profile, BuildTargetGroup.Standalone, ProjectionPaths);
        }

        private static OrpheusAudioEvent CreateEventAsset(
            string fileName,
            ushort key,
            AudioClip clip)
        {
            var audioEvent = CreateAsset<OrpheusAudioEvent>(fileName);
            OrpheusAudioEditorContractTests.SetField(audioEvent, "_key", key);
            OrpheusAudioEditorContractTests.SetField(
                audioEvent, "_playbackKind", OrpheusPlaybackKind.OneShot2D);
            OrpheusAudioEditorContractTests.SetField(
                audioEvent, "_category", OrpheusCategory.SfxUi);
            OrpheusAudioEditorContractTests.SetField(
                audioEvent, "_loadPolicy", OrpheusLoadPolicy.BootstrapTransient);
            OrpheusAudioEditorContractTests.SetField(audioEvent, "_polyphonyCap", (byte)1);
            OrpheusAudioEditorContractTests.SetField(audioEvent, "_clips", new[] { clip });
            EditorUtility.SetDirty(audioEvent);
            AssetDatabase.SaveAssets();
            return audioEvent;
        }

        private static T CreateAsset<T>(string fileName) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, TemporaryRoot + "/" + fileName);
            return asset;
        }

        private static AudioClip CreateImportedClip(string fileName)
        {
            var path = TemporaryRoot + "/" + fileName;
            File.WriteAllBytes(path, CreateWaveBytes());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static void ConfigureImporter(
            AudioClip clip,
            AudioClipLoadType loadType,
            bool preloadAudioData,
            bool loadInBackground,
            bool forceToMono)
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
            var sampleSettings = importer.defaultSampleSettings;
            sampleSettings.loadType = loadType;
            sampleSettings.preloadAudioData = preloadAudioData;
            importer.defaultSampleSettings = sampleSettings;
            importer.loadInBackground = loadInBackground;
            importer.forceToMono = forceToMono;
            importer.SaveAndReimport();
        }

        private static string CaptureImporter(AudioImporter importer)
        {
            var sample = importer.GetOverrideSampleSettings(BuildTargetGroup.Standalone);
            return sample.loadType + "|" + sample.preloadAudioData + "|" +
                   importer.loadInBackground + "|" + importer.forceToMono;
        }

        private static void WriteCurrentProjection(OrpheusAudioKeyManifest manifest)
        {
            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    manifest, out var source, out _),
                Is.True);
            Directory.CreateDirectory(ProjectionPaths.OutputDirectory);
            File.WriteAllText(
                ProjectionPaths.AssemblyPath,
                OrpheusAudioTypedKeyProjection.ExpectedAssembly);
            File.WriteAllText(ProjectionPaths.SourcePath, source);
        }

        private static byte[] CreateWaveBytes()
        {
            const int sampleCount = 32;
            const int dataLength = sampleCount * 2;
            using (var stream = new MemoryStream(44 + dataLength))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(36 + dataLength);
                writer.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                writer.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(8000);
                writer.Write(16000);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                writer.Write(dataLength);
                for (var index = 0; index < sampleCount; index++)
                {
                    writer.Write((short)0);
                }

                return stream.ToArray();
            }
        }

        private static void CreateTemporaryFolder()
        {
            if (!AssetDatabase.IsValidFolder(TemporaryRoot))
            {
                AssetDatabase.CreateFolder("Assets", "OrpheusIssue05Tests");
            }
        }

        private static void DeleteTemporaryAssets()
        {
            if (AssetDatabase.IsValidFolder(TemporaryRoot))
            {
                AssetDatabase.DeleteAsset(TemporaryRoot);
            }
        }

    }
}
