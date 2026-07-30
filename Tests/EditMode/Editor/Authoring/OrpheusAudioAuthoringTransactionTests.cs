using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioAuthoringTransactionTests
    {
        private const char AssetPathSeparator = (char)47;
        private const string ReloadJournalPhaseKey =
            "Orpheus.Task5.ReloadJournalPhase";
        private const string ReloadOriginalSourceKey =
            "Orpheus.Task5.ReloadOriginalSource";
        private const string ReloadAssetRootKey =
            "Orpheus.Task5.ReloadAssetRoot";
        private const string ReloadExecutionPathKey =
            "Orpheus.Task5.ReloadExecutionPath";
        private const string ReloadJournalPathKey =
            "Orpheus.Task5.ReloadJournalPath";
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "OrpheusTask5",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Test]
        public void Journal_IsAtomicHashedAndStrictlyReadable()
        {
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(_root, id, "journal.json");

            OrpheusAudioAuthoringTransaction.WriteJournalForTests(
                path,
                id,
                OrpheusAudioAuthoringTransactionPhase.Prepared,
                "{}");

            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.Exists(path + ".tmp"), Is.False);
            Assert.That(
                OrpheusAudioAuthoringTransaction.TryReadJournalForTests(
                    path,
                    out var phase,
                    out var payload),
                Is.True);
            Assert.That(phase, Is.EqualTo(OrpheusAudioAuthoringTransactionPhase.Prepared));
            Assert.That(payload, Is.EqualTo("{}"));
            Assert.That(
                File.ReadAllText(path),
                Is.EqualTo(JournalJson(id, "Prepared", "{}")));
        }

        [Test]
        public void Journal_RejectsTornHashAndForeignFolder()
        {
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(_root, id, "journal.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(
                path,
                "{\"schemaVersion\":1,\"transactionId\":\"" + id +
                "\",\"phase\":\"Prepared\",\"payload\":\"{}\",\"payloadSha256\":\"" +
                new string('0', 64) + "\"}");

            Assert.That(
                OrpheusAudioAuthoringTransaction.TryReadJournalForTests(
                    path,
                    out _,
                    out _),
                Is.False);
        }

        [TestCase("1")]
        [TestCase("Prepared\",\"extra\":true,\"ignored\":\"")]
        public void Journal_RejectsNumericPhaseAndExtraEnvelopeFields(
            string phase)
        {
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(_root, id, "journal.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JournalJson(id, phase, "{}"));

            Assert.That(
                OrpheusAudioAuthoringTransaction.TryReadJournalForTests(
                    path,
                    out _,
                    out _),
                Is.False);
        }

        [Test]
        public void Journal_RejectsReorderedEnvelope()
        {
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(_root, id, "journal.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(
                path,
                "{\"transactionId\":\"" + id +
                "\",\"schemaVersion\":1,\"phase\":\"Prepared\"," +
                "\"payload\":\"{}\",\"payloadSha256\":\"" +
                Sha256("{}") + "\"}\n");

            Assert.That(
                OrpheusAudioAuthoringTransaction.TryReadJournalForTests(
                    path,
                    out _,
                    out _),
                Is.False);
        }

        [TestCase("Assets/Generated/../../Outside.asset")]
        [TestCase("Assets/Generated/./Event.asset")]
        [TestCase("C\u003a\u002fOutside.asset")]
        public void Recovery_InvalidPayloadPersistsRollbackFailed(
            string snapshotPath)
        {
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(_root, id, "journal.json");
            var payload = SnapshotPayload(snapshotPath);
            OrpheusAudioAuthoringTransaction.WriteJournalForTests(
                path,
                id,
                OrpheusAudioAuthoringTransactionPhase.Prepared,
                payload);

            Assert.That(
                OrpheusAudioAuthoringTransaction
                    .TryRecoverInterruptedForTests(_root),
                Is.False);
            Assert.That(
                OrpheusAudioAuthoringTransaction.TryReadJournalForTests(
                    path,
                    out var phase,
                    out _),
                Is.True);
            Assert.That(
                phase,
                Is.EqualTo(
                    OrpheusAudioAuthoringTransactionPhase.RollbackFailed));
        }

        [Test]
        public void Recovery_InvalidEnvelopePersistsSeparateBlocker()
        {
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(_root, id, "journal.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(
                path,
                "{\"schemaVersion\":1,\"transactionId\":\"" + id +
                "\",\"phase\":\"Prepared\",\"payload\":\"{}\"," +
                "\"payloadSha256\":\"" + new string('0', 64) + "\"}\n");

            Assert.That(
                OrpheusAudioAuthoringTransaction
                    .TryRecoverInterruptedForTests(_root),
                Is.False);
            var journals = Directory.GetFiles(
                _root,
                "journal.json",
                SearchOption.AllDirectories);
            Assert.That(journals, Has.Length.EqualTo(2));
            var hasBlocker = false;
            for (var index = 0; index < journals.Length; index++)
            {
                if (OrpheusAudioAuthoringTransaction.TryReadJournalForTests(
                        journals[index],
                        out var phase,
                        out _) &&
                    phase ==
                    OrpheusAudioAuthoringTransactionPhase.RollbackFailed)
                {
                    hasBlocker = true;
                }
            }

            Assert.That(hasBlocker, Is.True);
        }

        [Test]
        public void Recovery_RejectsReparsePointTraversal()
        {
            var relativeRoot =
                "Library/Orpheus/RecoveryPathTests/" +
                Guid.NewGuid().ToString("N");
            var absoluteRoot = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                relativeRoot.Replace(
                    AssetPathSeparator,
                    Path.DirectorySeparatorChar));
            var target = _root + "-outside";
            var link = Path.Combine(absoluteRoot, "Link");
            Directory.CreateDirectory(absoluteRoot);
            Directory.CreateDirectory(target);
            var process = Process.Start(
                new ProcessStartInfo(
                    "cmd.exe",
                    AssetPathSeparator + "c mklink " +
                    AssetPathSeparator + "J \"" +
                    link + "\" \"" + target + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            process.WaitForExit();
            Assert.That(process.ExitCode, Is.Zero);
            try
            {
                var id = Guid.NewGuid().ToString("N");
                var path = Path.Combine(_root, id, "journal.json");
                OrpheusAudioAuthoringTransaction.WriteJournalForTests(
                    path,
                    id,
                    OrpheusAudioAuthoringTransactionPhase.Prepared,
                    SnapshotPayload(
                        relativeRoot + AssetPathSeparator +
                        "Link/escape.asset"));

                Assert.That(
                    OrpheusAudioAuthoringTransaction
                        .TryRecoverInterruptedForTests(_root),
                    Is.False);
                Assert.That(
                    OrpheusAudioAuthoringTransaction.TryReadJournalForTests(
                        path,
                        out var phase,
                        out _),
                    Is.True);
                Assert.That(
                    phase,
                    Is.EqualTo(
                        OrpheusAudioAuthoringTransactionPhase.RollbackFailed));
            }
            finally
            {
                if (Directory.Exists(link))
                {
                    Directory.Delete(link);
                }

                if (Directory.Exists(absoluteRoot))
                {
                    Directory.Delete(absoluteRoot, true);
                }

                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }
            }
        }

        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.Prepared)]
        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.Writing)]
        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.Importing)]
        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.Validating)]
        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.RollingBack)]
        public void Recovery_ValidNonTerminalRestoresAssetsAndLibraryIdentity(
            byte interruptedPhaseValue)
        {
            var interruptedPhase =
                (OrpheusAudioAuthoringTransactionPhase)interruptedPhaseValue;
            var assetRoot =
                "Assets/OrpheusAuthoringRecoveryTests/" +
                Guid.NewGuid().ToString("N");
            var assetPath =
                assetRoot + AssetPathSeparator + "Catalog.asset";
            var libraryPath =
                "Library/Orpheus/RecoveryFixture/" +
                Guid.NewGuid().ToString("N") + ".txt";
            Directory.CreateDirectory(assetRoot);
            Directory.CreateDirectory(
                Path.GetDirectoryName(libraryPath));
            AssetDatabase.Refresh();
            try
            {
                var catalog =
                    ScriptableObject.CreateInstance<OrpheusAudioCatalog>();
                AssetDatabase.CreateAsset(catalog, assetPath);
                AssetDatabase.SaveAssets();
                File.WriteAllText(libraryPath, "before");
                var assetBytes = File.ReadAllBytes(assetPath);
                var metaBytes = File.ReadAllBytes(assetPath + ".meta");
                var guid =
                    AssetDatabase.AssetPathToGUID(assetPath)
                        .ToLowerInvariant();
                var payload =
                    OrpheusAudioAuthoringTransaction
                        .CaptureRecoveryPayloadForTests(
                            assetPath,
                            assetPath + ".meta",
                            libraryPath);
                var id = Guid.NewGuid().ToString("N");
                var journal = Path.Combine(
                    _root,
                    id,
                    "journal.json");
                OrpheusAudioAuthoringTransaction.WriteJournalForTests(
                    journal,
                    id,
                    interruptedPhase,
                    payload);

                AssetDatabase.DeleteAsset(assetPath);
                File.WriteAllText(libraryPath, "after");

                Assert.That(
                    OrpheusAudioAuthoringTransaction
                        .TryRecoverInterruptedForTests(_root),
                    Is.True);
                CollectionAssert.AreEqual(
                    assetBytes,
                    File.ReadAllBytes(assetPath));
                CollectionAssert.AreEqual(
                    metaBytes,
                    File.ReadAllBytes(assetPath + ".meta"));
                Assert.That(
                    File.ReadAllText(libraryPath),
                    Is.EqualTo("before"));
                Assert.That(
                    AssetDatabase.AssetPathToGUID(assetPath)
                        .ToLowerInvariant(),
                    Is.EqualTo(guid));
                Assert.That(
                    AssetDatabase.LoadMainAssetAtPath(assetPath),
                    Is.TypeOf<OrpheusAudioCatalog>());
                Assert.That(
                    Directory.Exists(Path.GetDirectoryName(journal)),
                    Is.False);
            }
            finally
            {
                AssetDatabase.DeleteAsset(
                    "Assets/OrpheusAuthoringRecoveryTests");
                if (File.Exists(libraryPath))
                {
                    File.Delete(libraryPath);
                }

                AssetDatabase.Refresh();
            }
        }

        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.Prepared)]
        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.Writing)]
        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.Importing)]
        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.Validating)]
        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.RollingBack)]
        public void ActualWriterTermination_NextCompilerInvocationRecovers(
            byte interruptedPhaseValue)
        {
            var interruptedPhase =
                (OrpheusAudioAuthoringTransactionPhase)interruptedPhaseValue;
            var existingJournals =
                new HashSet<string>(
                    GetTransactionJournals(),
                    StringComparer.OrdinalIgnoreCase);
            AssertExistingJournalsAreTerminal(existingJournals);
            var fixture = new IntegrationFixture();
            try
            {
                var injection =
                    new DurablePhaseTerminationInjection(interruptedPhase);
                Assert.Throws<
                    OrpheusAudioAuthoringSimulatedProcessTerminationException>(
                    () => OrpheusAudioAuthoringTransaction.Execute(
                        fixture.Profile,
                        fixture.Plan,
                        fixture.Artifacts,
                        BuildTargetGroup.Standalone,
                        new[] { fixture.Profile },
                        injection));
                Assert.That(injection.WasTriggered, Is.True);

                var interruptedJournal =
                    GetSingleNewJournal(existingJournals);
                Assert.That(
                    OrpheusAudioAuthoringTransaction.TryReadJournalForTests(
                        interruptedJournal,
                        out var actualPhase,
                        out _),
                    Is.True);
                Assert.That(actualPhase, Is.EqualTo(interruptedPhase));

                var next = OrpheusAudioAuthoringCompiler.Run(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Compile,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile });

                Assert.That(
                    next.Status,
                    Is.Not.EqualTo(
                        OrpheusAudioAuthoringCompileStatus.RollbackFailed),
                    interruptedPhase + ":" +
                    OrpheusAudioAuthoringTransaction.LastFailureForTests);
                Assert.That(
                    Directory.Exists(
                        Path.GetDirectoryName(interruptedJournal)),
                    Is.False);
                fixture.AssertBaselineRestored();
            }
            finally
            {
                OrpheusAudioAuthoringTransaction.TryRecoverInterrupted();
                fixture.Dispose();
                DeleteNewTransactionDirectories(existingJournals);
            }
        }

        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.Committed)]
        [TestCase((byte)OrpheusAudioAuthoringTransactionPhase.RolledBack)]
        public void Recovery_SuccessfulTerminalPhasesArePruned(
            byte terminalPhaseValue)
        {
            var phase =
                (OrpheusAudioAuthoringTransactionPhase)terminalPhaseValue;
            var id = Guid.NewGuid().ToString("N");
            var journal = Path.Combine(_root, id, "journal.json");
            OrpheusAudioAuthoringTransaction.WriteJournalForTests(
                journal,
                id,
                phase,
                "[]");

            Assert.That(
                OrpheusAudioAuthoringTransaction
                    .TryRecoverInterruptedForTests(_root),
                Is.True);
            Assert.That(
                Directory.Exists(Path.GetDirectoryName(journal)),
                Is.False);
        }

        [Test]
        public void Recovery_TerminalCleanupPreservesForeignSiblingAndBlocks()
        {
            var id = Guid.NewGuid().ToString("N");
            var directory = Path.Combine(_root, id);
            var journal = Path.Combine(directory, "journal.json");
            var foreign = Path.Combine(directory, "foreign.bin");
            OrpheusAudioAuthoringTransaction.WriteJournalForTests(
                journal,
                id,
                OrpheusAudioAuthoringTransactionPhase.Committed,
                "[]");
            File.WriteAllText(foreign, "foreign");

            Assert.That(
                OrpheusAudioAuthoringTransaction
                    .TryRecoverInterruptedForTests(_root),
                Is.False);
            Assert.That(
                OrpheusAudioAuthoringTransaction.LastFailureForTests,
                Is.EqualTo("RecoveryTerminalCleanup:" + journal));
            Assert.That(File.Exists(journal), Is.True);
            Assert.That(File.ReadAllText(foreign), Is.EqualTo("foreign"));
        }

        [TestCase("top-extra")]
        [TestCase("nested-extra")]
        [TestCase("absent-fields")]
        [TestCase("base64")]
        [TestCase("sha")]
        [TestCase("guid-type")]
        [TestCase("directory-fields")]
        [TestCase("json-type")]
        public void Recovery_StrictPayloadRejectsMalformedSnapshots(
            string malformed)
        {
            var path = "Library/Orpheus/StrictPayload/file.bin";
            string payload;
            switch (malformed)
            {
                case "top-extra":
                    payload =
                        SnapshotPayload(path)
                            .Replace(
                                "{\"schemaVersion\":1,",
                                "{\"schemaVersion\":1,\"extra\":0,");
                    break;
                case "nested-extra":
                    payload =
                        SnapshotPayload(path)
                            .Replace(
                                "\"mainType\":\"\"}",
                                "\"mainType\":\"\",\"extra\":0}");
                    break;
                case "absent-fields":
                    payload =
                        SnapshotPayload(path)
                            .Replace(
                                "\"sha256\":\"\"",
                                "\"sha256\":\"" +
                                new string('0', 64) + "\"");
                    break;
                case "base64":
                    payload = ExistingSnapshotPayload(
                        path,
                        "***",
                        new string('0', 64),
                        string.Empty,
                        string.Empty);
                    break;
                case "sha":
                    payload = ExistingSnapshotPayload(
                        path,
                        Convert.ToBase64String(new byte[] { 1 }),
                        new string('0', 64),
                        string.Empty,
                        string.Empty);
                    break;
                case "guid-type":
                    payload = ExistingSnapshotPayload(
                        "Assets/StrictPayload.asset",
                        Convert.ToBase64String(new byte[] { 1 }),
                        Sha256Bytes(new byte[] { 1 }),
                        new string('f', 32),
                        string.Empty);
                    break;
                case "json-type":
                    payload =
                        SnapshotPayload(path)
                            .Replace(
                                "\"existed\":false",
                                "\"existed\":0");
                    break;
                default:
                    payload =
                        SnapshotPayload(path)
                            .Replace(
                                "\"kind\":\"file\"",
                                "\"kind\":\"directory\"")
                            .Replace(
                                "\"bytesBase64\":\"\"",
                                "\"bytesBase64\":\"AQ==\"");
                    break;
            }

            var id = Guid.NewGuid().ToString("N");
            var journal = Path.Combine(_root, id, "journal.json");
            OrpheusAudioAuthoringTransaction.WriteJournalForTests(
                journal,
                id,
                OrpheusAudioAuthoringTransactionPhase.Prepared,
                payload);
            Assert.That(
                OrpheusAudioAuthoringTransaction
                    .TryRecoverInterruptedForTests(_root),
                Is.False);
            Assert.That(
                OrpheusAudioAuthoringTransaction.TryReadJournalForTests(
                    journal,
                    out var phase,
                    out _),
                Is.True);
            Assert.That(
                phase,
                Is.EqualTo(
                    OrpheusAudioAuthoringTransactionPhase.RollbackFailed));
        }

        [Test]
        public void FileFixture_FailureRollsBackBytesDirectoriesAndMeta()
        {
            var asset = Path.Combine(_root, "Generated", "Event.asset");
            var meta = asset + ".meta";
            Directory.CreateDirectory(Path.GetDirectoryName(asset));
            File.WriteAllText(asset, "before");
            File.WriteAllText(meta, "guid: before");

            var result = OrpheusAudioAuthoringTransaction.ExecuteFileFixtureForTests(
                _root,
                new[] { asset, meta },
                () =>
                {
                    File.WriteAllText(asset, "after");
                    File.Delete(meta);
                    Directory.CreateDirectory(Path.Combine(_root, "Created"));
                    File.WriteAllText(Path.Combine(_root, "Created", "new.txt"), "new");
                    throw new InvalidOperationException("inject");
                },
                new[] { Path.Combine(_root, "Created", "new.txt") });

            Assert.That(result, Is.EqualTo(OrpheusAudioAuthoringCompileStatus.RolledBack));
            Assert.That(File.ReadAllText(asset), Is.EqualTo("before"));
            Assert.That(File.ReadAllText(meta), Is.EqualTo("guid: before"));
            Assert.That(Directory.Exists(Path.Combine(_root, "Created")), Is.False);
        }

        [Test]
        public void FileFixture_RepeatedRecoveryBoundsTerminalRetention()
        {
            for (var index = 0; index < 8; index++)
            {
                Assert.That(
                    OrpheusAudioAuthoringTransaction
                        .TryRecoverInterruptedForTests(_root),
                    Is.True);
                var result =
                    OrpheusAudioAuthoringTransaction.ExecuteFileFixtureForTests(
                        _root,
                        Array.Empty<string>(),
                        () => { },
                        Array.Empty<string>());

                Assert.That(
                    result,
                    Is.EqualTo(
                        OrpheusAudioAuthoringCompileStatus.SucceededChanged));
                Assert.That(
                    Directory.GetDirectories(_root),
                    Has.Length.EqualTo(1),
                    "iteration=" + index);
            }

            Assert.That(
                OrpheusAudioAuthoringTransaction
                    .TryRecoverInterruptedForTests(_root),
                Is.True);
            Assert.That(Directory.GetFileSystemEntries(_root), Is.Empty);
        }

        [Test]
        public void FileFixture_RollbackProofFailurePersistsRollbackFailed()
        {
            var result = OrpheusAudioAuthoringTransaction.ExecuteFileFixtureForTests(
                _root,
                Array.Empty<string>(),
                () => throw new InvalidOperationException("inject"),
                Array.Empty<string>(),
                failRollbackProof: true);

            Assert.That(result, Is.EqualTo(OrpheusAudioAuthoringCompileStatus.RollbackFailed));
            Assert.That(
                Directory.GetFiles(_root, "journal.json", SearchOption.AllDirectories),
                Has.Length.EqualTo(1));
            Assert.That(
                File.ReadAllText(
                    Directory.GetFiles(
                        _root,
                        "journal.json",
                        SearchOption.AllDirectories)[0]),
                Does.Contain("\"phase\":\"RollbackFailed\""));
        }

        [Test]
        public void RollbackFailedJournal_BlocksNextCompilerInvocation()
        {
            var fixture = new IntegrationFixture();
            var id = Guid.NewGuid().ToString("N");
            var directory = Path.Combine(
                "Library",
                "Orpheus",
                "AuthoringTransactions",
                id);
            var journal = Path.Combine(directory, "journal.json");
            try
            {
                OrpheusAudioAuthoringTransaction.WriteJournalForTests(
                    journal,
                    id,
                    OrpheusAudioAuthoringTransactionPhase.RollbackFailed,
                    "[]");
                var blockerBytes = File.ReadAllBytes(journal);
                fixture.AssertFreshTargetsAbsent();

                var result = OrpheusAudioAuthoringCompiler.Run(
                    fixture.Profile,
                    OrpheusAudioAuthoringCompileMode.Compile,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile });

                Assert.That(
                    result.Status,
                    Is.EqualTo(
                        OrpheusAudioAuthoringCompileStatus.RollbackFailed));
                Assert.That(result.ErrorCount, Is.GreaterThan(0));
                Assert.That(File.Exists(journal), Is.True);
                CollectionAssert.AreEqual(
                    blockerBytes,
                    File.ReadAllBytes(journal));
                fixture.AssertFreshTargetsAbsent();
            }
            finally
            {
                fixture.Dispose();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void AssetsIntegration_WritesEventCatalogEnrollmentAndTerminalJournal()
        {
            var fixture = new IntegrationFixture();
            try
            {
                fixture.AssertFreshTargetsAbsent();
                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    fixture.Artifacts,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile });

                Assert.That(
                    result,
                    Is.EqualTo(OrpheusAudioAuthoringCompileStatus.SucceededChanged),
                    OrpheusAudioAuthoringTransaction.LastFailureForTests);
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                        fixture.Event.AssetPath),
                    Is.Not.Null);
                var serializedCatalog =
                    new SerializedObject(fixture.Catalog);
                Assert.That(
                    serializedCatalog.FindProperty("_events").arraySize,
                    Is.EqualTo(1));
                Assert.That(
                    fixture.Profile.AuthoringEnrollmentGuid,
                    Is.Not.Empty);
                Assert.That(
                    File.ReadAllText(
                        fixture.AssetRoot + AssetPathSeparator +
                        "Generated/OrpheusAuthoringOwnership.json"),
                    Does.Contain(
                        "\"guid\":\"" +
                        AssetDatabase.AssetPathToGUID(
                            fixture.Event.AssetPath).ToLowerInvariant() +
                        "\",\"path\":\"" +
                         fixture.Event.AssetPath + "\""));
                var closurePath =
                    fixture.AssetRoot + AssetPathSeparator +
                    "Generated/OrpheusAuthoringClosure.json";
                var closureGuid = AssetDatabase.AssetPathToGUID(closurePath)
                    .ToLowerInvariant();
                Assert.That(closureGuid, Has.Length.EqualTo(32));
                Assert.That(
                    File.ReadAllText(
                        fixture.AssetRoot + AssetPathSeparator +
                        "Generated/OrpheusAuthoringOwnership.json"),
                    Does.Contain(
                        "\"kind\":\"closure\",\"moduleId\":\"\"," +
                        "\"symbol\":\"\",\"key\":0,\"guid\":\"" +
                        closureGuid + "\",\"path\":\"" + closurePath + "\""));
                Assert.That(
                    File.Exists(fixture.Artifacts.ExecutionPath),
                    Is.True);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void AssetsIntegration_ReportOnlyOrphanRemainsOwnedAndUndeleted()
        {
            var fixture = new IntegrationFixture(deleteOrphan: false);
            try
            {
                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    fixture.Artifacts,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile });

                Assert.That(
                    result,
                    Is.EqualTo(
                        OrpheusAudioAuthoringCompileStatus.SucceededChanged),
                    OrpheusAudioAuthoringTransaction.LastFailureForTests);
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                        fixture.OrphanPath),
                    Is.Not.Null);
                StringAssert.Contains(
                    "\"path\":\"" + fixture.OrphanPath + "\"",
                    File.ReadAllText(
                        fixture.AssetRoot + AssetPathSeparator +
                        "Generated/OrpheusAuthoringOwnership.json"));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void AssetsIntegration_ChangedOrphanGuidIsBlockedAndUndeleted()
        {
            var fixture = new IntegrationFixture();
            try
            {
                Assert.That(AssetDatabase.DeleteAsset(fixture.OrphanPath), Is.True);
                AssetDatabase.CreateAsset(
                    ScriptableObject.CreateInstance<OrpheusAudioEvent>(),
                    fixture.OrphanPath);
                AssetDatabase.SaveAssets();
                var execution = OrpheusAudioAuthoringReports.BuildExecution(
                    OrpheusAudioAuthoringCompileStatus.SucceededChanged,
                    "Standalone",
                    0,
                    0,
                    0,
                    0,
                    1,
                    Array.Empty<OrpheusAuthoringCompilationError>(),
                    null,
                    new[]
                    {
                        new OrpheusAuthoringReportChange(
                            "deleted",
                            fixture.OrphanPath,
                            "",
                            "",
                            Array.Empty<OrpheusAuthoringReportFieldChange>())
                    },
                    fixture.AssetRoot + AssetPathSeparator +
                    "Generated/OrpheusAuthoringClosure.json");
                var artifacts = new OrpheusAudioAuthoringCommitArtifacts(
                    fixture.Artifacts.OwnershipJson,
                    fixture.Artifacts.ClosureJson,
                    execution,
                    fixture.Artifacts.ExecutionPath);

                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    artifacts,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile });

                Assert.That(
                    result,
                    Is.EqualTo(
                        OrpheusAudioAuthoringCompileStatus.SucceededChanged),
                    OrpheusAudioAuthoringTransaction.LastFailureForTests);
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                        fixture.OrphanPath),
                    Is.Not.Null);
                StringAssert.Contains(
                    "\"action\":\"blocked\"",
                    File.ReadAllText(artifacts.ExecutionPath));
                StringAssert.DoesNotContain(
                    "\"action\":\"deleted\"",
                    File.ReadAllText(artifacts.ExecutionPath));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private static IEnumerable<object[]> FailurePoints
        {
            get
            {
                var operations = new[]
                {
                    "LockReloadAssemblies",
                    "StartAssetEditing",
                    "DirectoryCreate",
                    "AssetCreate",
                    "SerializedApply",
                    "SaveAsset",
                    "TextReplace",
                    "DeleteAsset",
                    OrpheusAuthoringWriteKind.CreateDirectory.ToString(),
                    OrpheusAuthoringWriteKind.CreateEvent.ToString(),
                    OrpheusAuthoringWriteKind.UpdateEvent.ToString(),
                    OrpheusAuthoringWriteKind.MoveRenamedEvent.ToString(),
                    OrpheusAuthoringWriteKind.KeepEvent.ToString(),
                    "MoveAsset",
                    OrpheusAuthoringWriteKind.WriteCatalog.ToString(),
                    OrpheusAuthoringWriteKind.WriteTypedKeys.ToString(),
                    OrpheusAuthoringWriteKind.WriteEnrollmentIdentity.ToString(),
                    OrpheusAuthoringWriteKind.WriteOwnershipIndex.ToString(),
                    OrpheusAuthoringWriteKind.DeleteTrackedOrphan.ToString(),
                    OrpheusAuthoringWriteKind.WriteClosureReport.ToString(),
                    "StopAssetEditing",
                    "Refresh",
                    "Import",
                    "OwnershipMaterialize",
                    "ValidateOutputs",
                    "SelectedAuthoringValidation",
                    "SharedValidation",
                    "ClosureReplace"
                };
                for (var index = 0; index < operations.Length; index++)
                {
                    yield return new object[] { operations[index], true };
                    yield return new object[] { operations[index], false };
                }
            }
        }

        [TestCaseSource(nameof(FailurePoints))]
        public void AssetsIntegration_EachFailureBoundaryRestoresBaseline(
            string operation,
            bool before)
        {
            var eventOperation =
                string.Equals(
                    operation,
                    OrpheusAuthoringWriteKind.UpdateEvent.ToString(),
                    StringComparison.Ordinal)
                    ? OrpheusAuthoringWriteKind.UpdateEvent
                    : string.Equals(
                          operation,
                          OrpheusAuthoringWriteKind.MoveRenamedEvent.ToString(),
                          StringComparison.Ordinal) ||
                      string.Equals(
                          operation,
                          "MoveAsset",
                          StringComparison.Ordinal)
                        ? OrpheusAuthoringWriteKind.MoveRenamedEvent
                        : OrpheusAuthoringWriteKind.CreateEvent;
            var fixture = new IntegrationFixture(
                eventOperation: eventOperation);
            try
            {
                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    fixture.Artifacts,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile },
                    new ThrowOnceInjection(operation, before));

                Assert.That(
                    result,
                    Is.EqualTo(OrpheusAudioAuthoringCompileStatus.RolledBack),
                    operation + " " + before + " " +
                    OrpheusAudioAuthoringTransaction.LastFailureForTests);
                fixture.AssertBaselineRestored();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void AssetsIntegration_NewDirectoryMetaIsRemovedByRollback()
        {
            var fixture =
                new IntegrationFixture(startWithoutEventsDirectory: true);
            try
            {
                var observedDirectoryMeta = false;
                var injection = new ActionInjection(
                    "Import",
                    true,
                    () =>
                    {
                        observedDirectoryMeta =
                            File.Exists(fixture.FreshDirectoryPath + ".meta");
                        throw new InvalidOperationException(
                            "InjectedAfterRefresh");
                    });

                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    fixture.Artifacts,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile },
                    injection);

                Assert.That(observedDirectoryMeta, Is.True);
                Assert.That(
                    result,
                    Is.EqualTo(OrpheusAudioAuthoringCompileStatus.RolledBack),
                    OrpheusAudioAuthoringTransaction.LastFailureForTests);
                Assert.That(
                    Directory.Exists(fixture.FreshDirectoryPath),
                    Is.False);
                Assert.That(
                    File.Exists(fixture.FreshDirectoryPath + ".meta"),
                    Is.False);
                fixture.AssertBaselineRestored();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void AssetsIntegration_RecapturesActualOutputsBeforeCommit()
        {
            var fixture = new IntegrationFixture();
            try
            {
                var injection = new ActionInjection(
                    OrpheusAuthoringWriteKind.WriteCatalog.ToString(),
                    false,
                    () =>
                    {
                        var serialized = new SerializedObject(fixture.Catalog);
                        serialized.FindProperty("_events").arraySize = 0;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(fixture.Catalog);
                        AssetDatabase.SaveAssetIfDirty(fixture.Catalog);
                    });

                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    fixture.Artifacts,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile },
                    injection);

                Assert.That(
                    result,
                    Is.EqualTo(OrpheusAudioAuthoringCompileStatus.RolledBack),
                    OrpheusAudioAuthoringTransaction.LastFailureForTests);
                Assert.That(
                    OrpheusAudioAuthoringTransaction.LastFailureForTests,
                    Does.Contain("ActualOutput"));
                fixture.AssertBaselineRestored();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void AssetsIntegration_ActualFingerprintRejectsPlannedHashOnly()
        {
            var fixture = new IntegrationFixture(
                string.Empty,
                useInvalidEventFingerprint: true);
            try
            {
                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    fixture.Artifacts,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile });

                Assert.That(
                    result,
                    Is.EqualTo(OrpheusAudioAuthoringCompileStatus.RolledBack),
                    OrpheusAudioAuthoringTransaction.LastFailureForTests);
                Assert.That(
                    OrpheusAudioAuthoringTransaction.LastFailureForTests,
                    Does.Contain("ActualOutput"));
                fixture.AssertBaselineRestored();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void AssetsIntegration_OwnershipRequiresFullCanonicalContract()
        {
            var fixture = new IntegrationFixture();
            try
            {
                var incomplete = new OrpheusAudioAuthoringCommitArtifacts(
                    "{\"artifacts\":[]}\n",
                    fixture.Artifacts.ClosureJson,
                    fixture.Artifacts.ExecutionJson,
                    fixture.Artifacts.ExecutionPath);
                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    incomplete,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile });

                Assert.That(
                    result,
                    Is.EqualTo(OrpheusAudioAuthoringCompileStatus.RolledBack),
                    OrpheusAudioAuthoringTransaction.LastFailureForTests);
                Assert.That(
                    OrpheusAudioAuthoringTransaction.LastFailureForTests,
                    Does.Contain("OwnershipMaterialization"));
                fixture.AssertBaselineRestored();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [TestCase("guid")]
        [TestCase("missing-kind")]
        public void AssetsIntegration_OwnershipRejectsIdentityOrKindDrift(
            string drift)
        {
            var fixture = new IntegrationFixture();
            try
            {
                string ownership;
                if (string.Equals(drift, "guid", StringComparison.Ordinal))
                {
                    ownership = fixture.OwnershipJson.Replace(
                        "\"guid\":\"" + fixture.EventGuid +
                        "\",\"path\":\"" + fixture.Event.AssetPath + "\"",
                        "\"guid\":\"" + new string('f', 32) +
                        "\",\"path\":\"" + fixture.Event.AssetPath + "\"");
                }
                else
                {
                    var marker = ",{\"kind\":\"closure\"";
                    var start = fixture.OwnershipJson.LastIndexOf(
                        marker,
                        StringComparison.Ordinal);
                    Assert.That(start, Is.GreaterThanOrEqualTo(0));
                    var end = fixture.OwnershipJson.IndexOf(
                        '}',
                        start + 1);
                    ownership =
                        fixture.OwnershipJson.Remove(start, end - start + 1);
                }

                var artifacts = new OrpheusAudioAuthoringCommitArtifacts(
                    ownership,
                    fixture.Artifacts.ClosureJson,
                    fixture.Artifacts.ExecutionJson,
                    fixture.Artifacts.ExecutionPath);
                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    artifacts,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile });

                Assert.That(
                    result,
                    Is.EqualTo(OrpheusAudioAuthoringCompileStatus.RolledBack),
                    OrpheusAudioAuthoringTransaction.LastFailureForTests);
                fixture.AssertBaselineRestored();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void AssetsIntegration_SharedValidationRejectsOtherEnabledProfile()
        {
            var fixture = new IntegrationFixture();
            try
            {
                var invalid =
                    ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
                OrpheusAudioEditorContractTests.SetField(
                    invalid,
                    "_enabled",
                    true);
                AssetDatabase.CreateAsset(
                    invalid,
                    fixture.AssetRoot + AssetPathSeparator +
                    "InvalidValidation.asset");
                AssetDatabase.SaveAssets();

                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    fixture.Artifacts,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile, invalid });

                Assert.That(
                    result,
                    Is.EqualTo(OrpheusAudioAuthoringCompileStatus.RolledBack),
                    OrpheusAudioAuthoringTransaction.LastFailureForTests);
                Assert.That(
                    OrpheusAudioAuthoringTransaction.LastFailureForTests,
                    Does.Contain("SharedValidation"));
                fixture.AssertBaselineRestored();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ExecutionReportFailure_IsRolledBackBeforeCommit(bool before)
        {
            var fixture = new IntegrationFixture();
            try
            {
                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    fixture.Artifacts,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile },
                    new ThrowOnceInjection(
                        "ExecutionReportReplace",
                        before));

                Assert.That(
                    result,
                    Is.EqualTo(
                        OrpheusAudioAuthoringCompileStatus.RolledBack));
                Assert.That(
                    OrpheusAudioAuthoringTransaction.LastFailureForTests,
                    Does.Contain("ExecutionReportReplace"));
                fixture.AssertBaselineRestored();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void UnlockReloadInjection_StillReleasesAndPreservesCommit(
            bool before)
        {
            var fixture = new IntegrationFixture();
            try
            {
                var injection = new ThrowOnceInjection(
                    "UnlockReloadAssemblies",
                    before);
                var result = OrpheusAudioAuthoringTransaction.Execute(
                    fixture.Profile,
                    fixture.Plan,
                    fixture.Artifacts,
                    BuildTargetGroup.Standalone,
                    new[] { fixture.Profile },
                    injection);

                Assert.That(injection.WasTriggered, Is.True);
                Assert.That(
                    result,
                    Is.EqualTo(
                        OrpheusAudioAuthoringCompileStatus.SucceededChanged));
                Assert.That(
                    OrpheusAudioAuthoringTransaction.LastFailureForTests,
                    Does.Contain("UnlockReloadAssemblies"));
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                        fixture.Event.AssetPath),
                    Is.Not.Null);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator TypedKeyWrite_CommitsBeforeReloadAndSurvivesNextDomain()
        {
            const string probe = "\n// Orpheus Task5 reload probe\n";
            SessionState.SetString(
                ReloadOriginalSourceKey,
                Convert.ToBase64String(
                    File.ReadAllBytes(OrpheusAudioTypedKeyProjection.SourcePath)));
            SessionState.EraseString(ReloadJournalPhaseKey);
            var existingJournals =
                new HashSet<string>(
                    GetTransactionJournals(),
                    StringComparer.OrdinalIgnoreCase);

            var fixture = new IntegrationFixture(probe);
            SessionState.SetString(ReloadAssetRootKey, fixture.AssetRoot);
            SessionState.SetString(
                ReloadExecutionPathKey,
                fixture.Artifacts.ExecutionPath);
            AssemblyReloadEvents.beforeAssemblyReload +=
                CaptureTerminalJournalBeforeReload;

            var result = OrpheusAudioAuthoringTransaction.Execute(
                fixture.Profile,
                fixture.Plan,
                fixture.Artifacts,
                BuildTargetGroup.Standalone,
                new[] { fixture.Profile });
            Assert.That(
                result,
                Is.EqualTo(OrpheusAudioAuthoringCompileStatus.SucceededChanged),
                OrpheusAudioAuthoringTransaction.LastFailureForTests);
            var currentJournals = GetTransactionJournals();
            var newJournal = string.Empty;
            for (var index = 0; index < currentJournals.Length; index++)
            {
                if (!existingJournals.Contains(currentJournals[index]))
                {
                    Assert.That(newJournal, Is.Empty);
                    newJournal = currentJournals[index];
                }
            }

            Assert.That(newJournal, Is.Not.Empty);
            SessionState.SetString(ReloadJournalPathKey, newJournal);

            yield return new WaitForDomainReload();

            Assert.That(
                SessionState.GetString(ReloadJournalPhaseKey, string.Empty),
                Is.EqualTo("Committed"));
            Assert.That(
                File.ReadAllText(OrpheusAudioTypedKeyProjection.SourcePath),
                Does.Contain("// Orpheus Task5 reload probe"));

            AssetDatabase.DeleteAsset(
                SessionState.GetString(ReloadAssetRootKey, string.Empty));
            DeleteFailureFixtureParentIfEmpty();
            var executionPath =
                SessionState.GetString(ReloadExecutionPathKey, string.Empty);
            if (File.Exists(executionPath))
            {
                File.Delete(executionPath);
            }

            File.WriteAllBytes(
                OrpheusAudioTypedKeyProjection.SourcePath,
                Convert.FromBase64String(
                    SessionState.GetString(
                        ReloadOriginalSourceKey,
                        string.Empty)));
            yield return new RecompileScripts();

            SessionState.EraseString(ReloadJournalPhaseKey);
            SessionState.EraseString(ReloadOriginalSourceKey);
            SessionState.EraseString(ReloadAssetRootKey);
            SessionState.EraseString(ReloadExecutionPathKey);
            SessionState.EraseString(ReloadJournalPathKey);
        }

        private static OrpheusAuthoringWriteOperationValue Op(
            OrpheusAuthoringWriteKind kind,
            string path,
            ushort key = 0)
        {
            return new OrpheusAuthoringWriteOperationValue(
                kind,
                key,
                key == 0 ? string.Empty : "ui",
                key == 0 ? string.Empty : "Cue",
                string.Empty,
                path,
                string.Empty);
        }

        private static OrpheusAuthoringWriteOperationValue MoveOp(
            string source,
            string target,
            ushort key)
        {
            return new OrpheusAuthoringWriteOperationValue(
                OrpheusAuthoringWriteKind.MoveRenamedEvent,
                key,
                "ui",
                "Cue",
                source,
                target,
                string.Empty);
        }

        private static string JournalJson(
            string id,
            string phase,
            string payload)
        {
            return "{\"schemaVersion\":1,\"transactionId\":\"" + id +
                   "\",\"phase\":\"" + phase +
                   "\",\"payload\":\"" +
                   payload.Replace("\\", "\\\\").Replace("\"", "\\\"") +
                   "\",\"payloadSha256\":\"" + Sha256(payload) + "\"}\n";
        }

        private static string SnapshotPayload(string path)
        {
            return "{\"schemaVersion\":1,\"snapshots\":[{" +
                   "\"path\":\"" + path + "\",\"kind\":\"file\"," +
                   "\"existed\":false,\"bytesBase64\":\"\"," +
                   "\"sha256\":\"\",\"guid\":\"\",\"mainType\":\"\"}]}";
        }

        private static string ExistingSnapshotPayload(
            string path,
            string bytesBase64,
            string sha256,
            string guid,
            string mainType)
        {
            return "{\"schemaVersion\":1,\"snapshots\":[{" +
                   "\"path\":\"" + path + "\",\"kind\":\"file\"," +
                   "\"existed\":true,\"bytesBase64\":\"" + bytesBase64 +
                   "\",\"sha256\":\"" + sha256 +
                   "\",\"guid\":\"" + guid +
                   "\",\"mainType\":\"" + mainType + "\"}]}";
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(
                    Encoding.UTF8.GetBytes(value ?? string.Empty));
                var result = new StringBuilder(64);
                for (var index = 0; index < digest.Length; index++)
                {
                    result.Append(digest[index].ToString("x2"));
                }

                return result.ToString();
            }
        }

        private static string Sha256Bytes(byte[] value)
        {
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(value);
                var result = new StringBuilder(64);
                for (var index = 0; index < digest.Length; index++)
                {
                    result.Append(digest[index].ToString("x2"));
                }

                return result.ToString();
            }
        }

        private static void CaptureTerminalJournalBeforeReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -=
                CaptureTerminalJournalBeforeReload;
            var journal =
                SessionState.GetString(ReloadJournalPathKey, string.Empty);
            if (!File.Exists(journal))
            {
                SessionState.SetString(
                    ReloadJournalPhaseKey,
                    "MissingJournal");
                return;
            }

            var json = File.ReadAllText(journal);
            SessionState.SetString(
                ReloadJournalPhaseKey,
                json.Contains("\"phase\":\"Committed\"")
                    ? "Committed"
                    : json);
        }

        private static string[] GetTransactionJournals()
        {
            var transactionRoot = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Library",
                "Orpheus",
                "AuthoringTransactions");
            if (!Directory.Exists(transactionRoot))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(
                transactionRoot,
                "journal.json",
                SearchOption.AllDirectories);
        }

        private static void AssertExistingJournalsAreTerminal(
            IEnumerable<string> journals)
        {
            foreach (var journal in journals)
            {
                Assert.That(
                    OrpheusAudioAuthoringTransaction.TryReadJournalForTests(
                        journal,
                        out var phase,
                        out _),
                    Is.True,
                    journal);
                Assert.That(
                    phase == OrpheusAudioAuthoringTransactionPhase.Committed ||
                    phase == OrpheusAudioAuthoringTransactionPhase.RolledBack,
                    Is.True,
                    journal + ":" + phase);
            }
        }

        private static string GetSingleNewJournal(
            ISet<string> existingJournals)
        {
            var newJournals = GetTransactionJournals()
                .Where(path => !existingJournals.Contains(path))
                .ToArray();
            Assert.That(newJournals, Has.Length.EqualTo(1));
            return newJournals[0];
        }

        private static void DeleteNewTransactionDirectories(
            ISet<string> existingJournals)
        {
            var current = GetTransactionJournals();
            for (var index = 0; index < current.Length; index++)
            {
                if (existingJournals.Contains(current[index]))
                {
                    continue;
                }

                var directory = Path.GetDirectoryName(current[index]);
                if (!string.IsNullOrEmpty(directory) &&
                    Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void DeleteFailureFixtureParentIfEmpty()
        {
            const string parent =
                "Assets/OrpheusAuthoringTask5FailureTests";
            if (Directory.Exists(parent) &&
                Directory.GetFileSystemEntries(parent).Length == 0)
            {
                AssetDatabase.DeleteAsset(parent);
            }
        }

        private sealed class ThrowOnceInjection :
            IOrpheusAudioAuthoringFailureInjection
        {
            private readonly string _operation;
            private readonly bool _before;
            private bool _thrown;

            internal ThrowOnceInjection(string operation, bool before)
            {
                _operation = operation;
                _before = before;
            }

            internal bool WasTriggered => _thrown;

            public void Before(string operation)
            {
                Throw(operation, true);
            }

            public void After(string operation)
            {
                Throw(operation, false);
            }

            private void Throw(string operation, bool before)
            {
                if (_thrown || before != _before ||
                    !string.Equals(
                        operation,
                        _operation,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _thrown = true;
                throw new InvalidOperationException(
                    "Injected:" + operation + ":" + before);
            }
        }

        private sealed class DurablePhaseTerminationInjection :
            IOrpheusAudioAuthoringFailureInjection
        {
            private readonly OrpheusAudioAuthoringTransactionPhase _phase;
            private bool _rollbackRequested;

            internal DurablePhaseTerminationInjection(
                OrpheusAudioAuthoringTransactionPhase phase)
            {
                _phase = phase;
            }

            internal bool WasTriggered { get; private set; }

            public void Before(string operation)
            {
                if (_phase ==
                        OrpheusAudioAuthoringTransactionPhase.RollingBack &&
                    !_rollbackRequested &&
                    string.Equals(
                        operation,
                        OrpheusAuthoringWriteKind.WriteCatalog.ToString(),
                        StringComparison.Ordinal))
                {
                    _rollbackRequested = true;
                    throw new InvalidOperationException(
                        "InjectedRollbackBeforeTermination");
                }
            }

            public void After(string operation)
            {
                if (WasTriggered ||
                    !string.Equals(
                        operation,
                        "Journal" + _phase,
                        StringComparison.Ordinal))
                {
                    return;
                }

                WasTriggered = true;
                throw new
                    OrpheusAudioAuthoringSimulatedProcessTerminationException();
            }
        }

        private sealed class ActionInjection :
            IOrpheusAudioAuthoringFailureInjection
        {
            private readonly string _operation;
            private readonly bool _before;
            private readonly Action _action;
            private bool _invoked;

            internal ActionInjection(
                string operation,
                bool before,
                Action action)
            {
                _operation = operation;
                _before = before;
                _action = action;
            }

            public void Before(string operation)
            {
                Invoke(operation, true);
            }

            public void After(string operation)
            {
                Invoke(operation, false);
            }

            private void Invoke(string operation, bool before)
            {
                if (_invoked || before != _before ||
                    !string.Equals(
                        operation,
                        _operation,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _invoked = true;
                _action();
            }
        }

        private sealed class IntegrationFixture : IDisposable
        {
            internal IntegrationFixture(
                string sourceSuffix = "",
                bool useInvalidEventFingerprint = false,
                OrpheusAuthoringWriteKind eventOperation =
                    OrpheusAuthoringWriteKind.CreateEvent,
                bool deleteOrphan = true,
                bool startWithoutEventsDirectory = false)
            {
                _startedWithoutEventsDirectory =
                    startWithoutEventsDirectory;
                AssetRoot =
                    "Assets/OrpheusAuthoringTask5FailureTests/" +
                    Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(
                    AssetRoot + AssetPathSeparator + "Generated");
                AssetDatabase.Refresh();

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Packages/com.orpheus.audio/Tests/Fixtures/NonEmptyHost/" +
                    "Editor/Resources/OrpheusIssue19/Audio/Clips/" +
                    "FixtureOneShot2DBootstrap.wav");
                Assert.That(clip, Is.Not.Null);
                Assert.That(
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        clip,
                        out var clipGuid,
                        out long clipLocalId),
                    Is.True);
                var recipe = new OrpheusAuthoringEventRecipeValue(
                    "Cue",
                    OrpheusPlaybackKind.OneShot2D,
                    OrpheusCategory.SfxUi,
                    OrpheusLoadPolicy.BootstrapTransient,
                    new[]
                    {
                        new OrpheusAuthoringClipValue(
                            clipGuid.ToLowerInvariant(),
                            clipLocalId,
                            AssetDatabase.GetAssetPath(clip))
                    },
                    1f, 1f, 1f, 1f, 128, 1, 0f, 0f, 0f,
                    OrpheusRolloffMode.Logarithmic,
                    string.Empty,
                    string.Empty);
                var preliminaryEvent =
                    new OrpheusAuthoringCompiledEventValue(
                    100,
                    "ui",
                    recipe,
                    AssetRoot + AssetPathSeparator +
                    "Generated/Events/AE_Cue.asset",
                    string.Empty);
                Event = new OrpheusAuthoringCompiledEventValue(
                    100,
                    "ui",
                    recipe,
                    preliminaryEvent.AssetPath,
                    useInvalidEventFingerprint
                        ? new string('f', 64)
                        : OrpheusAudioAuthoringTransaction
                            .ComputeEventFingerprintForTests(preliminaryEvent));

                Catalog = ScriptableObject.CreateInstance<OrpheusAudioCatalog>();
                AssetDatabase.CreateAsset(
                    Catalog,
                    AssetRoot + AssetPathSeparator +
                    "Generated/OrpheusAudioCatalog.asset");
                var manifest =
                    ScriptableObject.CreateInstance<OrpheusAudioKeyManifest>();
                var serializedManifest = new SerializedObject(manifest);
                var entries = serializedManifest.FindProperty("_entries");
                entries.arraySize = 1;
                var entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("_id").intValue = 100;
                entry.FindPropertyRelative("_symbol").stringValue = "Cue";
                entry.FindPropertyRelative("_status").intValue =
                    (int)OrpheusAudioKeyStatus.Active;
                serializedManifest.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(
                    manifest,
                    AssetRoot + AssetPathSeparator + "Manifest.asset");
                var authoring =
                    ScriptableObject.CreateInstance<OrpheusAudioAuthoringProfile>();
                OrpheusAudioEditorContractTests.SetField(
                    authoring,
                    "_catalog",
                    Catalog);
                OrpheusAudioEditorContractTests.SetField(
                    authoring,
                    "_keyManifest",
                    manifest);
                OrpheusAudioEditorContractTests.SetField(
                    authoring,
                    "_generatedRoot",
                    AssetRoot + AssetPathSeparator + "Generated");
                AssetDatabase.CreateAsset(
                    authoring,
                    AssetRoot + AssetPathSeparator + "Authoring.asset");
                Profile =
                    ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
                OrpheusAudioEditorContractTests.SetField(
                    Profile,
                    "_catalog",
                    Catalog);
                OrpheusAudioEditorContractTests.SetField(
                    Profile,
                    "_authoringProfile",
                    authoring);
                AssetDatabase.CreateAsset(
                    Profile,
                    AssetRoot + AssetPathSeparator + "Validation.asset");
                OrphanPath =
                    AssetRoot + AssetPathSeparator + "Generated/" +
                    (startWithoutEventsDirectory
                        ? "Orphans/"
                        : "Events/") +
                    "AE_Orphan.asset";
                Directory.CreateDirectory(
                    Path.GetDirectoryName(OrphanPath));
                AssetDatabase.CreateAsset(
                    ScriptableObject.CreateInstance<OrpheusAudioEvent>(),
                    OrphanPath);
                AssetDatabase.SaveAssets();
                var orphan = new OrpheusAuthoringOwnedEventValue(
                    200,
                    "legacy",
                    "Orphan",
                    AssetDatabase.AssetPathToGUID(OrphanPath)
                        .ToLowerInvariant(),
                    OrphanPath,
                    string.Empty);

                SourcePath =
                    AssetRoot + AssetPathSeparator +
                    "Generated/Events/AE_OldCue.asset";
                if (!startWithoutEventsDirectory ||
                    eventOperation != OrpheusAuthoringWriteKind.CreateEvent)
                {
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(Event.AssetPath));
                }

                AssetDatabase.Refresh();
                var initialEventPath =
                    eventOperation ==
                    OrpheusAuthoringWriteKind.MoveRenamedEvent
                        ? SourcePath
                        : Event.AssetPath;
                var reservedEventGuid = string.Empty;
                if (eventOperation !=
                    OrpheusAuthoringWriteKind.CreateEvent)
                {
                    var reserved =
                        ScriptableObject.CreateInstance<OrpheusAudioEvent>();
                    AssetDatabase.CreateAsset(reserved, initialEventPath);
                    AssetDatabase.SaveAssetIfDirty(reserved);
                    reservedEventGuid =
                        AssetDatabase.AssetPathToGUID(initialEventPath)
                            .ToLowerInvariant();
                }

                _baselineEventGuid = reservedEventGuid;
                _baselineEventPath = initialEventPath;
                EventGuid = reservedEventGuid;

                var ownershipPath =
                    AssetRoot + AssetPathSeparator +
                    "Generated/OrpheusAuthoringOwnership.json";
                var closurePath =
                    AssetRoot + AssetPathSeparator +
                    "Generated/OrpheusAuthoringClosure.json";
                AssetDatabase.Refresh(
                    ImportAssetOptions.ForceSynchronousImport);

                OwnershipJson =
                    "{\"schemaVersion\":1," +
                    "\"ownerAuthoringProfileGuid\":\"" +
                    GuidOf(authoring) + "\"," +
                    "\"enrolledValidationProfileGuid\":\"" +
                    GuidOf(Profile) + "\"," +
                    "\"manifestGuid\":\"" + GuidOf(manifest) + "\"," +
                    "\"catalogGuid\":\"" + GuidOf(Catalog) + "\"," +
                    "\"generatedRoot\":\"" + AssetRoot +
                    AssetPathSeparator + "Generated\"," +
                    "\"acceptedManifestSnapshot\":[{" +
                    "\"id\":100,\"symbol\":\"Cue\",\"status\":1}]," +
                    "\"artifacts\":[" +
                    Artifact(
                        "event",
                        "ui",
                        "Cue",
                        100,
                        reservedEventGuid,
                        Event.AssetPath) + "," +
                    Artifact(
                        "catalog",
                        string.Empty,
                        string.Empty,
                        0,
                        GuidOf(Catalog),
                        AssetDatabase.GetAssetPath(Catalog)) + "," +
                    Artifact(
                        "typed-key-assembly",
                        string.Empty,
                        string.Empty,
                        0,
                        AssetDatabase.AssetPathToGUID(
                            OrpheusAudioTypedKeyProjection.AssemblyPath)
                            .ToLowerInvariant(),
                        OrpheusAudioTypedKeyProjection.AssemblyPath) + "," +
                    Artifact(
                        "typed-key-source",
                        string.Empty,
                        string.Empty,
                        0,
                        AssetDatabase.AssetPathToGUID(
                            OrpheusAudioTypedKeyProjection.SourcePath)
                            .ToLowerInvariant(),
                        OrpheusAudioTypedKeyProjection.SourcePath) + "," +
                    Artifact(
                        "ownership",
                        string.Empty,
                        string.Empty,
                        0,
                        string.Empty,
                        ownershipPath) + "," +
                    (deleteOrphan
                        ? string.Empty
                        : Artifact(
                              "event",
                              "legacy",
                              "Orphan",
                              200,
                              orphan.Guid,
                              OrphanPath) + ",") +
                    Artifact(
                        "closure",
                        string.Empty,
                        string.Empty,
                        0,
                        string.Empty,
                        closurePath) + "]}\n";
                var operationList =
                    new List<OrpheusAuthoringWriteOperationValue>(new[]
                {
                    Op(
                        OrpheusAuthoringWriteKind.CreateDirectory,
                        AssetRoot + AssetPathSeparator + "Generated"),
                    Op(
                        OrpheusAuthoringWriteKind.CreateDirectory,
                        AssetRoot + AssetPathSeparator +
                        "Generated/Events"),
                    eventOperation ==
                    OrpheusAuthoringWriteKind.MoveRenamedEvent
                        ? MoveOp(SourcePath, Event.AssetPath, 100)
                        : Op(eventOperation, Event.AssetPath, 100),
                    Op(
                        OrpheusAuthoringWriteKind.KeepEvent,
                        Event.AssetPath,
                        100),
                    Op(
                        OrpheusAuthoringWriteKind.WriteCatalog,
                        AssetDatabase.GetAssetPath(Catalog)),
                    Op(
                        OrpheusAuthoringWriteKind.WriteTypedKeys,
                        OrpheusAudioTypedKeyProjection.SourcePath),
                    Op(
                        OrpheusAuthoringWriteKind.WriteEnrollmentIdentity,
                        AssetDatabase.GetAssetPath(Profile)),
                    Op(
                        OrpheusAuthoringWriteKind.WriteOwnershipIndex,
                        ownershipPath),
                    Op(
                        OrpheusAuthoringWriteKind.DeleteTrackedOrphan,
                        OrphanPath),
                    Op(
                        OrpheusAuthoringWriteKind.WriteClosureReport,
                        closurePath)
                });
                FreshDirectoryPath =
                    AssetRoot + AssetPathSeparator + "Generated/Events";

                if (!deleteOrphan)
                {
                    operationList.RemoveAll(operation =>
                        operation.Kind ==
                        OrpheusAuthoringWriteKind.DeleteTrackedOrphan);
                }

                var operations = operationList.ToArray();
                var assemblyText =
                    File.ReadAllText(OrpheusAudioTypedKeyProjection.AssemblyPath);
                var sourceText =
                    File.ReadAllText(OrpheusAudioTypedKeyProjection.SourcePath) +
                    sourceSuffix;
                var provisional = new OrpheusAuthoringCompilationPlan(
                    new[] { Event },
                    new ushort[] { 100 },
                    Array.Empty<OrpheusAuthoringFutureDeliveryValue>(),
                    operations,
                    new string('b', 64),
                    string.Empty,
                    AssetDatabase.AssetPathToGUID(
                            AssetDatabase.GetAssetPath(Catalog))
                        .ToLowerInvariant(),
                     AssetDatabase.GetAssetPath(Catalog),
                     assemblyText,
                     sourceText,
                     new[] { orphan });
                var outputFingerprint =
                    OrpheusAudioAuthoringTransaction
                        .ComputeOutputFingerprintForTests(
                            provisional,
                            OwnershipJson);
                Plan = new OrpheusAuthoringCompilationPlan(
                    new[] { Event },
                    new ushort[] { 100 },
                    Array.Empty<OrpheusAuthoringFutureDeliveryValue>(),
                    operations,
                    provisional.InputFingerprint,
                    outputFingerprint,
                    provisional.CatalogGuid,
                     provisional.CatalogAssetPath,
                     assemblyText,
                     sourceText,
                     new[] { orphan });
                Artifacts = new OrpheusAudioAuthoringCommitArtifacts(
                    OwnershipJson,
                    "{\"task\":5}\n",
                    "{\"status\":\"Committed\"}\n",
                    "Library/Orpheus/AuthoringExecution/task5-" +
                    Guid.NewGuid().ToString("N") + ".json");
                _baselineFiles = CaptureBaselineFiles(
                    AssetDatabase.GetAssetPath(Catalog),
                    AssetDatabase.GetAssetPath(authoring),
                    AssetDatabase.GetAssetPath(manifest),
                    AssetDatabase.GetAssetPath(Profile),
                    OrphanPath,
                    ownershipPath,
                    closurePath,
                    Event.AssetPath,
                    Event.AssetPath + ".meta",
                    SourcePath,
                    AssetRoot + AssetPathSeparator + "Generated",
                    AssetRoot + AssetPathSeparator + "Generated/Events",
                    OrpheusAudioTypedKeyProjection.AssemblyPath,
                    OrpheusAudioTypedKeyProjection.SourcePath);
            }

            private readonly Dictionary<string, byte[]> _baselineFiles;
            private readonly string _baselineEventGuid;
            private readonly string _baselineEventPath;
            private readonly bool _startedWithoutEventsDirectory;
            internal string AssetRoot { get; }
            internal OrpheusAudioCatalog Catalog { get; }
            internal OrpheusAudioValidationProfile Profile { get; }
            internal OrpheusAuthoringCompiledEventValue Event { get; }
            internal string EventGuid { get; }
            internal string SourcePath { get; }
            internal string OrphanPath { get; }
            internal string FreshDirectoryPath { get; }
            internal OrpheusAuthoringCompilationPlan Plan { get; }
            internal OrpheusAudioAuthoringCommitArtifacts Artifacts { get; }
            internal string OwnershipJson { get; }

            internal void AssertFreshTargetsAbsent()
            {
                Assert.That(File.Exists(Event.AssetPath), Is.False);
                Assert.That(File.Exists(Event.AssetPath + ".meta"), Is.False);
                Assert.That(
                    File.Exists(
                        AssetRoot + AssetPathSeparator +
                        "Generated/OrpheusAuthoringOwnership.json"),
                    Is.False);
                Assert.That(
                    File.Exists(
                        AssetRoot + AssetPathSeparator +
                        "Generated/OrpheusAuthoringClosure.json"),
                    Is.False);
            }

            internal void AssertBaselineRestored()
            {
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                        Event.AssetPath),
                    _baselineFiles.ContainsKey(Event.AssetPath)
                        ? Is.Not.Null
                        : Is.Null);
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                        SourcePath),
                    _baselineFiles.ContainsKey(SourcePath)
                        ? Is.Not.Null
                        : Is.Null);
                if (_baselineEventGuid.Length != 0)
                {
                    Assert.That(
                        AssetDatabase.AssetPathToGUID(_baselineEventPath)
                            .ToLowerInvariant(),
                        Is.EqualTo(_baselineEventGuid));
                }
                if (!_baselineFiles.ContainsKey(Event.AssetPath + ".meta"))
                {
                    Assert.That(
                        File.Exists(Event.AssetPath + ".meta"),
                        Is.False);
                }
                var serialized = new SerializedObject(Catalog);
                Assert.That(
                    serialized.FindProperty("_events").arraySize,
                    Is.Zero);
                Assert.That(Profile.AuthoringEnrollmentGuid, Is.Empty);
                Assert.That(
                    File.Exists(
                        AssetRoot + AssetPathSeparator +
                        "Generated/OrpheusAuthoringOwnership.json"),
                    Is.EqualTo(
                        _baselineFiles.ContainsKey(
                            AssetRoot + AssetPathSeparator +
                            "Generated/OrpheusAuthoringOwnership.json")));
                Assert.That(
                    File.Exists(
                        AssetRoot + AssetPathSeparator +
                        "Generated/OrpheusAuthoringClosure.json"),
                    Is.EqualTo(
                        _baselineFiles.ContainsKey(
                            AssetRoot + AssetPathSeparator +
                            "Generated/OrpheusAuthoringClosure.json")));
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                        OrphanPath),
                    Is.Not.Null);
                Assert.That(File.Exists(Artifacts.ExecutionPath), Is.False);
                Assert.That(
                    Directory.Exists(
                        AssetRoot + AssetPathSeparator +
                        "Generated/Events"),
                    Is.EqualTo(!_startedWithoutEventsDirectory));
                foreach (var pair in _baselineFiles)
                {
                    Assert.That(File.Exists(pair.Key), Is.True, pair.Key);
                    CollectionAssert.AreEqual(
                        pair.Value,
                        File.ReadAllBytes(pair.Key),
                        pair.Key);
                }
            }

            public void Dispose()
            {
                AssetDatabase.DeleteAsset(AssetRoot);
                DeleteFailureFixtureParentIfEmpty();

                if (File.Exists(Artifacts.ExecutionPath))
                {
                    File.Delete(Artifacts.ExecutionPath);
                }

                AssetDatabase.Refresh();
            }

            private static Dictionary<string, byte[]> CaptureBaselineFiles(
                params string[] paths)
            {
                var result =
                    new Dictionary<string, byte[]>(StringComparer.Ordinal);
                for (var index = 0; index < paths.Length; index++)
                {
                    AddBaselineFile(result, paths[index]);
                    AddBaselineFile(result, paths[index] + ".meta");
                }

                return result;
            }

            private static string GuidOf(UnityEngine.Object value)
            {
                return AssetDatabase.AssetPathToGUID(
                        AssetDatabase.GetAssetPath(value))
                    .ToLowerInvariant();
            }

            private static string Artifact(
                string kind,
                string moduleId,
                string symbol,
                int key,
                string guid,
                string path)
            {
                return "{\"kind\":\"" + kind +
                       "\",\"moduleId\":\"" + moduleId +
                       "\",\"symbol\":\"" + symbol +
                       "\",\"key\":" + key +
                       ",\"guid\":\"" + guid +
                       "\",\"path\":\"" + path + "\"}";
            }

            private static void AddBaselineFile(
                IDictionary<string, byte[]> result,
                string path)
            {
                if (File.Exists(path) && !result.ContainsKey(path))
                {
                    result.Add(path, File.ReadAllBytes(path));
                }
            }
        }
    }
}
