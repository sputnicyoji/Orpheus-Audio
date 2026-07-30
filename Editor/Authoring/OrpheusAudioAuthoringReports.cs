using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Orpheus.Audio.Core;

namespace Orpheus.Audio.Editor
{
    internal readonly struct OrpheusAuthoringReportArtifact
    {
        internal OrpheusAuthoringReportArtifact(
            string kind,
            string moduleId,
            string symbol,
            ushort key,
            string guid,
            string path)
        {
            Kind = kind ?? string.Empty;
            ModuleId = moduleId ?? string.Empty;
            Symbol = symbol ?? string.Empty;
            Key = key;
            Guid = guid ?? string.Empty;
            Path = path ?? string.Empty;
        }

        internal string Kind { get; }
        internal string ModuleId { get; }
        internal string Symbol { get; }
        internal ushort Key { get; }
        internal string Guid { get; }
        internal string Path { get; }
    }

    internal readonly struct OrpheusAuthoringCatalogClosureValue
    {
        internal OrpheusAuthoringCatalogClosureValue(
            ushort key,
            string symbol,
            string eventGuid,
            string eventPath)
        {
            Key = key;
            Symbol = symbol ?? string.Empty;
            EventGuid = eventGuid ?? string.Empty;
            EventPath = eventPath ?? string.Empty;
        }

        internal ushort Key { get; }
        internal string Symbol { get; }
        internal string EventGuid { get; }
        internal string EventPath { get; }
    }

    internal readonly struct OrpheusAuthoringClipClosureValue
    {
        internal OrpheusAuthoringClipClosureValue(
            ushort key,
            string symbol,
            int clipIndex,
            string guid,
            long localFileId,
            string path)
        {
            Key = key;
            Symbol = symbol ?? string.Empty;
            ClipIndex = clipIndex;
            Guid = guid ?? string.Empty;
            LocalFileId = localFileId;
            Path = path ?? string.Empty;
        }

        internal ushort Key { get; }
        internal string Symbol { get; }
        internal int ClipIndex { get; }
        internal string Guid { get; }
        internal long LocalFileId { get; }
        internal string Path { get; }
    }

    internal readonly struct OrpheusAuthoringReportFieldChange
    {
        internal OrpheusAuthoringReportFieldChange(
            string field,
            string before,
            string after)
        {
            Field = field ?? string.Empty;
            Before = before ?? string.Empty;
            After = after ?? string.Empty;
        }

        internal string Field { get; }
        internal string Before { get; }
        internal string After { get; }
    }

    internal sealed class OrpheusAuthoringReportChange
    {
        private readonly OrpheusAuthoringReportFieldChange[] _fieldChanges;

        internal OrpheusAuthoringReportChange(
            string action,
            string path,
            string beforeFingerprint,
            string afterFingerprint,
            OrpheusAuthoringReportFieldChange[] fieldChanges)
        {
            Action = action ?? string.Empty;
            Path = path ?? string.Empty;
            BeforeFingerprint = beforeFingerprint ?? string.Empty;
            AfterFingerprint = afterFingerprint ?? string.Empty;
            _fieldChanges = fieldChanges == null
                ? Array.Empty<OrpheusAuthoringReportFieldChange>()
                : (OrpheusAuthoringReportFieldChange[])fieldChanges.Clone();
        }

        internal string Action { get; }
        internal string Path { get; }
        internal string BeforeFingerprint { get; }
        internal string AfterFingerprint { get; }
        internal int FieldChangeCount => _fieldChanges.Length;

        internal OrpheusAuthoringReportFieldChange GetFieldChange(int index)
        {
            return _fieldChanges[index];
        }
    }

    internal readonly struct OrpheusAuthoringProposedEnrollmentValue
    {
        internal OrpheusAuthoringProposedEnrollmentValue(
            string authoringProfileGuid,
            string validationProfileGuid,
            string manifestGuid,
            string catalogGuid,
            string generatedRoot,
            string inputFingerprint)
        {
            AuthoringProfileGuid = authoringProfileGuid ?? string.Empty;
            ValidationProfileGuid = validationProfileGuid ?? string.Empty;
            ManifestGuid = manifestGuid ?? string.Empty;
            CatalogGuid = catalogGuid ?? string.Empty;
            GeneratedRoot = generatedRoot ?? string.Empty;
            InputFingerprint = inputFingerprint ?? string.Empty;
        }

        internal string AuthoringProfileGuid { get; }
        internal string ValidationProfileGuid { get; }
        internal string ManifestGuid { get; }
        internal string CatalogGuid { get; }
        internal string GeneratedRoot { get; }
        internal string InputFingerprint { get; }
    }

    internal static class OrpheusAudioAuthoringReports
    {
        private const char AssetPathSeparator = (char)47;
        private const char JsonEscapeCharacter = (char)92;
        private const string TypedKeyAssemblyPath =
            "Assets/OrpheusGenerated/Orpheus.Audio.Generated.asmdef";
        private const string TypedKeySourcePath =
            "Assets/OrpheusGenerated/OrpheusAudioKeys.g.cs";

        internal static string BuildOwnership(
            string ownerAuthoringProfileGuid,
            string enrolledValidationProfileGuid,
            string manifestGuid,
            string catalogGuid,
            string generatedRoot,
            OrpheusAuthoringManifestEntryValue[] acceptedManifestSnapshot,
            OrpheusAuthoringReportArtifact[] artifacts)
        {
            var manifest = acceptedManifestSnapshot == null
                ? Array.Empty<OrpheusAuthoringManifestEntryValue>()
                : (OrpheusAuthoringManifestEntryValue[])acceptedManifestSnapshot.Clone();
            Array.Sort(manifest, CompareManifest);
            var sortedArtifacts = artifacts == null
                ? Array.Empty<OrpheusAuthoringReportArtifact>()
                : (OrpheusAuthoringReportArtifact[])artifacts.Clone();
            Array.Sort(sortedArtifacts, CompareArtifacts);

            var builder = new StringBuilder(
                512 + manifest.Length * 80 + sortedArtifacts.Length * 180);
            builder.Append("{\n");
            AppendStringProperty(builder, 2, "schemaVersion", 1, true);
            AppendStringProperty(
                builder,
                2,
                "ownerAuthoringProfileGuid",
                ownerAuthoringProfileGuid,
                true);
            AppendStringProperty(
                builder,
                2,
                "enrolledValidationProfileGuid",
                enrolledValidationProfileGuid,
                true);
            AppendStringProperty(builder, 2, "manifestGuid", manifestGuid, true);
            AppendStringProperty(builder, 2, "catalogGuid", catalogGuid, true);
            AppendStringProperty(builder, 2, "generatedRoot", generatedRoot, true);
            builder.Append("  \"acceptedManifestSnapshot\": ");
            AppendManifestArray(builder, manifest);
            builder.Append(",\n  \"artifacts\": ");
            AppendArtifactArray(builder, sortedArtifacts);
            builder.Append("\n}\n");
            return builder.ToString();
        }

        internal static string BuildClosure(
            string inputFingerprint,
            string outputFingerprint,
            OrpheusAuthoringManifestEntryValue[] manifestSnapshot,
            OrpheusAuthoringCompilationError[] identityConflicts,
            OrpheusAuthoringCatalogClosureValue[] catalogClosure,
            OrpheusAuthoringClipClosureValue[] clipClosure,
            OrpheusAuthoringReportArtifact[] ownership,
            OrpheusAuthoringReportArtifact[] orphans,
            OrpheusAuthoringFutureDeliveryValue[] futureDeliveryProjection)
        {
            var manifest = manifestSnapshot == null
                ? Array.Empty<OrpheusAuthoringManifestEntryValue>()
                : (OrpheusAuthoringManifestEntryValue[])manifestSnapshot.Clone();
            Array.Sort(manifest, CompareManifest);
            var errors = identityConflicts == null
                ? Array.Empty<OrpheusAuthoringCompilationError>()
                : (OrpheusAuthoringCompilationError[])identityConflicts.Clone();
            Array.Sort(errors, CompareErrors);
            var catalog = catalogClosure == null
                ? Array.Empty<OrpheusAuthoringCatalogClosureValue>()
                : (OrpheusAuthoringCatalogClosureValue[])catalogClosure.Clone();
            Array.Sort(catalog, CompareCatalogClosure);
            var clips = clipClosure == null
                ? Array.Empty<OrpheusAuthoringClipClosureValue>()
                : (OrpheusAuthoringClipClosureValue[])clipClosure.Clone();
            Array.Sort(clips, CompareClipClosure);
            var owned = ownership == null
                ? Array.Empty<OrpheusAuthoringReportArtifact>()
                : (OrpheusAuthoringReportArtifact[])ownership.Clone();
            Array.Sort(owned, CompareArtifacts);
            var orphaned = orphans == null
                ? Array.Empty<OrpheusAuthoringReportArtifact>()
                : (OrpheusAuthoringReportArtifact[])orphans.Clone();
            Array.Sort(orphaned, CompareArtifacts);
            var future = futureDeliveryProjection == null
                ? Array.Empty<OrpheusAuthoringFutureDeliveryValue>()
                : (OrpheusAuthoringFutureDeliveryValue[])futureDeliveryProjection.Clone();
            Array.Sort(future, CompareFutureDelivery);

            var builder = new StringBuilder(
                768 + manifest.Length * 80 + errors.Length * 180 +
                catalog.Length * 140 + clips.Length * 160 +
                (owned.Length + orphaned.Length) * 180 + future.Length * 120);
            builder.Append("{\n");
            AppendStringProperty(builder, 2, "schemaVersion", 1, true);
            AppendStringProperty(builder, 2, "inputFingerprint", inputFingerprint, true);
            AppendStringProperty(builder, 2, "outputFingerprint", outputFingerprint, true);
            builder.Append("  \"manifestSnapshot\": ");
            AppendManifestArray(builder, manifest);
            builder.Append(",\n  \"identityConflicts\": ");
            AppendErrorArray(builder, errors);
            builder.Append(",\n  \"catalogClosure\": ");
            AppendCatalogClosureArray(builder, catalog);
            builder.Append(",\n  \"clipClosure\": ");
            AppendClipClosureArray(builder, clips);
            builder.Append(",\n  \"ownership\": ");
            AppendArtifactArray(builder, owned);
            builder.Append(",\n  \"orphans\": ");
            AppendArtifactArray(builder, orphaned);
            builder.Append(",\n  \"futureDeliveryProjection\": ");
            AppendFutureDeliveryArray(builder, future);
            builder.Append("\n}\n");
            return builder.ToString();
        }

        internal static string BuildExecution(
            OrpheusAudioAuthoringCompileStatus status,
            string targetBuildGroup,
            int generatedCount,
            int updatedCount,
            int unchangedCount,
            int rejectedCount,
            int orphanCount,
            OrpheusAuthoringCompilationError[] errors,
            OrpheusAuthoringProposedEnrollmentValue? proposedEnrollment,
            OrpheusAuthoringReportChange[] changes,
            string closureReportPath)
        {
            var sortedErrors = errors == null
                ? Array.Empty<OrpheusAuthoringCompilationError>()
                : (OrpheusAuthoringCompilationError[])errors.Clone();
            Array.Sort(sortedErrors, CompareErrors);
            var sortedChanges = changes == null
                ? Array.Empty<OrpheusAuthoringReportChange>()
                : (OrpheusAuthoringReportChange[])changes.Clone();
            Array.Sort(sortedChanges, CompareChanges);

            var builder = new StringBuilder(
                512 + sortedErrors.Length * 180 + sortedChanges.Length * 180);
            builder.Append("{\n");
            AppendStringProperty(builder, 2, "schemaVersion", 1, true);
            AppendStringProperty(builder, 2, "status", status.ToString(), true);
            AppendStringProperty(builder, 2, "targetBuildGroup", targetBuildGroup, true);
            AppendStringProperty(builder, 2, "generatedCount", generatedCount, true);
            AppendStringProperty(builder, 2, "updatedCount", updatedCount, true);
            AppendStringProperty(builder, 2, "unchangedCount", unchangedCount, true);
            AppendStringProperty(builder, 2, "rejectedCount", rejectedCount, true);
            AppendStringProperty(builder, 2, "orphanCount", orphanCount, true);
            AppendStringProperty(builder, 2, "errorCount", sortedErrors.Length, true);
            builder.Append("  \"proposedEnrollment\": ");
            if (proposedEnrollment.HasValue)
            {
                AppendProposedEnrollment(builder, proposedEnrollment.Value);
            }
            else
            {
                builder.Append("null");
            }

            builder.Append(",\n  \"changes\": ");
            AppendChangeArray(builder, sortedChanges);
            builder.Append(",\n  \"errors\": ");
            AppendErrorArray(builder, sortedErrors);
            builder.Append(",\n");
            AppendStringProperty(
                builder,
                2,
                "closureReportPath",
                closureReportPath,
                false);
            builder.Append("}\n");
            return builder.ToString();
        }

        internal static bool TryValidateOwnership(
            string json,
            string expectedAuthoringGuid,
            string expectedValidationGuid,
            string expectedManifestGuid,
            string expectedCatalogGuid,
            string expectedGeneratedRoot,
            out string error)
        {
            error = string.Empty;
            try
            {
                var root = new StrictJsonParser(json).Parse();
                RequireObjectShape(
                    root,
                    "schemaVersion",
                    "ownerAuthoringProfileGuid",
                    "enrolledValidationProfileGuid",
                    "manifestGuid",
                    "catalogGuid",
                    "generatedRoot",
                    "acceptedManifestSnapshot",
                    "artifacts");
                RequireInteger(root.Get("schemaVersion"), 1);
                RequireString(
                    root.Get("ownerAuthoringProfileGuid"),
                    expectedAuthoringGuid);
                RequireString(
                    root.Get("enrolledValidationProfileGuid"),
                    expectedValidationGuid);
                RequireString(root.Get("manifestGuid"), expectedManifestGuid);
                RequireString(root.Get("catalogGuid"), expectedCatalogGuid);
                RequireString(root.Get("generatedRoot"), expectedGeneratedRoot);
                if (!IsLowerGuid(expectedAuthoringGuid) ||
                    !IsLowerGuid(expectedValidationGuid) ||
                    !IsLowerGuid(expectedManifestGuid) ||
                    !IsLowerGuid(expectedCatalogGuid) ||
                    !IsCanonicalAssetPath(expectedGeneratedRoot))
                {
                    throw new FormatException("ExpectedIdentity");
                }

                ValidateManifest(root.Get("acceptedManifestSnapshot"));
                ValidateArtifacts(root.Get("artifacts"), expectedGeneratedRoot);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is FormatException ||
                exception is OverflowException)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static string FormatFloatForTests(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return value == 0f
                ? "0"
                : value.ToString("R", CultureInfo.InvariantCulture);
        }

        internal static bool TryReadClosureFingerprints(
            string json,
            out string inputFingerprint,
            out string outputFingerprint)
        {
            inputFingerprint = string.Empty;
            outputFingerprint = string.Empty;
            try
            {
                var root = new StrictJsonParser(json).Parse();
                RequireObjectShape(
                    root,
                    "schemaVersion",
                    "inputFingerprint",
                    "outputFingerprint",
                    "manifestSnapshot",
                    "identityConflicts",
                    "catalogClosure",
                    "clipClosure",
                    "ownership",
                    "orphans",
                    "futureDeliveryProjection");
                RequireInteger(root.Get("schemaVersion"), 1);
                inputFingerprint =
                    RequireString(root.Get("inputFingerprint"));
                outputFingerprint =
                    RequireString(root.Get("outputFingerprint"));
                if (!IsLowerHash(inputFingerprint) ||
                    !IsLowerHash(outputFingerprint))
                {
                    throw new FormatException("Fingerprint");
                }

                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is FormatException ||
                exception is OverflowException)
            {
                inputFingerprint = string.Empty;
                outputFingerprint = string.Empty;
                return false;
            }
        }

        internal static string ReplaceChangeAction(
            string executionJson,
            string path,
            string beforeAction,
            string afterAction)
        {
            var builder = new StringBuilder();
            builder.Append("{\"action\":");
            AppendJsonString(builder, beforeAction);
            builder.Append(",\"path\":");
            AppendJsonString(builder, path);
            var marker = builder.ToString();
            var source = executionJson ?? string.Empty;
            var index = source.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0 ||
                source.IndexOf(
                    marker,
                    index + marker.Length,
                    StringComparison.Ordinal) >= 0)
            {
                return source;
            }

            builder.Length = 0;
            builder.Append("{\"action\":");
            AppendJsonString(builder, afterAction);
            builder.Append(",\"path\":");
            AppendJsonString(builder, path);
            return source.Remove(index, marker.Length)
                .Insert(index, builder.ToString());
        }

        internal static bool TryReplaceInputFingerprint(
            string canonicalJson,
            string expected,
            string replacement,
            out string updated)
        {
            updated = canonicalJson ?? string.Empty;
            if (!IsLowerHash(expected) ||
                !IsLowerHash(replacement))
            {
                return false;
            }

            var marker =
                "\"inputFingerprint\": \"" + expected + "\"";
            var index = updated.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0 ||
                updated.IndexOf(
                    marker,
                    index + marker.Length,
                    StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            var value =
                "\"inputFingerprint\": \"" + replacement + "\"";
            updated = updated.Remove(index, marker.Length)
                .Insert(index, value);
            return true;
        }

        internal static bool TryMaterializeClosure(
            string canonicalJson,
            Func<string, string> guidResolver,
            out string materialized)
        {
            materialized = canonicalJson ?? string.Empty;
            if (guidResolver == null)
            {
                return false;
            }

            try
            {
                var root = new StrictJsonParser(materialized).Parse();
                RequireObjectShape(
                    root,
                    "schemaVersion",
                    "inputFingerprint",
                    "outputFingerprint",
                    "manifestSnapshot",
                    "identityConflicts",
                    "catalogClosure",
                    "clipClosure",
                    "ownership",
                    "orphans",
                    "futureDeliveryProjection");
                var catalog = root.Get("catalogClosure");
                RequireKind(catalog, JsonKind.Array);
                for (var index = 0; index < catalog.Items.Count; index++)
                {
                    var entry = catalog.Items[index];
                    RequireObjectShape(
                        entry,
                        "key",
                        "symbol",
                        "eventGuid",
                        "eventPath");
                    var path = RequireString(entry.Get("eventPath"));
                    var current = RequireString(entry.Get("eventGuid"));
                    var resolved = current.Length == 0
                        ? guidResolver(path) ?? string.Empty
                        : current;
                    if (!IsLowerGuid(resolved) ||
                        !ReplaceGuidPair(
                            ref materialized,
                            "eventGuid",
                            "eventPath",
                            path,
                            current,
                            resolved))
                    {
                        materialized = string.Empty;
                        return false;
                    }
                }

                if (!MaterializeArtifactGuids(
                        root.Get("ownership"),
                        guidResolver,
                        ref materialized) ||
                    !MaterializeArtifactGuids(
                        root.Get("orphans"),
                        guidResolver,
                        ref materialized))
                {
                    materialized = string.Empty;
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is FormatException ||
                exception is OverflowException)
            {
                materialized = string.Empty;
                return false;
            }
        }

        private static bool MaterializeArtifactGuids(
            JsonValue array,
            Func<string, string> guidResolver,
            ref string json)
        {
            RequireKind(array, JsonKind.Array);
            for (var index = 0; index < array.Items.Count; index++)
            {
                var artifact = array.Items[index];
                RequireObjectShape(
                    artifact,
                    "kind",
                    "moduleId",
                    "symbol",
                    "key",
                    "guid",
                    "path");
                var path = RequireString(artifact.Get("path"));
                var current = RequireString(artifact.Get("guid"));
                if (current.Length != 0)
                {
                    if (!IsLowerGuid(current)) return false;
                    continue;
                }

                var resolved = guidResolver(path) ?? string.Empty;
                if (resolved.Length == 0)
                {
                    continue;
                }

                if (!IsLowerGuid(resolved) ||
                    !ReplaceGuidPair(
                        ref json,
                        "guid",
                        "path",
                        path,
                        current,
                        resolved))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ReplaceGuidPair(
            ref string json,
            string guidProperty,
            string pathProperty,
            string path,
            string beforeGuid,
            string afterGuid)
        {
            if (string.Equals(beforeGuid, afterGuid, StringComparison.Ordinal))
            {
                return true;
            }

            var before = new StringBuilder();
            AppendJsonString(before, guidProperty);
            before.Append(':');
            AppendJsonString(before, beforeGuid);
            before.Append(',');
            AppendJsonString(before, pathProperty);
            before.Append(':');
            AppendJsonString(before, path);
            var marker = before.ToString();
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0 ||
                json.IndexOf(
                    marker,
                    index + marker.Length,
                    StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            var after = new StringBuilder();
            AppendJsonString(after, guidProperty);
            after.Append(':');
            AppendJsonString(after, afterGuid);
            after.Append(',');
            AppendJsonString(after, pathProperty);
            after.Append(':');
            AppendJsonString(after, path);
            json = json.Remove(index, marker.Length)
                .Insert(index, after.ToString());
            return true;
        }

        private static void ValidateManifest(JsonValue value)
        {
            RequireKind(value, JsonKind.Array);
            var ids = new HashSet<long>();
            var symbols = new HashSet<string>(StringComparer.Ordinal);
            long previous = -1;
            for (var index = 0; index < value.Items.Count; index++)
            {
                var entry = value.Items[index];
                RequireObjectShape(entry, "id", "symbol", "status");
                var id = RequireInteger(entry.Get("id"));
                var symbol = RequireString(entry.Get("symbol"));
                var status = RequireInteger(entry.Get("status"));
                if (id <= 0 || id > ushort.MaxValue ||
                    id <= previous ||
                    !ids.Add(id) ||
                    string.IsNullOrEmpty(symbol) ||
                    !symbols.Add(symbol) ||
                    status < (byte)OrpheusAudioKeyStatus.Active ||
                    status > (byte)OrpheusAudioKeyStatus.Retired)
                {
                    throw new FormatException("ManifestEntry");
                }

                previous = id;
            }
        }

        private static void ValidateArtifacts(JsonValue value, string root)
        {
            RequireKind(value, JsonKind.Array);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guids = new HashSet<string>(StringComparer.Ordinal);
            var eventKeys = new HashSet<long>();
            var last = default(OrpheusAuthoringReportArtifact);
            var hasLast = false;
            for (var index = 0; index < value.Items.Count; index++)
            {
                var item = value.Items[index];
                RequireObjectShape(
                    item,
                    "kind",
                    "moduleId",
                    "symbol",
                    "key",
                    "guid",
                    "path");
                var kind = RequireString(item.Get("kind"));
                var moduleId = RequireString(item.Get("moduleId"));
                var symbol = RequireString(item.Get("symbol"));
                var key = RequireInteger(item.Get("key"));
                var guid = RequireString(item.Get("guid"));
                var path = RequireString(item.Get("path"));
                if (!IsArtifactKind(kind) ||
                    key < 0 ||
                    key > ushort.MaxValue ||
                    !IsCanonicalAssetPath(path) ||
                    !paths.Add(path) ||
                    guid.Length != 0 && (!IsLowerGuid(guid) || !guids.Add(guid)))
                {
                    throw new FormatException("Artifact");
                }

                if (kind == "event")
                {
                    if (key == 0 ||
                        !eventKeys.Add(key) ||
                        string.IsNullOrEmpty(moduleId) ||
                        string.IsNullOrEmpty(symbol) ||
                        !string.Equals(
                            path,
                            root + AssetPathSeparator +
                            "Events/AE_" + symbol + ".asset",
                            StringComparison.Ordinal))
                    {
                        throw new FormatException("EventArtifact");
                    }
                }
                else
                {
                    if (key != 0 ||
                        moduleId.Length != 0 ||
                        symbol.Length != 0 ||
                        !IsExactNonEventPath(kind, path, root))
                    {
                        throw new FormatException("NonEventArtifact");
                    }
                }

                var current = new OrpheusAuthoringReportArtifact(
                    kind,
                    moduleId,
                    symbol,
                    (ushort)key,
                    guid,
                    path);
                if (hasLast && CompareArtifacts(last, current) >= 0)
                {
                    throw new FormatException("ArtifactOrder");
                }

                last = current;
                hasLast = true;
            }
        }

        private static bool IsExactNonEventPath(
            string kind,
            string path,
            string root)
        {
            switch (kind)
            {
                case "catalog":
                    return path ==
                           root + AssetPathSeparator +
                           "OrpheusAudioCatalog.asset";
                case "typed-key-assembly":
                    return path == TypedKeyAssemblyPath;
                case "typed-key-source":
                    return path == TypedKeySourcePath;
                case "ownership":
                    return path ==
                           root + AssetPathSeparator +
                           "OrpheusAuthoringOwnership.json";
                case "closure":
                    return path ==
                           root + AssetPathSeparator +
                           "OrpheusAuthoringClosure.json";
                default:
                    return false;
            }
        }

        private static void AppendManifestArray(
            StringBuilder builder,
            OrpheusAuthoringManifestEntryValue[] values)
        {
            AppendArrayStart(builder, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                if (index != 0) builder.Append(",\n");
                var value = values[index];
                builder.Append("    {\"id\":");
                AppendInteger(builder, value.Key);
                builder.Append(",\"symbol\":");
                AppendJsonString(builder, value.Symbol);
                builder.Append(",\"status\":");
                AppendInteger(builder, (byte)value.Status);
                builder.Append('}');
            }

            AppendArrayEnd(builder, values.Length);
        }

        private static void AppendArtifactArray(
            StringBuilder builder,
            OrpheusAuthoringReportArtifact[] values)
        {
            AppendArrayStart(builder, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                if (index != 0) builder.Append(",\n");
                var value = values[index];
                builder.Append("    {\"kind\":");
                AppendJsonString(builder, value.Kind);
                builder.Append(",\"moduleId\":");
                AppendJsonString(builder, value.ModuleId);
                builder.Append(",\"symbol\":");
                AppendJsonString(builder, value.Symbol);
                builder.Append(",\"key\":");
                AppendInteger(builder, value.Key);
                builder.Append(",\"guid\":");
                AppendJsonString(builder, value.Guid);
                builder.Append(",\"path\":");
                AppendJsonString(builder, value.Path);
                builder.Append('}');
            }

            AppendArrayEnd(builder, values.Length);
        }

        private static void AppendErrorArray(
            StringBuilder builder,
            OrpheusAuthoringCompilationError[] values)
        {
            AppendArrayStart(builder, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                if (index != 0) builder.Append(",\n");
                var value = values[index];
                builder.Append("    {\"code\":");
                AppendInteger(builder, (ushort)value.Code);
                builder.Append(",\"key\":");
                AppendInteger(builder, value.Key);
                builder.Append(",\"moduleId\":");
                AppendJsonString(builder, value.ModuleId);
                builder.Append(",\"symbol\":");
                AppendJsonString(builder, value.Symbol);
                builder.Append(",\"assetPath\":");
                AppendJsonString(builder, value.AssetPath);
                builder.Append(",\"clipIndex\":");
                AppendInteger(builder, value.ClipIndex);
                builder.Append(",\"detail\":");
                AppendJsonString(builder, value.Detail);
                builder.Append('}');
            }

            AppendArrayEnd(builder, values.Length);
        }

        private static void AppendCatalogClosureArray(
            StringBuilder builder,
            OrpheusAuthoringCatalogClosureValue[] values)
        {
            AppendArrayStart(builder, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                if (index != 0) builder.Append(",\n");
                var value = values[index];
                builder.Append("    {\"key\":");
                AppendInteger(builder, value.Key);
                builder.Append(",\"symbol\":");
                AppendJsonString(builder, value.Symbol);
                builder.Append(",\"eventGuid\":");
                AppendJsonString(builder, value.EventGuid);
                builder.Append(",\"eventPath\":");
                AppendJsonString(builder, value.EventPath);
                builder.Append('}');
            }

            AppendArrayEnd(builder, values.Length);
        }

        private static void AppendClipClosureArray(
            StringBuilder builder,
            OrpheusAuthoringClipClosureValue[] values)
        {
            AppendArrayStart(builder, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                if (index != 0) builder.Append(",\n");
                var value = values[index];
                builder.Append("    {\"key\":");
                AppendInteger(builder, value.Key);
                builder.Append(",\"symbol\":");
                AppendJsonString(builder, value.Symbol);
                builder.Append(",\"clipIndex\":");
                AppendInteger(builder, value.ClipIndex);
                builder.Append(",\"guid\":");
                AppendJsonString(builder, value.Guid);
                builder.Append(",\"localFileId\":");
                AppendInteger(builder, value.LocalFileId);
                builder.Append(",\"path\":");
                AppendJsonString(builder, value.Path);
                builder.Append('}');
            }

            AppendArrayEnd(builder, values.Length);
        }

        private static void AppendFutureDeliveryArray(
            StringBuilder builder,
            OrpheusAuthoringFutureDeliveryValue[] values)
        {
            AppendArrayStart(builder, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                if (index != 0) builder.Append(",\n");
                var value = values[index];
                builder.Append("    {\"moduleId\":");
                AppendJsonString(builder, value.ModuleId);
                builder.Append(",\"profileHint\":");
                AppendJsonString(builder, value.ProfileHint);
                builder.Append(",\"candidateContentBankId\":");
                AppendJsonString(builder, value.CandidateContentBankId);
                builder.Append(",\"key\":");
                AppendInteger(builder, value.Key);
                builder.Append(",\"symbol\":");
                AppendJsonString(builder, value.Symbol);
                builder.Append('}');
            }

            AppendArrayEnd(builder, values.Length);
        }

        private static void AppendChangeArray(
            StringBuilder builder,
            OrpheusAuthoringReportChange[] values)
        {
            AppendArrayStart(builder, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                if (index != 0) builder.Append(",\n");
                var value = values[index];
                builder.Append("    {\"action\":");
                AppendJsonString(builder, value.Action);
                builder.Append(",\"path\":");
                AppendJsonString(builder, value.Path);
                builder.Append(",\"beforeFingerprint\":");
                AppendJsonString(builder, value.BeforeFingerprint);
                builder.Append(",\"afterFingerprint\":");
                AppendJsonString(builder, value.AfterFingerprint);
                builder.Append(",\"fieldChanges\":");
                if (value.FieldChangeCount == 0)
                {
                    builder.Append("[]}");
                    continue;
                }

                builder.Append('[');
                for (var fieldIndex = 0;
                     fieldIndex < value.FieldChangeCount;
                     fieldIndex++)
                {
                    if (fieldIndex != 0) builder.Append(',');
                    var field = value.GetFieldChange(fieldIndex);
                    builder.Append("{\"field\":");
                    AppendJsonString(builder, field.Field);
                    builder.Append(",\"before\":");
                    AppendJsonString(builder, field.Before);
                    builder.Append(",\"after\":");
                    AppendJsonString(builder, field.After);
                    builder.Append('}');
                }

                builder.Append("]}");
            }

            AppendArrayEnd(builder, values.Length);
        }

        private static void AppendProposedEnrollment(
            StringBuilder builder,
            OrpheusAuthoringProposedEnrollmentValue value)
        {
            builder.Append("{\n");
            AppendStringProperty(
                builder,
                4,
                "authoringProfileGuid",
                value.AuthoringProfileGuid,
                true);
            AppendStringProperty(
                builder,
                4,
                "validationProfileGuid",
                value.ValidationProfileGuid,
                true);
            AppendStringProperty(builder, 4, "manifestGuid", value.ManifestGuid, true);
            AppendStringProperty(builder, 4, "catalogGuid", value.CatalogGuid, true);
            AppendStringProperty(builder, 4, "generatedRoot", value.GeneratedRoot, true);
            AppendStringProperty(
                builder,
                4,
                "inputFingerprint",
                value.InputFingerprint,
                false);
            builder.Append("  }");
        }

        private static void AppendStringProperty(
            StringBuilder builder,
            int spaces,
            string name,
            string value,
            bool comma)
        {
            builder.Append(' ', spaces);
            AppendJsonString(builder, name);
            builder.Append(": ");
            AppendJsonString(builder, value);
            builder.Append(comma ? ",\n" : "\n");
        }

        private static void AppendStringProperty(
            StringBuilder builder,
            int spaces,
            string name,
            int value,
            bool comma)
        {
            builder.Append(' ', spaces);
            AppendJsonString(builder, name);
            builder.Append(": ");
            AppendInteger(builder, value);
            builder.Append(comma ? ",\n" : "\n");
        }

        private static void AppendArrayStart(StringBuilder builder, int count)
        {
            builder.Append('[');
            if (count != 0) builder.Append('\n');
        }

        private static void AppendArrayEnd(StringBuilder builder, int count)
        {
            if (count != 0) builder.Append("\n  ");
            builder.Append(']');
        }

        private static void AppendInteger(StringBuilder builder, long value)
        {
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            value = value ?? string.Empty;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '"':
                        builder.Append(JsonEscapeCharacter);
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append(JsonEscapeCharacter);
                        builder.Append(JsonEscapeCharacter);
                        break;
                    case '\b':
                        builder.Append(JsonEscapeCharacter);
                        builder.Append('b');
                        break;
                    case '\f':
                        builder.Append(JsonEscapeCharacter);
                        builder.Append('f');
                        break;
                    case '\n':
                        builder.Append(JsonEscapeCharacter);
                        builder.Append('n');
                        break;
                    case '\r':
                        builder.Append(JsonEscapeCharacter);
                        builder.Append('r');
                        break;
                    case '\t':
                        builder.Append(JsonEscapeCharacter);
                        builder.Append('t');
                        break;
                    default:
                        if (character < 0x20 ||
                            char.IsSurrogate(character))
                        {
                            builder.Append(JsonEscapeCharacter);
                            builder.Append('u');
                            builder.Append(
                                ((int)character).ToString(
                                    "x4",
                                    CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private static int CompareManifest(
            OrpheusAuthoringManifestEntryValue left,
            OrpheusAuthoringManifestEntryValue right)
        {
            var result = left.Key.CompareTo(right.Key);
            if (result != 0) return result;
            result = string.Compare(left.Symbol, right.Symbol, StringComparison.Ordinal);
            return result != 0
                ? result
                : ((byte)left.Status).CompareTo((byte)right.Status);
        }

        private static int CompareArtifacts(
            OrpheusAuthoringReportArtifact left,
            OrpheusAuthoringReportArtifact right)
        {
            var result = ArtifactRank(left.Kind).CompareTo(ArtifactRank(right.Kind));
            if (result != 0) return result;
            result = left.Key.CompareTo(right.Key);
            if (result != 0) return result;
            result = string.Compare(left.ModuleId, right.ModuleId, StringComparison.Ordinal);
            if (result != 0) return result;
            result = string.Compare(left.Symbol, right.Symbol, StringComparison.Ordinal);
            if (result != 0) return result;
            result = string.Compare(left.Guid, right.Guid, StringComparison.Ordinal);
            return result != 0
                ? result
                : string.Compare(left.Path, right.Path, StringComparison.Ordinal);
        }

        private static int CompareErrors(
            OrpheusAuthoringCompilationError left,
            OrpheusAuthoringCompilationError right)
        {
            var result = ((ushort)left.Code).CompareTo((ushort)right.Code);
            if (result != 0) return result;
            result = left.Key.CompareTo(right.Key);
            if (result != 0) return result;
            result = string.Compare(left.ModuleId, right.ModuleId, StringComparison.Ordinal);
            if (result != 0) return result;
            result = string.Compare(left.Symbol, right.Symbol, StringComparison.Ordinal);
            if (result != 0) return result;
            result = string.Compare(left.AssetPath, right.AssetPath, StringComparison.Ordinal);
            if (result != 0) return result;
            result = left.ClipIndex.CompareTo(right.ClipIndex);
            return result != 0
                ? result
                : string.Compare(left.Detail, right.Detail, StringComparison.Ordinal);
        }

        private static int CompareCatalogClosure(
            OrpheusAuthoringCatalogClosureValue left,
            OrpheusAuthoringCatalogClosureValue right)
        {
            var result = left.Key.CompareTo(right.Key);
            return result != 0
                ? result
                : string.Compare(left.Symbol, right.Symbol, StringComparison.Ordinal);
        }

        private static int CompareClipClosure(
            OrpheusAuthoringClipClosureValue left,
            OrpheusAuthoringClipClosureValue right)
        {
            var result = left.Key.CompareTo(right.Key);
            if (result != 0) return result;
            result = string.Compare(left.Symbol, right.Symbol, StringComparison.Ordinal);
            return result != 0
                ? result
                : left.ClipIndex.CompareTo(right.ClipIndex);
        }

        private static int CompareFutureDelivery(
            OrpheusAuthoringFutureDeliveryValue left,
            OrpheusAuthoringFutureDeliveryValue right)
        {
            var result = string.Compare(
                left.ModuleId,
                right.ModuleId,
                StringComparison.Ordinal);
            if (result != 0) return result;
            result = left.Key.CompareTo(right.Key);
            if (result != 0) return result;
            result = string.Compare(
                left.Symbol,
                right.Symbol,
                StringComparison.Ordinal);
            if (result != 0) return result;
            result = string.Compare(
                left.ProfileHint,
                right.ProfileHint,
                StringComparison.Ordinal);
            if (result != 0) return result;
            result = string.Compare(
                left.CandidateContentBankId,
                right.CandidateContentBankId,
                StringComparison.Ordinal);
            return result;
        }

        private static int CompareChanges(
            OrpheusAuthoringReportChange left,
            OrpheusAuthoringReportChange right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            var result = string.Compare(left.Path, right.Path, StringComparison.Ordinal);
            return result != 0
                ? result
                : string.Compare(left.Action, right.Action, StringComparison.Ordinal);
        }

        private static int ArtifactRank(string kind)
        {
            switch (kind)
            {
                case "event": return 0;
                case "catalog": return 1;
                case "typed-key-assembly": return 2;
                case "typed-key-source": return 3;
                case "ownership": return 4;
                case "closure": return 5;
                default: return int.MaxValue;
            }
        }

        private static bool IsArtifactKind(string value)
        {
            return ArtifactRank(value) != int.MaxValue;
        }

        private static bool IsLowerGuid(string value)
        {
            return value != null && value.Length == 32 && IsLowerHex(value);
        }

        private static bool IsLowerHash(string value)
        {
            return value != null && value.Length == 64 && IsLowerHex(value);
        }

        private static bool IsLowerHex(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!(character >= '0' && character <= '9') &&
                    !(character >= 'a' && character <= 'f'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsCanonicalAssetPath(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                !value.StartsWith("Assets/", StringComparison.Ordinal) ||
                value[value.Length - 1] == AssetPathSeparator ||
                value.IndexOf('\\') >= 0 ||
                value.IndexOf(':') >= 0 ||
                value.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            var segments = value.Split(AssetPathSeparator);
            for (var index = 0; index < segments.Length; index++)
            {
                if (segments[index].Length == 0 ||
                    segments[index] == "." ||
                    segments[index] == "..")
                {
                    return false;
                }
            }

            return true;
        }

        private static void RequireObjectShape(
            JsonValue value,
            params string[] expected)
        {
            RequireKind(value, JsonKind.Object);
            if (value.Properties.Count != expected.Length)
            {
                throw new FormatException("ObjectShape");
            }

            for (var index = 0; index < expected.Length; index++)
            {
                if (!string.Equals(
                        value.Properties[index].Name,
                        expected[index],
                        StringComparison.Ordinal))
                {
                    throw new FormatException("PropertyOrder");
                }
            }
        }

        private static void RequireKind(JsonValue value, JsonKind kind)
        {
            if (value == null || value.Kind != kind)
            {
                throw new FormatException("JsonKind");
            }
        }

        private static string RequireString(JsonValue value)
        {
            RequireKind(value, JsonKind.String);
            return value.Text;
        }

        private static void RequireString(JsonValue value, string expected)
        {
            if (!string.Equals(
                    RequireString(value),
                    expected ?? string.Empty,
                    StringComparison.Ordinal))
            {
                throw new FormatException("StringValue");
            }
        }

        private static long RequireInteger(JsonValue value)
        {
            RequireKind(value, JsonKind.Number);
            if (value.Text.Length == 0 ||
                value.Text[0] == '+' ||
                value.Text.Length > 1 && value.Text[0] == '0' ||
                value.Text.Length > 2 &&
                value.Text[0] == '-' &&
                value.Text[1] == '0' ||
                value.Text.IndexOf('.') >= 0 ||
                value.Text.IndexOf('e') >= 0 ||
                value.Text.IndexOf('E') >= 0 ||
                !long.TryParse(
                    value.Text,
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var result))
            {
                throw new FormatException("Integer");
            }

            return result;
        }

        private static void RequireInteger(JsonValue value, long expected)
        {
            if (RequireInteger(value) != expected)
            {
                throw new FormatException("IntegerValue");
            }
        }

        private enum JsonKind : byte
        {
            Object,
            Array,
            String,
            Number,
            True,
            False,
            Null
        }

        private sealed class JsonProperty
        {
            internal JsonProperty(string name, JsonValue value)
            {
                Name = name;
                Value = value;
            }

            internal string Name { get; }
            internal JsonValue Value { get; }
        }

        private sealed class JsonValue
        {
            internal JsonValue(JsonKind kind, string text = "")
            {
                Kind = kind;
                Text = text ?? string.Empty;
                Properties = new List<JsonProperty>();
                Items = new List<JsonValue>();
            }

            internal JsonKind Kind { get; }
            internal string Text { get; }
            internal List<JsonProperty> Properties { get; }
            internal List<JsonValue> Items { get; }

            internal JsonValue Get(string name)
            {
                for (var index = 0; index < Properties.Count; index++)
                {
                    if (string.Equals(
                            Properties[index].Name,
                            name,
                            StringComparison.Ordinal))
                    {
                        return Properties[index].Value;
                    }
                }

                throw new FormatException("MissingProperty");
            }
        }

        private sealed class StrictJsonParser
        {
            private readonly string _text;
            private int _index;

            internal StrictJsonParser(string text)
            {
                _text = text ?? throw new ArgumentNullException(nameof(text));
            }

            internal JsonValue Parse()
            {
                SkipWhite();
                var value = ParseValue();
                SkipWhite();
                if (_index != _text.Length)
                {
                    throw new FormatException("TrailingJson");
                }

                return value;
            }

            private JsonValue ParseValue()
            {
                if (_index >= _text.Length)
                {
                    throw new FormatException("UnexpectedEnd");
                }

                switch (_text[_index])
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return new JsonValue(JsonKind.String, ParseString());
                    case 't':
                        ReadLiteral("true");
                        return new JsonValue(JsonKind.True);
                    case 'f':
                        ReadLiteral("false");
                        return new JsonValue(JsonKind.False);
                    case 'n':
                        ReadLiteral("null");
                        return new JsonValue(JsonKind.Null);
                    default:
                        return ParseNumber();
                }
            }

            private JsonValue ParseObject()
            {
                _index++;
                var result = new JsonValue(JsonKind.Object);
                var names = new HashSet<string>(StringComparer.Ordinal);
                SkipWhite();
                if (TryRead('}')) return result;
                while (true)
                {
                    SkipWhite();
                    var name = ParseString();
                    if (!names.Add(name))
                    {
                        throw new FormatException("DuplicateProperty");
                    }

                    SkipWhite();
                    Read(':');
                    SkipWhite();
                    result.Properties.Add(new JsonProperty(name, ParseValue()));
                    SkipWhite();
                    if (TryRead('}')) return result;
                    Read(',');
                }
            }

            private JsonValue ParseArray()
            {
                _index++;
                var result = new JsonValue(JsonKind.Array);
                SkipWhite();
                if (TryRead(']')) return result;
                while (true)
                {
                    SkipWhite();
                    result.Items.Add(ParseValue());
                    SkipWhite();
                    if (TryRead(']')) return result;
                    Read(',');
                }
            }

            private string ParseString()
            {
                Read('"');
                var builder = new StringBuilder();
                while (_index < _text.Length)
                {
                    var character = _text[_index++];
                    if (character == '"') return builder.ToString();
                    if (character < 0x20)
                    {
                        throw new FormatException("ControlCharacter");
                    }

                    if (character != '\\')
                    {
                        builder.Append(character);
                        continue;
                    }

                    if (_index >= _text.Length)
                    {
                        throw new FormatException("StringEscape");
                    }

                    var escaped = _text[_index++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case AssetPathSeparator:
                            builder.Append(AssetPathSeparator);
                            break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            builder.Append(ParseUnicode());
                            break;
                        default:
                            throw new FormatException("StringEscape");
                    }
                }

                throw new FormatException("UnterminatedString");
            }

            private char ParseUnicode()
            {
                if (_index + 4 > _text.Length)
                {
                    throw new FormatException("UnicodeEscape");
                }

                var value = 0;
                for (var offset = 0; offset < 4; offset++)
                {
                    var character = _text[_index++];
                    value <<= 4;
                    if (character >= '0' && character <= '9')
                    {
                        value += character - '0';
                    }
                    else if (character >= 'a' && character <= 'f')
                    {
                        value += character - 'a' + 10;
                    }
                    else if (character >= 'A' && character <= 'F')
                    {
                        value += character - 'A' + 10;
                    }
                    else
                    {
                        throw new FormatException("UnicodeEscape");
                    }
                }

                return (char)value;
            }

            private JsonValue ParseNumber()
            {
                var start = _index;
                while (_index < _text.Length)
                {
                    var character = _text[_index];
                    if ((character >= '0' && character <= '9') ||
                        character == '-' ||
                        character == '+' ||
                        character == '.' ||
                        character == 'e' ||
                        character == 'E')
                    {
                        _index++;
                        continue;
                    }

                    break;
                }

                if (_index == start)
                {
                    throw new FormatException("Number");
                }

                return new JsonValue(
                    JsonKind.Number,
                    _text.Substring(start, _index - start));
            }

            private void ReadLiteral(string value)
            {
                if (_index + value.Length > _text.Length ||
                    !string.Equals(
                        _text.Substring(_index, value.Length),
                        value,
                        StringComparison.Ordinal))
                {
                    throw new FormatException("Literal");
                }

                _index += value.Length;
            }

            private void SkipWhite()
            {
                while (_index < _text.Length)
                {
                    var character = _text[_index];
                    if (character != ' ' &&
                        character != '\t' &&
                        character != '\r' &&
                        character != '\n')
                    {
                        return;
                    }

                    _index++;
                }
            }

            private bool TryRead(char value)
            {
                if (_index < _text.Length && _text[_index] == value)
                {
                    _index++;
                    return true;
                }

                return false;
            }

            private void Read(char value)
            {
                if (!TryRead(value))
                {
                    throw new FormatException("ExpectedCharacter");
                }
            }
        }
    }
}
