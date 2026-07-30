using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace Orpheus.Audio.Editor
{
    internal enum OrpheusAudioAuthoringCompileMode : byte
    {
        Invalid = 0,
        Analyze = 1,
        Compile = 2,
        CompileAndAcceptEnrollment = 3,
        CompileAndDeleteTrackedOrphans = 4
    }

    internal enum OrpheusAudioAuthoringCompileStatus : byte
    {
        Invalid = 0,
        SucceededUnchanged = 1,
        SucceededChanged = 2,
        Rejected = 3,
        RolledBack = 4,
        RollbackFailed = 5
    }

    internal readonly struct OrpheusAudioAuthoringCompileResult
    {
        internal OrpheusAudioAuthoringCompileResult(
            OrpheusAudioAuthoringCompileStatus status,
            int generatedCount,
            int updatedCount,
            int unchangedCount,
            int rejectedCount,
            int orphanCount,
            int errorCount,
            string reportPath)
        {
            Status = status;
            GeneratedCount = generatedCount;
            UpdatedCount = updatedCount;
            UnchangedCount = unchangedCount;
            RejectedCount = rejectedCount;
            OrphanCount = orphanCount;
            ErrorCount = errorCount;
            ReportPath = reportPath ?? string.Empty;
        }

        internal OrpheusAudioAuthoringCompileStatus Status { get; }

        internal int GeneratedCount { get; }

        internal int UpdatedCount { get; }

        internal int UnchangedCount { get; }

        internal int RejectedCount { get; }

        internal int OrphanCount { get; }

        internal int ErrorCount { get; }

        internal string ReportPath { get; }
    }

    internal static class OrpheusAudioAuthoringCompiler
    {
        private const char AssetPathSeparator = (char)47;
        internal static OrpheusAudioAuthoringCompileResult Run(
            OrpheusAudioValidationProfile profile,
            OrpheusAudioAuthoringCompileMode mode,
            BuildTargetGroup targetGroup)
        {
            return Run(profile, mode, targetGroup, null);
        }

        internal static OrpheusAudioAuthoringCompileResult Run(
            OrpheusAudioValidationProfile profile,
            OrpheusAudioAuthoringCompileMode mode,
            BuildTargetGroup targetGroup,
            OrpheusAudioValidationProfile[] discoveredProfiles)
        {
            return Run(
                profile,
                mode,
                targetGroup,
                discoveredProfiles,
                default(OrpheusAudioAuthoringCommitArtifacts));
        }

        internal static OrpheusAudioAuthoringCompileResult Run(
            OrpheusAudioValidationProfile profile,
            OrpheusAudioAuthoringCompileMode mode,
            BuildTargetGroup targetGroup,
            OrpheusAudioValidationProfile[] discoveredProfiles,
            OrpheusAudioAuthoringCommitArtifacts artifacts)
        {
            var projectRoot =
                Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            var lockPath = Path.Combine(
                projectRoot,
                "Library",
                "Orpheus",
                "AuthoringTransactions",
                "compile.lock");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lockPath));
                using (new FileStream(
                           lockPath,
                           FileMode.OpenOrCreate,
                           FileAccess.ReadWrite,
                           FileShare.None))
                {
                    return RunLocked(
                        profile,
                        mode,
                        targetGroup,
                        discoveredProfiles,
                        artifacts);
                }
            }
            catch (IOException)
            {
                return new OrpheusAudioAuthoringCompileResult(
                    OrpheusAudioAuthoringCompileStatus.Rejected,
                    0,
                    0,
                    0,
                    1,
                    0,
                    1,
                    string.Empty);
            }
            catch (UnauthorizedAccessException)
            {
                return new OrpheusAudioAuthoringCompileResult(
                    OrpheusAudioAuthoringCompileStatus.Rejected,
                    0,
                    0,
                    0,
                    1,
                    0,
                    1,
                    string.Empty);
            }
        }

        private static OrpheusAudioAuthoringCompileResult RunLocked(
            OrpheusAudioValidationProfile profile,
            OrpheusAudioAuthoringCompileMode mode,
            BuildTargetGroup targetGroup,
            OrpheusAudioValidationProfile[] discoveredProfiles,
            OrpheusAudioAuthoringCommitArtifacts artifacts)
        {
            if (mode != OrpheusAudioAuthoringCompileMode.Analyze &&
                !OrpheusAudioAuthoringTransaction.TryRecoverInterrupted())
            {
                var recoveryErrors = new[]
                {
                    new OrpheusAuthoringCompilationError(
                        OrpheusAuthoringErrorCode.RollbackFailed,
                        0,
                        string.Empty,
                        string.Empty,
                        AssetDatabase.GetAssetPath(profile),
                        -1,
                        string.Empty)
                };
                var recoveryReportPath = WriteTerminalExecutionReport(
                    profile,
                    targetGroup,
                    OrpheusAudioAuthoringCompileStatus.RollbackFailed,
                    recoveryErrors);
                return new OrpheusAudioAuthoringCompileResult(
                    OrpheusAudioAuthoringCompileStatus.RollbackFailed,
                    0, 0, 0, 1, 0, 1, recoveryReportPath);
            }

            if (!OrpheusAudioAuthoringCapture.TryCapture(
                    profile,
                    mode,
                    targetGroup,
                    discoveredProfiles,
                    out var input,
                    out var plan,
                    out var errors))
            {
                if (errors == null || errors.Length == 0)
                {
                    errors = new[]
                    {
                        new OrpheusAuthoringCompilationError(
                            OrpheusAuthoringErrorCode.InvalidValidationProfile,
                            0,
                            string.Empty,
                            string.Empty,
                            AssetDatabase.GetAssetPath(profile),
                            -1,
                            string.Empty)
                    };
                }

                var reportPath = WriteTerminalExecutionReport(
                    profile,
                    targetGroup,
                    OrpheusAudioAuthoringCompileStatus.Rejected,
                    errors);
                return new OrpheusAudioAuthoringCompileResult(
                    OrpheusAudioAuthoringCompileStatus.Rejected,
                    0,
                    0,
                    0,
                    errors.Length,
                    0,
                    errors.Length,
                    reportPath);
            }

            if (mode == OrpheusAudioAuthoringCompileMode.Analyze)
            {
                var generated = 0;
                var updated = 0;
                var unchanged = 0;
                var orphans = plan.OrphanCount;
                for (var index = 0; index < plan.WriteOperationCount; index++)
                {
                    switch (plan.GetWriteOperation(index).Kind)
                    {
                        case OrpheusAuthoringWriteKind.CreateEvent:
                            generated++;
                            break;
                        case OrpheusAuthoringWriteKind.UpdateEvent:
                        case OrpheusAuthoringWriteKind.MoveRenamedEvent:
                            updated++;
                            break;
                        case OrpheusAuthoringWriteKind.KeepEvent:
                            unchanged++;
                            break;
                    }
                }

                var analysisStatus = generated + updated + orphans == 0
                    ? OrpheusAudioAuthoringCompileStatus.SucceededUnchanged
                    : OrpheusAudioAuthoringCompileStatus.SucceededChanged;
                var reportPath = WriteExecutionReport(
                    profile,
                    targetGroup,
                    input,
                    plan,
                    analysisStatus,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    generated,
                    updated,
                    unchanged,
                    0,
                    orphans,
                    Array.Empty<OrpheusAuthoringCompilationError>(),
                    string.Empty);
                return new OrpheusAudioAuthoringCompileResult(
                    analysisStatus,
                    generated,
                    updated,
                    unchanged,
                    0,
                    orphans,
                    0,
                    reportPath);
            }

            var generatedArtifacts =
                string.IsNullOrEmpty(artifacts.OwnershipJson) ||
                string.IsNullOrEmpty(artifacts.ClosureJson);
            if (generatedArtifacts)
            {
                artifacts = BuildCommitArtifacts(
                    profile,
                    mode,
                    targetGroup,
                    input,
                    plan);
            }

            var transactionStatus = OrpheusAudioAuthoringTransaction.Execute(
                profile,
                plan,
                artifacts,
                targetGroup,
                discoveredProfiles);
            if (transactionStatus ==
                    OrpheusAudioAuthoringCompileStatus.SucceededChanged ||
                transactionStatus ==
                    OrpheusAudioAuthoringCompileStatus.SucceededUnchanged)
            {
                transactionStatus = GetSuccessStatus(input, plan);
            }

            if (transactionStatus !=
                    OrpheusAudioAuthoringCompileStatus.SucceededChanged &&
                transactionStatus !=
                    OrpheusAudioAuthoringCompileStatus.SucceededUnchanged)
            {
                var transactionErrors = transactionStatus ==
                                        OrpheusAudioAuthoringCompileStatus.RollbackFailed
                    ? new[]
                    {
                        new OrpheusAuthoringCompilationError(
                            OrpheusAuthoringErrorCode.RollbackFailed,
                            0,
                            string.Empty,
                            string.Empty,
                            AssetDatabase.GetAssetPath(profile),
                            -1,
                            string.Empty)
                    }
                    : Array.Empty<OrpheusAuthoringCompilationError>();
                var failureReportPath = WriteExecutionReport(
                    profile,
                    targetGroup,
                    input,
                    plan,
                    transactionStatus,
                    mode,
                    0,
                    0,
                    0,
                    1,
                    plan.OrphanCount,
                    transactionErrors,
                    string.Empty);

                return new OrpheusAudioAuthoringCompileResult(
                    transactionStatus,
                    0, 0, 0, 1,
                    plan.OrphanCount,
                    transactionErrors.Length,
                    failureReportPath);
            }

            var writeGenerated = 0;
            var writeUpdated = 0;
            var writeUnchanged = 0;
            var writeOrphans = plan.OrphanCount;
            for (var index = 0; index < plan.WriteOperationCount; index++)
            {
                switch (plan.GetWriteOperation(index).Kind)
                {
                    case OrpheusAuthoringWriteKind.CreateEvent:
                        writeGenerated++;
                        break;
                    case OrpheusAuthoringWriteKind.UpdateEvent:
                    case OrpheusAuthoringWriteKind.MoveRenamedEvent:
                        writeUpdated++;
                        break;
                    case OrpheusAuthoringWriteKind.KeepEvent:
                        writeUnchanged++;
                        break;
                }
            }

            return new OrpheusAudioAuthoringCompileResult(
                transactionStatus,
                writeGenerated,
                writeUpdated,
                writeUnchanged,
                0,
                writeOrphans,
                0,
                artifacts.ExecutionPath);

        }

        private static OrpheusAudioAuthoringCommitArtifacts BuildCommitArtifacts(
            OrpheusAudioValidationProfile profile,
            OrpheusAudioAuthoringCompileMode mode,
            BuildTargetGroup targetGroup,
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringCompilationPlan plan)
        {
            CountOperations(
                plan,
                out var generated,
                out var updated,
                out var unchanged);
            var validationGuid = GetGuid(AssetDatabase.GetAssetPath(profile));
            var authoringGuid = GetGuid(
                AssetDatabase.GetAssetPath(profile.AuthoringProfile));
            var manifestGuid = GetGuid(
                AssetDatabase.GetAssetPath(profile.KeyManifest));
            var manifest = CaptureManifest(input);
            var ownedArtifacts = BuildOwnedArtifacts(
                input,
                plan,
                mode !=
                OrpheusAudioAuthoringCompileMode.CompileAndDeleteTrackedOrphans);
            var ownership = OrpheusAudioAuthoringReports.BuildOwnership(
                authoringGuid,
                validationGuid,
                manifestGuid,
                plan.CatalogGuid,
                input.GeneratedRoot,
                manifest,
                ownedArtifacts);
            var closure = OrpheusAudioAuthoringReports.BuildClosure(
                plan.InputFingerprint,
                plan.OutputFingerprint,
                manifest,
                Array.Empty<OrpheusAuthoringCompilationError>(),
                BuildCatalogClosure(plan),
                BuildClipClosure(plan),
                ownedArtifacts,
                mode ==
                OrpheusAudioAuthoringCompileMode.CompileAndDeleteTrackedOrphans
                    ? Array.Empty<OrpheusAuthoringReportArtifact>()
                    : BuildOrphanArtifacts(plan),
                CaptureFutureDelivery(plan));
            var reportPath = GetExecutionReportPath(validationGuid);
            var successStatus = GetSuccessStatus(input, plan);
            var execution = OrpheusAudioAuthoringReports.BuildExecution(
                successStatus,
                targetGroup.ToString(),
                generated,
                updated,
                unchanged,
                0,
                plan.OrphanCount,
                Array.Empty<OrpheusAuthoringCompilationError>(),
                BuildProposedEnrollment(profile, input, plan),
                BuildChanges(input, plan, mode, false),
                input.GeneratedRoot + AssetPathSeparator +
                "OrpheusAuthoringClosure.json");
            return new OrpheusAudioAuthoringCommitArtifacts(
                ownership,
                closure,
                execution,
                reportPath);
        }

        internal static OrpheusAudioAuthoringCompileStatus GetSuccessStatus(
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringCompilationPlan plan)
        {
            var eventStatus =
                OrpheusAudioAuthoringTransaction.GetSuccessStatus(plan);
            if (eventStatus ==
                OrpheusAudioAuthoringCompileStatus.SucceededChanged)
            {
                return eventStatus;
            }

            if (!input.HasAcceptedManifest ||
                input.AcceptedManifestCount != input.Manifest.EntryCount)
            {
                return OrpheusAudioAuthoringCompileStatus.SucceededChanged;
            }

            for (var index = 0; index < input.Manifest.EntryCount; index++)
            {
                var current = input.Manifest.GetEntry(index);
                var matched = false;
                for (var acceptedIndex = 0;
                     acceptedIndex < input.AcceptedManifestCount;
                     acceptedIndex++)
                {
                    var accepted =
                        input.GetAcceptedManifestEntry(acceptedIndex);
                    if (current.Key != accepted.Key)
                    {
                        continue;
                    }

                    if (current.Status != accepted.Status ||
                        !string.Equals(
                            current.Symbol,
                            accepted.Symbol,
                            StringComparison.Ordinal))
                    {
                        return OrpheusAudioAuthoringCompileStatus
                            .SucceededChanged;
                    }

                    matched = true;
                    break;
                }

                if (!matched)
                {
                    return OrpheusAudioAuthoringCompileStatus.SucceededChanged;
                }
            }

            if (input.Catalog == null ||
                input.Catalog.EventCount != plan.EventCount ||
                !input.Catalog.HasEventAssetPaths)
            {
                return OrpheusAudioAuthoringCompileStatus.SucceededChanged;
            }

            for (var index = 0; index < plan.EventCount; index++)
            {
                if (!string.Equals(
                        input.Catalog.GetEventAssetPath(index),
                        plan.GetEvent(index).AssetPath,
                        StringComparison.Ordinal))
                {
                    return OrpheusAudioAuthoringCompileStatus.SucceededChanged;
                }
            }

            return OrpheusAudioAuthoringCompileStatus.SucceededUnchanged;
        }

        private static string WriteExecutionReport(
            OrpheusAudioValidationProfile profile,
            BuildTargetGroup targetGroup,
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringCompilationPlan plan,
            OrpheusAudioAuthoringCompileStatus status,
            OrpheusAudioAuthoringCompileMode mode,
            int generated,
            int updated,
            int unchanged,
            int rejected,
            int orphans,
            OrpheusAuthoringCompilationError[] errors,
            string closureReportPath)
        {
            var validationPath = AssetDatabase.GetAssetPath(profile);
            var validationGuid =
                AssetDatabase.AssetPathToGUID(validationPath).ToLowerInvariant();
            var reportPath = GetExecutionReportPath(validationGuid);
            var json = OrpheusAudioAuthoringReports.BuildExecution(
                status,
                targetGroup.ToString(),
                generated,
                updated,
                unchanged,
                rejected,
                orphans,
                errors,
                BuildProposedEnrollment(profile, input, plan),
                BuildChanges(
                    input,
                    plan,
                    mode,
                    status == OrpheusAudioAuthoringCompileStatus.RolledBack ||
                    status == OrpheusAudioAuthoringCompileStatus.RollbackFailed ||
                    status == OrpheusAudioAuthoringCompileStatus.Rejected),
                closureReportPath);
            var absolute = Path.Combine(
                Directory.GetParent(UnityEngine.Application.dataPath).FullName,
                reportPath.Replace(
                    AssetPathSeparator,
                    Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            WriteIfChanged(absolute, json);
            return reportPath;
        }

        private static string WriteTerminalExecutionReport(
            OrpheusAudioValidationProfile profile,
            BuildTargetGroup targetGroup,
            OrpheusAudioAuthoringCompileStatus status,
            OrpheusAuthoringCompilationError[] errors)
        {
            var validationGuid = GetGuid(AssetDatabase.GetAssetPath(profile));
            if (validationGuid.Length != 32)
            {
                return string.Empty;
            }

            var reportPath = GetExecutionReportPath(validationGuid);
            var errorCount = errors == null ? 0 : errors.Length;
            var json = OrpheusAudioAuthoringReports.BuildExecution(
                status,
                targetGroup.ToString(),
                0,
                0,
                0,
                errorCount,
                0,
                errors,
                null,
                Array.Empty<OrpheusAuthoringReportChange>(),
                string.Empty);
            var absolute = Path.Combine(
                Directory.GetParent(UnityEngine.Application.dataPath).FullName,
                reportPath.Replace(
                    AssetPathSeparator,
                    Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            WriteIfChanged(absolute, json);
            return reportPath;
        }

        private static OrpheusAuthoringManifestEntryValue[] CaptureManifest(
            OrpheusAuthoringCompilationInput input)
        {
            var result =
                new OrpheusAuthoringManifestEntryValue[input.Manifest.EntryCount];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = input.Manifest.GetEntry(index);
            }

            return result;
        }

        private static OrpheusAuthoringReportArtifact[] BuildOwnedArtifacts(
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringCompilationPlan plan,
            bool retainOrphans)
        {
            var result = new OrpheusAuthoringReportArtifact[
                plan.EventCount + (retainOrphans ? plan.OrphanCount : 0) + 5];
            var cursor = 0;
            for (var index = 0; index < plan.EventCount; index++)
            {
                var value = plan.GetEvent(index);
                result[cursor++] = new OrpheusAuthoringReportArtifact(
                    "event",
                    value.ModuleId,
                    value.Symbol,
                    value.Key,
                    GetGuid(value.AssetPath),
                    value.AssetPath);
            }

            if (retainOrphans)
            {
                for (var index = 0; index < plan.OrphanCount; index++)
                {
                    var value = plan.GetOrphan(index);
                    result[cursor++] = new OrpheusAuthoringReportArtifact(
                        "event",
                        value.ModuleId,
                        value.Symbol,
                        value.Key,
                        value.Guid,
                        value.AssetPath);
                }
            }

            result[cursor++] = NonEvent(
                "catalog",
                plan.CatalogGuid,
                plan.CatalogAssetPath);
            result[cursor++] = NonEvent(
                "typed-key-assembly",
                GetGuid(OrpheusAudioTypedKeyProjection.AssemblyPath),
                OrpheusAudioTypedKeyProjection.AssemblyPath);
            result[cursor++] = NonEvent(
                "typed-key-source",
                GetGuid(OrpheusAudioTypedKeyProjection.SourcePath),
                OrpheusAudioTypedKeyProjection.SourcePath);
            result[cursor++] = NonEvent(
                "ownership",
                GetGuid(
                    input.GeneratedRoot + AssetPathSeparator +
                    "OrpheusAuthoringOwnership.json"),
                input.GeneratedRoot + AssetPathSeparator +
                "OrpheusAuthoringOwnership.json");
            result[cursor] = NonEvent(
                "closure",
                GetGuid(
                    input.GeneratedRoot + AssetPathSeparator +
                    "OrpheusAuthoringClosure.json"),
                input.GeneratedRoot + AssetPathSeparator +
                "OrpheusAuthoringClosure.json");
            return result;
        }

        private static OrpheusAuthoringReportArtifact[] BuildOrphanArtifacts(
            OrpheusAuthoringCompilationPlan plan)
        {
            var result =
                new OrpheusAuthoringReportArtifact[plan.OrphanCount];
            for (var index = 0; index < result.Length; index++)
            {
                var value = plan.GetOrphan(index);
                result[index] = new OrpheusAuthoringReportArtifact(
                    "event",
                    value.ModuleId,
                    value.Symbol,
                    value.Key,
                    value.Guid,
                    value.AssetPath);
            }

            return result;
        }

        private static OrpheusAuthoringCatalogClosureValue[] BuildCatalogClosure(
            OrpheusAuthoringCompilationPlan plan)
        {
            var result =
                new OrpheusAuthoringCatalogClosureValue[plan.EventCount];
            for (var index = 0; index < result.Length; index++)
            {
                var value = plan.GetEvent(index);
                result[index] = new OrpheusAuthoringCatalogClosureValue(
                    value.Key,
                    value.Symbol,
                    GetGuid(value.AssetPath),
                    value.AssetPath);
            }

            return result;
        }

        private static OrpheusAuthoringClipClosureValue[] BuildClipClosure(
            OrpheusAuthoringCompilationPlan plan)
        {
            var count = 0;
            for (var index = 0; index < plan.EventCount; index++)
            {
                count += plan.GetEvent(index).ClipCount;
            }

            var result = new OrpheusAuthoringClipClosureValue[count];
            var cursor = 0;
            for (var index = 0; index < plan.EventCount; index++)
            {
                var audioEvent = plan.GetEvent(index);
                for (var clipIndex = 0;
                     clipIndex < audioEvent.ClipCount;
                     clipIndex++)
                {
                    var clip = audioEvent.GetClip(clipIndex);
                    result[cursor++] = new OrpheusAuthoringClipClosureValue(
                        audioEvent.Key,
                        audioEvent.Symbol,
                        clipIndex,
                        clip.Guid,
                        clip.LocalFileId,
                        clip.AssetPath);
                }
            }

            return result;
        }

        private static OrpheusAuthoringFutureDeliveryValue[] CaptureFutureDelivery(
            OrpheusAuthoringCompilationPlan plan)
        {
            var result =
                new OrpheusAuthoringFutureDeliveryValue[plan.FutureDeliveryCount];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = plan.GetFutureDelivery(index);
            }

            return result;
        }

        internal static OrpheusAuthoringReportChange[] BuildChanges(
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringCompilationPlan plan,
            OrpheusAudioAuthoringCompileMode mode,
            bool blocked)
        {
            var result = new System.Collections.Generic.List<
                OrpheusAuthoringReportChange>();
            for (var index = 0; index < plan.WriteOperationCount; index++)
            {
                var operation = plan.GetWriteOperation(index);
                if (operation.Kind == OrpheusAuthoringWriteKind.CreateDirectory)
                {
                    continue;
                }

                var action = GetAction(operation.Kind);
                if (blocked &&
                    !string.Equals(
                        action,
                        "unchanged",
                        StringComparison.Ordinal))
                {
                    action = "blocked";
                }

                result.Add(
                    new OrpheusAuthoringReportChange(
                        action,
                        operation.AssetPath,
                        GetBeforeFingerprint(input, operation.Key),
                        operation.ContentFingerprint,
                        BuildFieldChanges(input, operation)));
            }

            if (mode !=
                OrpheusAudioAuthoringCompileMode.CompileAndDeleteTrackedOrphans)
            {
                for (var index = 0; index < plan.OrphanCount; index++)
                {
                    var orphan = plan.GetOrphan(index);
                    result.Add(
                        new OrpheusAuthoringReportChange(
                            "report",
                            orphan.AssetPath,
                            orphan.ContentFingerprint,
                            orphan.ContentFingerprint,
                            Array.Empty<OrpheusAuthoringReportFieldChange>()));
                }
            }

            return result.ToArray();
        }

        private static OrpheusAuthoringReportFieldChange[] BuildFieldChanges(
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringWriteOperationValue operation)
        {
            if (operation.Kind != OrpheusAuthoringWriteKind.MoveRenamedEvent)
            {
                return Array.Empty<OrpheusAuthoringReportFieldChange>();
            }

            for (var index = 0; index < input.OwnedEventCount; index++)
            {
                var owned = input.GetOwnedEvent(index);
                if (owned.Key == operation.Key)
                {
                    return new[]
                    {
                        new OrpheusAuthoringReportFieldChange(
                            "symbol",
                            owned.Symbol,
                            operation.Symbol)
                    };
                }
            }

            return Array.Empty<OrpheusAuthoringReportFieldChange>();
        }

        private static string GetBeforeFingerprint(
            OrpheusAuthoringCompilationInput input,
            ushort key)
        {
            if (key == 0) return string.Empty;
            for (var index = 0; index < input.OwnedEventCount; index++)
            {
                var owned = input.GetOwnedEvent(index);
                if (owned.Key == key) return owned.ContentFingerprint;
            }

            return string.Empty;
        }

        private static string GetAction(OrpheusAuthoringWriteKind kind)
        {
            switch (kind)
            {
                case OrpheusAuthoringWriteKind.CreateEvent: return "created";
                case OrpheusAuthoringWriteKind.UpdateEvent: return "updated";
                case OrpheusAuthoringWriteKind.KeepEvent: return "unchanged";
                case OrpheusAuthoringWriteKind.MoveRenamedEvent: return "moved";
                case OrpheusAuthoringWriteKind.DeleteTrackedOrphan: return "deleted";
                case OrpheusAuthoringWriteKind.WriteCatalog: return "updated";
                case OrpheusAuthoringWriteKind.WriteTypedKeys: return "updated";
                case OrpheusAuthoringWriteKind.WriteEnrollmentIdentity: return "updated";
                case OrpheusAuthoringWriteKind.WriteOwnershipIndex: return "updated";
                case OrpheusAuthoringWriteKind.WriteClosureReport: return "updated";
                default: return string.Empty;
            }
        }

        private static OrpheusAuthoringProposedEnrollmentValue?
            BuildProposedEnrollment(
                OrpheusAudioValidationProfile profile,
                OrpheusAuthoringCompilationInput input,
                OrpheusAuthoringCompilationPlan plan)
        {
            if (profile == null ||
                profile.AuthoringProfile == null ||
                !string.IsNullOrEmpty(profile.AuthoringEnrollmentGuid))
            {
                return null;
            }

            return new OrpheusAuthoringProposedEnrollmentValue(
                GetGuid(AssetDatabase.GetAssetPath(profile.AuthoringProfile)),
                GetGuid(AssetDatabase.GetAssetPath(profile)),
                GetGuid(AssetDatabase.GetAssetPath(profile.KeyManifest)),
                plan.CatalogGuid,
                input.GeneratedRoot,
                plan.InputFingerprint);
        }

        private static OrpheusAuthoringReportArtifact NonEvent(
            string kind,
            string guid,
            string path)
        {
            return new OrpheusAuthoringReportArtifact(
                kind,
                string.Empty,
                string.Empty,
                0,
                guid,
                path);
        }

        private static void CountOperations(
            OrpheusAuthoringCompilationPlan plan,
            out int generated,
            out int updated,
            out int unchanged)
        {
            generated = 0;
            updated = 0;
            unchanged = 0;
            for (var index = 0; index < plan.WriteOperationCount; index++)
            {
                switch (plan.GetWriteOperation(index).Kind)
                {
                    case OrpheusAuthoringWriteKind.CreateEvent:
                        generated++;
                        break;
                    case OrpheusAuthoringWriteKind.UpdateEvent:
                    case OrpheusAuthoringWriteKind.MoveRenamedEvent:
                        updated++;
                        break;
                    case OrpheusAuthoringWriteKind.KeepEvent:
                        unchanged++;
                        break;
                }
            }
        }

        private static string GetExecutionReportPath(string validationGuid)
        {
            return "Library/Orpheus/AuthoringAnalysis/" +
                   (validationGuid ?? string.Empty) + ".json";
        }

        private static string GetGuid(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            var value = AssetDatabase.AssetPathToGUID(path);
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.ToLowerInvariant();
        }

        private static void WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) &&
                string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
            {
                return;
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
