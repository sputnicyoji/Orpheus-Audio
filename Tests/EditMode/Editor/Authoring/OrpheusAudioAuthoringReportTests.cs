using System;
using System.IO;
using NUnit.Framework;
using Orpheus.Audio.Core;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioAuthoringReportTests
    {
        private const char AssetPathSeparator = (char)47;
        private const string AuthoringGuid = "11111111111111111111111111111111";
        private const string ValidationGuid = "22222222222222222222222222222222";
        private const string ManifestGuid = "33333333333333333333333333333333";
        private const string CatalogGuid = "44444444444444444444444444444444";
        private const string EventGuid = "55555555555555555555555555555555";
        private const string ClipGuid = "66666666666666666666666666666666";
        private const string Root = "Assets/GeneratedAudio";

        [Test]
        public void OwnershipWriter_ProducesExactCanonicalGolden()
        {
            var manifest = new[]
            {
                new OrpheusAuthoringManifestEntryValue(
                    20,
                    "VOICE_\"二\"",
                    OrpheusAudioKeyStatus.Retired),
                new OrpheusAuthoringManifestEntryValue(
                    10,
                    "UI_BACK",
                    OrpheusAudioKeyStatus.Active)
            };
            var artifacts = new[]
            {
                Artifact("closure", "", "", 0, "", Root + AssetPathSeparator + "OrpheusAuthoringClosure.json"),
                Artifact("event", "ui", "UI_BACK", 10, EventGuid, Root + AssetPathSeparator + "Events/AE_UI_BACK.asset"),
                Artifact("typed-key-source", "", "", 0, "77777777777777777777777777777777", "Assets/OrpheusGenerated/OrpheusAudioKeys.g.cs"),
                Artifact("catalog", "", "", 0, CatalogGuid, Root + AssetPathSeparator + "OrpheusAudioCatalog.asset"),
                Artifact("ownership", "", "", 0, "", Root + AssetPathSeparator + "OrpheusAuthoringOwnership.json"),
                Artifact("typed-key-assembly", "", "", 0, "88888888888888888888888888888888", "Assets/OrpheusGenerated/Orpheus.Audio.Generated.asmdef")
            };

            var actual = OrpheusAudioAuthoringReports.BuildOwnership(
                AuthoringGuid,
                ValidationGuid,
                ManifestGuid,
                CatalogGuid,
                Root,
                manifest,
                artifacts);

            const string expected =
                "{\n" +
                "  \"schemaVersion\": 1,\n" +
                "  \"ownerAuthoringProfileGuid\": \"11111111111111111111111111111111\",\n" +
                "  \"enrolledValidationProfileGuid\": \"22222222222222222222222222222222\",\n" +
                "  \"manifestGuid\": \"33333333333333333333333333333333\",\n" +
                "  \"catalogGuid\": \"44444444444444444444444444444444\",\n" +
                "  \"generatedRoot\": \"Assets/GeneratedAudio\",\n" +
                "  \"acceptedManifestSnapshot\": [\n" +
                "    {\"id\":10,\"symbol\":\"UI_BACK\",\"status\":1},\n" +
                "    {\"id\":20,\"symbol\":\"VOICE_\\\"二\\\"\",\"status\":3}\n" +
                "  ],\n" +
                "  \"artifacts\": [\n" +
                "    {\"kind\":\"event\",\"moduleId\":\"ui\",\"symbol\":\"UI_BACK\",\"key\":10,\"guid\":\"55555555555555555555555555555555\",\"path\":\"Assets/GeneratedAudio/Events/AE_UI_BACK.asset\"},\n" +
                "    {\"kind\":\"catalog\",\"moduleId\":\"\",\"symbol\":\"\",\"key\":0,\"guid\":\"44444444444444444444444444444444\",\"path\":\"Assets/GeneratedAudio/OrpheusAudioCatalog.asset\"},\n" +
                "    {\"kind\":\"typed-key-assembly\",\"moduleId\":\"\",\"symbol\":\"\",\"key\":0,\"guid\":\"88888888888888888888888888888888\",\"path\":\"Assets/OrpheusGenerated/Orpheus.Audio.Generated.asmdef\"},\n" +
                "    {\"kind\":\"typed-key-source\",\"moduleId\":\"\",\"symbol\":\"\",\"key\":0,\"guid\":\"77777777777777777777777777777777\",\"path\":\"Assets/OrpheusGenerated/OrpheusAudioKeys.g.cs\"},\n" +
                "    {\"kind\":\"ownership\",\"moduleId\":\"\",\"symbol\":\"\",\"key\":0,\"guid\":\"\",\"path\":\"Assets/GeneratedAudio/OrpheusAuthoringOwnership.json\"},\n" +
                "    {\"kind\":\"closure\",\"moduleId\":\"\",\"symbol\":\"\",\"key\":0,\"guid\":\"\",\"path\":\"Assets/GeneratedAudio/OrpheusAuthoringClosure.json\"}\n" +
                "  ]\n" +
                "}\n";
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(
                File.ReadAllText(
                    "Packages/com.orpheus.audio/Tests/Fixtures/" +
                    "AuthoringAutomation/Reports/ownership-golden.json"),
                Is.EqualTo(actual));
        }

        [Test]
        public void ClosureWriter_ProducesExactCanonicalGolden()
        {
            var manifest = new[]
            {
                new OrpheusAuthoringManifestEntryValue(
                    10,
                    "UI_BACK",
                    OrpheusAudioKeyStatus.Active)
            };
            var ownership = new[]
            {
                Artifact("event", "ui", "UI_BACK", 10, EventGuid, Root + AssetPathSeparator + "Events/AE_UI_BACK.asset")
            };
            var actual = OrpheusAudioAuthoringReports.BuildClosure(
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                manifest,
                new[]
                {
                    new OrpheusAuthoringCompilationError(
                        OrpheusAuthoringErrorCode.InvalidClipIdentity,
                        10,
                        "ui",
                        "UI_BACK",
                        "Assets/Audio/back.wav",
                        0,
                        "quote \" slash \\ newline\n")
                },
                new[]
                {
                    new OrpheusAuthoringCatalogClosureValue(
                        10,
                        "UI_BACK",
                        EventGuid,
                        Root + AssetPathSeparator + "Events/AE_UI_BACK.asset")
                },
                new[]
                {
                    new OrpheusAuthoringClipClosureValue(
                        10,
                        "UI_BACK",
                        0,
                        ClipGuid,
                        2800000L,
                        "Assets/Audio/二/back.wav")
                },
                ownership,
                Array.Empty<OrpheusAuthoringReportArtifact>(),
                new[]
                {
                    new OrpheusAuthoringFutureDeliveryValue(
                        "ui",
                        "menu",
                        "base",
                        10,
                        "UI_BACK")
                });

            const string expected =
                "{\n" +
                "  \"schemaVersion\": 1,\n" +
                "  \"inputFingerprint\": \"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\n" +
                "  \"outputFingerprint\": \"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\n" +
                "  \"manifestSnapshot\": [\n" +
                "    {\"id\":10,\"symbol\":\"UI_BACK\",\"status\":1}\n" +
                "  ],\n" +
                "  \"identityConflicts\": [\n" +
                "    {\"code\":22,\"key\":10,\"moduleId\":\"ui\",\"symbol\":\"UI_BACK\",\"assetPath\":\"Assets/Audio/back.wav\",\"clipIndex\":0,\"detail\":\"quote \\\" slash \\\\ newline\\n\"}\n" +
                "  ],\n" +
                "  \"catalogClosure\": [\n" +
                "    {\"key\":10,\"symbol\":\"UI_BACK\",\"eventGuid\":\"55555555555555555555555555555555\",\"eventPath\":\"Assets/GeneratedAudio/Events/AE_UI_BACK.asset\"}\n" +
                "  ],\n" +
                "  \"clipClosure\": [\n" +
                "    {\"key\":10,\"symbol\":\"UI_BACK\",\"clipIndex\":0,\"guid\":\"66666666666666666666666666666666\",\"localFileId\":2800000,\"path\":\"Assets/Audio/二/back.wav\"}\n" +
                "  ],\n" +
                "  \"ownership\": [\n" +
                "    {\"kind\":\"event\",\"moduleId\":\"ui\",\"symbol\":\"UI_BACK\",\"key\":10,\"guid\":\"55555555555555555555555555555555\",\"path\":\"Assets/GeneratedAudio/Events/AE_UI_BACK.asset\"}\n" +
                "  ],\n" +
                "  \"orphans\": [],\n" +
                "  \"futureDeliveryProjection\": [\n" +
                "    {\"moduleId\":\"ui\",\"profileHint\":\"menu\",\"candidateContentBankId\":\"base\",\"key\":10,\"symbol\":\"UI_BACK\"}\n" +
                "  ]\n" +
                "}\n";
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(
                File.ReadAllText(
                    "Packages/com.orpheus.audio/Tests/Fixtures/" +
                    "AuthoringAutomation/Reports/closure-golden.json"),
                Is.EqualTo(actual));
        }

        [Test]
        public void InputFingerprintReplacement_RequiresOneCanonicalField()
        {
            var before =
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var after =
                "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
            var json =
                "{\n  \"inputFingerprint\": \"" + before + "\"\n}\n";

            Assert.That(
                OrpheusAudioAuthoringReports.TryReplaceInputFingerprint(
                    json,
                    before,
                    after,
                    out var updated),
                Is.True);
            StringAssert.Contains(
                "\"inputFingerprint\": \"" + after + "\"",
                updated);
            StringAssert.DoesNotContain(before, updated);

            Assert.That(
                OrpheusAudioAuthoringReports.TryReplaceInputFingerprint(
                    json + json,
                    before,
                    after,
                    out _),
                Is.False);
            Assert.That(
                OrpheusAudioAuthoringReports.TryReplaceInputFingerprint(
                    json,
                    string.Empty,
                    after,
                    out _),
                Is.False);
        }

        [Test]
        public void ClosureWriter_PreservesModuleThenNumericKeyProjectionOrder()
        {
            var actual = OrpheusAudioAuthoringReports.BuildClosure(
                string.Empty,
                string.Empty,
                Array.Empty<OrpheusAuthoringManifestEntryValue>(),
                Array.Empty<OrpheusAuthoringCompilationError>(),
                Array.Empty<OrpheusAuthoringCatalogClosureValue>(),
                Array.Empty<OrpheusAuthoringClipClosureValue>(),
                Array.Empty<OrpheusAuthoringReportArtifact>(),
                Array.Empty<OrpheusAuthoringReportArtifact>(),
                new[]
                {
                    new OrpheusAuthoringFutureDeliveryValue(
                        "ui",
                        "aaa",
                        "aaa",
                        20,
                        "TWENTY"),
                    new OrpheusAuthoringFutureDeliveryValue(
                        "ui",
                        "zzz",
                        "zzz",
                        10,
                        "TEN")
                });

            Assert.That(
                actual.IndexOf("\"key\":10", StringComparison.Ordinal),
                Is.LessThan(
                    actual.IndexOf("\"key\":20", StringComparison.Ordinal)));
        }

        [Test]
        public void ExecutionWriter_UsesExactShapeAndOrphanActions()
        {
            var actual = OrpheusAudioAuthoringReports.BuildExecution(
                OrpheusAudioAuthoringCompileStatus.SucceededChanged,
                "Standalone",
                1,
                2,
                3,
                0,
                2,
                Array.Empty<OrpheusAuthoringCompilationError>(),
                new OrpheusAuthoringProposedEnrollmentValue(
                    AuthoringGuid,
                    ValidationGuid,
                    ManifestGuid,
                    CatalogGuid,
                    Root,
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                new[]
                {
                    new OrpheusAuthoringReportChange(
                        "blocked",
                        Root + AssetPathSeparator + "Events/AE_FOREIGN.asset",
                        "",
                        "",
                        Array.Empty<OrpheusAuthoringReportFieldChange>()),
                    new OrpheusAuthoringReportChange(
                        "report",
                        Root + AssetPathSeparator + "Events/AE_OLD.asset",
                        EventGuid,
                        "",
                        new[]
                        {
                            new OrpheusAuthoringReportFieldChange("status", "Active", "Retired")
                        })
                },
                Root + AssetPathSeparator + "OrpheusAuthoringClosure.json");

            StringAssert.StartsWith(
                "{\n  \"schemaVersion\": 1,\n  \"status\": \"SucceededChanged\",\n" +
                "  \"targetBuildGroup\": \"Standalone\",\n",
                actual);
            StringAssert.Contains("\"proposedEnrollment\": {", actual);
            StringAssert.Contains(
                "{\"action\":\"blocked\",\"path\":\"Assets/GeneratedAudio/Events/AE_FOREIGN.asset\"",
                actual);
            StringAssert.Contains(
                "{\"action\":\"report\",\"path\":\"Assets/GeneratedAudio/Events/AE_OLD.asset\"",
                actual);
            StringAssert.EndsWith(
                "  \"closureReportPath\": \"Assets/GeneratedAudio/OrpheusAuthoringClosure.json\"\n}\n",
                actual);

            var deleted = OrpheusAudioAuthoringReports.BuildExecution(
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
                        Root + AssetPathSeparator + "Events/AE_\"OLD\".asset",
                        EventGuid,
                        "",
                        Array.Empty<OrpheusAuthoringReportFieldChange>())
                },
                Root + AssetPathSeparator + "OrpheusAuthoringClosure.json");
            Assert.That(
                OrpheusAudioAuthoringReports.ReplaceChangeAction(
                    deleted,
                    Root + AssetPathSeparator + "Events/AE_\"OLD\".asset",
                    "deleted",
                    "blocked"),
                Does.Contain(
                    "{\"action\":\"blocked\",\"path\":\"Assets/GeneratedAudio/Events/AE_\\\"OLD\\\".asset\""));
        }

        [Test]
        public void OwnershipValidator_RejectsMalformedOrAmbiguousLease()
        {
            var valid = OrpheusAudioAuthoringReports.BuildOwnership(
                AuthoringGuid,
                ValidationGuid,
                ManifestGuid,
                CatalogGuid,
                Root,
                new[]
                {
                    new OrpheusAuthoringManifestEntryValue(
                        10,
                        "UI_BACK",
                        OrpheusAudioKeyStatus.Active)
                },
                RequiredArtifacts());

            Assert.That(
                OrpheusAudioAuthoringReports.TryValidateOwnership(
                    valid,
                    AuthoringGuid,
                    ValidationGuid,
                    ManifestGuid,
                    CatalogGuid,
                    Root,
                    out var error),
                Is.True,
                error);

            AssertRejected(valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"));
            AssertRejected(valid.Replace("\"ownerAuthoringProfileGuid\": \"" + AuthoringGuid, "\"ownerAuthoringProfileGuid\": \"99999999999999999999999999999999"));
            AssertRejected(valid.Replace("\"kind\":\"catalog\"", "\"kind\":\"foreign\""));
            AssertRejected(valid.Replace(
                "\"path\":\"Assets/GeneratedAudio/Events/AE_UI_BACK.asset\"",
                "\"path\":\"Assets/Elsewhere/AE_UI_BACK.asset\""));
            AssertRejected(valid.Replace(
                "\"guid\":\"" + CatalogGuid + "\",\"path\":\"Assets/GeneratedAudio/OrpheusAudioCatalog.asset\"",
                "\"guid\":\"" + EventGuid + "\",\"path\":\"Assets/GeneratedAudio/OrpheusAudioCatalog.asset\""));
            AssertRejected(valid.Replace(
                "\"kind\":\"event\",\"moduleId\":\"ui\",\"symbol\":\"UI_BACK\",\"key\":10",
                "\"symbol\":\"UI_BACK\",\"moduleId\":\"ui\",\"kind\":\"event\",\"key\":10"));
        }

        [Test]
        public void CanonicalScalars_NormalizeNegativeZeroAndRejectNonFinite()
        {
            Assert.That(OrpheusAudioAuthoringReports.FormatFloatForTests(-0f), Is.EqualTo("0"));
            Assert.That(
                OrpheusAudioAuthoringReports.FormatFloatForTests(1.25f),
                Is.EqualTo("1.25"));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => OrpheusAudioAuthoringReports.FormatFloatForTests(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => OrpheusAudioAuthoringReports.FormatFloatForTests(float.PositiveInfinity));
        }

        [Test]
        public void ClosureMaterialization_BindsImportedEventGuidCanonically()
        {
            var path =
                Root + AssetPathSeparator + "Events/AE_UI_BACK.asset";
            var closure = OrpheusAudioAuthoringReports.BuildClosure(
                new string('a', 64),
                new string('b', 64),
                Array.Empty<OrpheusAuthoringManifestEntryValue>(),
                Array.Empty<OrpheusAuthoringCompilationError>(),
                new[]
                {
                    new OrpheusAuthoringCatalogClosureValue(
                        10,
                        "UI_BACK",
                        "",
                        path)
                },
                Array.Empty<OrpheusAuthoringClipClosureValue>(),
                new[]
                {
                    Artifact("event", "ui", "UI_BACK", 10, "", path)
                },
                Array.Empty<OrpheusAuthoringReportArtifact>(),
                Array.Empty<OrpheusAuthoringFutureDeliveryValue>());

            Assert.That(
                OrpheusAudioAuthoringReports.TryMaterializeClosure(
                    closure,
                    candidate => candidate == path ? EventGuid : string.Empty,
                    out var materialized),
                Is.True);
            StringAssert.Contains(
                "\"eventGuid\":\"" + EventGuid + "\",\"eventPath\":\"" +
                path + "\"",
                materialized);
            StringAssert.Contains(
                "\"guid\":\"" + EventGuid + "\",\"path\":\"" + path + "\"",
                materialized);
        }

        private static OrpheusAuthoringReportArtifact Artifact(
            string kind,
            string moduleId,
            string symbol,
            ushort key,
            string guid,
            string path)
        {
            return new OrpheusAuthoringReportArtifact(
                kind,
                moduleId,
                symbol,
                key,
                guid,
                path);
        }

        private static OrpheusAuthoringReportArtifact[] RequiredArtifacts()
        {
            return new[]
            {
                Artifact("event", "ui", "UI_BACK", 10, EventGuid, Root + AssetPathSeparator + "Events/AE_UI_BACK.asset"),
                Artifact("catalog", "", "", 0, CatalogGuid, Root + AssetPathSeparator + "OrpheusAudioCatalog.asset"),
                Artifact("typed-key-assembly", "", "", 0, "77777777777777777777777777777777", "Assets/OrpheusGenerated/Orpheus.Audio.Generated.asmdef"),
                Artifact("typed-key-source", "", "", 0, "88888888888888888888888888888888", "Assets/OrpheusGenerated/OrpheusAudioKeys.g.cs"),
                Artifact("ownership", "", "", 0, "", Root + AssetPathSeparator + "OrpheusAuthoringOwnership.json"),
                Artifact("closure", "", "", 0, "", Root + AssetPathSeparator + "OrpheusAuthoringClosure.json")
            };
        }

        private static void AssertRejected(string json)
        {
            Assert.That(
                OrpheusAudioAuthoringReports.TryValidateOwnership(
                    json,
                    AuthoringGuid,
                    ValidationGuid,
                    ManifestGuid,
                    CatalogGuid,
                    Root,
                    out _),
                Is.False);
        }
    }
}
