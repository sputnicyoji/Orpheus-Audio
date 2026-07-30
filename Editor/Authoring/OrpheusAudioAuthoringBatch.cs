using System;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor
{
    internal readonly struct OrpheusAudioAuthoringBatchRequest
    {
        internal OrpheusAudioAuthoringBatchRequest(
            string profileGuid,
            OrpheusAudioAuthoringCompileMode mode,
            BuildTarget buildTarget)
        {
            ProfileGuid = profileGuid ?? string.Empty;
            Mode = mode;
            BuildTarget = buildTarget;
        }

        internal string ProfileGuid { get; }
        internal OrpheusAudioAuthoringCompileMode Mode { get; }
        internal BuildTarget BuildTarget { get; }
    }

    internal static class OrpheusAudioAuthoringBatch
    {
        private const string ProfileArgument = "-orpheusProfileGuid";
        private const string ModeArgument = "-orpheusMode";
        private const string TargetArgument = "-orpheusBuildTarget";

        public static void Run()
        {
            var exitCode = 2;
            try
            {
                if (!TryParseArguments(
                        Environment.GetCommandLineArgs(),
                        out var request,
                        out var error))
                {
                    Debug.LogError(
                        "Orpheus authoring batch arguments rejected: " +
                        error);
                    EditorApplication.Exit(exitCode);
                    return;
                }

                var path =
                    AssetDatabase.GUIDToAssetPath(request.ProfileGuid);
                var profile =
                    AssetDatabase.LoadAssetAtPath<
                        OrpheusAudioValidationProfile>(path);
                if (profile == null ||
                    !string.Equals(
                        AssetDatabase.AssetPathToGUID(path)
                            .ToLowerInvariant(),
                        request.ProfileGuid,
                        StringComparison.Ordinal))
                {
                    Debug.LogError(
                        "Orpheus authoring batch profile is invalid.");
                    EditorApplication.Exit(exitCode);
                    return;
                }

                var targetGroup =
                    BuildPipeline.GetBuildTargetGroup(request.BuildTarget);
                var result = OrpheusAudioAuthoringCompiler.Run(
                    profile,
                    request.Mode,
                    targetGroup,
                    OrpheusAudioValidationProfileDiscovery.Discover());
                exitCode = GetExitCode(result.Status);
                var message =
                    "Orpheus authoring batch " + result.Status +
                    ". Report: " + result.ReportPath;
                if (exitCode == 0)
                {
                    Debug.Log(message);
                }
                else
                {
                    Debug.LogError(message);
                }
            }
            catch (Exception exception)
                when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                Debug.LogError(
                    "Orpheus authoring batch failed: " +
                    exception.GetType().Name);
                exitCode = 2;
            }

            EditorApplication.Exit(exitCode);
        }

        internal static bool TryParseArguments(
            string[] arguments,
            out OrpheusAudioAuthoringBatchRequest request,
            out string error)
        {
            request = default(OrpheusAudioAuthoringBatchRequest);
            error = string.Empty;
            var profileGuid = string.Empty;
            var modeText = string.Empty;
            var targetText = string.Empty;
            var hasProfile = false;
            var hasMode = false;
            var hasTarget = false;
            if (arguments == null)
            {
                error = "MissingArguments";
                return false;
            }

            for (var index = 0; index < arguments.Length; index++)
            {
                var argument = arguments[index] ?? string.Empty;
                if (!argument.StartsWith(
                        "-orpheus",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (index + 1 >= arguments.Length ||
                    string.IsNullOrEmpty(arguments[index + 1]) ||
                    arguments[index + 1][0] == '-')
                {
                    error = "MissingValue";
                    return false;
                }

                var value = arguments[++index];
                switch (argument)
                {
                    case ProfileArgument:
                        if (hasProfile)
                        {
                            error = "RepeatedProfile";
                            return false;
                        }

                        hasProfile = true;
                        profileGuid = value;
                        break;
                    case ModeArgument:
                        if (hasMode)
                        {
                            error = "RepeatedMode";
                            return false;
                        }

                        hasMode = true;
                        modeText = value;
                        break;
                    case TargetArgument:
                        if (hasTarget)
                        {
                            error = "RepeatedTarget";
                            return false;
                        }

                        hasTarget = true;
                        targetText = value;
                        break;
                    default:
                        error = "UnknownArgument";
                        return false;
                }
            }

            if (!hasProfile || !hasMode || !hasTarget ||
                !IsLowerGuid(profileGuid) ||
                !TryParseMode(modeText, out var mode) ||
                !TryParseTarget(targetText, out var target))
            {
                error = "InvalidArguments";
                return false;
            }

            request = new OrpheusAudioAuthoringBatchRequest(
                profileGuid,
                mode,
                target);
            return true;
        }

        internal static int GetExitCode(
            OrpheusAudioAuthoringCompileStatus status)
        {
            switch (status)
            {
                case OrpheusAudioAuthoringCompileStatus.SucceededChanged:
                case OrpheusAudioAuthoringCompileStatus.SucceededUnchanged:
                    return 0;
                case OrpheusAudioAuthoringCompileStatus.Rejected:
                    return 3;
                case OrpheusAudioAuthoringCompileStatus.RolledBack:
                    return 4;
                case OrpheusAudioAuthoringCompileStatus.RollbackFailed:
                    return 5;
                default:
                    return 2;
            }
        }

        private static bool TryParseMode(
            string value,
            out OrpheusAudioAuthoringCompileMode mode)
        {
            mode = OrpheusAudioAuthoringCompileMode.Invalid;
            return Enum.TryParse(value, false, out mode) &&
                   (mode == OrpheusAudioAuthoringCompileMode.Analyze ||
                    mode == OrpheusAudioAuthoringCompileMode.Compile) &&
                   string.Equals(
                       mode.ToString(),
                       value,
                       StringComparison.Ordinal);
        }

        private static bool TryParseTarget(
            string value,
            out BuildTarget target)
        {
            target = BuildTarget.NoTarget;
            if (!Enum.TryParse(value, false, out target) ||
                !string.Equals(
                    target.ToString(),
                    value,
                    StringComparison.Ordinal))
            {
                return false;
            }

            return target == BuildTarget.StandaloneWindows ||
                   target == BuildTarget.StandaloneWindows64 ||
                   target == BuildTarget.Android;
        }

        private static bool IsLowerGuid(string value)
        {
            if (value == null || value.Length != 32)
            {
                return false;
            }

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
