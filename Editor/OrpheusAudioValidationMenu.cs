using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor
{
    internal static class OrpheusAudioValidationMenu
    {
        // Reports only: Debug.LogError does not affect process exit code.
        // The build gate that fails the build is OrpheusAudioBuildPreprocessor.ThrowIfErrors.
        [MenuItem("Tools/Orpheus/Validate Audio Profiles")]
        private static void ValidateProfiles()
        {
            var errors = OrpheusAudioValidationProfileDiscovery.ValidateEnabled(
                EditorUserBuildSettings.activeBuildTarget);
            if (errors.Count == 0)
            {
                Debug.Log("Orpheus audio validation passed.");
                return;
            }

            for (var index = 0; index < errors.Count; index++)
            {
                Debug.LogError(errors[index].ToString());
            }
        }
    }
}
