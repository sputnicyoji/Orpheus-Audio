using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioIntegrationValidatorTests
    {
        private const string TemporaryRoot = "Assets/OrpheusIssue18Tests";
        private const string MixerPath = OrpheusAudioEditorTestAssets.MixerPath;
        private const string EmptySceneFixturePath = OrpheusAudioEditorTestAssets.EmptyScenePath;

        private static readonly OrpheusAudioTypedKeyProjectionPaths ProjectionPaths =
            new OrpheusAudioTypedKeyProjectionPaths(
                Path.Combine("Library", "OrpheusIssue18TestsGenerated"));

        private static readonly object[] InvalidDurations =
        {
            new object[] { "_bgmCrossfadeSeconds", 0.014f,
                OrpheusAudioValidationErrorCode.InvalidBgmCrossfadeDuration },
            new object[] { "_bgmCrossfadeSeconds", 5.001f,
                OrpheusAudioValidationErrorCode.InvalidBgmCrossfadeDuration },
            new object[] { "_bgmCrossfadeSeconds", float.NaN,
                OrpheusAudioValidationErrorCode.InvalidBgmCrossfadeDuration },
            new object[] { "_bgmCrossfadeSeconds", float.PositiveInfinity,
                OrpheusAudioValidationErrorCode.InvalidBgmCrossfadeDuration },
            new object[] { "_bgmCrossfadeSeconds", float.NegativeInfinity,
                OrpheusAudioValidationErrorCode.InvalidBgmCrossfadeDuration },
            new object[] { "_profileAmbienceCrossfadeSeconds", 0.014f,
                OrpheusAudioValidationErrorCode.InvalidProfileAmbienceCrossfadeDuration },
            new object[] { "_profileAmbienceCrossfadeSeconds", 5.001f,
                OrpheusAudioValidationErrorCode.InvalidProfileAmbienceCrossfadeDuration },
            new object[] { "_profileAmbienceCrossfadeSeconds", float.NaN,
                OrpheusAudioValidationErrorCode.InvalidProfileAmbienceCrossfadeDuration },
            new object[] { "_snapshotTransitionSeconds", 0.014f,
                OrpheusAudioValidationErrorCode.InvalidSnapshotTransitionDuration },
            new object[] { "_snapshotTransitionSeconds", 5.001f,
                OrpheusAudioValidationErrorCode.InvalidSnapshotTransitionDuration },
            new object[] { "_snapshotTransitionSeconds", float.NaN,
                OrpheusAudioValidationErrorCode.InvalidSnapshotTransitionDuration }
        };

        private static int _fixtureSequence;

        [SetUp]
        public void SetUp()
        {
            DeleteTemporaryAssets();
            DeleteGeneratedProjection();
            AssetDatabase.CreateFolder("Assets", "OrpheusIssue18Tests");
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTemporaryAssets();
            DeleteGeneratedProjection();
            AssetDatabase.Refresh();
        }

        [Test]
        public void RealMixerAndValidIntegrationProfile_PassTheSharedValidator()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            Assert.That(
                OrpheusAudioIntegrationValidation.TryCaptureMixerModel(mixer, out var model),
                Is.True);
            Assert.That(
                model.ExposedParameters[0].Parameter,
                Is.EqualTo(model.Groups[0].VolumeParameter));

            var errors = Validate(fixture.Profile);

            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void PendingAuthoringProfile_IsReadOnlyAndBlocksBuild()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            var authoring =
                ScriptableObject.CreateInstance<OrpheusAudioAuthoringProfile>();
            OrpheusAudioEditorContractTests.SetField(
                authoring,
                "_keyManifest",
                fixture.Profile.KeyManifest);
            OrpheusAudioEditorContractTests.SetField(
                authoring,
                "_catalog",
                fixture.Profile.Catalog);
            OrpheusAudioEditorContractTests.SetField(
                authoring,
                "_generatedRoot",
                TemporaryRoot + "/Generated");
            AssetDatabase.CreateAsset(
                authoring,
                TemporaryRoot + "/Authoring.asset");
            OrpheusAudioEditorContractTests.SetField(
                fixture.Profile,
                "_authoringProfile",
                authoring);
            OrpheusAudioEditorContractTests.SetField(
                fixture.Profile,
                "_authoringEnrollmentGuid",
                string.Empty);
            AssetDatabase.SaveAssets();

            var before = File.ReadAllBytes(
                AssetDatabase.GetAssetPath(fixture.Profile));
            var errors = Validate(fixture.Profile);
            Assert.That(
                errors.Select(error => error.Code),
                Does.Contain(
                    OrpheusAudioValidationErrorCode.InvalidAuthoringProfile));
            CollectionAssert.AreEqual(
                before,
                File.ReadAllBytes(
                    AssetDatabase.GetAssetPath(fixture.Profile)));

            var exception = Assert.Throws<BuildFailedException>(
                () => OrpheusAudioBuildPreprocessor.ValidateOrThrow(
                    BuildTarget.StandaloneWindows64,
                    new[] { fixture.Profile },
                    ProjectionPaths));
            StringAssert.Contains(
                OrpheusAudioValidationErrorCode.InvalidAuthoringProfile
                    .ToString(),
                exception.Message);
        }

        [Test]
        public void DurationBoundaries_AreInclusiveForEverySerializedDuration()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            foreach (var fieldName in new[]
                     {
                         "_bgmCrossfadeSeconds",
                         "_profileAmbienceCrossfadeSeconds",
                         "_snapshotTransitionSeconds"
                     })
            {
                SetSerializedFloat(fixture.Settings, fieldName, 0.015f);
                Assert.That(Validate(fixture.Profile), Is.Empty, fieldName + " minimum");
                SetSerializedFloat(fixture.Settings, fieldName, 5f);
                Assert.That(Validate(fixture.Profile), Is.Empty, fieldName + " maximum");
                SetSerializedFloat(
                    fixture.Settings, fieldName,
                    fieldName == "_snapshotTransitionSeconds" ? 0.6f : 0.4f);
            }
        }

        [TestCaseSource(nameof(InvalidDurations))]
        public void InvalidDuration_IsRejectedByField(
            string fieldName,
            float value,
            object expectedValue)
        {
            var expected = (OrpheusAudioValidationErrorCode)Convert.ToUInt16(expectedValue);
            var fixture = CreateFixture(BankDefect.None, true);
            SetSerializedFloat(fixture.Settings, fieldName, value);

            var integrationErrors = IntegrationErrors(Validate(fixture.Profile));

            Assert.That(integrationErrors.Select(error => error.Code), Is.EqualTo(new[] { expected }));
        }

        [Test]
        public void MissingDirectGroupAndSnapshot_AreReportedInContractOrder()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            OrpheusAudioEditorContractTests.SetField(fixture.Settings, "_musicUser", null);
            OrpheusAudioEditorContractTests.SetField(fixture.Settings, "_peace", null);

            var codes = IntegrationErrors(Validate(fixture.Profile))
                .Select(error => error.Code)
                .ToArray();

            Assert.That(codes.Take(2), Is.EqualTo(new[]
            {
                OrpheusAudioValidationErrorCode.MissingMixerGroup,
                OrpheusAudioValidationErrorCode.MissingMixerSnapshot
            }));
            Assert.That(codes, Does.Contain(OrpheusAudioValidationErrorCode.MismatchedMixerReference));
        }

        [Test]
        public void RealMixerWithNormalUpdateMode_IsRejectedWithoutEditingTheTrackedFixture()
        {
            const string copyPath = TemporaryRoot + "/NormalUpdate.mixer";
            Assert.That(AssetDatabase.CopyAsset(MixerPath, copyPath), Is.True);
            var copiedMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(copyPath);
            copiedMixer.updateMode = AudioMixerUpdateMode.Normal;
            EditorUtility.SetDirty(copiedMixer);
            AssetDatabase.SaveAssets();
            var fixture = CreateFixture(BankDefect.None, true);
            OrpheusAudioEditorTestAssets.ConfigureValidSettings(fixture.Settings, copiedMixer);

            var codes = IntegrationErrors(Validate(fixture.Profile))
                .Select(error => error.Code)
                .ToArray();

            Assert.That(codes, Does.Contain(OrpheusAudioValidationErrorCode.InvalidMixerUpdateMode));
        }

        [Test]
        public void SerializedMixerDuplicateChild_IsRejectedThroughSharedCapture()
        {
            AssertSerializedMixerMutation(
                "DuplicateChild.mixer",
                mixer =>
                {
                    var master = mixer.FindMatchingGroups(string.Empty)
                        .Single(group => string.Equals(
                            group.name, "Master", StringComparison.Ordinal));
                    var serialized = new SerializedObject(master);
                    var children = serialized.FindProperty("m_Children");
                    var duplicate = children.GetArrayElementAtIndex(0).objectReferenceValue;
                    children.arraySize++;
                    children.GetArrayElementAtIndex(children.arraySize - 1)
                        .objectReferenceValue = duplicate;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                },
                OrpheusAudioValidationErrorCode.InvalidMixerGroupContract);
        }

        [Test]
        public void SerializedMixerDuplicateSnapshot_IsRejectedThroughSharedCapture()
        {
            AssertSerializedMixerMutation(
                "DuplicateSnapshot.mixer",
                mixer =>
                {
                    var serialized = new SerializedObject(mixer);
                    var snapshots = serialized.FindProperty("m_Snapshots");
                    var duplicate = snapshots.GetArrayElementAtIndex(0).objectReferenceValue;
                    snapshots.GetArrayElementAtIndex(1)
                        .objectReferenceValue = duplicate;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                },
                OrpheusAudioValidationErrorCode.InvalidMixerSnapshotContract);
        }

        [Test]
        public void NormalizedMixerModel_MutationsCoverEveryOpaqueSerializedAxis()
        {
            AssertMixerModelMutation(
                model => model.ActualGroupCount--,
                OrpheusAudioValidationErrorCode.InvalidMixerGroupContract);
            AssertMixerModelMutation(
                model => model.Groups[1].ParentReferenceCount = 2,
                OrpheusAudioValidationErrorCode.InvalidMixerGroupContract);
            AssertMixerModelMutation(
                model => model.ExposedParameters[0].Name = "WrongVolume",
                OrpheusAudioValidationErrorCode.InvalidMixerExposedParameterContract);
            AssertMixerModelMutation(
                model => model.ActualSnapshotCount--,
                OrpheusAudioValidationErrorCode.InvalidMixerSnapshotContract);
            AssertMixerModelMutation(
                model => model.Snapshots[0] = null,
                OrpheusAudioValidationErrorCode.InvalidMixerSnapshotContract);
            AssertMixerModelMutation(
                model => model.Snapshots[0].Parameters = new string[0],
                OrpheusAudioValidationErrorCode.InvalidMixerSnapshotOverride);
            AssertMixerModelMutation(
                model => model.Snapshots[0].Values[0] = float.NaN,
                OrpheusAudioValidationErrorCode.InvalidMixerAttenuation);
            AssertMixerModelMutation(
                model =>
                {
                    var uiParameter = model.Groups[8].VolumeParameter;
                    var uiIndex = Array.IndexOf(model.Snapshots[2].Parameters, uiParameter);
                    Assert.That(uiIndex, Is.GreaterThanOrEqualTo(0));
                    model.Snapshots[2].Values[uiIndex] = -80f;
                },
                OrpheusAudioValidationErrorCode.InaudibleStateUi);
        }

        [Test]
        public void IntegrationErrors_AreAppendedInDurationHostThenSceneOrder()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            SetSerializedFloat(fixture.Settings, "_bgmCrossfadeSeconds", float.NaN);
            OrpheusAudioEditorContractTests.SetField(
                fixture.Settings, "_profileAmbienceCrossfadeSeconds", float.PositiveInfinity);
            SetSerializedFloat(fixture.Settings, "_snapshotTransitionSeconds", 0f);
            OrpheusAudioEditorContractTests.SetField(fixture.Profile, "_runtimeHostPrefab", null);
            OrpheusAudioEditorContractTests.SetField(
                fixture.Profile, "_listenerScenes", new SceneAsset[] { null });

            var first = IntegrationErrors(Validate(fixture.Profile));
            var second = IntegrationErrors(Validate(fixture.Profile));

            var expected = new[]
            {
                OrpheusAudioValidationErrorCode.InvalidBgmCrossfadeDuration,
                OrpheusAudioValidationErrorCode.InvalidProfileAmbienceCrossfadeDuration,
                OrpheusAudioValidationErrorCode.InvalidSnapshotTransitionDuration,
                OrpheusAudioValidationErrorCode.MissingRuntimeHostPrefab,
                OrpheusAudioValidationErrorCode.NullListenerScene
            };
            Assert.That(first.Select(error => error.Code), Is.EqualTo(expected));
            Assert.That(first.Select(error => error.ToString()),
                Is.EqualTo(second.Select(error => error.ToString())));
        }

        [TestCase(BankDefect.NoRuntimeHost,
            OrpheusAudioValidationErrorCode.InvalidRuntimeHostCount)]
        [TestCase(BankDefect.MultipleRuntimeHosts,
            OrpheusAudioValidationErrorCode.InvalidRuntimeHostCount)]
        [TestCase(BankDefect.MissingSourceBank,
            OrpheusAudioValidationErrorCode.MissingSourceBank)]
        [TestCase(BankDefect.MultipleSourceBanks,
            OrpheusAudioValidationErrorCode.InvalidSourceBankCount)]
        [TestCase(BankDefect.MismatchedSourceBank,
            OrpheusAudioValidationErrorCode.MismatchedSourceBankReference)]
        [TestCase(BankDefect.RoleCount,
            OrpheusAudioValidationErrorCode.InvalidSourceBankRoleCount)]
        [TestCase(BankDefect.MissingLeaf,
            OrpheusAudioValidationErrorCode.MissingSourceBankLeaf)]
        [TestCase(BankDefect.WrongLeafName,
            OrpheusAudioValidationErrorCode.InvalidSourceBankLeafName)]
        [TestCase(BankDefect.DuplicateLeaf,
            OrpheusAudioValidationErrorCode.DuplicateSourceBankLeaf)]
        [TestCase(BankDefect.MultipleAudioSourcesOnLeaf,
            OrpheusAudioValidationErrorCode.InvalidSourceBankLeafAudioSourceCount)]
        [TestCase(BankDefect.UnexpectedAudioSource,
            OrpheusAudioValidationErrorCode.UnexpectedSourceBankAudioSource)]
        [TestCase(BankDefect.OutsideBank,
            OrpheusAudioValidationErrorCode.SourceBankLeafOutsideBank)]
        [TestCase(BankDefect.PlayOnAwake,
            OrpheusAudioValidationErrorCode.SourceBankPlayOnAwake)]
        [TestCase(BankDefect.PresetClip,
            OrpheusAudioValidationErrorCode.SourceBankPresetClip)]
        [TestCase(BankDefect.StaleRoute,
            OrpheusAudioValidationErrorCode.SourceBankStaleRoute)]
        public void RuntimeHostAndSourceBankDefect_IsRejected(
            BankDefect defect,
            object expectedValue)
        {
            var expected = (OrpheusAudioValidationErrorCode)Convert.ToUInt16(expectedValue);
            var fixture = CreateFixture(defect, true);

            var codes = IntegrationErrors(Validate(fixture.Profile))
                .Select(error => error.Code)
                .ToArray();

            Assert.That(codes, Does.Contain(expected));
        }

        [Test]
        public void MissingAndNonPrefabRuntimeHostReferences_AreDistinct()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            OrpheusAudioEditorContractTests.SetField(fixture.Profile, "_runtimeHostPrefab", null);
            Assert.That(
                IntegrationErrors(Validate(fixture.Profile)).Select(error => error.Code),
                Does.Contain(OrpheusAudioValidationErrorCode.MissingRuntimeHostPrefab));

            var transient = new GameObject("NotAPrefab");
            try
            {
                OrpheusAudioEditorContractTests.SetField(
                    fixture.Profile, "_runtimeHostPrefab", transient);
                Assert.That(
                    IntegrationErrors(Validate(fixture.Profile)).Select(error => error.Code),
                    Does.Contain(OrpheusAudioValidationErrorCode.InvalidRuntimeHostPrefab));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(transient);
            }
        }

        [Test]
        public void EmptyListenerSceneList_IsValidAndSceneContentsAreNotInferred()
        {
            var fixture = CreateFixture(BankDefect.None, false);
            OrpheusAudioEditorContractTests.SetField(
                fixture.Profile, "_listenerScenes", Array.Empty<SceneAsset>());

            Assert.That(Validate(fixture.Profile), Is.Empty);

            var scene = CreateSceneAsset("NoListenerOrHost.unity");
            OrpheusAudioEditorContractTests.SetField(
                fixture.Profile, "_listenerScenes", new[] { scene });
            Assert.That(Validate(fixture.Profile), Is.Empty);
        }

        [Test]
        public void ListenerSceneValidation_PreservesOpenSceneSetupAndActiveScene()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            var previousActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var existing = EditorSceneManager.OpenScene(
                EmptySceneFixturePath,
                OpenSceneMode.Additive);
            try
            {
                Assert.That(
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(existing),
                    Is.True);
                var before = EditorSceneManager.GetSceneManagerSetup();

                Assert.That(Validate(fixture.Profile), Is.Empty);

                AssertSceneSetupEqual(before, EditorSceneManager.GetSceneManagerSetup());
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                {
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousActive);
                }

                if (existing.IsValid())
                {
                    EditorSceneManager.CloseScene(existing, true);
                }
            }
        }

        [Test]
        public void ListenerSceneUnloadedInSceneSetup_IsValid()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            var scenePath = AssetDatabase.GetAssetPath(fixture.ListenerScene);
            var previousActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var anchor = EditorSceneManager.OpenScene(
                EmptySceneFixturePath,
                OpenSceneMode.Additive);
            UnityEngine.SceneManagement.Scene unloaded = default;
            try
            {
                Assert.That(
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(anchor),
                    Is.True);
                unloaded = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.AdditiveWithoutLoading);
                var before = EditorSceneManager.GetSceneManagerSetup();

                Assert.That(Validate(fixture.Profile), Is.Empty);

                AssertSceneSetupEqual(before, EditorSceneManager.GetSceneManagerSetup());
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                {
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousActive);
                }

                if (unloaded.IsValid())
                {
                    EditorSceneManager.CloseScene(unloaded, true);
                }

                if (anchor.IsValid())
                {
                    EditorSceneManager.CloseScene(anchor, true);
                }
            }
        }

        [Test]
        public void NullListenerScene_IsRejectedAtItsSerializedIndex()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            OrpheusAudioEditorContractTests.SetField(
                fixture.Profile,
                "_listenerScenes",
                new[] { fixture.ListenerScene, null, fixture.ListenerScene });

            var error = IntegrationErrors(Validate(fixture.Profile))
                .Single(item => item.Code == OrpheusAudioValidationErrorCode.NullListenerScene);

            Assert.That(error.RelatedIndex, Is.EqualTo(1));
        }

        [Test]
        public void DeletedListenerScene_PreservesARejectableSerializedIdentity()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            var scenePath = AssetDatabase.GetAssetPath(fixture.ListenerScene);
            AssetDatabase.SaveAssets();
            Assert.That(AssetDatabase.DeleteAsset(scenePath), Is.True);
            AssetDatabase.Refresh();
            var reloaded = AssetDatabase.LoadAssetAtPath<OrpheusAudioValidationProfile>(
                AssetDatabase.GetAssetPath(fixture.Profile));

            var codes = IntegrationErrors(Validate(reloaded)).Select(error => error.Code).ToArray();

            Assert.That(codes, Does.Contain(OrpheusAudioValidationErrorCode.UnresolvedListenerScene));
        }

        [Test]
        public void WrongTypeListenerReference_IsRejectedBySerializedAssetIdentity()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            var path = TemporaryRoot + "/WrongType.txt";
            File.WriteAllText(path, "not a scene");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var wrongType = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(
                OrpheusAudioIntegrationValidation.EvaluateListenerSceneAsset(wrongType),
                Is.EqualTo(OrpheusAudioValidationErrorCode.InvalidListenerSceneType));

            Assert.That(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    fixture.ListenerScene, out var sceneGuid, out long sceneLocalId),
                Is.True);
            Assert.That(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    wrongType, out var wrongGuid, out long wrongLocalId),
                Is.True);
            var profilePath = AssetDatabase.GetAssetPath(fixture.Profile);
            var yaml = File.ReadAllText(profilePath);
            var sceneIdentity = "{fileID: " + sceneLocalId + ", guid: " + sceneGuid +
                                ", type: 3}";
            var wrongIdentity = "{fileID: " + wrongLocalId + ", guid: " + wrongGuid +
                                ", type: 3}";
            Assert.That(yaml, Does.Contain(sceneIdentity));
            File.WriteAllText(profilePath, yaml.Replace(sceneIdentity, wrongIdentity));
            AssetDatabase.ImportAsset(
                profilePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var corruptedProfile = AssetDatabase.LoadAssetAtPath<OrpheusAudioValidationProfile>(
                profilePath);
            Assert.That(
                IntegrationErrors(Validate(corruptedProfile)).Select(error => error.Code),
                Does.Contain(OrpheusAudioValidationErrorCode.InvalidListenerSceneType));
        }

        [Test]
        public void SceneFileMissingBehindACachedIdentity_IsUnloadable()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            var scenePath = AssetDatabase.GetAssetPath(fixture.ListenerScene);
            File.Delete(scenePath);

            Assert.That(
                IntegrationErrors(Validate(fixture.Profile)).Select(error => error.Code),
                Does.Contain(OrpheusAudioValidationErrorCode.UnloadableListenerScene));
        }

        [Test]
        public void LoadedListenerSceneWithMissingBackingFile_IsUnloadable()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            var scenePath = AssetDatabase.GetAssetPath(fixture.ListenerScene);
            var loadedScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                Assert.That(loadedScene.IsValid() && loadedScene.isLoaded, Is.True);
                File.Delete(scenePath);

                Assert.That(
                    IntegrationErrors(Validate(fixture.Profile)).Select(error => error.Code),
                    Does.Contain(OrpheusAudioValidationErrorCode.UnloadableListenerScene));
            }
            finally
            {
                if (loadedScene.IsValid())
                {
                    EditorSceneManager.CloseScene(loadedScene, true);
                }
            }
        }

        [Test]
        public void InvalidEnabledProfile_BuildFailureContainsTheAppendedRule()
        {
            var fixture = CreateFixture(BankDefect.None, true);
            SetSerializedFloat(fixture.Settings, "_bgmCrossfadeSeconds", float.NaN);
            EditorUtility.SetDirty(fixture.Settings);
            AssetDatabase.SaveAssets();

            var exception = Assert.Throws<BuildFailedException>(
                () => OrpheusAudioBuildPreprocessor.ValidateOrThrow(
                    BuildTarget.StandaloneWindows64));

            Assert.That(exception.Message,
                Does.Contain(nameof(OrpheusAudioValidationErrorCode.InvalidBgmCrossfadeDuration)));
        }

        [Test]
        public void MultipleValidConsumerProfiles_PassIndependentlyWithOneSharedManifest()
        {
            var first = CreateFixture(BankDefect.None, true, "A");
            var second = CreateFixture(BankDefect.None, true, "B", first.Manifest);

            Assert.That(Validate(first.Profile), Is.Empty);
            Assert.That(Validate(second.Profile), Is.Empty);
            Assert.DoesNotThrow(() => OrpheusAudioBuildPreprocessor.ValidateOrThrow(
                BuildTarget.StandaloneWindows64,
                new[] { second.Profile, first.Profile },
                ProjectionPaths));
            Assert.That(
                OrpheusAudioValidationProfileDiscovery.HasExactlyOneEnabledProfile(
                    new[] { second.Profile, first.Profile }),
                Is.False);
        }

        private static Fixture CreateFixture(
            BankDefect defect,
            bool includeListenerScene,
            string prefix = "Fixture",
            OrpheusAudioKeyManifest sharedManifest = null)
        {
            if (string.Equals(prefix, "Fixture", StringComparison.Ordinal))
            {
                prefix += (++_fixtureSequence).ToString("D4");
            }

            var runtimeHost = CreateRuntimeHostPrefab(prefix + "RuntimeHost.prefab", defect);
            var listenerScene = includeListenerScene
                ? CreateSceneAsset(prefix + "Listener.unity")
                : null;
            var settings = CreateAsset<OrpheusAudioSettings>(prefix + "Settings.asset");
            OrpheusAudioEditorTestAssets.ConfigureValidSettings(settings);
            var catalog = CreateAsset<OrpheusAudioCatalog>(prefix + "Catalog.asset");
            OrpheusAudioEditorContractTests.SetField(
                catalog, "_events", Array.Empty<OrpheusAudioEvent>());
            var manifest = sharedManifest ??
                           CreateAsset<OrpheusAudioKeyManifest>(prefix + "Manifest.asset");
            if (sharedManifest == null)
            {
                OrpheusAudioEditorContractTests.SetField(
                    manifest, "_entries", Array.Empty<OrpheusAudioKeyManifestEntry>());
                WriteProjection(manifest);
            }

            var profile = CreateAsset<OrpheusAudioValidationProfile>(prefix + "Profile.asset");
            OrpheusAudioEditorContractTests.SetField(profile, "_enabled", true);
            OrpheusAudioEditorContractTests.SetField(profile, "_settings", settings);
            OrpheusAudioEditorContractTests.SetField(profile, "_catalog", catalog);
            OrpheusAudioEditorContractTests.SetField(profile, "_keyManifest", manifest);
            OrpheusAudioEditorContractTests.SetField(profile, "_runtimeHostPrefab", runtimeHost);
            OrpheusAudioEditorContractTests.SetField(
                profile,
                "_listenerScenes",
                includeListenerScene ? new[] { listenerScene } : Array.Empty<SceneAsset>());
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(manifest);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return new Fixture(settings, manifest, profile, listenerScene);
        }

        private static void AssertMixerModelMutation(
            Action<OrpheusAudioMixerValidationModel> mutation,
            OrpheusAudioValidationErrorCode expected)
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            Assert.That(
                OrpheusAudioIntegrationValidation.TryCaptureMixerModel(mixer, out var model),
                Is.True);
            var settings = ScriptableObject.CreateInstance<OrpheusAudioSettings>();
            try
            {
                OrpheusAudioEditorTestAssets.ConfigureValidSettings(settings, mixer);
                var serialized = new SerializedObject(settings);
                var groups = new[]
                {
                    "_master", "_musicUser", "_musicState", "_sfxCombatUser",
                    "_sfxCombatState", "_sfxWorldUser", "_sfxWorldState", "_sfxUiUser",
                    "_sfxUiState", "_ambienceUser", "_ambienceState"
                }.Select(name => serialized.FindProperty(name).objectReferenceValue as AudioMixerGroup)
                    .ToArray();
                var snapshots = new[] { "_peace", "_combat", "_menu", "_pause" }
                    .Select(name => serialized.FindProperty(name).objectReferenceValue as AudioMixerSnapshot)
                    .ToArray();
                mutation(model);
                var errors = new List<OrpheusAudioValidationError>();

                OrpheusAudioIntegrationValidation.ValidateMixerModel(
                    model, groups, snapshots, "Profile", MixerPath, errors);

                Assert.That(errors.Select(error => error.Code), Does.Contain(expected));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        private static void AssertSerializedMixerMutation(
            string fileName,
            Action<AudioMixer> mutation,
            OrpheusAudioValidationErrorCode expected)
        {
            var copyPath = TemporaryRoot + "/" + fileName;
            Assert.That(AssetDatabase.CopyAsset(MixerPath, copyPath), Is.True);
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(copyPath);
            Assert.That(mixer, Is.Not.Null);
            mutation(mixer);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(copyPath, ImportAssetOptions.ForceSynchronousImport);

            mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(copyPath);
            var fixture = CreateFixture(BankDefect.None, true, fileName.Replace(".mixer", string.Empty));
            OrpheusAudioEditorTestAssets.ConfigureValidSettings(
                fixture.Settings, mixer, true);

            Assert.That(
                IntegrationErrors(Validate(fixture.Profile)).Select(error => error.Code),
                Does.Contain(expected));
        }

        private static void SetSerializedFloat(
            OrpheusAudioSettings settings,
            string fieldName,
            float value)
        {
            var serialized = new SerializedObject(settings);
            serialized.FindProperty(fieldName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateRuntimeHostPrefab(string fileName, BankDefect defect)
        {
            var root = new GameObject("OrpheusRuntimeHost");
            try
            {
                var host = defect == BankDefect.NoRuntimeHost
                    ? null
                    : root.AddComponent<OrpheusAudioRuntimeHost>();
                if (defect == BankDefect.MultipleRuntimeHosts)
                {
                    root.AddComponent<OrpheusAudioRuntimeHost>();
                }

                var bankOwner = root.transform;
                if (defect == BankDefect.OutsideBank)
                {
                    var bankObject = new GameObject("SourceBank");
                    bankObject.transform.SetParent(root.transform, false);
                    bankOwner = bankObject.transform;
                }

                var bank = bankOwner.gameObject.AddComponent<OrpheusAudioSourceBank>();
                OrpheusAudioSourceBank secondBank = null;
                if (defect == BankDefect.MultipleSourceBanks ||
                    defect == BankDefect.MismatchedSourceBank)
                {
                    secondBank = root.AddComponent<OrpheusAudioSourceBank>();
                }

                var sources = OrpheusAudioEditorTestAssets.CreateSourceLeaves(bankOwner);
                if (defect == BankDefect.WrongLeafName)
                {
                    sources[3].name = "Wrong_03";
                }
                else if (defect == BankDefect.DuplicateLeaf)
                {
                    sources[1] = sources[0];
                }
                else if (defect == BankDefect.MissingLeaf)
                {
                    sources[2] = null;
                }
                else if (defect == BankDefect.MultipleAudioSourcesOnLeaf)
                {
                    sources[0].gameObject.AddComponent<AudioSource>().playOnAwake = false;
                }
                else if (defect == BankDefect.UnexpectedAudioSource)
                {
                    root.AddComponent<AudioSource>().playOnAwake = false;
                }
                else if (defect == BankDefect.OutsideBank)
                {
                    sources[0].transform.SetParent(root.transform, false);
                }
                else if (defect == BankDefect.PlayOnAwake)
                {
                    sources[0].playOnAwake = true;
                }
                else if (defect == BankDefect.PresetClip)
                {
                    sources[0].clip = CreateImportedClip();
                }
                else if (defect == BankDefect.StaleRoute)
                {
                    var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
                    sources[0].outputAudioMixerGroup = mixer.FindMatchingGroups("Music_State").Single();
                }

                OrpheusAudioEditorTestAssets.SetBankSources(
                    bank, sources, defect == BankDefect.RoleCount ? 11 : 12);
                if (host != null && defect != BankDefect.MissingSourceBank)
                {
                    OrpheusAudioEditorContractTests.SetField(
                        host,
                        "_sourceBank",
                        defect == BankDefect.MismatchedSourceBank ? secondBank : bank);
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, TemporaryRoot + "/" + fileName);
                Assert.That(prefab, Is.Not.Null);
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static SceneAsset CreateSceneAsset(string fileName)
        {
            return OrpheusAudioEditorTestAssets.CopyEmptyScene(
                TemporaryRoot + "/" + fileName);
        }

        private static void AssertSceneSetupEqual(SceneSetup[] expected, SceneSetup[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.That(actual[index].path, Is.EqualTo(expected[index].path));
                Assert.That(actual[index].isLoaded, Is.EqualTo(expected[index].isLoaded));
                Assert.That(actual[index].isActive, Is.EqualTo(expected[index].isActive));
            }
        }

        private static AudioClip CreateImportedClip()
        {
            var path = TemporaryRoot + "/Preset.wav";
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, CreateWaveBytes());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static byte[] CreateWaveBytes()
        {
            const int sampleCount = 8;
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

        private static T CreateAsset<T>(string fileName) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, TemporaryRoot + "/" + fileName);
            return asset;
        }

        private static void WriteProjection(OrpheusAudioKeyManifest manifest)
        {
            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    manifest, out var source, out _),
                Is.True);
            Directory.CreateDirectory(ProjectionPaths.OutputDirectory);
            File.WriteAllText(ProjectionPaths.AssemblyPath, OrpheusAudioTypedKeyProjection.ExpectedAssembly);
            File.WriteAllText(ProjectionPaths.SourcePath, source);
        }

        private static List<OrpheusAudioValidationError> Validate(
            OrpheusAudioValidationProfile profile)
        {
            return OrpheusAudioValidator.ValidateProfile(
                profile, BuildTargetGroup.Standalone, ProjectionPaths);
        }

        private static OrpheusAudioValidationError[] IntegrationErrors(
            IEnumerable<OrpheusAudioValidationError> errors)
        {
            return errors.Where(error => (ushort)error.Code >= 28).ToArray();
        }

        private static void DeleteTemporaryAssets()
        {
            if (AssetDatabase.IsValidFolder(TemporaryRoot))
            {
                AssetDatabase.DeleteAsset(TemporaryRoot);
            }
        }

        private static void DeleteGeneratedProjection()
        {
            if (Directory.Exists(ProjectionPaths.OutputDirectory))
            {
                Directory.Delete(ProjectionPaths.OutputDirectory, true);
            }
        }

        public enum BankDefect
        {
            None,
            NoRuntimeHost,
            MultipleRuntimeHosts,
            MissingSourceBank,
            MultipleSourceBanks,
            MismatchedSourceBank,
            RoleCount,
            MissingLeaf,
            WrongLeafName,
            DuplicateLeaf,
            MultipleAudioSourcesOnLeaf,
            UnexpectedAudioSource,
            OutsideBank,
            PlayOnAwake,
            PresetClip,
            StaleRoute
        }

        private readonly struct Fixture
        {
            internal Fixture(
                OrpheusAudioSettings settings,
                OrpheusAudioKeyManifest manifest,
                OrpheusAudioValidationProfile profile,
                SceneAsset listenerScene)
            {
                Settings = settings;
                Manifest = manifest;
                Profile = profile;
                ListenerScene = listenerScene;
            }

            internal OrpheusAudioSettings Settings { get; }
            internal OrpheusAudioKeyManifest Manifest { get; }
            internal OrpheusAudioValidationProfile Profile { get; }
            internal SceneAsset ListenerScene { get; }
        }
    }
}
