using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioAuthoringCommandTests
    {
        private const string Root = "Assets/OrpheusAuthoringTask7CommandTests";
        private const string AssetPathSeparator = "/";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Root);
            AssetDatabase.CreateFolder("Assets", "OrpheusAuthoringTask7CommandTests");
        }

        [TearDown]
        public void TearDown()
        {
            Selection.activeObject = null;
            AssetDatabase.DeleteAsset(Root);
        }

        [Test]
        public void Menu_DeclaresExactCommandsAndStateEnablement()
        {
            var menuItems = typeof(OrpheusAudioAuthoringMenu)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .SelectMany(method => method
                    .GetCustomAttributes(typeof(MenuItem), false)
                    .Cast<MenuItem>())
                .Select(attribute => attribute.menuItem)
                .Distinct()
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            CollectionAssert.AreEqual(
                new[]
                {
                    "Tools/Orpheus/Accept And Compile Enrollment",
                    "Tools/Orpheus/Analyze Enrolled Authoring",
                    "Tools/Orpheus/Compile And Delete Tracked Orphans",
                    "Tools/Orpheus/Compile Enrolled Authoring"
                },
                menuItems);

            var fixture = CreateProfile();
            Assert.That(
                OrpheusAudioAuthoringMenu.CanExecute(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Analyze),
                Is.True);
            Assert.That(
                OrpheusAudioAuthoringMenu.CanExecute(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.CompileAndAcceptEnrollment),
                Is.True);
            Assert.That(
                OrpheusAudioAuthoringMenu.CanExecute(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Compile),
                Is.False);

            Set(
                fixture.Profile,
                "_authoringEnrollmentGuid",
                AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(fixture.Authoring))
                    .ToLowerInvariant());
            Assert.That(
                OrpheusAudioAuthoringMenu.CanExecute(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Analyze),
                Is.True);
            Assert.That(
                OrpheusAudioAuthoringMenu.CanExecute(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.CompileAndAcceptEnrollment),
                Is.False);
            Assert.That(
                OrpheusAudioAuthoringMenu.CanExecute(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Compile),
                Is.True);
            Assert.That(
                OrpheusAudioAuthoringMenu.CanExecute(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode
                        .CompileAndDeleteTrackedOrphans),
                Is.True);
        }

        [Test]
        public void Menu_ConfirmationMessagesNameRequiredIdentities()
        {
            var fixture = CreateProfile();
            var message =
                OrpheusAudioAuthoringMenu.BuildEnrollmentConfirmation(
                    fixture.Profile);
            StringAssert.Contains(
                AssetDatabase.GetAssetPath(fixture.Profile),
                message);
            StringAssert.Contains(
                AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(fixture.Authoring))
                    .ToLowerInvariant(),
                message);
            StringAssert.Contains(
                fixture.Authoring.CaptureValue().GeneratedRoot,
                message);
            StringAssert.Contains("100:Cue:Active", message);

            var deletion =
                OrpheusAudioAuthoringMenu.BuildOrphanDeletionConfirmation(
                    fixture.Authoring.CaptureValue().GeneratedRoot,
                    3);
            StringAssert.Contains(
                fixture.Authoring.CaptureValue().GeneratedRoot,
                deletion);
            StringAssert.Contains("3", deletion);
        }

        [Test]
        public void Batch_ParsesOnlyExactUniqueRequiredArguments()
        {
            var guid = new string('a', 32);
            Assert.That(
                OrpheusAudioAuthoringBatch.TryParseArguments(
                    new[]
                    {
                        "-batchmode",
                        "-orpheusProfileGuid", guid,
                        "-orpheusMode", "Compile",
                        "-orpheusBuildTarget", "StandaloneWindows64"
                    },
                    out var request,
                    out _),
                Is.True);
            Assert.That(request.ProfileGuid, Is.EqualTo(guid));
            Assert.That(
                request.Mode,
                Is.EqualTo(OrpheusAudioAuthoringCompileMode.Compile));
            Assert.That(
                request.BuildTarget,
                Is.EqualTo(BuildTarget.StandaloneWindows64));

            AssertRejected("-orpheusProfileGuid", guid);
            AssertRejected(
                "-orpheusProfileGuid", guid,
                "-orpheusProfileGuid", guid,
                "-orpheusMode", "Analyze",
                "-orpheusBuildTarget", "Android");
            AssertRejected(
                "-orpheusProfileGuid", guid,
                "-orpheusMode", "Analyze",
                "-orpheusBuildTarget", "iOS");
            AssertRejected(
                "-orpheusProfileGuid", guid,
                "-orpheusMode", "Analyze",
                "-orpheusBuildTarget", "Android",
                "-orpheusConfirm", "true");
            AssertRejected(
                "-orpheusProfileGuid", guid,
                "-orpheusMode", "CompileAndAcceptEnrollment",
                "-orpheusBuildTarget", "Android");
            AssertRejected(
                "-orpheusProfileGuid", guid,
                "-orpheusMode", "CompileAndDeleteTrackedOrphans",
                "-orpheusBuildTarget", "Android");
        }

        [Test]
        public void Batch_MapsOnlySuccessfulStatusesToZero()
        {
            Assert.That(
                OrpheusAudioAuthoringBatch.GetExitCode(
                    OrpheusAudioAuthoringCompileStatus.SucceededChanged),
                Is.Zero);
            Assert.That(
                OrpheusAudioAuthoringBatch.GetExitCode(
                    OrpheusAudioAuthoringCompileStatus.SucceededUnchanged),
                Is.Zero);
            Assert.That(
                OrpheusAudioAuthoringBatch.GetExitCode(
                    OrpheusAudioAuthoringCompileStatus.Rejected),
                Is.Not.Zero);
            Assert.That(
                OrpheusAudioAuthoringBatch.GetExitCode(
                    OrpheusAudioAuthoringCompileStatus.RolledBack),
                Is.Not.Zero);
            Assert.That(
                OrpheusAudioAuthoringBatch.GetExitCode(
                    OrpheusAudioAuthoringCompileStatus.RollbackFailed),
                Is.Not.Zero);
        }

        [Test]
        public void Inspector_HidesEnrollmentGuidAndLocksProfileAfterEnrollment()
        {
            var fixture = CreateProfile();
            CollectionAssert.DoesNotContain(
                OrpheusAudioValidationProfileEditor.VisibleProperties,
                "_authoringEnrollmentGuid");
            Assert.That(
                OrpheusAudioValidationProfileEditor
                    .CanAssignAuthoringProfile(fixture.Profile),
                Is.True);

            Set(
                fixture.Profile,
                "_authoringEnrollmentGuid",
                AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(fixture.Authoring))
                    .ToLowerInvariant());
            Assert.That(
                OrpheusAudioValidationProfileEditor
                    .CanAssignAuthoringProfile(fixture.Profile),
                Is.False);
        }

        private static void AssertRejected(params string[] arguments)
        {
            Assert.That(
                OrpheusAudioAuthoringBatch.TryParseArguments(
                    arguments,
                    out _,
                    out _),
                Is.False);
        }

        private static Fixture CreateProfile()
        {
            var manifest = OrpheusAudioEditorContractTests.CreateManifest(
                OrpheusAudioEditorContractTests.CreateEntry(
                    100,
                    "Cue",
                    OrpheusAudioKeyStatus.Active));
            AssetDatabase.CreateAsset(
                manifest,
                Root + AssetPathSeparator + "Manifest.asset");

            var authoring =
                ScriptableObject.CreateInstance<OrpheusAudioAuthoringProfile>();
            Set(authoring, "_keyManifest", manifest);
            Set(
                authoring,
                "_generatedRoot",
                Root + AssetPathSeparator + "Generated");
            AssetDatabase.CreateAsset(
                authoring,
                Root + AssetPathSeparator + "Authoring.asset");

            var profile =
                ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            Set(profile, "_enabled", true);
            Set(profile, "_keyManifest", manifest);
            Set(profile, "_authoringProfile", authoring);
            Set(profile, "_authoringEnrollmentGuid", string.Empty);
            AssetDatabase.CreateAsset(
                profile,
                Root + AssetPathSeparator + "Validation.asset");
            AssetDatabase.SaveAssets();
            return new Fixture(profile, authoring);
        }

        private static void Set(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
            if (target is UnityEngine.Object unityObject)
            {
                EditorUtility.SetDirty(unityObject);
            }
        }

        private readonly struct Fixture
        {
            internal Fixture(
                OrpheusAudioValidationProfile profile,
                OrpheusAudioAuthoringProfile authoring)
            {
                Profile = profile;
                Authoring = authoring;
            }

            internal OrpheusAudioValidationProfile Profile { get; }
            internal OrpheusAudioAuthoringProfile Authoring { get; }
        }
    }
}
