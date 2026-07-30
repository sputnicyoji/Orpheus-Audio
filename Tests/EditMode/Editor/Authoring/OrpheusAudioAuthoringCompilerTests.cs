using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioAuthoringCompilerTests
    {
        private const char AssetPathSeparator = (char)47;
        private const string Root = "Assets/OrpheusAuthoringTask4Tests";
        private static readonly string GeneratedRoot =
            Root + AssetPathSeparator + "Generated";
        private const string TransactionRoot =
            "Library/Orpheus/AuthoringTransactions";
        private const string TypedKeyRoot = "Assets/OrpheusGenerated";
        private string _typedKeyBackupRoot;

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Root);
            DeleteLibraryDirectory(TransactionRoot);
            _typedKeyBackupRoot = Path.Combine(
                "Library",
                "Orpheus",
                "AuthoringCompilerTests",
                Guid.NewGuid().ToString("N"));
            try
            {
                IsolateTypedKeyProjection();
                Directory.CreateDirectory(Root);
                AssetDatabase.Refresh();
            }
            catch
            {
                RestoreTypedKeyProjection();
                throw;
            }
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                DeleteFixtureJunction();
                AssetDatabase.DeleteAsset(Root);
                DeleteLibraryDirectory(TransactionRoot);
            }
            finally
            {
                try
                {
                    RestoreTypedKeyProjection();
                }
                finally
                {
                    AssetDatabase.Refresh();
                }
            }
        }

        [Test]
        public void Capture_RejectsNullProfileWithoutMutation()
        {
            Assert.That(
                OrpheusAudioAuthoringCapture.TryCapture(
                    null,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    BuildTargetGroup.Standalone,
                    out _,
                    out _,
                    out var errors),
                Is.False);
            Assert.That(errors[0].Code, Is.EqualTo(OrpheusAuthoringErrorCode.InvalidValidationProfile));
        }

        [Test]
        public void Capture_ValidPendingProfileUsesLowerGuidAndSignedLocalId()
        {
            var fixture = CreatePendingFixture();

            Assert.That(
                OrpheusAudioAuthoringCapture.TryCaptureDetailed(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile },
                    out _,
                    out var plan,
                    out var identities,
                    out var errors),
                Is.True,
                Format(errors));
            Assert.That(plan.EventCount, Is.GreaterThanOrEqualTo(1));
            var captured = plan.GetEvent(0).GetClip(0);
            Assert.That(captured.Guid, Is.EqualTo(captured.Guid.ToLowerInvariant()));
            Assert.That(
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    fixture.Clip,
                    out var guid,
                    out long localId),
                Is.True);
            Assert.That(captured.Guid, Is.EqualTo(guid.ToLowerInvariant()));
            Assert.That(captured.LocalFileId, Is.EqualTo(localId));
            Assert.That(identities.Count, Is.GreaterThanOrEqualTo(6));
            for (var index = 0; index < identities.Count; index++)
            {
                var identity = identities.Get(index);
                Assert.That(identity.Guid, Is.EqualTo(identity.Guid.ToLowerInvariant()));
                Assert.That(identity.Guid, Has.Length.EqualTo(32));
                Assert.That(identity.AssetPath, Does.StartWith("Assets/"));
                Assert.That(identity.Kind, Is.Not.Empty);
                Assert.That(
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                            identity.AssetPath),
                        out var capturedGuid,
                        out long capturedLocalId),
                    Is.True);
                Assert.That(identity.Guid, Is.EqualTo(capturedGuid.ToLowerInvariant()));
                Assert.That(identity.LocalFileId, Is.EqualTo(capturedLocalId));
            }
        }

        [Test]
        public void CaptureOwnedEventFingerprints_NullRecipesRemainStructured()
        {
            var tracked = new OrpheusAuthoringOwnedEventValue(
                100,
                "ui",
                "Cue",
                new string('a', 32),
                GeneratedRoot + AssetPathSeparator + "Events" +
                AssetPathSeparator + "AE_Cue.asset",
                string.Empty);
            var errors = new List<OrpheusAuthoringCompilationError>();

            var captured =
                OrpheusAudioAuthoringCapture.CaptureOwnedEventFingerprints(
                    new[] { tracked },
                    null,
                    errors);

            Assert.That(captured, Has.Length.EqualTo(1));
            Assert.That(captured[0].ContentFingerprint, Is.Empty);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void CaptureOwnedEventFingerprints_RejectsSchemaTamper()
        {
            var fixture = CreatePendingFixture();
            Assert.That(
                OrpheusAudioAuthoringCapture.TryCapture(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile },
                    out var input,
                    out var plan,
                    out var captureErrors),
                Is.True,
                Format(captureErrors));
            var expected = plan.GetEvent(0);
            var path = expected.AssetPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.Refresh();
            var actual = ScriptableObject.CreateInstance<OrpheusAudioEvent>();
            var serialized = new SerializedObject(actual);
            serialized.FindProperty("_schemaVersion").intValue = 0;
            serialized.FindProperty("_key").intValue = expected.Key;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(actual, path);
            AssetDatabase.SaveAssets();

            var tracked = new OrpheusAuthoringOwnedEventValue(
                expected.Key,
                expected.ModuleId,
                expected.Symbol,
                AssetDatabase.AssetPathToGUID(path).ToLowerInvariant(),
                path,
                string.Empty);
            var recipes = new OrpheusAuthoringModuleRecipeValue[input.RecipeCount];
            for (var index = 0; index < recipes.Length; index++)
            {
                recipes[index] = input.GetRecipe(index);
            }

            var errors = new List<OrpheusAuthoringCompilationError>();
            var captured =
                OrpheusAudioAuthoringCapture.CaptureOwnedEventFingerprints(
                    new[] { tracked },
                    recipes,
                    errors);

            Assert.That(captured[0].ContentFingerprint, Is.Empty);
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(
                errors[0].Code,
                Is.EqualTo(
                    OrpheusAuthoringErrorCode.ActualOutputFingerprintMismatch));
        }

        [Test]
        public void Capture_RejectsRootEscapeAndDisabledOwner()
        {
            var fixture = CreatePendingFixture();
            Set(fixture.Authoring, "_generatedRoot", "../Outside");
            Set(fixture.Profile, "_enabled", false);

            Assert.That(
                OrpheusAudioAuthoringCapture.TryCapture(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile },
                    out _,
                    out _,
                    out var errors),
                Is.False);
            Assert.That(Array.Exists(
                errors,
                error => error.Code == OrpheusAuthoringErrorCode.DisabledValidationProfile),
                Is.True);
            Assert.That(Array.Exists(
                errors,
                error => error.Code == OrpheusAuthoringErrorCode.InvalidGeneratedRoot),
                Is.True);
        }

        [Test]
        public void Capture_RejectsSecondDisabledPendingOwner()
        {
            var fixture = CreatePendingFixture();
            var second = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            Set(second, "_enabled", false);
            Set(second, "_catalog", fixture.Catalog);
            Set(second, "_keyManifest", fixture.Manifest);
            Set(second, "_authoringProfile", fixture.Authoring);
            Set(second, "_authoringEnrollmentGuid", string.Empty);
            AssetDatabase.CreateAsset(
                second,
                Root + AssetPathSeparator + "Second.asset");
            AssetDatabase.SaveAssets();

            Assert.That(
                OrpheusAudioAuthoringCapture.TryCapture(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile, second },
                    out _,
                    out _,
                    out var errors),
                Is.False);
            Assert.That(Array.Exists(
                errors,
                error => error.Code == OrpheusAuthoringErrorCode.AuthoringProfileConflict),
                Is.True);
        }

        [Test]
        public void Compiler_AnalyzeWritesOnlyLibraryProposal()
        {
            var fixture = CreatePendingFixture();
            var before = SnapshotRepository();

            var result = OrpheusAudioAuthoringCompiler.Run(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                BuildTargetGroup.Standalone,
                new[] { fixture.Profile });

            Assert.That(result.Status, Is.EqualTo(OrpheusAudioAuthoringCompileStatus.SucceededChanged));
            Assert.That(result.ReportPath, Does.StartWith("Library/"));
            Assert.That(File.Exists(result.ReportPath), Is.True);
            StringAssert.Contains(
                "\"status\": \"SucceededChanged\"",
                File.ReadAllText(result.ReportPath));
            StringAssert.Contains(
                "\"proposedEnrollment\": {",
                File.ReadAllText(result.ReportPath));
            CollectionAssert.AreEqual(before, SnapshotRepository());
        }

        [Test]
        public void Compiler_RejectedAnalyzeWritesCanonicalErrorReport()
        {
            var fixture = CreatePendingFixture();
            Set(fixture.Profile, "_enabled", false);

            var result = OrpheusAudioAuthoringCompiler.Run(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                BuildTargetGroup.Standalone,
                new[] { fixture.Profile });

            Assert.That(
                result.Status,
                Is.EqualTo(OrpheusAudioAuthoringCompileStatus.Rejected));
            Assert.That(result.ReportPath, Does.StartWith("Library/"));
            Assert.That(File.Exists(result.ReportPath), Is.True);
            var report = File.ReadAllText(result.ReportPath);
            StringAssert.Contains("\"status\": \"Rejected\"", report);
            StringAssert.Contains("\"errorCount\": 1", report);
            StringAssert.Contains(
                "\"code\":3,\"key\":0,\"moduleId\":\"\"",
                report);
        }

        [Test]
        public void Compiler_RolledBackCompileWritesCanonicalFailureReport()
        {
            var fixture = CreatePendingFixture();
            var result = OrpheusAudioAuthoringCompiler.Run(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.CompileAndAcceptEnrollment,
                BuildTargetGroup.Standalone,
                new[] { fixture.Profile },
                new OrpheusAudioAuthoringCommitArtifacts(
                    "{",
                    "{\"task\":6}\n",
                    "{\"stale\":true}\n",
                    "Library/Orpheus/AuthoringAnalysis/stale.json"));

            Assert.That(
                result.Status,
                Is.EqualTo(OrpheusAudioAuthoringCompileStatus.RolledBack),
                OrpheusAudioAuthoringTransaction.LastFailureForTests);
            Assert.That(result.ErrorCount, Is.EqualTo(0));
            Assert.That(result.ReportPath, Does.StartWith("Library/"));
            Assert.That(result.ReportPath, Does.Not.Contain("stale.json"));
            var report = File.ReadAllText(result.ReportPath);
            StringAssert.Contains("\"status\": \"RolledBack\"", report);
            StringAssert.Contains("\"errorCount\": 0", report);
            StringAssert.Contains("\"errors\": []", report);
            StringAssert.Contains("\"action\":\"blocked\"", report);
        }

        [Test]
        public void SharedValidation_IsReadOnlyAcrossManualPendingAndEnrolledStates()
        {
            Assert.That(
                (ushort)OrpheusAudioValidationErrorCode.InvalidAuthoringProfile,
                Is.EqualTo(63));
            Assert.That(
                (ushort)OrpheusAudioValidationErrorCode.StaleGeneratedOwnership,
                Is.EqualTo(64));
            Assert.That(
                (ushort)OrpheusAudioValidationErrorCode
                    .AuthoringEnrollmentIdentityMismatch,
                Is.EqualTo(65));
            Assert.That(
                (ushort)OrpheusAudioValidationErrorCode
                    .LostAuthoringOwnership,
                Is.EqualTo(66));
            Assert.That(
                (ushort)OrpheusAudioValidationErrorCode
                    .GeneratedCatalogMismatch,
                Is.EqualTo(67));
            Assert.That(
                (ushort)OrpheusAudioValidationErrorCode.StaleAuthoringInput,
                Is.EqualTo(68));
            Assert.That(
                (ushort)OrpheusAudioValidationErrorCode
                    .AuthoringOutputFingerprintMismatch,
                Is.EqualTo(69));

            var manual =
                ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            Set(manual, "_enabled", true);
            AssetDatabase.CreateAsset(
                manual,
                Root + AssetPathSeparator + "Manual.asset");
            Assert.That(
                OrpheusAudioAuthoringReadOnlyValidation.Validate(
                    manual,
                    BuildTargetGroup.Standalone,
                    new[] { manual }),
                Is.Empty);

            var fixture = CreatePendingFixture();
            var pending =
                OrpheusAudioAuthoringReadOnlyValidation.Validate(
                    fixture.Profile,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile });
            Assert.That(
                pending.Select(error => error.Code),
                Is.EqualTo(
                    new[]
                    {
                        OrpheusAudioValidationErrorCode
                            .InvalidAuthoringProfile
                    }));

            Set(
                fixture.Profile,
                "_authoringEnrollmentGuid",
                AssetDatabase.AssetPathToGUID(
                    AssetDatabase.GetAssetPath(fixture.Authoring))
                    .ToLowerInvariant());
            var before = SnapshotRepository();
            var enrolled =
                OrpheusAudioAuthoringReadOnlyValidation.Validate(
                    fixture.Profile,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile });
            Assert.That(
                enrolled.Select(error => error.Code),
                Does.Contain(
                    OrpheusAudioValidationErrorCode
                        .LostAuthoringOwnership));
            CollectionAssert.AreEqual(before, SnapshotRepository());

            Assert.That(
                OrpheusAudioAuthoringReports.TryReadClosureFingerprints(
                    "{\"schemaVersion\":1," +
                    "\"inputFingerprint\":\"" +
                    new string('a', 64) + "\"," +
                    "\"outputFingerprint\":\"" +
                    new string('b', 64) + "\"," +
                    "\"manifestSnapshot\":[]," +
                    "\"identityConflicts\":[]," +
                    "\"catalogClosure\":[]," +
                    "\"clipClosure\":[]," +
                    "\"ownership\":[]," +
                    "\"orphans\":[]," +
                    "\"futureDeliveryProjection\":[]}\n",
                    out var inputFingerprint,
                    out var outputFingerprint),
                Is.True);
            Assert.That(inputFingerprint, Is.EqualTo(new string('a', 64)));
            Assert.That(outputFingerprint, Is.EqualTo(new string('b', 64)));
        }

        [Test]
        public void Capture_RejectsManualInvalidModeAndMissingOwnershipStates()
        {
            var fixture = CreatePendingFixture();
            Set(fixture.Profile, "_authoringProfile", null);
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.ManualProfileNotCompilable);

            Set(fixture.Profile, "_authoringProfile", fixture.Authoring);
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Compile,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.EnrollmentIdentityMismatch);

            var guid = AssetDatabase.AssetPathToGUID(
                AssetDatabase.GetAssetPath(fixture.Authoring)).ToLowerInvariant();
            Set(fixture.Profile, "_authoringEnrollmentGuid", guid);
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.LostOwnershipState);

            Set(fixture.Profile, "_authoringEnrollmentGuid", new string('f', 32));
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.EnrollmentIdentityMismatch);
        }

        [Test]
        public void Capture_RejectsEveryUnsafeGeneratedRootShape()
        {
            var fixture = CreatePendingFixture();
            var roots = new[]
            {
                "../Outside",
                "Assets/../Outside",
                "C:" + AssetPathSeparator + "Outside",
                new string(AssetPathSeparator, 2) + "server/share",
                "Assets\\Mixed/Root",
                "Assets/Double//Root",
                "Packages/Outside"
            };
            for (var index = 0; index < roots.Length; index++)
            {
                Set(fixture.Authoring, "_generatedRoot", roots[index]);
                AssertCaptureError(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    new[] { fixture.Profile },
                    OrpheusAuthoringErrorCode.InvalidGeneratedRoot);
            }

            Set(fixture.Authoring, "_generatedRoot", "Assets/OrpheusGenerated/Nested");
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.InvalidGeneratedRoot);
        }

        [Test]
        public void Capture_RejectsCatalogAndImporterPreflightMismatch()
        {
            var fixture = CreatePendingFixture();
            var audioEvent = ScriptableObject.CreateInstance<OrpheusAudioEvent>();
            Set(fixture.Catalog, "_events", new[] { audioEvent });
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.CatalogNotEmptyAtEnrollment);

            Set(fixture.Catalog, "_events", Array.Empty<OrpheusAudioEvent>());
            var importer = (AudioImporter)AssetImporter.GetAtPath(
                AssetDatabase.GetAssetPath(fixture.Clip));
            importer.loadInBackground = true;
            importer.SaveAndReimport();
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.ImporterPolicyMismatch);
            UnityEngine.Object.DestroyImmediate(audioEvent);
        }

        [Test]
        public void Capture_RejectsMalformedAndNonTerminalJournalReadOnly()
        {
            var fixture = CreatePendingFixture();
            var malformed =
                Path.Combine(TransactionRoot, new string('a', 32), "journal.json");
            Directory.CreateDirectory(Path.GetDirectoryName(malformed));
            File.WriteAllText(malformed, "{}");
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.RollbackFailed);

            DeleteLibraryDirectory(TransactionRoot);
            var payload = "{}";
            var transactionId = new string('b', 32);
            var journal = Path.Combine(TransactionRoot, transactionId, "journal.json");
            Directory.CreateDirectory(Path.GetDirectoryName(journal));
            File.WriteAllText(
                journal,
                "{\"schemaVersion\":1,\"transactionId\":\"" + transactionId +
                "\",\"phase\":\"Prepared\",\"payload\":\"{}\",\"payloadSha256\":\"" +
                Sha256(payload) + "\"}");
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.TransactionRecoveryRequired);
        }

        [Test]
        public void Capture_AllowsOnlyNamedValidatingJournalForInFlightRecapture()
        {
            var fixture = CreatePendingFixture();
            var transactionId = new string('c', 32);
            var payload = "{}";
            var journal =
                Path.Combine(TransactionRoot, transactionId, "journal.json");
            Directory.CreateDirectory(Path.GetDirectoryName(journal));
            File.WriteAllText(
                journal,
                "{\"schemaVersion\":1,\"transactionId\":\"" +
                transactionId +
                "\",\"phase\":\"Validating\",\"payload\":\"{}\"," +
                "\"payloadSha256\":\"" + Sha256(payload) + "\"}");

            Assert.That(
                OrpheusAudioAuthoringCapture.TryCaptureDetailed(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile },
                    transactionId,
                    out _,
                    out _,
                    out _,
                    out var errors),
                Is.True,
                Format(errors));

            Assert.That(
                OrpheusAudioAuthoringCapture.TryCaptureDetailed(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile },
                    new string('d', 32),
                    out _,
                    out _,
                    out _,
                    out errors),
                Is.False);
            Assert.That(
                errors.Select(error => error.Code),
                Does.Contain(OrpheusAuthoringErrorCode.RollbackFailed));
        }

        [Test]
        public void Capture_RejectsUnsavedAndSubassetSelectedProfile()
        {
            var fixture = CreatePendingFixture();
            var unsaved = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            Set(unsaved, "_enabled", true);
            Set(unsaved, "_catalog", fixture.Catalog);
            Set(unsaved, "_keyManifest", fixture.Manifest);
            Set(unsaved, "_authoringProfile", fixture.Authoring);
            AssertCaptureError(
                unsaved,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { unsaved },
                OrpheusAuthoringErrorCode.InvalidValidationProfile);

            AssetDatabase.AddObjectToAsset(unsaved, fixture.Authoring);
            AssetDatabase.SaveAssets();
            AssertCaptureError(
                unsaved,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { unsaved },
                OrpheusAuthoringErrorCode.InvalidValidationProfile);
        }

        [Test]
        public void Capture_RejectsForeignPendingTypedKeysAndGeneratedReports()
        {
            var fixture = CreatePendingFixture();
            var foreignManifest = OrpheusAudioEditorContractTests.CreateManifest(
                OrpheusAudioEditorContractTests.CreateEntry(
                    200,
                    "Foreign",
                    OrpheusAudioKeyStatus.Active));
            Assert.That(
                OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    foreignManifest,
                    out var foreignSource,
                    out _),
                Is.True);
            OrpheusAudioTypedKeyProjection.WriteExpectedNoRefresh(
                OrpheusAudioTypedKeyProjection.ExpectedAssembly,
                foreignSource);
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.TypedKeyOwnershipConflict);

            Directory.Delete(TypedKeyRoot, true);
            var closurePath =
                GeneratedRoot + AssetPathSeparator +
                "OrpheusAuthoringClosure.json";
            File.WriteAllText(closurePath, "{}");
            AssetDatabase.ImportAsset(
                closurePath,
                ImportAssetOptions.ForceSynchronousImport);
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.OutputPathCollision);
        }

        [Test]
        public void Capture_RejectsForeignExpectedEventType()
        {
            var fixture = CreatePendingFixture();
            var entries = fixture.Manifest.CaptureEntries();
            string symbol = null;
            for (var index = 0; index < entries.Length; index++)
            {
                if (entries[index].Status == OrpheusAudioKeyStatus.Active)
                {
                    symbol = entries[index].Symbol;
                    break;
                }
            }

            var eventDirectory =
                GeneratedRoot + AssetPathSeparator + "Events";
            Directory.CreateDirectory(eventDirectory);
            var foreign = ScriptableObject.CreateInstance<OrpheusAudioCatalog>();
            AssetDatabase.CreateAsset(
                foreign,
                eventDirectory + AssetPathSeparator +
                "AE_" + symbol + ".asset");
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.OutputPathCollision);
        }

        [Test]
        public void Capture_RejectsReparseAncestor()
        {
            var fixture = CreatePendingFixture();
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var target = Path.Combine(
                projectRoot,
                "Library",
                "Orpheus",
                "Task4ReparseTarget");
            var junction = Path.Combine(projectRoot, Root, "Junction");
            Directory.CreateDirectory(target);
            var startInfo = new System.Diagnostics.ProcessStartInfo(
                "cmd.exe",
                AssetPathSeparator + "d " +
                AssetPathSeparator + "c mklink " +
                AssetPathSeparator + "J \"" +
                junction + "\" \"" + target + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using (var process = System.Diagnostics.Process.Start(startInfo))
            {
                process.WaitForExit();
                Assert.That(process.ExitCode, Is.EqualTo(0));
            }

            var junctionManifest =
                OrpheusAudioEditorContractTests.CreateManifest(
                    OrpheusAudioEditorContractTests.CreateEntry(
                        100,
                        "Cue",
                        OrpheusAudioKeyStatus.Active));
            AssetDatabase.CreateAsset(
                junctionManifest,
                Root + AssetPathSeparator + "Junction/Manifest.asset");
            Set(fixture.Authoring, "_keyManifest", junctionManifest);
            Set(fixture.Profile, "_keyManifest", junctionManifest);
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.MissingManifest);

            Set(fixture.Authoring, "_keyManifest", fixture.Manifest);
            Set(fixture.Profile, "_keyManifest", fixture.Manifest);
            Set(
                fixture.Authoring,
                "_generatedRoot",
                Root + AssetPathSeparator + "Junction/Generated");
            AssertCaptureError(
                fixture.Profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                new[] { fixture.Profile },
                OrpheusAuthoringErrorCode.InvalidGeneratedRoot);
        }

        private static Fixture CreatePendingFixture()
        {
            var clipPath = Root + AssetPathSeparator + "Cue.wav";
            File.WriteAllBytes(clipPath, CreateWaveBytes());
            AssetDatabase.ImportAsset(clipPath, ImportAssetOptions.ForceSynchronousImport);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            ConfigureImporter(clip);

            var manifest = OrpheusAudioEditorContractTests.CreateManifest(
                OrpheusAudioEditorContractTests.CreateEntry(
                    100,
                    "Cue",
                    OrpheusAudioKeyStatus.Active));
            AssetDatabase.CreateAsset(
                manifest,
                Root + AssetPathSeparator + "Manifest.asset");

            var entries = manifest.CaptureEntries();
            var eventRecipes =
                new System.Collections.Generic.List<OrpheusAudioModuleEventRecipe>();
            for (var index = 0; index < entries.Length; index++)
            {
                if (entries[index].Status == OrpheusAudioKeyStatus.Active)
                {
                    eventRecipes.Add(CreateEventRecipe(entries[index].Symbol, clip));
                }
            }

            var recipe = ScriptableObject.CreateInstance<OrpheusAudioModuleRecipe>();
            Set(recipe, "_moduleId", "ui");
            Set(recipe, "_events", eventRecipes.ToArray());
            AssetDatabase.CreateAsset(
                recipe,
                Root + AssetPathSeparator + "Recipe.asset");

            Directory.CreateDirectory(GeneratedRoot);
            AssetDatabase.Refresh();
            var catalog = ScriptableObject.CreateInstance<OrpheusAudioCatalog>();
            AssetDatabase.CreateAsset(
                catalog,
                GeneratedRoot + AssetPathSeparator +
                "OrpheusAudioCatalog.asset");

            var authoring = ScriptableObject.CreateInstance<OrpheusAudioAuthoringProfile>();
            Set(authoring, "_keyManifest", manifest);
            Set(authoring, "_moduleRecipes", new[] { recipe });
            Set(authoring, "_catalog", catalog);
            Set(authoring, "_generatedRoot", GeneratedRoot);
            AssetDatabase.CreateAsset(
                authoring,
                Root + AssetPathSeparator + "Authoring.asset");

            var profile = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            Set(profile, "_enabled", true);
            Set(profile, "_catalog", catalog);
            Set(profile, "_keyManifest", manifest);
            Set(profile, "_authoringProfile", authoring);
            Set(profile, "_authoringEnrollmentGuid", string.Empty);
            AssetDatabase.CreateAsset(
                profile,
                Root + AssetPathSeparator + "Validation.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return new Fixture(profile, authoring, manifest, catalog, clip);
        }

        private void IsolateTypedKeyProjection()
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(_typedKeyBackupRoot));
            if (Directory.Exists(TypedKeyRoot))
            {
                Directory.Move(TypedKeyRoot, _typedKeyBackupRoot);
            }

            var metaPath = TypedKeyRoot + ".meta";
            if (File.Exists(metaPath))
            {
                File.Move(metaPath, _typedKeyBackupRoot + ".meta");
            }
        }

        private void RestoreTypedKeyProjection()
        {
            AssetDatabase.DeleteAsset(TypedKeyRoot);
            if (Directory.Exists(TypedKeyRoot))
            {
                Directory.Delete(TypedKeyRoot, true);
            }

            var metaPath = TypedKeyRoot + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }

            if (!string.IsNullOrEmpty(_typedKeyBackupRoot) &&
                Directory.Exists(_typedKeyBackupRoot))
            {
                Directory.Move(_typedKeyBackupRoot, TypedKeyRoot);
            }

            var backupMetaPath = _typedKeyBackupRoot + ".meta";
            if (!string.IsNullOrEmpty(_typedKeyBackupRoot) &&
                File.Exists(backupMetaPath))
            {
                File.Move(backupMetaPath, metaPath);
            }

            if (!string.IsNullOrEmpty(_typedKeyBackupRoot))
            {
                var backupDirectory =
                    Path.GetDirectoryName(_typedKeyBackupRoot);
                if (Directory.Exists(_typedKeyBackupRoot))
                {
                    Directory.Delete(_typedKeyBackupRoot, true);
                }

                if (File.Exists(backupMetaPath))
                {
                    File.Delete(backupMetaPath);
                }

                if (Directory.Exists(backupDirectory) &&
                    !Directory.EnumerateFileSystemEntries(
                        backupDirectory).Any())
                {
                    Directory.Delete(backupDirectory, false);
                }
            }
        }

        private static OrpheusAudioModuleEventRecipe CreateEventRecipe(
            string symbol,
            AudioClip clip)
        {
            object boxed = default(OrpheusAudioModuleEventRecipe);
            Set(boxed, "_symbol", symbol);
            Set(boxed, "_playbackKind", OrpheusPlaybackKind.OneShot2D);
            Set(boxed, "_category", OrpheusCategory.SfxUi);
            Set(boxed, "_loadPolicy", OrpheusLoadPolicy.BootstrapTransient);
            Set(boxed, "_clips", new[] { clip });
            Set(boxed, "_volumeMin", 1f);
            Set(boxed, "_volumeMax", 1f);
            Set(boxed, "_pitchMin", 1f);
            Set(boxed, "_pitchMax", 1f);
            Set(boxed, "_priority", (byte)128);
            Set(boxed, "_polyphonyCap", (byte)1);
            Set(boxed, "_rolloffMode", OrpheusRolloffMode.Logarithmic);
            return (OrpheusAudioModuleEventRecipe)boxed;
        }

        private static void ConfigureImporter(AudioClip clip)
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
            importer.forceToMono = false;
            importer.SaveAndReimport();
        }

        private static byte[] CreateWaveBytes()
        {
            const int samples = 32;
            using (var stream = new MemoryStream(44 + samples * 2))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(36 + samples * 2);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(44100);
                writer.Write(88200);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                writer.Write(samples * 2);
                for (var index = 0; index < samples; index++)
                {
                    writer.Write((short)0);
                }

                return stream.ToArray();
            }
        }

        private static string[] SnapshotRepository()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var files = Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories);
            var values = new System.Collections.Generic.List<string>();
            var directories =
                Directory.GetDirectories(projectRoot, "*", SearchOption.AllDirectories);
            for (var index = 0; index < directories.Length; index++)
            {
                var relative =
                    directories[index].Substring(projectRoot.Length + 1).Replace('\\', '/');
                if (IsIgnoredRepositoryPath(relative + "/"))
                {
                    continue;
                }

                values.Add("D|" + relative);
            }

            for (var index = 0; index < files.Length; index++)
            {
                var relative = files[index].Substring(projectRoot.Length + 1).Replace('\\', '/');
                if (IsIgnoredRepositoryPath(relative))
                {
                    continue;
                }

                try
                {
                    values.Add(
                        "F|" + relative + "|" +
                        Convert.ToBase64String(File.ReadAllBytes(files[index])));
                }
                catch (IOException)
                {
                    // Runtime-owned files are outside the repository evidence surface.
                }
            }

            values.Sort(StringComparer.Ordinal);
            return values.ToArray();
        }

        private static bool IsIgnoredRepositoryPath(string relative)
        {
            return relative.StartsWith("Library/", StringComparison.OrdinalIgnoreCase) ||
                   relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                   relative.StartsWith("Logs/", StringComparison.OrdinalIgnoreCase) ||
                   relative.StartsWith("Temp/", StringComparison.OrdinalIgnoreCase) ||
                   relative.StartsWith("TestResults/", StringComparison.OrdinalIgnoreCase);
        }

        private static string Format(OrpheusAuthoringCompilationError[] errors)
        {
            if (errors == null || errors.Length == 0)
            {
                return string.Empty;
            }

            var text = new System.Text.StringBuilder();
            for (var index = 0; index < errors.Length; index++)
            {
                text.Append(errors[index].Code);
                text.Append(':');
                text.Append(errors[index].AssetPath);
                text.Append(':');
                text.Append(errors[index].Detail);
                text.Append('|');
            }

            return text.ToString();
        }

        private static void AssertCaptureError(
            OrpheusAudioValidationProfile profile,
            OrpheusAudioAuthoringCompileMode mode,
            OrpheusAudioValidationProfile[] profiles,
            OrpheusAuthoringErrorCode expected)
        {
            Assert.That(
                OrpheusAudioAuthoringCapture.TryCapture(
                    profile,
                    mode,
                    BuildTargetGroup.Standalone,
                    profiles,
                    out _,
                    out _,
                    out var errors),
                Is.False);
            Assert.That(
                Array.Exists(errors, error => error.Code == expected),
                Is.True,
                Format(errors));
        }

        private static void DeleteLibraryDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private static void DeleteFixtureJunction()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var junction = Path.Combine(projectRoot, Root, "Junction");
            if (Directory.Exists(junction) &&
                (File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(junction);
            }
        }

        private static string Sha256(string value)
        {
            using (var algorithm = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
                var text = new System.Text.StringBuilder(64);
                for (var index = 0; index < bytes.Length; index++)
                {
                    text.Append(bytes[index].ToString("x2"));
                }

                return text.ToString();
            }
        }

        private static void Set(object target, string field, object value)
        {
            OrpheusAudioEditorContractTests.SetField(target, field, value);
        }

        private readonly struct Fixture
        {
            internal Fixture(
                OrpheusAudioValidationProfile profile,
                OrpheusAudioAuthoringProfile authoring,
                OrpheusAudioKeyManifest manifest,
                OrpheusAudioCatalog catalog,
                AudioClip clip)
            {
                Profile = profile;
                Authoring = authoring;
                Manifest = manifest;
                Catalog = catalog;
                Clip = clip;
            }

            internal OrpheusAudioValidationProfile Profile { get; }
            internal OrpheusAudioAuthoringProfile Authoring { get; }
            internal OrpheusAudioKeyManifest Manifest { get; }
            internal OrpheusAudioCatalog Catalog { get; }
            internal AudioClip Clip { get; }
        }
    }
}
