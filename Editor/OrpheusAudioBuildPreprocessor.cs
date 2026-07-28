using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Orpheus.Audio.Editor
{
    internal static class OrpheusAudioValidationProfileDiscovery
    {
        internal static OrpheusAudioValidationProfile[] Discover()
        {
            var guids = AssetDatabase.FindAssets("t:OrpheusAudioValidationProfile");
            var paths = new string[guids.Length];
            for (var index = 0; index < guids.Length; index++)
            {
                paths[index] = AssetDatabase.GUIDToAssetPath(guids[index]);
            }

            Array.Sort(paths, StringComparer.Ordinal);
            var profiles = new OrpheusAudioValidationProfile[paths.Length];
            for (var index = 0; index < paths.Length; index++)
            {
                profiles[index] = AssetDatabase.LoadAssetAtPath<OrpheusAudioValidationProfile>(paths[index]);
            }

            return profiles;
        }

        internal static List<OrpheusAudioValidationError> ValidateEnabled(BuildTarget target)
        {
            return ValidateEnabled(
                target,
                Discover(),
                OrpheusAudioTypedKeyProjection.ProductionPaths);
        }

        internal static List<OrpheusAudioValidationError> ValidateEnabled(
            BuildTarget target,
            OrpheusAudioValidationProfile[] profiles,
            OrpheusAudioTypedKeyProjectionPaths projectionPaths)
        {
            var errors = new List<OrpheusAudioValidationError>();
            var targetGroup = BuildPipeline.GetBuildTargetGroup(target);
            OrpheusAudioKeyManifest firstManifest = null;
            for (var index = 0; index < profiles.Length; index++)
            {
                var profile = profiles[index];
                if (profile == null || !profile.Enabled)
                {
                    continue;
                }

                errors.AddRange(OrpheusAudioValidator.ValidateProfile(
                    profile, targetGroup, projectionPaths));
                var manifest = profile.KeyManifest;
                if (manifest == null)
                {
                    continue;
                }

                if (firstManifest == null)
                {
                    firstManifest = manifest;
                }
                else if (!ReferenceEquals(firstManifest, manifest))
                {
                    errors.Add(new OrpheusAudioValidationError(
                        OrpheusAudioValidationErrorCode.MultipleKeyManifests,
                        AssetDatabase.GetAssetPath(profile),
                        AssetDatabase.GetAssetPath(manifest)));
                }
            }

            return errors;
        }

        internal static bool HasExactlyOneEnabledProfile()
        {
            return HasExactlyOneEnabledProfile(Discover());
        }

        internal static bool HasExactlyOneEnabledProfile(
            OrpheusAudioValidationProfile[] profiles)
        {
            var enabledCount = 0;
            for (var index = 0; index < profiles.Length; index++)
            {
                if (profiles[index] != null && profiles[index].Enabled)
                {
                    enabledCount++;
                }
            }

            return enabledCount == 1;
        }
    }

    internal sealed class OrpheusAudioBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            ValidateOrThrow(report.summary.platform);
        }

        internal static void ValidateOrThrow(BuildTarget target)
        {
            ThrowIfErrors(OrpheusAudioValidationProfileDiscovery.ValidateEnabled(target));
        }

        internal static void ValidateOrThrow(
            BuildTarget target,
            OrpheusAudioValidationProfile[] profiles,
            OrpheusAudioTypedKeyProjectionPaths projectionPaths)
        {
            ThrowIfErrors(OrpheusAudioValidationProfileDiscovery.ValidateEnabled(
                target, profiles, projectionPaths));
        }

        private static void ThrowIfErrors(List<OrpheusAudioValidationError> errors)
        {
            if (errors.Count == 0)
            {
                return;
            }

            var message = new StringBuilder(128 + errors.Count * 96);
            message.Append("Orpheus audio validation failed with ");
            message.Append(errors.Count);
            message.Append(" error(s).\n");
            for (var index = 0; index < errors.Count; index++)
            {
                message.Append(index + 1);
                message.Append(": ");
                message.Append(errors[index].ToString());
                message.Append('\n');
            }

            throw new BuildFailedException(message.ToString());
        }
    }
}
