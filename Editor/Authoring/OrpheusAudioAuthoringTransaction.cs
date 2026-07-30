using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor
{
    internal enum OrpheusAudioAuthoringTransactionPhase : byte
    {
        Invalid,
        Prepared,
        Writing,
        Importing,
        Validating,
        Committed,
        RollingBack,
        RolledBack,
        RollbackFailed
    }

    internal readonly struct OrpheusAudioAuthoringCommitArtifacts
    {
        internal OrpheusAudioAuthoringCommitArtifacts(
            string ownershipJson,
            string closureJson,
            string executionJson,
            string executionPath)
        {
            OwnershipJson = ownershipJson ?? string.Empty;
            ClosureJson = closureJson ?? string.Empty;
            ExecutionJson = executionJson ?? string.Empty;
            ExecutionPath = executionPath ?? string.Empty;
        }

        internal string OwnershipJson { get; }
        internal string ClosureJson { get; }
        internal string ExecutionJson { get; }
        internal string ExecutionPath { get; }
    }

    internal interface IOrpheusAudioAuthoringFailureInjection
    {
        void Before(string operation);
        void After(string operation);
    }

    internal sealed class
        OrpheusAudioAuthoringSimulatedProcessTerminationException : Exception
    {
    }

    internal static class OrpheusAudioAuthoringTransaction
    {
        private const char AssetPathSeparator = (char)47;
        private const char JsonEscapeCharacter = (char)92;
        private const string TransactionRoot =
            "Library/Orpheus/AuthoringTransactions";
        internal static string LastFailureForTests { get; private set; } = string.Empty;

        internal static OrpheusAudioAuthoringCompileStatus GetSuccessStatus(
            OrpheusAuthoringCompilationPlan plan)
        {
            for (var index = 0; index < plan.WriteOperationCount; index++)
            {
                switch (plan.GetWriteOperation(index).Kind)
                {
                    case OrpheusAuthoringWriteKind.CreateEvent:
                    case OrpheusAuthoringWriteKind.UpdateEvent:
                    case OrpheusAuthoringWriteKind.MoveRenamedEvent:
                    case OrpheusAuthoringWriteKind.DeleteTrackedOrphan:
                    case OrpheusAuthoringWriteKind.WriteEnrollmentIdentity:
                        return OrpheusAudioAuthoringCompileStatus.SucceededChanged;
                }
            }

            return OrpheusAudioAuthoringCompileStatus.SucceededUnchanged;
        }

        internal static OrpheusAudioAuthoringCompileStatus Execute(
            OrpheusAudioValidationProfile profile,
            OrpheusAuthoringCompilationPlan plan,
            OrpheusAudioAuthoringCommitArtifacts artifacts,
            IOrpheusAudioAuthoringFailureInjection injection = null)
        {
            return Execute(
                profile,
                plan,
                artifacts,
                BuildPipeline.GetBuildTargetGroup(
                    EditorUserBuildSettings.activeBuildTarget),
                null,
                injection);
        }

        internal static OrpheusAudioAuthoringCompileStatus Execute(
            OrpheusAudioValidationProfile profile,
            OrpheusAuthoringCompilationPlan plan,
            OrpheusAudioAuthoringCommitArtifacts artifacts,
            BuildTargetGroup targetGroup,
            OrpheusAudioValidationProfile[] discoveredProfiles,
            IOrpheusAudioAuthoringFailureInjection injection = null)
        {
            LastFailureForTests = string.Empty;
            if (profile == null || plan == null ||
                string.IsNullOrEmpty(artifacts.OwnershipJson) ||
                string.IsNullOrEmpty(artifacts.ClosureJson))
            {
                return OrpheusAudioAuthoringCompileStatus.Rejected;
            }

            var id = Guid.NewGuid().ToString("N");
            var journalPath = Absolute(
                TransactionRoot + AssetPathSeparator + id +
                AssetPathSeparator + "journal.json");
            var snapshots = CaptureSnapshots(profile, plan, artifacts);
            NormalizeSnapshots(snapshots);
            var payload = BuildPayload(snapshots);
            WriteJournal(journalPath, id, OrpheusAudioAuthoringTransactionPhase.Prepared, payload);
            Step(injection, false, "JournalPrepared");
            var reloadLocked = false;
            var assetEditing = false;
            var materializedOwnership = artifacts.OwnershipJson;
            var materializedExecution = artifacts.ExecutionJson;
            var materializedClosure = artifacts.ClosureJson;
            try
            {
                Step(injection, true, "LockReloadAssemblies");
                EditorApplication.LockReloadAssemblies();
                reloadLocked = true;
                Step(injection, false, "LockReloadAssemblies");
                WriteJournal(journalPath, id, OrpheusAudioAuthoringTransactionPhase.Writing, payload);
                Step(injection, false, "JournalWriting");
                try
                {
                    Step(injection, true, "StartAssetEditing");
                    AssetDatabase.StartAssetEditing();
                    assetEditing = true;
                    Step(injection, false, "StartAssetEditing");
                    ApplyPlan(
                        profile,
                        plan,
                        artifacts,
                        ref materializedExecution,
                        injection);
                }
                finally
                {
                    if (assetEditing)
                    {
                        Step(injection, true, "StopAssetEditing");
                        AssetDatabase.StopAssetEditing();
                        assetEditing = false;
                        Step(injection, false, "StopAssetEditing");
                    }
                }

                WriteJournal(journalPath, id, OrpheusAudioAuthoringTransactionPhase.Importing, payload);
                Step(injection, false, "JournalImporting");
                Step(injection, true, "Refresh");
                Step(injection, true, "Import");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Step(injection, false, "Import");
                Step(injection, false, "Refresh");
                Step(injection, true, "OwnershipMaterialize");
                if (!TryMaterializeOwnershipTemplate(
                        materializedOwnership,
                        out materializedOwnership))
                {
                    throw new InvalidDataException(
                        "OwnershipMaterialization");
                }

                WriteTextAtomic(
                    Absolute(GetOwnershipPath(plan)),
                    materializedOwnership);
                Step(injection, false, "OwnershipMaterialize");
                WriteJournal(journalPath, id, OrpheusAudioAuthoringTransactionPhase.Validating, payload);
                Step(injection, false, "JournalValidating");
                Step(injection, true, "ValidateOutputs");
                if (!ValidateOutputs(
                        profile,
                        plan,
                        materializedOwnership))
                {
                    throw new InvalidDataException(
                        "ActualOutput:" + LastFailureForTests);
                }

                Step(injection, false, "ValidateOutputs");
                Step(injection, true, "SelectedAuthoringValidation");
                var committedInputFingerprint = plan.InputFingerprint;
                if (profile.Enabled &&
                    !ValidateSelectedAuthoringState(
                        profile,
                        targetGroup,
                        discoveredProfiles,
                        id,
                        plan,
                        out committedInputFingerprint))
                {
                    throw new InvalidDataException(
                        "SelectedAuthoringValidation:" +
                        LastFailureForTests);
                }

                if (!string.Equals(
                        committedInputFingerprint,
                        plan.InputFingerprint,
                        StringComparison.Ordinal))
                {
                    if (!OrpheusAudioAuthoringReports
                            .TryReplaceInputFingerprint(
                                materializedClosure,
                                plan.InputFingerprint,
                                committedInputFingerprint,
                                out materializedClosure))
                    {
                        throw new InvalidDataException(
                            "CommittedInputFingerprint");
                    }

                    if (materializedExecution.IndexOf(
                            "\"inputFingerprint\":",
                            StringComparison.Ordinal) >= 0 &&
                        !OrpheusAudioAuthoringReports
                            .TryReplaceInputFingerprint(
                                materializedExecution,
                                plan.InputFingerprint,
                                committedInputFingerprint,
                                out materializedExecution))
                    {
                        throw new InvalidDataException(
                            "ExecutionInputFingerprint");
                    }
                }

                Step(injection, false, "SelectedAuthoringValidation");
                Step(injection, true, "SharedValidation");
                if (!ValidateSharedProfiles(
                        profile,
                        targetGroup,
                        discoveredProfiles))
                {
                    throw new InvalidDataException("SharedValidation");
                }

                Step(injection, false, "SharedValidation");
                Step(injection, true, "ClosureReplace");
                if (materializedClosure.IndexOf(
                        "\"catalogClosure\"",
                        StringComparison.Ordinal) >= 0 &&
                    !OrpheusAudioAuthoringReports.TryMaterializeClosure(
                        materializedClosure,
                        ResolveAssetGuid,
                        out materializedClosure))
                {
                    throw new InvalidDataException("ClosureMaterialization");
                }

                WriteTextAtomic(
                    Absolute(GetClosurePath(plan)),
                    materializedClosure);
                Step(injection, false, "ClosureReplace");
                if (!string.IsNullOrEmpty(artifacts.ExecutionPath))
                {
                    Step(injection, true, "ExecutionReportReplace");
                    WriteTextAtomic(
                         Absolute(artifacts.ExecutionPath),
                         materializedExecution);
                    Step(injection, false, "ExecutionReportReplace");
                }

                WriteJournal(
                    journalPath,
                    id,
                    OrpheusAudioAuthoringTransactionPhase.Committed,
                    payload);
                return GetSuccessStatus(plan);
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                LastFailureForTests = exception.GetType().FullName + ":" + exception.Message;
                if (assetEditing)
                {
                    try
                    {
                        AssetDatabase.StopAssetEditing();
                    }
                    catch (Exception stopException) when (!IsCatastrophic(stopException))
                    {
                    }
                }

                return Rollback(journalPath, id, payload, snapshots, injection, false);
            }
            finally
            {
                if (reloadLocked)
                {
                    try
                    {
                        Step(
                            injection,
                            true,
                            "UnlockReloadAssemblies");
                        EditorApplication.UnlockReloadAssemblies();
                        reloadLocked = false;
                        Step(
                            injection,
                            false,
                            "UnlockReloadAssemblies");
                    }
                    catch (Exception exception) when (!IsCatastrophic(exception))
                    {
                        if (reloadLocked)
                        {
                            EditorApplication.UnlockReloadAssemblies();
                            reloadLocked = false;
                        }

                        LastFailureForTests =
                            exception.GetType().FullName + ":" +
                            exception.Message;
                    }
                }
            }
        }

        internal static bool TryRecoverInterrupted()
        {
            return TryRecoverInterruptedAtRoot(Absolute(TransactionRoot));
        }

        internal static bool TryRecoverInterruptedForTests(string root)
        {
            return TryRecoverInterruptedAtRoot(root);
        }

        private static bool TryRecoverInterruptedAtRoot(string root)
        {
            LastFailureForTests = string.Empty;
            if (!Directory.Exists(root))
            {
                return true;
            }

            if (!TryEnumerateRecoveryJournals(root, out var journals))
            {
                LastFailureForTests = "RecoveryEnumerate";
                PersistRecoveryBlocker(root);
                return false;
            }

            var pending = new List<JournalValue>();
            var terminal = new List<JournalValue>();
            for (var index = 0; index < journals.Length; index++)
            {
                if (!TryReadJournal(journals[index], out var value))
                {
                    LastFailureForTests = "RecoveryRead:" + journals[index];
                    PersistRecoveryBlocker(root);
                    return false;
                }

                if (value.Phase == OrpheusAudioAuthoringTransactionPhase.RollbackFailed)
                {
                    LastFailureForTests =
                        "RecoveryRollbackFailed:" + value.Path;
                    return false;
                }

                if (value.Phase != OrpheusAudioAuthoringTransactionPhase.Committed &&
                    value.Phase != OrpheusAudioAuthoringTransactionPhase.RolledBack)
                {
                    pending.Add(value);
                }
                else
                {
                    terminal.Add(value);
                }
            }

            if (pending.Count > 1)
            {
                LastFailureForTests =
                    "RecoveryAmbiguous:" + pending.Count;
                PersistRecoveryBlocker(root);
                return false;
            }

            for (var index = 0; index < terminal.Count; index++)
            {
                if (TryDeleteSuccessfulTerminalJournal(terminal[index].Path))
                {
                    continue;
                }

                LastFailureForTests =
                    "RecoveryTerminalCleanup:" + terminal[index].Path;
                return false;
            }

            if (pending.Count == 0)
            {
                return true;
            }

            var candidate = pending[0];
            List<Snapshot> snapshots;
            try
            {
                snapshots = ParsePayload(candidate.Payload);
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                LastFailureForTests =
                    "RecoveryPayload:" + exception.GetType().FullName +
                    ":" + exception.Message;
                if (!TryPersistRollbackFailed(
                        candidate.Path,
                        candidate.Id,
                        candidate.Payload))
                {
                    PersistRecoveryBlocker(root);
                }

                return false;
            }
            EditorApplication.LockReloadAssemblies();
            try
            {
                if (Rollback(
                        candidate.Path,
                        candidate.Id,
                        candidate.Payload,
                        snapshots,
                        null,
                        false) !=
                    OrpheusAudioAuthoringCompileStatus.RolledBack)
                {
                    return false;
                }

                if (TryDeleteSuccessfulTerminalJournal(candidate.Path))
                {
                    return true;
                }

                LastFailureForTests =
                    "RecoveryTerminalCleanup:" + candidate.Path;
                return false;
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
            }
        }

        private static void ApplyPlan(
            OrpheusAudioValidationProfile profile,
            OrpheusAuthoringCompilationPlan plan,
            OrpheusAudioAuthoringCommitArtifacts artifacts,
            ref string executionJson,
            IOrpheusAudioAuthoringFailureInjection injection)
        {
            var movedEvents = new Dictionary<ushort, OrpheusAudioEvent>();
            for (var index = 0; index < plan.WriteOperationCount; index++)
            {
                var operation = plan.GetWriteOperation(index);
                Step(injection, true, operation.Kind.ToString());
                switch (operation.Kind)
                {
                    case OrpheusAuthoringWriteKind.CreateDirectory:
                        Step(injection, true, "DirectoryCreate");
                        Directory.CreateDirectory(Absolute(operation.AssetPath));
                        Step(injection, false, "DirectoryCreate");
                        break;
                    case OrpheusAuthoringWriteKind.CreateEvent:
                    case OrpheusAuthoringWriteKind.UpdateEvent:
                        WriteEvent(
                            plan,
                            operation.Key,
                            operation.AssetPath,
                            injection);
                        break;
                    case OrpheusAuthoringWriteKind.MoveRenamedEvent:
                        var movedEvent =
                            AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                                operation.SourcePath);
                        if (movedEvent == null)
                        {
                            throw new InvalidDataException(
                                "MoveSource:" + operation.SourcePath);
                        }

                        WriteEvent(
                            plan,
                            operation.Key,
                            operation.SourcePath,
                            injection,
                            movedEvent);
                        Step(injection, true, "MoveAsset");
                        var moveError = AssetDatabase.MoveAsset(
                            operation.SourcePath,
                            operation.AssetPath);
                        if (!string.IsNullOrEmpty(moveError))
                        {
                            throw new IOException(moveError);
                        }

                        Step(injection, false, "MoveAsset");
                        movedEvents.Add(operation.Key, movedEvent);
                        break;
                    case OrpheusAuthoringWriteKind.WriteCatalog:
                        WriteCatalog(
                            profile.Catalog,
                            plan,
                            movedEvents,
                            injection);
                        break;
                    case OrpheusAuthoringWriteKind.WriteTypedKeys:
                        Step(injection, true, "TextReplace");
                        OrpheusAudioTypedKeyProjection.WriteExpectedNoRefresh(
                            plan.TypedKeyAssemblyText,
                            plan.TypedKeySourceText);
                        Step(injection, false, "TextReplace");
                        break;
                    case OrpheusAuthoringWriteKind.WriteEnrollmentIdentity:
                        WriteEnrollment(profile, injection);
                        break;
                    case OrpheusAuthoringWriteKind.WriteOwnershipIndex:
                        Step(injection, true, "TextReplace");
                        WriteTextAtomic(
                            Absolute(operation.AssetPath),
                            artifacts.OwnershipJson);
                        Step(injection, false, "TextReplace");
                        break;
                    case OrpheusAuthoringWriteKind.DeleteTrackedOrphan:
                        Step(injection, true, "DeleteAsset");
                        if (!CanDeleteTrackedOrphan(plan, operation))
                        {
                            executionJson =
                                OrpheusAudioAuthoringReports.ReplaceChangeAction(
                                    executionJson,
                                    operation.AssetPath,
                                    "deleted",
                                    "blocked");
                            Step(injection, false, "DeleteAsset");
                            break;
                        }

                        if (!AssetDatabase.DeleteAsset(operation.AssetPath))
                        {
                            throw new IOException(operation.AssetPath);
                        }

                        Step(injection, false, "DeleteAsset");
                        break;
                    case OrpheusAuthoringWriteKind.KeepEvent:
                        break;
                    case OrpheusAuthoringWriteKind.WriteClosureReport:
                        var closureAbsolute =
                            Absolute(operation.AssetPath);
                        if (!File.Exists(closureAbsolute))
                        {
                            WriteTextAtomic(closureAbsolute, "{}\n");
                        }

                        break;
                }

                Step(injection, false, operation.Kind.ToString());
            }
        }

        private static void WriteEvent(
            OrpheusAuthoringCompilationPlan plan,
            ushort key,
            string path,
            IOrpheusAudioAuthoringFailureInjection injection,
            OrpheusAudioEvent existingEvent = null)
        {
            OrpheusAuthoringCompiledEventValue value = null;
            for (var index = 0; index < plan.EventCount; index++)
            {
                if (plan.GetEvent(index).Key == key)
                {
                    value = plan.GetEvent(index);
                    break;
                }
            }

            if (value == null)
            {
                throw new InvalidDataException("EventPlan");
            }

            Step(injection, true, "DirectoryCreate");
            Directory.CreateDirectory(Path.GetDirectoryName(Absolute(path)));
            Step(injection, false, "DirectoryCreate");
            var audioEvent = existingEvent ??
                             AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                                 path);
            if (audioEvent == null)
            {
                audioEvent = ScriptableObject.CreateInstance<OrpheusAudioEvent>();
                Step(injection, true, "AssetCreate");
                AssetDatabase.CreateAsset(audioEvent, path);
                Step(injection, false, "AssetCreate");
            }

            var serialized = new SerializedObject(audioEvent);
            Set(serialized, "_schemaVersion", 1);
            Set(serialized, "_key", value.Key);
            Set(serialized, "_playbackKind", (int)value.PlaybackKind);
            Set(serialized, "_category", (int)value.Category);
            Set(serialized, "_loadPolicy", (int)value.LoadPolicy);
            Set(serialized, "_volumeMin", value.VolumeMinimum);
            Set(serialized, "_volumeMax", value.VolumeMaximum);
            Set(serialized, "_pitchMin", value.PitchMinimum);
            Set(serialized, "_pitchMax", value.PitchMaximum);
            Set(serialized, "_priority", value.Priority);
            Set(serialized, "_polyphonyCap", value.PolyphonyCap);
            Set(serialized, "_cooldownSeconds", value.CooldownSeconds);
            Set(serialized, "_minimumDistance", value.MinimumDistance);
            Set(serialized, "_maximumDistance", value.MaximumDistance);
            Set(serialized, "_rolloffMode", (int)value.RolloffMode);
            var clips = serialized.FindProperty("_clips");
            clips.arraySize = value.ClipCount;
            for (var index = 0; index < value.ClipCount; index++)
            {
                clips.GetArrayElementAtIndex(index).objectReferenceValue =
                    ResolveClip(value.GetClip(index));
            }

            Step(injection, true, "SerializedApply");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Step(injection, false, "SerializedApply");
            EditorUtility.SetDirty(audioEvent);
            Step(injection, true, "SaveAsset");
            AssetDatabase.SaveAssetIfDirty(audioEvent);
            Step(injection, false, "SaveAsset");
        }

        private static AudioClip ResolveClip(OrpheusAuthoringClipValue value)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(value.AssetPath);
            for (var index = 0; index < assets.Length; index++)
            {
                if (assets[index] is AudioClip clip &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        clip,
                        out var guid,
                        out long localId) &&
                    string.Equals(guid.ToLowerInvariant(), value.Guid, StringComparison.Ordinal) &&
                    localId == value.LocalFileId)
                {
                    return clip;
                }
            }

            throw new InvalidDataException("ClipIdentity");
        }

        private static void WriteCatalog(
            OrpheusAudioCatalog catalog,
            OrpheusAuthoringCompilationPlan plan,
            Dictionary<ushort, OrpheusAudioEvent> movedEvents,
            IOrpheusAudioAuthoringFailureInjection injection)
        {
            var serialized = new SerializedObject(catalog);
            var events = serialized.FindProperty("_events");
            events.arraySize = plan.EventCount;
            for (var index = 0; index < plan.EventCount; index++)
            {
                var expected = plan.GetEvent(index);
                if (!movedEvents.TryGetValue(
                        expected.Key,
                        out var audioEvent))
                {
                    audioEvent =
                        AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                            expected.AssetPath);
                }

                events.GetArrayElementAtIndex(index).objectReferenceValue =
                    audioEvent;
            }

            Step(injection, true, "SerializedApply");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Step(injection, false, "SerializedApply");
            EditorUtility.SetDirty(catalog);
            Step(injection, true, "SaveAsset");
            AssetDatabase.SaveAssetIfDirty(catalog);
            Step(injection, false, "SaveAsset");
        }

        private static void WriteEnrollment(
            OrpheusAudioValidationProfile profile,
            IOrpheusAudioAuthoringFailureInjection injection)
        {
            var authoringPath = AssetDatabase.GetAssetPath(profile.AuthoringProfile);
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("_authoringEnrollmentGuid").stringValue =
                AssetDatabase.AssetPathToGUID(authoringPath).ToLowerInvariant();
            Step(injection, true, "SerializedApply");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Step(injection, false, "SerializedApply");
            EditorUtility.SetDirty(profile);
            Step(injection, true, "SaveAsset");
            AssetDatabase.SaveAssetIfDirty(profile);
            Step(injection, false, "SaveAsset");
        }

        private static bool ValidateOutputs(
            OrpheusAudioValidationProfile profile,
            OrpheusAuthoringCompilationPlan plan,
            string expectedOwnership)
        {
            if (profile == null || profile.Catalog == null ||
                !string.Equals(
                    AssetDatabase.GetAssetPath(profile.Catalog),
                    plan.CatalogAssetPath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    AssetDatabase.AssetPathToGUID(plan.CatalogAssetPath)
                        .ToLowerInvariant(),
                    plan.CatalogGuid,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var actualEventFingerprints = new string[plan.EventCount];
            for (var index = 0; index < plan.EventCount; index++)
            {
                var expected = plan.GetEvent(index);
                var actual =
                    AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                        expected.AssetPath);
                if (!EventMatches(actual, expected))
                {
                    LastFailureForTests = "Event:" + expected.AssetPath;
                    return false;
                }

                actualEventFingerprints[index] =
                    ComputeActualEventFingerprint(actual, expected);
            }

            var serializedCatalog = new SerializedObject(profile.Catalog);
            var catalogEvents = serializedCatalog.FindProperty("_events");
            if (catalogEvents == null || !catalogEvents.isArray ||
                catalogEvents.arraySize != plan.EventCount)
            {
                LastFailureForTests = "CatalogCount";
                return false;
            }

            var actualCatalogKeys = new ushort[plan.EventCount];
            for (var index = 0; index < plan.EventCount; index++)
            {
                var actualEvent =
                    catalogEvents.GetArrayElementAtIndex(index)
                        .objectReferenceValue as OrpheusAudioEvent;
                var expected = plan.GetEvent(index);
                if (actualEvent == null ||
                    !string.Equals(
                        AssetDatabase.GetAssetPath(actualEvent),
                        expected.AssetPath,
                        StringComparison.Ordinal) ||
                    actualEvent.Key.Value != expected.Key)
                {
                    LastFailureForTests = "CatalogEvent:" + expected.AssetPath;
                    return false;
                }

                actualCatalogKeys[index] = actualEvent.Key.Value;
            }

            if (!TryReadNormalizedText(
                    OrpheusAudioTypedKeyProjection.AssemblyPath,
                    out var actualAssembly) ||
                !TryReadNormalizedText(
                    OrpheusAudioTypedKeyProjection.SourcePath,
                    out var actualSource) ||
                !string.Equals(
                    actualAssembly,
                    NormalizeNewlines(plan.TypedKeyAssemblyText),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    actualSource,
                    NormalizeNewlines(plan.TypedKeySourceText),
                    StringComparison.Ordinal))
            {
                LastFailureForTests = "TypedKeys";
                return false;
            }

            var ownershipPath = GetOwnershipPath(plan);
            if (!TryReadNormalizedText(ownershipPath, out var actualOwnership) ||
                !string.Equals(
                    actualOwnership,
                    NormalizeNewlines(expectedOwnership),
                    StringComparison.Ordinal) ||
                !TryValidateOwnership(
                    profile,
                    plan,
                    actualOwnership,
                    ownershipPath,
                    out var ownershipRecords))
            {
                LastFailureForTests = "Ownership";
                return false;
            }

            if (HasOperation(
                    plan,
                    OrpheusAuthoringWriteKind.WriteEnrollmentIdentity))
            {
                var authoringPath =
                    AssetDatabase.GetAssetPath(profile.AuthoringProfile);
                var expectedEnrollment =
                    AssetDatabase.AssetPathToGUID(authoringPath)
                        .ToLowerInvariant();
                if (!string.Equals(
                        profile.AuthoringEnrollmentGuid,
                        expectedEnrollment,
                        StringComparison.Ordinal))
                {
                    LastFailureForTests = "Enrollment";
                    return false;
                }
            }

            var actualFingerprint = ComputeOutputFingerprint(
                plan,
                actualCatalogKeys,
                AssetDatabase.AssetPathToGUID(plan.CatalogAssetPath)
                    .ToLowerInvariant(),
                AssetDatabase.GetAssetPath(profile.Catalog),
                actualAssembly,
                actualSource,
                ownershipRecords,
                actualEventFingerprints);
            if (!string.Equals(
                    actualFingerprint,
                    plan.OutputFingerprint,
                    StringComparison.Ordinal))
            {
                LastFailureForTests =
                    "Fingerprint:" + plan.OutputFingerprint + ":" +
                    actualFingerprint;
                return false;
            }

            return true;
        }

        private static bool EventMatches(
            OrpheusAudioEvent actual,
            OrpheusAuthoringCompiledEventValue expected)
        {
            if (actual == null)
            {
                return false;
            }

            var serialized = new SerializedObject(actual);
            if (serialized.FindProperty("_schemaVersion").intValue !=
                    OrpheusAudioAuthoringSchema.Current ||
                serialized.FindProperty("_key").intValue != expected.Key ||
                serialized.FindProperty("_playbackKind").intValue !=
                    (int)expected.PlaybackKind ||
                serialized.FindProperty("_category").intValue !=
                    (int)expected.Category ||
                serialized.FindProperty("_loadPolicy").intValue !=
                    (int)expected.LoadPolicy ||
                !serialized.FindProperty("_volumeMin").floatValue.Equals(
                    expected.VolumeMinimum) ||
                !serialized.FindProperty("_volumeMax").floatValue.Equals(
                    expected.VolumeMaximum) ||
                !serialized.FindProperty("_pitchMin").floatValue.Equals(
                    expected.PitchMinimum) ||
                !serialized.FindProperty("_pitchMax").floatValue.Equals(
                    expected.PitchMaximum) ||
                serialized.FindProperty("_priority").intValue !=
                    expected.Priority ||
                serialized.FindProperty("_polyphonyCap").intValue !=
                    expected.PolyphonyCap ||
                !serialized.FindProperty("_cooldownSeconds").floatValue.Equals(
                    expected.CooldownSeconds) ||
                !serialized.FindProperty("_minimumDistance").floatValue.Equals(
                    expected.MinimumDistance) ||
                !serialized.FindProperty("_maximumDistance").floatValue.Equals(
                    expected.MaximumDistance) ||
                serialized.FindProperty("_rolloffMode").intValue !=
                    (int)expected.RolloffMode)
            {
                return false;
            }

            var clips = serialized.FindProperty("_clips");
            if (clips == null || !clips.isArray ||
                clips.arraySize != expected.ClipCount)
            {
                return false;
            }

            for (var index = 0; index < expected.ClipCount; index++)
            {
                var clip =
                    clips.GetArrayElementAtIndex(index).objectReferenceValue;
                if (clip == null ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        clip,
                        out var guid,
                        out long localId) ||
                    !string.Equals(
                        guid.ToLowerInvariant(),
                        expected.GetClip(index).Guid,
                        StringComparison.Ordinal) ||
                    localId != expected.GetClip(index).LocalFileId)
                {
                    return false;
                }
            }

            return true;
        }

        internal static string ComputeActualEventFingerprint(
            OrpheusAudioEvent actual,
            OrpheusAuthoringCompiledEventValue expected)
        {
            var serialized = new SerializedObject(actual);
            var writer = new OutputFingerprintWriter();
            writer.WriteString("Orpheus.Audio.AuthoringCompilation\0v1\0");
            writer.WriteString("Event");
            writer.WriteString(expected.ModuleId);
            writer.WriteUInt16(
                (ushort)serialized.FindProperty("_key").intValue);
            writer.WriteString(expected.Symbol);
            writer.WriteByte(
                (byte)serialized.FindProperty("_playbackKind").intValue);
            writer.WriteByte(
                (byte)serialized.FindProperty("_category").intValue);
            writer.WriteByte(
                (byte)serialized.FindProperty("_loadPolicy").intValue);
            writer.WriteSingle(
                serialized.FindProperty("_volumeMin").floatValue);
            writer.WriteSingle(
                serialized.FindProperty("_volumeMax").floatValue);
            writer.WriteSingle(
                serialized.FindProperty("_pitchMin").floatValue);
            writer.WriteSingle(
                serialized.FindProperty("_pitchMax").floatValue);
            writer.WriteByte(
                (byte)serialized.FindProperty("_priority").intValue);
            writer.WriteByte(
                (byte)serialized.FindProperty("_polyphonyCap").intValue);
            writer.WriteSingle(
                serialized.FindProperty("_cooldownSeconds").floatValue);
            writer.WriteSingle(
                serialized.FindProperty("_minimumDistance").floatValue);
            writer.WriteSingle(
                serialized.FindProperty("_maximumDistance").floatValue);
            writer.WriteByte(
                (byte)serialized.FindProperty("_rolloffMode").intValue);
            writer.WriteString(expected.ProfileHint);
            writer.WriteString(expected.CandidateContentBankId);
            var clips = serialized.FindProperty("_clips");
            writer.WriteInt32(clips.arraySize);
            for (var index = 0; index < clips.arraySize; index++)
            {
                var clip =
                    clips.GetArrayElementAtIndex(index).objectReferenceValue;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    clip,
                    out var guid,
                    out long localId);
                writer.WriteString(guid.ToLowerInvariant());
                writer.WriteInt64(localId);
            }

            return writer.Finish();
        }

        internal static string ComputeEventFingerprintForTests(
            OrpheusAuthoringCompiledEventValue value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            var writer = new OutputFingerprintWriter();
            writer.WriteString("Orpheus.Audio.AuthoringCompilation\0v1\0");
            writer.WriteString("Event");
            writer.WriteString(value.ModuleId);
            writer.WriteUInt16(value.Key);
            writer.WriteString(value.Symbol);
            writer.WriteByte((byte)value.PlaybackKind);
            writer.WriteByte((byte)value.Category);
            writer.WriteByte((byte)value.LoadPolicy);
            writer.WriteSingle(value.VolumeMinimum);
            writer.WriteSingle(value.VolumeMaximum);
            writer.WriteSingle(value.PitchMinimum);
            writer.WriteSingle(value.PitchMaximum);
            writer.WriteByte(value.Priority);
            writer.WriteByte(value.PolyphonyCap);
            writer.WriteSingle(value.CooldownSeconds);
            writer.WriteSingle(value.MinimumDistance);
            writer.WriteSingle(value.MaximumDistance);
            writer.WriteByte((byte)value.RolloffMode);
            writer.WriteString(value.ProfileHint);
            writer.WriteString(value.CandidateContentBankId);
            writer.WriteInt32(value.ClipCount);
            for (var index = 0; index < value.ClipCount; index++)
            {
                var clip = value.GetClip(index);
                writer.WriteString(clip.Guid);
                writer.WriteInt64(clip.LocalFileId);
            }

            return writer.Finish();
        }

        private static bool ValidateSharedProfiles(
            OrpheusAudioValidationProfile selected,
            BuildTargetGroup targetGroup,
            OrpheusAudioValidationProfile[] discoveredProfiles)
        {
            var profiles = new List<OrpheusAudioValidationProfile>();
            if (discoveredProfiles == null)
            {
                var guids =
                    AssetDatabase.FindAssets("t:OrpheusAudioValidationProfile");
                Array.Sort(guids, StringComparer.Ordinal);
                for (var index = 0; index < guids.Length; index++)
                {
                    var candidate =
                        AssetDatabase.LoadAssetAtPath
                            <OrpheusAudioValidationProfile>(
                                AssetDatabase.GUIDToAssetPath(guids[index]));
                    if (candidate != null)
                    {
                        profiles.Add(candidate);
                    }
                }
            }
            else
            {
                for (var index = 0; index < discoveredProfiles.Length; index++)
                {
                    if (discoveredProfiles[index] != null)
                    {
                        profiles.Add(discoveredProfiles[index]);
                    }
                }
            }

            if (selected != null && !profiles.Contains(selected))
            {
                profiles.Add(selected);
            }

            profiles.Sort(
                (left, right) => string.Compare(
                    AssetDatabase.GetAssetPath(left),
                    AssetDatabase.GetAssetPath(right),
                    StringComparison.Ordinal));
            var validationSet = profiles.ToArray();
            for (var index = 0; index < profiles.Count; index++)
            {
                var candidate = profiles[index];
                if (candidate.Enabled &&
                    OrpheusAudioValidator.ValidateProfile(
                        candidate,
                        targetGroup,
                        OrpheusAudioTypedKeyProjection.ProductionPaths,
                        validationSet,
                        !ReferenceEquals(candidate, selected)).Count != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateSelectedAuthoringState(
            OrpheusAudioValidationProfile selected,
            BuildTargetGroup targetGroup,
            OrpheusAudioValidationProfile[] discoveredProfiles,
            string transactionId,
            OrpheusAuthoringCompilationPlan expectedPlan,
            out string committedInputFingerprint)
        {
            committedInputFingerprint = string.Empty;
            if (!OrpheusAudioAuthoringCapture.TryCaptureDetailed(
                    selected,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    targetGroup,
                    discoveredProfiles,
                    transactionId,
                    out var currentInput,
                    out var currentPlan,
                    out _,
                    out var errors))
            {
                LastFailureForTests =
                    "Capture:" +
                    (errors == null || errors.Length == 0
                        ? string.Empty
                        : errors[0].Code.ToString());
                return false;
            }

            if (OrpheusAudioAuthoringCompiler.GetSuccessStatus(
                    currentInput,
                    currentPlan) !=
                OrpheusAudioAuthoringCompileStatus.SucceededUnchanged)
            {
                LastFailureForTests = "CurrentPlanChanged";
                return false;
            }

            if (!string.Equals(
                    currentPlan.OutputFingerprint,
                    expectedPlan.OutputFingerprint,
                    StringComparison.Ordinal))
            {
                LastFailureForTests = "OutputFingerprint";
                return false;
            }

            committedInputFingerprint = currentPlan.InputFingerprint;
            return true;
        }

        private static bool HasOperation(
            OrpheusAuthoringCompilationPlan plan,
            OrpheusAuthoringWriteKind kind)
        {
            for (var index = 0; index < plan.WriteOperationCount; index++)
            {
                if (plan.GetWriteOperation(index).Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanDeleteTrackedOrphan(
            OrpheusAuthoringCompilationPlan plan,
            OrpheusAuthoringWriteOperationValue operation)
        {
            if (plan == null ||
                operation.Kind !=
                OrpheusAuthoringWriteKind.DeleteTrackedOrphan)
            {
                return false;
            }

            OrpheusAuthoringOwnedEventValue tracked = default;
            var matches = 0;
            for (var index = 0; index < plan.OrphanCount; index++)
            {
                var candidate = plan.GetOrphan(index);
                if (candidate.Key == operation.Key &&
                    string.Equals(
                        candidate.AssetPath,
                        operation.AssetPath,
                        StringComparison.Ordinal))
                {
                    tracked = candidate;
                    matches++;
                }
            }

            if (matches != 1 ||
                !IsLowerGuid(tracked.Guid) ||
                !string.Equals(
                    AssetDatabase.AssetPathToGUID(tracked.AssetPath)
                        .ToLowerInvariant(),
                    tracked.Guid,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var ownershipPath = GetOwnershipPath(plan);
            var suffix =
                AssetPathSeparator + "OrpheusAuthoringOwnership.json";
            if (!ownershipPath.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            var generatedRoot = ownershipPath.Substring(
                0,
                ownershipPath.Length - suffix.Length);
            if (!tracked.AssetPath.StartsWith(
                    generatedRoot + AssetPathSeparator + "Events/",
                    StringComparison.Ordinal) ||
                AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                    tracked.AssetPath) == null)
            {
                return false;
            }

            for (var index = 0; index < plan.EventCount; index++)
            {
                if (string.Equals(
                        plan.GetEvent(index).Symbol,
                        tracked.Symbol,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetOwnershipPath(
            OrpheusAuthoringCompilationPlan plan)
        {
            for (var index = 0; index < plan.WriteOperationCount; index++)
            {
                var operation = plan.GetWriteOperation(index);
                if (operation.Kind ==
                    OrpheusAuthoringWriteKind.WriteOwnershipIndex)
                {
                    return operation.AssetPath;
                }
            }

            return string.Empty;
        }

        private static bool TryReadNormalizedText(
            string relativePath,
            out string text)
        {
            text = string.Empty;
            if (string.IsNullOrEmpty(relativePath))
            {
                return false;
            }

            try
            {
                var absolute = Absolute(relativePath);
                if (!File.Exists(absolute))
                {
                    return false;
                }

                text = NormalizeNewlines(
                    File.ReadAllText(absolute, Encoding.UTF8));
                return true;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                return false;
            }
        }

        private static string NormalizeNewlines(string value)
        {
            return (value ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
        }

        private static bool TryValidateOwnership(
            OrpheusAudioValidationProfile profile,
            OrpheusAuthoringCompilationPlan plan,
            string json,
            string ownershipPath,
            out List<OwnershipFingerprintRecord> records)
        {
            records = new List<OwnershipFingerprintRecord>();
            try
            {
                if (!HasTopLevelOrder(
                        json,
                        "schemaVersion",
                        "ownerAuthoringProfileGuid",
                        "enrolledValidationProfileGuid",
                        "manifestGuid",
                        "catalogGuid",
                        "generatedRoot",
                        "acceptedManifestSnapshot",
                        "artifacts") ||
                    !HasObjectArrayShape(
                        json,
                        "acceptedManifestSnapshot",
                        "id",
                        "symbol",
                        "status") ||
                    !HasObjectArrayShape(
                        json,
                        "artifacts",
                        "kind",
                        "moduleId",
                        "symbol",
                        "key",
                        "guid",
                        "path"))
                {
                    return false;
                }

                var dto = JsonUtility.FromJson<OwnershipDto>(json);
                if (dto == null ||
                    dto.schemaVersion != OrpheusAudioAuthoringSchema.Current ||
                    dto.acceptedManifestSnapshot == null ||
                    dto.artifacts == null ||
                    profile == null ||
                    profile.AuthoringProfile == null)
                {
                    return false;
                }

                var authoring = profile.AuthoringProfile.CaptureValue();
                if (authoring.KeyManifest == null ||
                    !string.Equals(
                        dto.ownerAuthoringProfileGuid,
                        GetAssetGuid(profile.AuthoringProfile),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        dto.enrolledValidationProfileGuid,
                        GetAssetGuid(profile),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        dto.manifestGuid,
                        GetAssetGuid(authoring.KeyManifest),
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        dto.catalogGuid,
                        plan.CatalogGuid,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        dto.generatedRoot,
                        authoring.GeneratedRoot,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                var manifest = authoring.KeyManifest.CaptureEntries();
                if (manifest == null ||
                    manifest.Length != dto.acceptedManifestSnapshot.Length)
                {
                    return false;
                }

                Array.Sort(
                    manifest,
                    (left, right) => left.Id.CompareTo(right.Id));
                for (var index = 0; index < manifest.Length; index++)
                {
                    var expected = manifest[index];
                    var actual = dto.acceptedManifestSnapshot[index];
                    if (actual == null ||
                        actual.id != expected.Id ||
                        !string.Equals(
                            actual.symbol,
                            expected.Symbol,
                            StringComparison.Ordinal) ||
                        actual.status != (int)expected.Status)
                    {
                        return false;
                    }
                }

                var paths = new HashSet<string>(StringComparer.Ordinal);
                var eventPaths =
                    new Dictionary<string, OrpheusAuthoringCompiledEventValue>(
                        StringComparer.Ordinal);
                for (var index = 0; index < plan.EventCount; index++)
                {
                    var planned = plan.GetEvent(index);
                    eventPaths.Add(planned.AssetPath, planned);
                }

                var orphanPaths =
                    new Dictionary<string, OrpheusAuthoringOwnedEventValue>(
                        StringComparer.Ordinal);
                var retainOrphans = !HasOperation(
                    plan,
                    OrpheusAuthoringWriteKind.DeleteTrackedOrphan);
                if (retainOrphans)
                {
                    for (var index = 0; index < plan.OrphanCount; index++)
                    {
                        var orphan = plan.GetOrphan(index);
                        orphanPaths.Add(orphan.AssetPath, orphan);
                    }
                }

                var eventCount = 0;
                var catalogCount = 0;
                var assemblyCount = 0;
                var sourceCount = 0;
                var ownershipCount = 0;
                var closureCount = 0;
                for (var index = 0; index < dto.artifacts.Length; index++)
                {
                    var artifact = dto.artifacts[index];
                    if (artifact == null ||
                        !IsOwnershipArtifactKind(artifact.kind) ||
                        string.IsNullOrEmpty(artifact.path) ||
                        !paths.Add(artifact.path) ||
                        !TryResolveOwnedArtifactPath(
                            artifact.path,
                            out var absolute))
                    {
                        return false;
                    }

                    var exists = File.Exists(absolute);
                    var metaExists = File.Exists(absolute + ".meta");
                    var actualGuid =
                        exists
                            ? AssetDatabase.AssetPathToGUID(artifact.path)
                                .ToLowerInvariant()
                            : string.Empty;
                    if (exists != metaExists ||
                        exists &&
                        (!IsLowerGuid(artifact.guid) ||
                         !string.Equals(
                             artifact.guid,
                             actualGuid,
                             StringComparison.Ordinal)) ||
                        !exists && !string.IsNullOrEmpty(artifact.guid))
                    {
                        return false;
                    }

                    if (string.Equals(
                            artifact.kind,
                            "event",
                            StringComparison.Ordinal))
                    {
                        var expectedKey = 0;
                        var expectedModule = string.Empty;
                        var expectedSymbol = string.Empty;
                        if (eventPaths.TryGetValue(
                                artifact.path,
                                out var expected))
                        {
                            expectedKey = expected.Key;
                            expectedModule = expected.ModuleId;
                            expectedSymbol = expected.Symbol;
                        }
                        else if (orphanPaths.TryGetValue(
                                     artifact.path,
                                     out var orphan))
                        {
                            expectedKey = orphan.Key;
                            expectedModule = orphan.ModuleId;
                            expectedSymbol = orphan.Symbol;
                        }
                        else
                        {
                            return false;
                        }

                        if (artifact.key != expectedKey ||
                            !string.Equals(
                                artifact.moduleId,
                                expectedModule,
                                StringComparison.Ordinal) ||
                            !string.Equals(
                                artifact.symbol,
                                expectedSymbol,
                                StringComparison.Ordinal) ||
                            !exists ||
                            AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                                artifact.path) == null)
                        {
                            return false;
                        }

                        eventCount++;
                        records.Add(
                            new OwnershipFingerprintRecord(
                                (ushort)artifact.key,
                                artifact.moduleId,
                                artifact.symbol,
                                artifact.path));
                        continue;
                    }

                    if (artifact.key != 0 ||
                        !string.IsNullOrEmpty(artifact.moduleId) ||
                        !string.IsNullOrEmpty(artifact.symbol))
                    {
                        return false;
                    }

                    if (string.Equals(
                            artifact.kind,
                            "catalog",
                            StringComparison.Ordinal))
                    {
                        catalogCount++;
                        if (!string.Equals(
                                artifact.path,
                                plan.CatalogAssetPath,
                                StringComparison.Ordinal) ||
                            !exists ||
                            AssetDatabase.LoadAssetAtPath<OrpheusAudioCatalog>(
                                artifact.path) == null)
                        {
                            return false;
                        }
                    }
                    else if (string.Equals(
                                 artifact.kind,
                                 "typed-key-assembly",
                                 StringComparison.Ordinal))
                    {
                        assemblyCount++;
                        if (!string.Equals(
                                artifact.path,
                                OrpheusAudioTypedKeyProjection.AssemblyPath,
                                StringComparison.Ordinal) ||
                            !exists)
                        {
                            return false;
                        }
                    }
                    else if (string.Equals(
                                 artifact.kind,
                                 "typed-key-source",
                                 StringComparison.Ordinal))
                    {
                        sourceCount++;
                        if (!string.Equals(
                                artifact.path,
                                OrpheusAudioTypedKeyProjection.SourcePath,
                                StringComparison.Ordinal) ||
                            !exists)
                        {
                            return false;
                        }
                    }
                    else if (string.Equals(
                                 artifact.kind,
                                 "ownership",
                                 StringComparison.Ordinal))
                    {
                        ownershipCount++;
                        if (!string.Equals(
                                artifact.path,
                                ownershipPath,
                                StringComparison.Ordinal) ||
                            !exists)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        closureCount++;
                        if (!string.Equals(
                                artifact.path,
                                GetClosurePath(plan),
                                StringComparison.Ordinal))
                        {
                            return false;
                        }
                    }
                }

                if (eventCount != plan.EventCount + orphanPaths.Count ||
                    catalogCount != 1 ||
                    assemblyCount != 1 ||
                    sourceCount != 1 ||
                    ownershipCount != 1 ||
                    closureCount != 1)
                {
                    return false;
                }

                records.Sort(CompareOwnershipRecords);
                return true;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                records.Clear();
                return false;
            }
        }

        private static bool TryMaterializeOwnershipTemplate(
            string template,
            out string materialized)
        {
            materialized = template ?? string.Empty;
            if (!TryCaptureOwnershipRecords(
                    materialized,
                    out _))
            {
                return false;
            }

            try
            {
                var dto = JsonUtility.FromJson<OwnershipDto>(materialized);
                var replacements =
                    new List<OwnershipGuidReplacement>();
                for (var index = 0; index < dto.artifacts.Length; index++)
                {
                    var artifact = dto.artifacts[index];
                    if (artifact.guid == null ||
                        !TryResolveOwnedArtifactPath(
                            artifact.path,
                            out var absolute))
                    {
                        return false;
                    }

                    var exists = File.Exists(absolute);
                    var metaExists = File.Exists(absolute + ".meta");
                    if (exists != metaExists)
                    {
                        return false;
                    }

                    if (!exists || artifact.guid.Length != 0)
                    {
                        continue;
                    }

                    var guid = AssetDatabase.AssetPathToGUID(
                            artifact.path)
                        .ToLowerInvariant();
                    if (!IsLowerGuid(guid))
                    {
                        return false;
                    }

                    var pathMarker =
                        "\"path\":\"" + Escape(artifact.path) + "\"";
                    var pathIndex = materialized.IndexOf(
                        pathMarker,
                        StringComparison.Ordinal);
                    if (pathIndex < 0 ||
                        materialized.IndexOf(
                            pathMarker,
                            pathIndex + pathMarker.Length,
                            StringComparison.Ordinal) >= 0)
                    {
                        return false;
                    }

                    var objectStart =
                        materialized.LastIndexOf('{', pathIndex);
                    const string guidMarker = "\"guid\":\"\"";
                    var guidIndex = materialized.IndexOf(
                        guidMarker,
                        objectStart,
                        StringComparison.Ordinal);
                    if (objectStart < 0 || guidIndex < objectStart ||
                        guidIndex > pathIndex)
                    {
                        return false;
                    }

                    replacements.Add(
                        new OwnershipGuidReplacement(
                            guidIndex + "\"guid\":\"".Length,
                            guid));
                }

                replacements.Sort(
                    (left, right) => right.Index.CompareTo(left.Index));
                var builder = new StringBuilder(materialized);
                for (var index = 0; index < replacements.Count; index++)
                {
                    var replacement = replacements[index];
                    builder.Insert(replacement.Index, replacement.Guid);
                }

                materialized = builder.ToString();
                return true;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                materialized = string.Empty;
                return false;
            }
        }

        private static bool TryCaptureOwnershipRecords(
            string json,
            out List<OwnershipFingerprintRecord> records)
        {
            records = new List<OwnershipFingerprintRecord>();
            try
            {
                if (!HasTopLevelOrder(
                        json,
                        "schemaVersion",
                        "ownerAuthoringProfileGuid",
                        "enrolledValidationProfileGuid",
                        "manifestGuid",
                        "catalogGuid",
                        "generatedRoot",
                        "acceptedManifestSnapshot",
                        "artifacts") ||
                    !HasObjectArrayShape(
                        json,
                        "acceptedManifestSnapshot",
                        "id",
                        "symbol",
                        "status") ||
                    !HasObjectArrayShape(
                        json,
                        "artifacts",
                        "kind",
                        "moduleId",
                        "symbol",
                        "key",
                        "guid",
                        "path"))
                {
                    return false;
                }

                var dto = JsonUtility.FromJson<OwnershipDto>(json);
                if (dto == null ||
                    dto.schemaVersion != OrpheusAudioAuthoringSchema.Current ||
                    dto.acceptedManifestSnapshot == null ||
                    dto.artifacts == null)
                {
                    return false;
                }

                var paths = new HashSet<string>(StringComparer.Ordinal);
                var kinds = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < dto.artifacts.Length; index++)
                {
                    var artifact = dto.artifacts[index];
                    if (artifact == null ||
                        !IsOwnershipArtifactKind(artifact.kind) ||
                        string.IsNullOrEmpty(artifact.path) ||
                        !paths.Add(artifact.path))
                    {
                        return false;
                    }

                    kinds.Add(artifact.kind);
                    if (string.Equals(
                            artifact.kind,
                            "event",
                            StringComparison.Ordinal))
                    {
                        if (artifact.key <= 0 ||
                            artifact.key > ushort.MaxValue ||
                            string.IsNullOrEmpty(artifact.moduleId) ||
                            string.IsNullOrEmpty(artifact.symbol))
                        {
                            return false;
                        }

                        records.Add(
                            new OwnershipFingerprintRecord(
                                (ushort)artifact.key,
                                artifact.moduleId,
                                artifact.symbol,
                                artifact.path));
                    }
                    else if (artifact.key != 0 ||
                             !string.IsNullOrEmpty(artifact.moduleId) ||
                             !string.IsNullOrEmpty(artifact.symbol))
                    {
                        return false;
                    }
                }

                if (!kinds.Contains("catalog") ||
                    !kinds.Contains("typed-key-assembly") ||
                    !kinds.Contains("typed-key-source") ||
                    !kinds.Contains("ownership") ||
                    !kinds.Contains("closure"))
                {
                    return false;
                }

                records.Sort(CompareOwnershipRecords);
                return true;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                records.Clear();
                return false;
            }
        }

        private static string ComputeOutputFingerprint(
            OrpheusAuthoringCompilationPlan plan,
            ushort[] catalogKeys,
            string catalogGuid,
            string catalogPath,
            string assemblyText,
            string sourceText,
            IList<OwnershipFingerprintRecord> ownership,
            string[] eventFingerprints)
        {
            var writer = new OutputFingerprintWriter();
            writer.WriteString("Orpheus.Audio.AuthoringCompilation\0v1\0");
            writer.WriteString("Output");
            writer.WriteInt32(plan.EventCount);
            for (var index = 0; index < plan.EventCount; index++)
            {
                var value = plan.GetEvent(index);
                writer.WriteUInt16(value.Key);
                writer.WriteString(value.ModuleId);
                writer.WriteString(value.Symbol);
                writer.WriteString(value.AssetPath);
                writer.WriteString(
                    eventFingerprints == null
                        ? value.ContentFingerprint
                        : eventFingerprints[index]);
            }

            writer.WriteInt32(catalogKeys.Length);
            for (var index = 0; index < catalogKeys.Length; index++)
            {
                writer.WriteUInt16(catalogKeys[index]);
            }

            writer.WriteString(catalogGuid);
            writer.WriteString(catalogPath);
            writer.WriteString(assemblyText);
            writer.WriteString(sourceText);
            writer.WriteString("OwnershipWithoutMetaGuid");
            writer.WriteInt32(ownership.Count);
            for (var index = 0; index < ownership.Count; index++)
            {
                var record = ownership[index];
                writer.WriteUInt16(record.Key);
                writer.WriteString(record.ModuleId);
                writer.WriteString(record.Symbol);
                writer.WriteString(record.AssetPath);
            }

            return writer.Finish();
        }

        private static int CompareOwnershipRecords(
            OwnershipFingerprintRecord left,
            OwnershipFingerprintRecord right)
        {
            var result = left.Key.CompareTo(right.Key);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(
                left.ModuleId,
                right.ModuleId,
                StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(
                left.Symbol,
                right.Symbol,
                StringComparison.Ordinal);
            return result != 0
                ? result
                : string.Compare(
                    left.AssetPath,
                    right.AssetPath,
                    StringComparison.Ordinal);
        }

        internal static string ComputeOutputFingerprintForTests(
            OrpheusAuthoringCompilationPlan plan,
            string ownershipJson)
        {
            if (plan == null ||
                !TryCaptureOwnershipRecords(
                    ownershipJson,
                    out var ownership))
            {
                return string.Empty;
            }

            return ComputeOutputFingerprint(
                plan,
                plan.CatalogKeys,
                plan.CatalogGuid,
                plan.CatalogAssetPath,
                NormalizeNewlines(plan.TypedKeyAssemblyText),
                NormalizeNewlines(plan.TypedKeySourceText),
                ownership,
                null);
        }

        internal static string CaptureRecoveryPayloadForTests(
            params string[] paths)
        {
            var snapshots = new List<Snapshot>();
            for (var index = 0; index < paths.Length; index++)
            {
                CapturePath(paths[index], snapshots);
            }

            NormalizeSnapshots(snapshots);
            return BuildPayload(snapshots);
        }

        private static List<Snapshot> CaptureSnapshots(
            OrpheusAudioValidationProfile profile,
            OrpheusAuthoringCompilationPlan plan,
            OrpheusAudioAuthoringCommitArtifacts artifacts)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < plan.WriteOperationCount; index++)
            {
                var operation = plan.GetWriteOperation(index);
                if (operation.Kind != OrpheusAuthoringWriteKind.CreateDirectory)
                {
                    AddPath(paths, operation.AssetPath);
                }

                AddPath(paths, operation.SourcePath);
            }

            AddPath(paths, OrpheusAudioTypedKeyProjection.AssemblyPath);
            AddPath(paths, OrpheusAudioTypedKeyProjection.SourcePath);
            AddPath(paths, AssetDatabase.GetAssetPath(profile.Catalog));
            AddPath(paths, AssetDatabase.GetAssetPath(profile));
            AddPath(paths, artifacts.ExecutionPath);
            var ordered = new List<string>(paths);
            ordered.Sort(StringComparer.Ordinal);
            var snapshots = new List<Snapshot>();
            for (var index = 0; index < ordered.Count; index++)
            {
                CapturePath(ordered[index], snapshots);
                CapturePath(ordered[index] + ".meta", snapshots);
                CapturePath(ordered[index] + ".tmp", snapshots);
                var parent = Path.GetDirectoryName(ordered[index])
                    .Replace('\\', AssetPathSeparator);
                while (!string.IsNullOrEmpty(parent) &&
                       !string.Equals(parent, "Assets", StringComparison.Ordinal) &&
                       !string.Equals(parent, "Library", StringComparison.Ordinal))
                {
                    CaptureDirectory(parent, snapshots);
                    CapturePath(parent + ".meta", snapshots);
                    parent = Path.GetDirectoryName(parent)
                        ?.Replace('\\', AssetPathSeparator);
                }
            }

            for (var index = 0; index < plan.WriteOperationCount; index++)
            {
                var operation = plan.GetWriteOperation(index);
                if (operation.Kind == OrpheusAuthoringWriteKind.CreateDirectory)
                {
                    CaptureDirectory(operation.AssetPath, snapshots);
                    CapturePath(operation.AssetPath + ".meta", snapshots);
                }
            }

            return snapshots;
        }

        private static void AddPath(ISet<string> paths, string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path.Replace('\\', AssetPathSeparator));
            }
        }

        private static void CapturePath(string path, ICollection<Snapshot> result)
        {
            var absolute = Absolute(path);
            result.Add(
                new Snapshot
                {
                    path = path,
                    kind = "file",
                    existed = File.Exists(absolute),
                    bytesBase64 = File.Exists(absolute)
                        ? Convert.ToBase64String(File.ReadAllBytes(absolute))
                        : string.Empty,
                    sha256 = File.Exists(absolute)
                        ? Sha256Bytes(File.ReadAllBytes(absolute))
                        : string.Empty,
                    guid = path.StartsWith("Assets/", StringComparison.Ordinal)
                        ? AssetDatabase.AssetPathToGUID(path).ToLowerInvariant()
                        : string.Empty,
                    mainType = path.StartsWith("Assets/", StringComparison.Ordinal) &&
                               AssetDatabase.LoadMainAssetAtPath(path) != null
                        ? AssetDatabase.LoadMainAssetAtPath(path).GetType().FullName
                        : string.Empty
                });
        }

        private static void CaptureDirectory(string path, ICollection<Snapshot> result)
        {
            result.Add(
                new Snapshot
                {
                    path = path,
                    kind = "directory",
                    existed = Directory.Exists(Absolute(path)),
                    bytesBase64 = string.Empty
                });
        }

        private static OrpheusAudioAuthoringCompileStatus Rollback(
            string journalPath,
            string id,
            string payload,
            IList<Snapshot> snapshots,
            IOrpheusAudioAuthoringFailureInjection injection,
            bool forceProofFailure)
        {
            try
            {
                WriteJournal(journalPath, id, OrpheusAudioAuthoringTransactionPhase.RollingBack, payload);
                Step(injection, false, "JournalRollingBack");
                for (var index = snapshots.Count - 1; index >= 0; index--)
                {
                    var snapshot = snapshots[index];
                    var absolute = AbsoluteOrNative(snapshot.path);
                    if (snapshot.kind == "file")
                    {
                        if (snapshot.existed)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(absolute));
                            File.WriteAllBytes(
                                absolute,
                                Convert.FromBase64String(snapshot.bytesBase64));
                        }
                        else if (File.Exists(absolute))
                        {
                            File.Delete(absolute);
                        }
                    }
                    else if (!snapshot.existed && Directory.Exists(absolute) &&
                             Directory.GetFileSystemEntries(absolute).Length == 0)
                    {
                        Directory.Delete(absolute);
                    }
                }

                for (var index = snapshots.Count - 1; index >= 0; index--)
                {
                    var snapshot = snapshots[index];
                    var absolute = AbsoluteOrNative(snapshot.path);
                    if (snapshot.kind == "directory" &&
                        !snapshot.existed &&
                        Directory.Exists(absolute) &&
                        Directory.GetFileSystemEntries(absolute).Length == 0)
                    {
                        Directory.Delete(absolute);
                    }
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (forceProofFailure || !Prove(snapshots))
                {
                    throw new InvalidDataException("RollbackProof");
                }

                WriteJournal(journalPath, id, OrpheusAudioAuthoringTransactionPhase.RolledBack, payload);
                return OrpheusAudioAuthoringCompileStatus.RolledBack;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                LastFailureForTests =
                    exception.GetType().FullName + ":" + exception.Message;
                try
                {
                    WriteJournal(journalPath, id, OrpheusAudioAuthoringTransactionPhase.RollbackFailed, payload);
                }
                catch (Exception persistException) when (!IsCatastrophic(persistException))
                {
                }

                return OrpheusAudioAuthoringCompileStatus.RollbackFailed;
            }
        }

        private static bool Prove(IList<Snapshot> snapshots)
        {
            for (var index = 0; index < snapshots.Count; index++)
            {
                var snapshot = snapshots[index];
                var absolute = AbsoluteOrNative(snapshot.path);
                if (snapshot.kind == "file")
                {
                    if (File.Exists(absolute) != snapshot.existed)
                    {
                        return false;
                    }

                    if (snapshot.existed &&
                        Convert.ToBase64String(File.ReadAllBytes(absolute)) !=
                        snapshot.bytesBase64)
                    {
                        return false;
                    }

                    if (snapshot.existed &&
                        !string.Equals(
                            Sha256Bytes(File.ReadAllBytes(absolute)),
                            snapshot.sha256,
                            StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (snapshot.existed && !string.IsNullOrEmpty(snapshot.guid) &&
                        (!string.Equals(
                             AssetDatabase.AssetPathToGUID(snapshot.path).ToLowerInvariant(),
                             snapshot.guid,
                             StringComparison.Ordinal) ||
                         AssetDatabase.LoadMainAssetAtPath(snapshot.path) == null ||
                         !string.Equals(
                             AssetDatabase.LoadMainAssetAtPath(snapshot.path)
                                 .GetType().FullName,
                             snapshot.mainType,
                             StringComparison.Ordinal)))
                    {
                        return false;
                    }
                }
                else if (Directory.Exists(absolute) != snapshot.existed)
                {
                    return false;
                }
            }

            return true;
        }

        internal static void WriteJournalForTests(
            string path,
            string id,
            OrpheusAudioAuthoringTransactionPhase phase,
            string payload)
        {
            WriteJournal(path, id, phase, payload);
        }

        internal static bool TryReadJournalForTests(
            string path,
            out OrpheusAudioAuthoringTransactionPhase phase,
            out string payload)
        {
            var result = TryReadJournal(path, out var value);
            phase = result ? value.Phase : OrpheusAudioAuthoringTransactionPhase.Invalid;
            payload = result ? value.Payload : string.Empty;
            return result;
        }

        internal static OrpheusAudioAuthoringCompileStatus ExecuteFileFixtureForTests(
            string root,
            string[] existingPaths,
            Action mutation,
            string[] createdPaths,
            bool failRollbackProof = false)
        {
            var id = Guid.NewGuid().ToString("N");
            var journal = Path.Combine(root, id, "journal.json");
            var snapshots = new List<Snapshot>();
            for (var index = 0; index < existingPaths.Length; index++)
            {
                var path = existingPaths[index];
                snapshots.Add(
                    new Snapshot
                    {
                        path = path,
                        kind = "file",
                        existed = File.Exists(path),
                        bytesBase64 = File.Exists(path)
                            ? Convert.ToBase64String(File.ReadAllBytes(path))
                            : string.Empty,
                        sha256 = File.Exists(path)
                            ? Sha256Bytes(File.ReadAllBytes(path))
                            : string.Empty,
                        guid = string.Empty,
                        mainType = string.Empty
                    });
            }

            for (var index = 0; index < createdPaths.Length; index++)
            {
                snapshots.Add(
                    new Snapshot
                    {
                        path = createdPaths[index],
                        kind = "file",
                        existed = false,
                        bytesBase64 = string.Empty,
                        sha256 = string.Empty,
                        guid = string.Empty,
                        mainType = string.Empty
                    });
                var parent = Path.GetDirectoryName(createdPaths[index]);
                snapshots.Add(
                    new Snapshot
                    {
                        path = parent,
                        kind = "directory",
                        existed = Directory.Exists(parent),
                        bytesBase64 = string.Empty,
                        sha256 = string.Empty,
                        guid = string.Empty,
                        mainType = string.Empty
                    });
            }

            NormalizeSnapshots(snapshots);
            var payload = BuildPayload(snapshots);
            WriteJournal(journal, id, OrpheusAudioAuthoringTransactionPhase.Prepared, payload);
            try
            {
                mutation();
                WriteJournal(journal, id, OrpheusAudioAuthoringTransactionPhase.Committed, payload);
                return OrpheusAudioAuthoringCompileStatus.SucceededChanged;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                return Rollback(
                    journal,
                    id,
                    payload,
                    snapshots,
                    null,
                    failRollbackProof);
            }
        }

        private static void WriteJournal(
            string path,
            string id,
            OrpheusAudioAuthoringTransactionPhase phase,
            string payload)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var json = "{\"schemaVersion\":1,\"transactionId\":\"" + id +
                       "\",\"phase\":\"" + phase +
                       "\",\"payload\":\"" + Escape(payload) +
                       "\",\"payloadSha256\":\"" + Sha256(payload) + "\"}\n";
            var temporary = path + ".tmp";
            using (var stream = new FileStream(
                       temporary,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                ReplaceWithRetry(temporary, path);
            }
            else
            {
                File.Move(temporary, path);
            }
        }

        private static void PersistRecoveryBlocker(string root)
        {
            try
            {
                var id = Guid.NewGuid().ToString("N");
                WriteJournal(
                    Path.Combine(root, id, "journal.json"),
                    id,
                    OrpheusAudioAuthoringTransactionPhase.RollbackFailed,
                    "{\"schemaVersion\":1,\"snapshots\":[]}");
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
            }
        }

        private static bool TryPersistRollbackFailed(
            string path,
            string id,
            string payload)
        {
            try
            {
                WriteJournal(
                    path,
                    id,
                    OrpheusAudioAuthoringTransactionPhase.RollbackFailed,
                    payload);
                return true;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                return false;
            }
        }

        private static bool TryReadJournal(string path, out JournalValue value)
        {
            value = default(JournalValue);
            try
            {
                var raw = File.ReadAllText(path, Encoding.UTF8);
                var dto = JsonUtility.FromJson<JournalDto>(raw);
                var folder = new DirectoryInfo(Path.GetDirectoryName(path)).Name;
                if (!HasTopLevelOrder(
                        raw,
                        "schemaVersion",
                        "transactionId",
                        "phase",
                        "payload",
                        "payloadSha256") ||
                    dto == null || dto.schemaVersion != 1 ||
                    !IsLowerGuid(dto.transactionId) ||
                    !string.Equals(folder, dto.transactionId, StringComparison.Ordinal) ||
                    !IsJournalPhase(dto.phase) ||
                    !Enum.TryParse(
                        dto.phase,
                        false,
                        out OrpheusAudioAuthoringTransactionPhase phase) ||
                    !string.Equals(dto.payloadSha256, Sha256(dto.payload ?? string.Empty), StringComparison.Ordinal))
                {
                    return false;
                }

                value = new JournalValue(path, dto.transactionId, phase, dto.payload);
                return true;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                return false;
            }
        }

        private static string BuildPayload(IList<Snapshot> snapshots)
        {
            var builder = new StringBuilder(256 + snapshots.Count * 160);
            builder.Append("{\"schemaVersion\":1,\"snapshots\":[");
            for (var index = 0; index < snapshots.Count; index++)
            {
                if (index != 0) builder.Append(',');
                var item = snapshots[index];
                builder.Append("{\"path\":\"").Append(Escape(item.path));
                builder.Append("\",\"kind\":\"").Append(item.kind);
                builder.Append("\",\"existed\":").Append(item.existed ? "true" : "false");
                builder.Append(",\"bytesBase64\":\"").Append(item.bytesBase64);
                builder.Append("\",\"sha256\":\"").Append(item.sha256);
                builder.Append("\",\"guid\":\"").Append(item.guid);
                builder.Append("\",\"mainType\":\"").Append(Escape(item.mainType)).Append("\"}");
            }

            return builder.Append("]}").ToString();
        }

        private static List<Snapshot> ParsePayload(string payload)
        {
            if (!HasTopLevelOrder(
                    payload,
                    "schemaVersion",
                    "snapshots") ||
                !HasObjectArrayShape(
                    payload,
                    "snapshots",
                    "path",
                    "kind",
                    "existed",
                    "bytesBase64",
                    "sha256",
                    "guid",
                    "mainType"))
            {
                throw new InvalidDataException("SnapshotShape");
            }

            var dto = JsonUtility.FromJson<PayloadDto>(payload);
            if (dto == null || dto.schemaVersion != 1 || dto.snapshots == null)
            {
                throw new InvalidDataException("SnapshotPayload");
            }

            var result = new List<Snapshot>(dto.snapshots);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string previous = null;
            for (var index = 0; index < result.Count; index++)
            {
                var item = result[index];
                if (item == null ||
                    !TryResolveRecoveryPath(item.path, out _) ||
                    (item.kind != "file" && item.kind != "directory") ||
                    !seen.Add(item.path) ||
                    previous != null &&
                    string.CompareOrdinal(previous, item.kind + "|" + item.path) >= 0)
                {
                    throw new InvalidDataException("SnapshotEntry");
                }

                if (item.bytesBase64 == null ||
                    item.sha256 == null ||
                    item.guid == null ||
                    item.mainType == null)
                {
                    throw new InvalidDataException("SnapshotNull");
                }

                if (item.kind == "directory")
                {
                    if (item.bytesBase64.Length != 0 ||
                        item.sha256.Length != 0 ||
                        item.guid.Length != 0 ||
                        item.mainType.Length != 0)
                    {
                        throw new InvalidDataException(
                            "SnapshotDirectoryFields");
                    }
                }
                else if (!item.existed)
                {
                    if (item.bytesBase64.Length != 0 ||
                        item.sha256.Length != 0 ||
                        item.guid.Length != 0 ||
                        item.mainType.Length != 0)
                    {
                        throw new InvalidDataException(
                            "SnapshotAbsentFields");
                    }
                }
                else
                {
                    byte[] bytes;
                    try
                    {
                        bytes = Convert.FromBase64String(item.bytesBase64);
                    }
                    catch (FormatException)
                    {
                        throw new InvalidDataException(
                            "SnapshotBase64");
                    }

                    if (!string.Equals(
                            Convert.ToBase64String(bytes),
                            item.bytesBase64,
                            StringComparison.Ordinal) ||
                        !IsLowerHash(item.sha256) ||
                        !string.Equals(
                            Sha256Bytes(bytes),
                            item.sha256,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "SnapshotBytes");
                    }

                    if (item.guid.Length == 0)
                    {
                        if (item.mainType.Length != 0)
                        {
                            throw new InvalidDataException(
                                "SnapshotTypeWithoutGuid");
                        }
                    }
                    else if (!item.path.StartsWith(
                                 "Assets/",
                                 StringComparison.Ordinal) ||
                             item.path.EndsWith(
                                 ".meta",
                                 StringComparison.Ordinal) ||
                             !IsLowerGuid(item.guid) ||
                             item.mainType.Length == 0)
                    {
                        throw new InvalidDataException(
                            "SnapshotAssetIdentity");
                    }
                }

                previous = item.kind + "|" + item.path;
            }

            if (!string.Equals(
                    BuildPayload(result),
                    payload,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "SnapshotCanonicalPayload");
            }

            return result;
        }

        private static void NormalizeSnapshots(List<Snapshot> snapshots)
        {
            var unique = new Dictionary<string, Snapshot>(StringComparer.Ordinal);
            for (var index = 0; index < snapshots.Count; index++)
            {
                var item = snapshots[index];
                var key = item.kind + "|" + item.path;
                if (!unique.ContainsKey(key)) unique.Add(key, item);
            }

            snapshots.Clear();
            snapshots.AddRange(unique.Values);
            snapshots.Sort((left, right) => string.CompareOrdinal(
                left.kind + "|" + left.path,
                right.kind + "|" + right.path));
        }

        private static void WriteTextAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text ?? string.Empty);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(path)) ReplaceWithRetry(temp, path);
            else File.Move(temp, path);
        }

        private static void ReplaceWithRetry(string temporary, string path)
        {
            IOException last = null;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    File.Replace(temporary, path, null);
                    return;
                }
                catch (IOException exception)
                {
                    last = exception;
                    System.Threading.Thread.Sleep(10);
                }
            }

            throw last ?? new IOException(path);
        }

        private static string GetClosurePath(OrpheusAuthoringCompilationPlan plan)
        {
            for (var index = 0; index < plan.WriteOperationCount; index++)
            {
                var operation = plan.GetWriteOperation(index);
                if (operation.Kind == OrpheusAuthoringWriteKind.WriteClosureReport)
                    return operation.AssetPath;
            }

            throw new InvalidDataException("ClosurePath");
        }

        private static void Set(SerializedObject value, string field, int input)
        {
            value.FindProperty(field).intValue = input;
        }

        private static void Set(SerializedObject value, string field, float input)
        {
            value.FindProperty(field).floatValue = input;
        }

        private static void Step(
            IOrpheusAudioAuthoringFailureInjection injection,
            bool before,
            string operation)
        {
            if (injection == null) return;
            if (before) injection.Before(operation);
            else injection.After(operation);
        }

        private static string Absolute(string relative)
        {
            return Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                relative.Replace(
                    AssetPathSeparator,
                    Path.DirectorySeparatorChar));
        }

        private static string ResolveAssetGuid(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var guid = AssetDatabase.AssetPathToGUID(path);
            return string.IsNullOrEmpty(guid)
                ? string.Empty
                : guid.ToLowerInvariant();
        }

        private static string AbsoluteOrNative(string path)
        {
            return Path.IsPathRooted(path) ? path : Absolute(path);
        }

        private static bool TryEnumerateRecoveryJournals(
            string root,
            out string[] journals)
        {
            journals = Array.Empty<string>();
            try
            {
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                var directories =
                    Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
                Array.Sort(directories, StringComparer.Ordinal);
                journals = new string[directories.Length];
                for (var index = 0; index < directories.Length; index++)
                {
                    var directory = directories[index];
                    var name = Path.GetFileName(directory);
                    if (!IsLowerGuid(name) ||
                        (File.GetAttributes(directory) &
                         FileAttributes.ReparsePoint) != 0)
                    {
                        return false;
                    }

                    var journal = Path.Combine(directory, "journal.json");
                    if (!File.Exists(journal) ||
                        (File.GetAttributes(journal) &
                         FileAttributes.ReparsePoint) != 0)
                    {
                        return false;
                    }

                    journals[index] = journal;
                }

                return true;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                journals = Array.Empty<string>();
                return false;
            }
        }

        private static bool TryDeleteSuccessfulTerminalJournal(
            string journalPath)
        {
            try
            {
                if (string.IsNullOrEmpty(journalPath) ||
                    !string.Equals(
                        Path.GetFileName(journalPath),
                        "journal.json",
                        StringComparison.Ordinal))
                {
                    return false;
                }

                var directory = Path.GetDirectoryName(
                    Path.GetFullPath(journalPath));
                if (string.IsNullOrEmpty(directory) ||
                    !Directory.Exists(directory) ||
                    !IsLowerGuid(Path.GetFileName(directory)) ||
                    (File.GetAttributes(directory) &
                     FileAttributes.ReparsePoint) != 0 ||
                    !File.Exists(journalPath) ||
                    (File.GetAttributes(journalPath) &
                     FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                var entries = Directory.GetFileSystemEntries(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly);
                if (entries.Length != 1 ||
                    !string.Equals(
                        Path.GetFullPath(entries[0]),
                        Path.GetFullPath(journalPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                File.Delete(journalPath);
                Directory.Delete(directory, false);
                return !Directory.Exists(directory);
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                return false;
            }
        }

        private static bool TryResolveRecoveryPath(
            string relative,
            out string absolute)
        {
            absolute = string.Empty;
            if (string.IsNullOrEmpty(relative) ||
                relative.IndexOf('\\') >= 0 ||
                Path.IsPathRooted(relative) ||
                (!relative.StartsWith("Assets/", StringComparison.Ordinal) &&
                 !relative.StartsWith("Library/", StringComparison.Ordinal)))
            {
                return false;
            }

            var segments = relative.Split(AssetPathSeparator);
            for (var index = 0; index < segments.Length; index++)
            {
                if (segments[index].Length == 0 ||
                    string.Equals(segments[index], ".", StringComparison.Ordinal) ||
                    string.Equals(segments[index], "..", StringComparison.Ordinal) ||
                    segments[index].IndexOf(':') >= 0)
                {
                    return false;
                }
            }

            try
            {
                var projectRoot =
                    Directory.GetParent(Application.dataPath).FullName;
                var rootWithSeparator =
                    projectRoot.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                absolute = Path.GetFullPath(
                    Path.Combine(
                        projectRoot,
                        relative.Replace(
                            AssetPathSeparator,
                            Path.DirectorySeparatorChar)));
                if (!absolute.StartsWith(
                        rootWithSeparator,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        absolute.Substring(rootWithSeparator.Length)
                            .Replace('\\', AssetPathSeparator),
                        relative,
                        StringComparison.Ordinal))
                {
                    absolute = string.Empty;
                    return false;
                }

                var cursor = projectRoot;
                for (var index = 0; index < segments.Length; index++)
                {
                    cursor = Path.Combine(cursor, segments[index]);
                    if ((Directory.Exists(cursor) || File.Exists(cursor)) &&
                        (File.GetAttributes(cursor) &
                         FileAttributes.ReparsePoint) != 0)
                    {
                        absolute = string.Empty;
                        return false;
                    }
                }

                return true;
            }
            catch (Exception exception) when (!IsCatastrophic(exception))
            {
                absolute = string.Empty;
                return false;
            }
        }

        private static bool TryResolveOwnedArtifactPath(
            string path,
            out string absolute)
        {
            absolute = string.Empty;
            return path != null &&
                   path.StartsWith("Assets/", StringComparison.Ordinal) &&
                   TryResolveRecoveryPath(path, out absolute);
        }

        private static string GetAssetGuid(UnityEngine.Object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            var path = AssetDatabase.GetAssetPath(value);
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(path).ToLowerInvariant();
        }

        private static bool IsOwnershipArtifactKind(string value)
        {
            return string.Equals(value, "event", StringComparison.Ordinal) ||
                   string.Equals(value, "catalog", StringComparison.Ordinal) ||
                   string.Equals(
                       value,
                       "typed-key-assembly",
                       StringComparison.Ordinal) ||
                   string.Equals(
                       value,
                       "typed-key-source",
                       StringComparison.Ordinal) ||
                   string.Equals(value, "ownership", StringComparison.Ordinal) ||
                   string.Equals(value, "closure", StringComparison.Ordinal);
        }

        private static bool IsJournalPhase(string value)
        {
            return string.Equals(value, "Prepared", StringComparison.Ordinal) ||
                   string.Equals(value, "Writing", StringComparison.Ordinal) ||
                   string.Equals(value, "Importing", StringComparison.Ordinal) ||
                   string.Equals(value, "Validating", StringComparison.Ordinal) ||
                   string.Equals(value, "Committed", StringComparison.Ordinal) ||
                   string.Equals(value, "RollingBack", StringComparison.Ordinal) ||
                   string.Equals(value, "RolledBack", StringComparison.Ordinal) ||
                   string.Equals(value, "RollbackFailed", StringComparison.Ordinal);
        }

        private static bool HasTopLevelOrder(
            string json,
            params string[] expected)
        {
            var names = GetTopLevelPropertyNames(json);
            if (names.Count != expected.Length)
            {
                return false;
            }

            for (var index = 0; index < expected.Length; index++)
            {
                if (!string.Equals(
                        names[index],
                        expected[index],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> GetTopLevelPropertyNames(string json)
        {
            var result = new List<string>();
            var depth = 0;
            var inString = false;
            var escaped = false;
            var stringStart = -1;
            for (var index = 0; index < (json ?? string.Empty).Length; index++)
            {
                var character = json[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                        if (depth == 1)
                        {
                            var cursor = index + 1;
                            while (cursor < json.Length &&
                                   char.IsWhiteSpace(json[cursor]))
                            {
                                cursor++;
                            }

                            if (cursor < json.Length && json[cursor] == ':')
                            {
                                result.Add(
                                    json.Substring(
                                        stringStart,
                                        index - stringStart));
                            }
                        }

                        continue;
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                    stringStart = index + 1;
                }
                else if (character == '{' || character == '[')
                {
                    depth++;
                }
                else if (character == '}' || character == ']')
                {
                    depth--;
                }
            }

            return result;
        }

        private static bool HasObjectArrayShape(
            string json,
            string property,
            params string[] expected)
        {
            var marker = "\"" + property + "\"";
            var markerIndex = (json ?? string.Empty).IndexOf(
                marker,
                StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return false;
            }

            var cursor = markerIndex + marker.Length;
            while (cursor < json.Length && char.IsWhiteSpace(json[cursor]))
            {
                cursor++;
            }

            if (cursor >= json.Length || json[cursor++] != ':')
            {
                return false;
            }

            while (cursor < json.Length && char.IsWhiteSpace(json[cursor]))
            {
                cursor++;
            }

            if (cursor >= json.Length || json[cursor++] != '[')
            {
                return false;
            }

            while (cursor < json.Length)
            {
                while (cursor < json.Length &&
                       (char.IsWhiteSpace(json[cursor]) ||
                        json[cursor] == ','))
                {
                    cursor++;
                }

                if (cursor >= json.Length)
                {
                    return false;
                }

                if (json[cursor] == ']')
                {
                    return true;
                }

                if (json[cursor] != '{')
                {
                    return false;
                }

                var start = cursor;
                var depth = 0;
                var inString = false;
                var escaped = false;
                for (; cursor < json.Length; cursor++)
                {
                    var character = json[cursor];
                    if (inString)
                    {
                        if (escaped)
                        {
                            escaped = false;
                        }
                        else if (character == '\\')
                        {
                            escaped = true;
                        }
                        else if (character == '"')
                        {
                            inString = false;
                        }

                        continue;
                    }

                    if (character == '"')
                    {
                        inString = true;
                    }
                    else if (character == '{')
                    {
                        depth++;
                    }
                    else if (character == '}' && --depth == 0)
                    {
                        var item = json.Substring(
                            start,
                            cursor - start + 1);
                        if (!HasTopLevelOrder(item, expected))
                        {
                            return false;
                        }

                        cursor++;
                        break;
                    }
                }
            }

            return false;
        }

        private static string Escape(string value)
        {
            value = value ?? string.Empty;
            var builder = new StringBuilder(value.Length);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                switch (character)
                {
                    case '\\':
                        builder.Append(JsonEscapeCharacter);
                        builder.Append(JsonEscapeCharacter);
                        break;
                    case '"':
                        builder.Append(JsonEscapeCharacter);
                        builder.Append('"');
                        break;
                    case '\r':
                        builder.Append(JsonEscapeCharacter);
                        builder.Append('r');
                        break;
                    case '\n':
                        builder.Append(JsonEscapeCharacter);
                        builder.Append('n');
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string Sha256(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var text = new StringBuilder(64);
                for (var index = 0; index < bytes.Length; index++)
                    text.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        private static string Sha256Bytes(byte[] value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(value);
                var text = new StringBuilder(64);
                for (var index = 0; index < bytes.Length; index++)
                    text.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        private static bool IsLowerGuid(string value)
        {
            if (value == null || value.Length != 32) return false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!(character >= '0' && character <= '9') &&
                    !(character >= 'a' && character <= 'f'))
                    return false;
            }

            return true;
        }

        private static bool IsLowerHash(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

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

        private static bool IsCatastrophic(Exception exception)
        {
            return exception is
                       OrpheusAudioAuthoringSimulatedProcessTerminationException ||
                   exception is OutOfMemoryException ||
                   exception is StackOverflowException ||
                   exception is AccessViolationException;
        }

        [Serializable]
        private sealed class JournalDto
        {
            public int schemaVersion;
            public string transactionId;
            public string phase;
            public string payload;
            public string payloadSha256;
        }

        [Serializable]
        private sealed class PayloadDto
        {
            public int schemaVersion;
            public Snapshot[] snapshots;
        }

        [Serializable]
        private sealed class OwnershipDto
        {
            public int schemaVersion;
            public string ownerAuthoringProfileGuid;
            public string enrolledValidationProfileGuid;
            public string manifestGuid;
            public string catalogGuid;
            public string generatedRoot;
            public OwnershipManifestEntryDto[] acceptedManifestSnapshot;
            public OwnershipArtifactDto[] artifacts;
        }

        [Serializable]
        private sealed class OwnershipManifestEntryDto
        {
            public int id;
            public string symbol;
            public int status;
        }

        [Serializable]
        private sealed class OwnershipArtifactDto
        {
            public string kind;
            public string moduleId;
            public string symbol;
            public int key;
            public string guid;
            public string path;
        }

        private sealed class OwnershipFingerprintRecord
        {
            internal OwnershipFingerprintRecord(
                ushort key,
                string moduleId,
                string symbol,
                string assetPath)
            {
                Key = key;
                ModuleId = moduleId ?? string.Empty;
                Symbol = symbol ?? string.Empty;
                AssetPath = assetPath ?? string.Empty;
            }

            internal ushort Key { get; }
            internal string ModuleId { get; }
            internal string Symbol { get; }
            internal string AssetPath { get; }
        }

        private sealed class OwnershipGuidReplacement
        {
            internal OwnershipGuidReplacement(int index, string guid)
            {
                Index = index;
                Guid = guid;
            }

            internal int Index { get; }
            internal string Guid { get; }
        }

        private sealed class OutputFingerprintWriter
        {
            private readonly MemoryStream _stream = new MemoryStream();

            internal void WriteByte(byte value)
            {
                WriteBytes(new[] { value });
            }

            internal void WriteUInt16(ushort value)
            {
                WriteBytes(
                    new[]
                    {
                        (byte)value,
                        (byte)(value >> 8)
                    });
            }

            internal void WriteInt64(long value)
            {
                WriteBytes(
                    new[]
                    {
                        (byte)value,
                        (byte)(value >> 8),
                        (byte)(value >> 16),
                        (byte)(value >> 24),
                        (byte)(value >> 32),
                        (byte)(value >> 40),
                        (byte)(value >> 48),
                        (byte)(value >> 56)
                    });
            }

            internal void WriteSingle(float value)
            {
                var normalized = value == 0f ? 0f : value;
                var bytes = BitConverter.GetBytes(normalized);
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                WriteBytes(bytes);
            }

            internal void WriteInt32(int value)
            {
                WriteBytes(
                    new[]
                    {
                        (byte)value,
                        (byte)(value >> 8),
                        (byte)(value >> 16),
                        (byte)(value >> 24)
                    });
            }

            internal void WriteString(string value)
            {
                WriteBytes(Encoding.UTF8.GetBytes(value ?? string.Empty));
            }

            internal string Finish()
            {
                using (var sha256 = SHA256.Create())
                {
                    var digest = sha256.ComputeHash(_stream.ToArray());
                    var result = new StringBuilder(digest.Length * 2);
                    for (var index = 0; index < digest.Length; index++)
                    {
                        result.Append(
                            digest[index].ToString(
                                "x2",
                                CultureInfo.InvariantCulture));
                    }

                    return result.ToString();
                }
            }

            private void WriteBytes(byte[] value)
            {
                var length = value.Length;
                _stream.WriteByte((byte)length);
                _stream.WriteByte((byte)(length >> 8));
                _stream.WriteByte((byte)(length >> 16));
                _stream.WriteByte((byte)(length >> 24));
                _stream.Write(value, 0, value.Length);
            }
        }

        [Serializable]
        private sealed class Snapshot
        {
            public string path;
            public string kind;
            public bool existed;
            public string bytesBase64;
            public string sha256;
            public string guid;
            public string mainType;
        }

        private readonly struct JournalValue
        {
            internal JournalValue(
                string path,
                string id,
                OrpheusAudioAuthoringTransactionPhase phase,
                string payload)
            {
                Path = path;
                Id = id;
                Phase = phase;
                Payload = payload;
            }

            internal string Path { get; }
            internal string Id { get; }
            internal OrpheusAudioAuthoringTransactionPhase Phase { get; }
            internal string Payload { get; }
        }
    }
}
