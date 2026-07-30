using System;
using UnityEditor;

namespace Orpheus.Audio.Editor
{
    [CustomEditor(typeof(OrpheusAudioValidationProfile))]
    internal sealed class OrpheusAudioValidationProfileEditor : UnityEditor.Editor
    {
        internal static readonly string[] VisibleProperties =
        {
            "_schemaVersion",
            "_enabled",
            "_settings",
            "_catalog",
            "_keyManifest",
            "_runtimeHostPrefab",
            "_listenerScenes",
            "_authoringProfile"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            for (var index = 0; index < VisibleProperties.Length; index++)
            {
                var property =
                    serializedObject.FindProperty(VisibleProperties[index]);
                if (property == null)
                {
                    continue;
                }

                if (string.Equals(
                        property.name,
                        "_authoringProfile",
                        StringComparison.Ordinal))
                {
                    using (new EditorGUI.DisabledScope(
                               !CanAssignAuthoringProfile(
                                   target as
                                       OrpheusAudioValidationProfile)))
                    {
                        EditorGUILayout.PropertyField(property, true);
                    }
                }
                else
                {
                    EditorGUILayout.PropertyField(property, true);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        internal static bool CanAssignAuthoringProfile(
            OrpheusAudioValidationProfile profile)
        {
            return profile != null &&
                   string.IsNullOrEmpty(
                       profile.AuthoringEnrollmentGuid);
        }
    }
}
