using System;
using System.Text;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor
{
    internal static class OrpheusAudioAuthoringMenu
    {
        private const string AnalyzePath =
            "Tools/Orpheus/Analyze Enrolled Authoring";
        private const string AcceptPath =
            "Tools/Orpheus/Accept And Compile Enrollment";
        private const string CompilePath =
            "Tools/Orpheus/Compile Enrolled Authoring";
        private const string DeletePath =
            "Tools/Orpheus/Compile And Delete Tracked Orphans";

        [MenuItem(AnalyzePath)]
        private static void Analyze()
        {
            Execute(OrpheusAudioAuthoringCompileMode.Analyze);
        }

        [MenuItem(AnalyzePath, true)]
        private static bool CanAnalyze()
        {
            return CanExecuteSelected(
                OrpheusAudioAuthoringCompileMode.Analyze);
        }

        [MenuItem(AcceptPath)]
        private static void AcceptAndCompile()
        {
            var profile = Selection.activeObject as
                OrpheusAudioValidationProfile;
            if (profile == null ||
                !EditorUtility.DisplayDialog(
                    "Accept Orpheus Audio Enrollment",
                    BuildEnrollmentConfirmation(profile),
                    "Accept And Compile",
                    "Cancel"))
            {
                return;
            }

            Execute(
                OrpheusAudioAuthoringCompileMode
                    .CompileAndAcceptEnrollment);
        }

        [MenuItem(AcceptPath, true)]
        private static bool CanAcceptAndCompile()
        {
            return CanExecuteSelected(
                OrpheusAudioAuthoringCompileMode
                    .CompileAndAcceptEnrollment);
        }

        [MenuItem(CompilePath)]
        private static void Compile()
        {
            Execute(OrpheusAudioAuthoringCompileMode.Compile);
        }

        [MenuItem(CompilePath, true)]
        private static bool CanCompile()
        {
            return CanExecuteSelected(
                OrpheusAudioAuthoringCompileMode.Compile);
        }

        [MenuItem(DeletePath)]
        private static void CompileAndDeleteTrackedOrphans()
        {
            var profile = Selection.activeObject as
                OrpheusAudioValidationProfile;
            if (profile == null ||
                !TryGetActiveTargetGroup(out var targetGroup))
            {
                return;
            }

            var analysis = OrpheusAudioAuthoringCompiler.Run(
                profile,
                OrpheusAudioAuthoringCompileMode.Analyze,
                targetGroup,
                OrpheusAudioValidationProfileDiscovery.Discover());
            if (analysis.Status !=
                    OrpheusAudioAuthoringCompileStatus.SucceededChanged &&
                analysis.Status !=
                    OrpheusAudioAuthoringCompileStatus.SucceededUnchanged)
            {
                Report(analysis);
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Delete Tracked Orpheus Audio Orphans",
                    BuildOrphanDeletionConfirmation(
                        profile.AuthoringProfile.CaptureValue().GeneratedRoot,
                        analysis.OrphanCount),
                    "Compile And Delete",
                    "Cancel"))
            {
                return;
            }

            Execute(
                OrpheusAudioAuthoringCompileMode
                    .CompileAndDeleteTrackedOrphans);
        }

        [MenuItem(DeletePath, true)]
        private static bool CanCompileAndDeleteTrackedOrphans()
        {
            return CanExecuteSelected(
                OrpheusAudioAuthoringCompileMode
                    .CompileAndDeleteTrackedOrphans);
        }

        internal static bool CanExecute(
            OrpheusAudioValidationProfile profile,
            OrpheusAudioAuthoringCompileMode mode)
        {
            if (profile == null || !profile.Enabled ||
                string.IsNullOrEmpty(AssetDatabase.GetAssetPath(profile)) ||
                profile.AuthoringProfile == null)
            {
                return false;
            }

            var authoringPath =
                AssetDatabase.GetAssetPath(profile.AuthoringProfile);
            var authoringGuid =
                AssetDatabase.AssetPathToGUID(authoringPath)
                    .ToLowerInvariant();
            if (string.IsNullOrEmpty(authoringGuid))
            {
                return false;
            }

            var enrollmentGuid = profile.AuthoringEnrollmentGuid;
            var pending = string.IsNullOrEmpty(enrollmentGuid);
            var enrolled = string.Equals(
                enrollmentGuid,
                authoringGuid,
                StringComparison.Ordinal);
            switch (mode)
            {
                case OrpheusAudioAuthoringCompileMode.Analyze:
                    return pending || enrolled;
                case OrpheusAudioAuthoringCompileMode
                    .CompileAndAcceptEnrollment:
                    return pending;
                case OrpheusAudioAuthoringCompileMode.Compile:
                case OrpheusAudioAuthoringCompileMode
                    .CompileAndDeleteTrackedOrphans:
                    return enrolled;
                default:
                    return false;
            }
        }

        internal static string BuildEnrollmentConfirmation(
            OrpheusAudioValidationProfile profile)
        {
            if (profile == null || profile.AuthoringProfile == null)
            {
                return string.Empty;
            }

            var authoring = profile.AuthoringProfile;
            var captured = authoring.CaptureValue();
            var authoringPath = AssetDatabase.GetAssetPath(authoring);
            var builder = new StringBuilder(256);
            builder.Append("Validation Profile: ");
            builder.Append(AssetDatabase.GetAssetPath(profile));
            builder.Append("\nAuthoring Profile GUID: ");
            builder.Append(
                AssetDatabase.AssetPathToGUID(authoringPath)
                    .ToLowerInvariant());
            builder.Append("\nManifest Snapshot: ");
            AppendManifestSnapshot(builder, captured.KeyManifest);
            builder.Append("\nGenerated Root: ");
            builder.Append(captured.GeneratedRoot);
            return builder.ToString();
        }

        internal static string BuildOrphanDeletionConfirmation(
            string generatedRoot,
            int orphanCount)
        {
            return "Generated Root: " + (generatedRoot ?? string.Empty) +
                   "\nTracked Orphan Count: " + orphanCount +
                   "\nOnly compiler-owned Events that still satisfy every " +
                   "ownership condition will be deleted.";
        }

        private static bool CanExecuteSelected(
            OrpheusAudioAuthoringCompileMode mode)
        {
            return TryGetActiveTargetGroup(out _) &&
                   CanExecute(
                       Selection.activeObject as
                           OrpheusAudioValidationProfile,
                       mode);
        }

        private static void Execute(
            OrpheusAudioAuthoringCompileMode mode)
        {
            var profile = Selection.activeObject as
                OrpheusAudioValidationProfile;
            if (!CanExecute(profile, mode) ||
                !TryGetActiveTargetGroup(out var targetGroup))
            {
                return;
            }

            Report(
                OrpheusAudioAuthoringCompiler.Run(
                    profile,
                    mode,
                    targetGroup,
                    OrpheusAudioValidationProfileDiscovery.Discover()));
        }

        private static void Report(
            OrpheusAudioAuthoringCompileResult result)
        {
            var message =
                "Orpheus authoring " + result.Status +
                ". Report: " + result.ReportPath;
            if (result.Status ==
                    OrpheusAudioAuthoringCompileStatus.SucceededChanged ||
                result.Status ==
                    OrpheusAudioAuthoringCompileStatus.SucceededUnchanged)
            {
                Debug.Log(message);
            }
            else
            {
                Debug.LogError(message);
            }
        }

        private static bool TryGetActiveTargetGroup(
            out BuildTargetGroup targetGroup)
        {
            targetGroup = BuildPipeline.GetBuildTargetGroup(
                EditorUserBuildSettings.activeBuildTarget);
            return targetGroup == BuildTargetGroup.Standalone ||
                   targetGroup == BuildTargetGroup.Android;
        }

        private static void AppendManifestSnapshot(
            StringBuilder builder,
            OrpheusAudioKeyManifest manifest)
        {
            if (manifest == null)
            {
                return;
            }

            var entries =
                new OrpheusAudioKeyManifestEntryValue[manifest.EntryCount];
            for (var index = 0; index < entries.Length; index++)
            {
                entries[index] = manifest.GetEntry(index);
            }

            Array.Sort(
                entries,
                (left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < entries.Length; index++)
            {
                if (index != 0)
                {
                    builder.Append(", ");
                }

                builder.Append(entries[index].Id);
                builder.Append(':');
                builder.Append(entries[index].Symbol);
                builder.Append(':');
                builder.Append(entries[index].Status);
            }
        }
    }
}
