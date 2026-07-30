using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio.Editor
{
    internal sealed class OrpheusAudioMixerValidationModel
    {
        internal AudioMixer Mixer;
        internal AudioMixerGroup MasterGroup;
        internal int ActualGroupCount;
        internal OrpheusAudioMixerGroupValidationRecord[] Groups;
        internal OrpheusAudioExposedParameterValidationRecord[] ExposedParameters;
        internal int ActualSnapshotCount;
        internal OrpheusAudioSnapshotValidationRecord[] Snapshots;
    }

    internal sealed class OrpheusAudioMixerGroupValidationRecord
    {
        internal AudioMixerGroup Asset;
        internal AudioMixerGroup Parent;
        internal int ParentReferenceCount;
        internal AudioMixerGroup[] Children;
        internal string VolumeParameter;
    }

    internal struct OrpheusAudioExposedParameterValidationRecord
    {
        internal string Name;
        internal string Parameter;
    }

    internal sealed class OrpheusAudioSnapshotValidationRecord
    {
        internal AudioMixerSnapshot Asset;
        internal string[] Parameters;
        internal float[] Values;
    }

    internal static class OrpheusAudioIntegrationValidation
    {
        private const float MinimumAttenuationDb = -80f;
        private const float MaximumAttenuationDb = 0f;
        private const uint ContractCountMismatch = 1u;
        private const uint ContractMissing = 2u;
        private const uint ContractDuplicate = 4u;
        private const uint ContractIdentityMismatch = 8u;
        private const uint ContractTopologyMismatch = 16u;

        private static readonly string[] GroupNames =
        {
            "Master",
            "Music_User",
            "Music_State",
            "SFX_Combat_User",
            "SFX_Combat_State",
            "SFX_World_User",
            "SFX_World_State",
            "SFX_UI_User",
            "SFX_UI_State",
            "Ambience_User",
            "Ambience_State"
        };

        private static readonly int[] GroupParentIndices =
        {
            -1, 0, 1, 0, 3, 0, 5, 0, 7, 0, 9
        };

        private static readonly int[][] GroupChildIndices =
        {
            new[] { 1, 3, 5, 7, 9 },
            new[] { 2 },
            Array.Empty<int>(),
            new[] { 4 },
            Array.Empty<int>(),
            new[] { 6 },
            Array.Empty<int>(),
            new[] { 8 },
            Array.Empty<int>(),
            new[] { 10 },
            Array.Empty<int>()
        };

        private static readonly string[] SettingsGroupProperties =
        {
            "_master",
            "_musicUser",
            "_musicState",
            "_sfxCombatUser",
            "_sfxCombatState",
            "_sfxWorldUser",
            "_sfxWorldState",
            "_sfxUiUser",
            "_sfxUiState",
            "_ambienceUser",
            "_ambienceState"
        };

        private static readonly string[] ExposedParameterNames =
        {
            "MasterVolume",
            "MusicVolume",
            "SfxCombatVolume",
            "SfxWorldVolume",
            "SfxUiVolume",
            "AmbienceVolume"
        };

        private static readonly int[] ExposedGroupIndices = { 0, 1, 3, 5, 7, 9 };
        private static readonly int[] StateGroupIndices = { 2, 4, 6, 8, 10 };

        private static readonly string[] SnapshotNames = { "Peace", "Combat", "Menu", "Pause" };
        private static readonly string[] SettingsSnapshotProperties =
        {
            "_peace", "_combat", "_menu", "_pause"
        };

        private static readonly string[] BankRoleProperties =
        {
            "_oneShot3D", "_oneShot2D", "_bgm", "_profileAmbience", "_globalLoop"
        };

        private static readonly int[] BankRoleCounts = { 12, 4, 2, 2, 4 };
        private static readonly string[] BankRolePrefixes =
        {
            "OneShot3D_", "OneShot2D_", "Bgm_", "ProfileAmbience_", "GlobalLoop_"
        };

        internal static void ValidateSettingsAndMixer(
            OrpheusAudioSettings settings,
            string profilePath,
            List<OrpheusAudioValidationError> errors)
        {
            if (settings == null)
            {
                return;
            }

            var settingsPath = AssetDatabase.GetAssetPath(settings);
            if (!OrpheusAudioDurationPolicy.IsValid(settings.BgmCrossfadeSeconds))
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidBgmCrossfadeDuration,
                    profilePath, settingsPath);
            }

            if (!OrpheusAudioDurationPolicy.IsValid(settings.ProfileAmbienceCrossfadeSeconds))
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidProfileAmbienceCrossfadeDuration,
                    profilePath, settingsPath);
            }

            if (!OrpheusAudioDurationPolicy.IsValid(settings.SnapshotTransitionSeconds))
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidSnapshotTransitionDuration,
                    profilePath, settingsPath);
            }

            var mixer = settings.Mixer;
            if (mixer == null)
            {
                Add(errors, OrpheusAudioValidationErrorCode.MissingMixer,
                    profilePath, settingsPath);
            }

            var serializedSettings = new SerializedObject(settings);
            var directGroups = new AudioMixerGroup[SettingsGroupProperties.Length];
            for (var groupIndex = 0; groupIndex < SettingsGroupProperties.Length; groupIndex++)
            {
                var property = serializedSettings.FindProperty(SettingsGroupProperties[groupIndex]);
                var group = property == null ? null : property.objectReferenceValue as AudioMixerGroup;
                directGroups[groupIndex] = group;
                if (group == null)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.MissingMixerGroup,
                        profilePath, settingsPath, groupIndex);
                }
                else if (mixer == null || !ReferenceEquals(group.audioMixer, mixer))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.MismatchedMixerReference,
                        profilePath, AssetDatabase.GetAssetPath(group), groupIndex);
                }
            }

            var directSnapshots = new AudioMixerSnapshot[SettingsSnapshotProperties.Length];
            for (var snapshotIndex = 0; snapshotIndex < SettingsSnapshotProperties.Length; snapshotIndex++)
            {
                var property = serializedSettings.FindProperty(SettingsSnapshotProperties[snapshotIndex]);
                var snapshot = property == null ? null : property.objectReferenceValue as AudioMixerSnapshot;
                directSnapshots[snapshotIndex] = snapshot;
                if (snapshot == null)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.MissingMixerSnapshot,
                        profilePath, settingsPath, snapshotIndex);
                }
                else if (mixer == null || !ReferenceEquals(snapshot.audioMixer, mixer))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.MismatchedMixerReference,
                        profilePath, AssetDatabase.GetAssetPath(snapshot),
                        SettingsGroupProperties.Length + snapshotIndex);
                }
            }

            if (mixer == null)
            {
                return;
            }

            var mixerPath = AssetDatabase.GetAssetPath(mixer);
            if (mixer.updateMode != AudioMixerUpdateMode.UnscaledTime)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerUpdateMode,
                    profilePath, mixerPath);
            }

            OrpheusAudioMixerValidationModel model;
            if (!TryCaptureMixerModel(mixer, out model))
            {
                Add(errors, OrpheusAudioValidationErrorCode.UnsupportedMixerSerialization,
                    profilePath, mixerPath);
                return;
            }

            ValidateMixerModel(model, directGroups, directSnapshots,
                profilePath, mixerPath, errors);
        }

        internal static bool TryCaptureMixerModel(
            AudioMixer mixer,
            out OrpheusAudioMixerValidationModel model)
        {
            model = null;
            if (mixer == null)
            {
                return false;
            }

            try
            {
                var mixerPath = AssetDatabase.GetAssetPath(mixer);
                if (string.IsNullOrEmpty(mixerPath))
                {
                    return false;
                }

                var assets = AssetDatabase.LoadAllAssetsAtPath(mixerPath);
                var allGroups = new List<AudioMixerGroup>();
                for (var assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                {
                    var group = assets[assetIndex] as AudioMixerGroup;
                    if (group != null)
                    {
                        allGroups.Add(group);
                    }
                }

                var serializedMixer = new SerializedObject(mixer);
                var masterProperty = serializedMixer.FindProperty("m_MasterGroup");
                var snapshotsProperty = serializedMixer.FindProperty("m_Snapshots");
                var exposedProperty = serializedMixer.FindProperty("m_ExposedParameters");
                if (masterProperty == null || snapshotsProperty == null || !snapshotsProperty.isArray ||
                    exposedProperty == null || !exposedProperty.isArray)
                {
                    return false;
                }

                var captured = new OrpheusAudioMixerValidationModel
                {
                    Mixer = mixer,
                    MasterGroup = masterProperty.objectReferenceValue as AudioMixerGroup,
                    ActualGroupCount = allGroups.Count,
                    Groups = new OrpheusAudioMixerGroupValidationRecord[GroupNames.Length],
                    ActualSnapshotCount = snapshotsProperty.arraySize,
                    Snapshots = new OrpheusAudioSnapshotValidationRecord[SnapshotNames.Length]
                };

                var groupRecords = new Dictionary<AudioMixerGroup, OrpheusAudioMixerGroupValidationRecord>();
                for (var groupIndex = 0; groupIndex < allGroups.Count; groupIndex++)
                {
                    var group = allGroups[groupIndex];
                    var serializedGroup = new SerializedObject(group);
                    var children = serializedGroup.FindProperty("m_Children");
                    var volume = serializedGroup.FindProperty("m_Volume");
                    if (children == null || !children.isArray || volume == null)
                    {
                        return false;
                    }

                    string volumeParameter;
                    if (!TryReadIdentifier(volume, out volumeParameter))
                    {
                        return false;
                    }

                    var record = new OrpheusAudioMixerGroupValidationRecord
                    {
                        Asset = group,
                        Children = new AudioMixerGroup[children.arraySize],
                        VolumeParameter = volumeParameter
                    };
                    for (var childIndex = 0; childIndex < children.arraySize; childIndex++)
                    {
                        record.Children[childIndex] = children.GetArrayElementAtIndex(childIndex)
                            .objectReferenceValue as AudioMixerGroup;
                    }

                    groupRecords.Add(group, record);
                }

                foreach (var pair in groupRecords)
                {
                    var children = pair.Value.Children;
                    for (var childIndex = 0; childIndex < children.Length; childIndex++)
                    {
                        var child = children[childIndex];
                        OrpheusAudioMixerGroupValidationRecord childRecord;
                        if (child != null && groupRecords.TryGetValue(child, out childRecord))
                        {
                            childRecord.ParentReferenceCount++;
                            if (childRecord.Parent == null)
                            {
                                childRecord.Parent = pair.Key;
                            }
                        }
                    }
                }

                for (var contractIndex = 0; contractIndex < GroupNames.Length; contractIndex++)
                {
                    OrpheusAudioMixerGroupValidationRecord match = null;
                    for (var actualIndex = 0; actualIndex < allGroups.Count; actualIndex++)
                    {
                        if (string.Equals(allGroups[actualIndex].name,
                                GroupNames[contractIndex], StringComparison.Ordinal))
                        {
                            if (match != null)
                            {
                                match = null;
                                break;
                            }

                            match = groupRecords[allGroups[actualIndex]];
                        }
                    }

                    captured.Groups[contractIndex] = match;
                }

                captured.ExposedParameters = new OrpheusAudioExposedParameterValidationRecord[
                    exposedProperty.arraySize];
                for (var parameterIndex = 0; parameterIndex < exposedProperty.arraySize; parameterIndex++)
                {
                    var element = exposedProperty.GetArrayElementAtIndex(parameterIndex);
                    var name = element.FindPropertyRelative("name");
                    var parameter = element.FindPropertyRelative("guid");
                    if (name == null || parameter == null)
                    {
                        return false;
                    }

                    string parameterId;
                    if (!TryReadIdentifier(parameter, out parameterId))
                    {
                        return false;
                    }

                    captured.ExposedParameters[parameterIndex] =
                        new OrpheusAudioExposedParameterValidationRecord
                        {
                            Name = name.stringValue,
                            Parameter = parameterId
                        };
                }

                for (var contractIndex = 0; contractIndex < SnapshotNames.Length; contractIndex++)
                {
                    AudioMixerSnapshot match = null;
                    for (var actualIndex = 0; actualIndex < snapshotsProperty.arraySize; actualIndex++)
                    {
                        var listedSnapshot = snapshotsProperty.GetArrayElementAtIndex(actualIndex)
                            .objectReferenceValue as AudioMixerSnapshot;
                        if (listedSnapshot != null && string.Equals(listedSnapshot.name,
                                SnapshotNames[contractIndex], StringComparison.Ordinal))
                        {
                            if (match != null)
                            {
                                match = null;
                                break;
                            }

                            match = listedSnapshot;
                        }
                    }

                    if (match == null)
                    {
                        continue;
                    }

                    var serializedSnapshot = new SerializedObject(match);
                    var valuesProperty = serializedSnapshot.FindProperty("m_FloatValues");
                    string[] parameters;
                    float[] values;
                    if (valuesProperty == null ||
                        !TryReadHashFloatMap(valuesProperty, out parameters, out values))
                    {
                        return false;
                    }

                    captured.Snapshots[contractIndex] = new OrpheusAudioSnapshotValidationRecord
                    {
                        Asset = match,
                        Parameters = parameters,
                        Values = values
                    };
                }

                model = captured;
                return true;
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                model = null;
                return false;
            }
        }

        internal static void ValidateMixerModel(
            OrpheusAudioMixerValidationModel model,
            AudioMixerGroup[] directGroups,
            AudioMixerSnapshot[] directSnapshots,
            string profilePath,
            string mixerPath,
            List<OrpheusAudioValidationError> errors)
        {
            if (model.ActualGroupCount != GroupNames.Length)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerGroupContract,
                    profilePath, mixerPath, -1, ContractCountMismatch);
            }

            for (var groupIndex = 0; groupIndex < GroupNames.Length; groupIndex++)
            {
                var group = model.Groups[groupIndex];
                if (group == null || group.Asset == null)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerGroupContract,
                        profilePath, mixerPath, groupIndex, ContractMissing);
                    continue;
                }

                if (groupIndex == 0)
                {
                    if (!ReferenceEquals(model.MasterGroup, group.Asset) ||
                        group.Parent != null || group.ParentReferenceCount != 0)
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerGroupContract,
                            profilePath, mixerPath, groupIndex, ContractTopologyMismatch);
                    }
                }
                else
                {
                    var parentIndex = GroupParentIndices[groupIndex];
                    var expectedParent = model.Groups[parentIndex];
                    if (expectedParent == null ||
                        !ReferenceEquals(group.Parent, expectedParent.Asset) ||
                        group.ParentReferenceCount != 1)
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerGroupContract,
                            profilePath, mixerPath, groupIndex, ContractTopologyMismatch);
                    }
                }

                if (!HasExactChildren(model, group, groupIndex))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerGroupContract,
                        profilePath, mixerPath, groupIndex, ContractTopologyMismatch);
                }

                if (directGroups == null || groupIndex >= directGroups.Length ||
                    !ReferenceEquals(directGroups[groupIndex], group.Asset))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.MismatchedMixerReference,
                        profilePath, mixerPath, groupIndex, ContractIdentityMismatch);
                }
            }

            ValidateExposedParameters(model, profilePath, mixerPath, errors);

            if (model.ActualSnapshotCount != SnapshotNames.Length)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerSnapshotContract,
                    profilePath, mixerPath, -1, ContractCountMismatch);
            }

            for (var snapshotIndex = 0; snapshotIndex < SnapshotNames.Length; snapshotIndex++)
            {
                var snapshot = model.Snapshots[snapshotIndex];
                if (snapshot == null || snapshot.Asset == null ||
                    !ReferenceEquals(snapshot.Asset.audioMixer, model.Mixer))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerSnapshotContract,
                        profilePath, mixerPath, snapshotIndex, ContractMissing);
                    continue;
                }

                if (directSnapshots == null || snapshotIndex >= directSnapshots.Length ||
                    !ReferenceEquals(directSnapshots[snapshotIndex], snapshot.Asset))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.MismatchedMixerReference,
                        profilePath, mixerPath, SettingsGroupProperties.Length + snapshotIndex,
                        ContractIdentityMismatch);
                }

                ValidateSnapshot(model, snapshot, snapshotIndex,
                    profilePath, mixerPath, errors);
            }
        }

        private static bool HasExactChildren(
            OrpheusAudioMixerValidationModel model,
            OrpheusAudioMixerGroupValidationRecord group,
            int groupIndex)
        {
            var actual = group.Children ?? Array.Empty<AudioMixerGroup>();
            var expectedIndices = GroupChildIndices[groupIndex];
            if (actual.Length != expectedIndices.Length)
            {
                return false;
            }

            for (var expectedIndex = 0; expectedIndex < expectedIndices.Length; expectedIndex++)
            {
                var expectedGroup = model.Groups[expectedIndices[expectedIndex]];
                if (expectedGroup == null || expectedGroup.Asset == null)
                {
                    return false;
                }

                var count = 0;
                for (var actualIndex = 0; actualIndex < actual.Length; actualIndex++)
                {
                    if (ReferenceEquals(actual[actualIndex], expectedGroup.Asset))
                    {
                        count++;
                    }
                }

                if (count != 1)
                {
                    return false;
                }
            }

            return true;
        }

        internal static void ValidateRuntimeHostAndSourceBank(
            GameObject runtimeHostPrefab,
            string profilePath,
            List<OrpheusAudioValidationError> errors)
        {
            if (runtimeHostPrefab == null)
            {
                Add(errors, OrpheusAudioValidationErrorCode.MissingRuntimeHostPrefab,
                    profilePath, profilePath);
                return;
            }

            var prefabPath = AssetDatabase.GetAssetPath(runtimeHostPrefab);
            if (string.IsNullOrEmpty(prefabPath) ||
                PrefabUtility.GetPrefabAssetType(runtimeHostPrefab) == PrefabAssetType.NotAPrefab ||
                !PrefabUtility.IsPartOfPrefabAsset(runtimeHostPrefab))
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidRuntimeHostPrefab,
                    profilePath, prefabPath);
                return;
            }

            var hosts = runtimeHostPrefab.GetComponentsInChildren<OrpheusAudioRuntimeHost>(true);
            if (hosts.Length != 1)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidRuntimeHostCount,
                    profilePath, prefabPath, -1, (uint)hosts.Length);
            }

            var banks = runtimeHostPrefab.GetComponentsInChildren<OrpheusAudioSourceBank>(true);
            if (banks.Length == 0)
            {
                Add(errors, OrpheusAudioValidationErrorCode.MissingSourceBank,
                    profilePath, prefabPath);
                return;
            }

            if (banks.Length != 1)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidSourceBankCount,
                    profilePath, prefabPath, -1, (uint)banks.Length);
            }

            if (hosts.Length == 1 && hosts[0].SourceBank == null)
            {
                Add(errors, OrpheusAudioValidationErrorCode.MissingSourceBank,
                    profilePath, prefabPath);
            }
            else if (hosts.Length == 1 && !ReferenceEquals(hosts[0].SourceBank, banks[0]))
            {
                Add(errors, OrpheusAudioValidationErrorCode.MismatchedSourceBankReference,
                    profilePath, prefabPath);
            }

            ValidateSourceBank(banks[0], profilePath, prefabPath, errors);
        }

        internal static void ValidateListenerScenes(
            OrpheusAudioValidationProfile profile,
            string profilePath,
            List<OrpheusAudioValidationError> errors)
        {
            var serializedProfile = new SerializedObject(profile);
            var scenes = serializedProfile.FindProperty("_listenerScenes");
            if (scenes == null || !scenes.isArray)
            {
                Add(errors, OrpheusAudioValidationErrorCode.UnresolvedListenerScene,
                    profilePath, profilePath);
                return;
            }

            for (var sceneIndex = 0; sceneIndex < scenes.arraySize; sceneIndex++)
            {
                var element = scenes.GetArrayElementAtIndex(sceneIndex);
                var asset = element.objectReferenceValue;
                if (asset == null)
                {
                    var instanceId = element.objectReferenceInstanceIDValue;
                    if (instanceId == 0)
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.NullListenerScene,
                            profilePath, profilePath, sceneIndex);
                        continue;
                    }

                    asset = EditorUtility.InstanceIDToObject(instanceId);
                    if (asset == null)
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.UnresolvedListenerScene,
                            profilePath, profilePath, sceneIndex);
                        continue;
                    }

                    if (!(asset is SceneAsset))
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.InvalidListenerSceneType,
                            profilePath, AssetDatabase.GetAssetPath(asset), sceneIndex);
                        continue;
                    }
                }

                string guid;
                long localId;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out guid, out localId))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.UnresolvedListenerScene,
                        profilePath, AssetDatabase.GetAssetPath(asset), sceneIndex);
                    continue;
                }

                var scenePath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(scenePath))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.UnresolvedListenerScene,
                        profilePath, string.Empty, sceneIndex);
                    continue;
                }

                var sceneError = EvaluateListenerSceneAsset(asset);
                if (sceneError != OrpheusAudioValidationErrorCode.None)
                {
                    Add(errors, sceneError,
                        profilePath, scenePath, sceneIndex);
                    continue;
                }

                var loaded = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                string loadedGuid;
                long loadedLocalId;
                if (loaded == null ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        loaded, out loadedGuid, out loadedLocalId) ||
                    !string.Equals(guid, loadedGuid, StringComparison.Ordinal) ||
                    localId != loadedLocalId)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.UnloadableListenerScene,
                        profilePath, scenePath, sceneIndex);
                    continue;
                }

                // Spec assigns Listener scene loadability to PlayMode/sample gates.
                // Editor validation only proves a non-empty, readable Scene asset file.
                if (!CanReadSceneFile(scenePath))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.UnloadableListenerScene,
                        profilePath, scenePath, sceneIndex);
                }
            }
        }

        internal static OrpheusAudioValidationErrorCode EvaluateListenerSceneAsset(
            UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return OrpheusAudioValidationErrorCode.NullListenerScene;
            }

            if (!(asset is SceneAsset))
            {
                return OrpheusAudioValidationErrorCode.InvalidListenerSceneType;
            }

            return OrpheusAudioValidationErrorCode.None;
        }

        private static bool CanReadSceneFile(string scenePath)
        {
            try
            {
                using (var stream = new FileStream(
                           scenePath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                {
                    return stream.Length > 0;
                }
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                return false;
            }
        }

        private static void ValidateExposedParameters(
            OrpheusAudioMixerValidationModel model,
            string profilePath,
            string mixerPath,
            List<OrpheusAudioValidationError> errors)
        {
            var exposed = model.ExposedParameters ??
                          new OrpheusAudioExposedParameterValidationRecord[0];
            if (exposed.Length != ExposedParameterNames.Length)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerExposedParameterContract,
                    profilePath, mixerPath, -1, ContractCountMismatch);
            }

            for (var contractIndex = 0; contractIndex < ExposedParameterNames.Length; contractIndex++)
            {
                var matchCount = 0;
                string parameter = null;
                for (var actualIndex = 0; actualIndex < exposed.Length; actualIndex++)
                {
                    if (string.Equals(exposed[actualIndex].Name,
                            ExposedParameterNames[contractIndex], StringComparison.Ordinal))
                    {
                        matchCount++;
                        parameter = exposed[actualIndex].Parameter;
                    }
                }

                if (matchCount == 0)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerExposedParameterContract,
                        profilePath, mixerPath, contractIndex, ContractMissing);
                    continue;
                }

                if (matchCount != 1)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerExposedParameterContract,
                        profilePath, mixerPath, contractIndex, ContractDuplicate);
                    continue;
                }

                var group = model.Groups[ExposedGroupIndices[contractIndex]];
                if (group == null || !string.Equals(
                        parameter, group.VolumeParameter, StringComparison.Ordinal))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerExposedParameterContract,
                        profilePath, mixerPath, contractIndex, ContractIdentityMismatch);
                }
            }
        }

        private static void ValidateSnapshot(
            OrpheusAudioMixerValidationModel model,
            OrpheusAudioSnapshotValidationRecord snapshot,
            int snapshotIndex,
            string profilePath,
            string mixerPath,
            List<OrpheusAudioValidationError> errors)
        {
            var parameters = snapshot.Parameters ?? new string[0];
            var values = snapshot.Values ?? new float[0];
            if (parameters.Length != StateGroupIndices.Length || values.Length != parameters.Length)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerSnapshotOverride,
                    profilePath, mixerPath, snapshotIndex, ContractCountMismatch);
            }

            for (var stateIndex = 0; stateIndex < StateGroupIndices.Length; stateIndex++)
            {
                var relatedIndex = snapshotIndex * StateGroupIndices.Length + stateIndex;
                var group = model.Groups[StateGroupIndices[stateIndex]];
                if (group == null)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerSnapshotOverride,
                        profilePath, mixerPath, relatedIndex, ContractMissing);
                    continue;
                }

                var matchCount = 0;
                var value = 0f;
                for (var valueIndex = 0;
                     valueIndex < parameters.Length && valueIndex < values.Length;
                     valueIndex++)
                {
                    if (string.Equals(
                            parameters[valueIndex], group.VolumeParameter,
                            StringComparison.Ordinal))
                    {
                        matchCount++;
                        value = values[valueIndex];
                    }
                }

                if (matchCount == 0)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerSnapshotOverride,
                        profilePath, mixerPath, relatedIndex, ContractMissing);
                    continue;
                }

                if (matchCount != 1)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerSnapshotOverride,
                        profilePath, mixerPath, relatedIndex, ContractDuplicate);
                    continue;
                }

                if (float.IsNaN(value) || float.IsInfinity(value) ||
                    value < MinimumAttenuationDb || value > MaximumAttenuationDb)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidMixerAttenuation,
                        profilePath, mixerPath, relatedIndex);
                    continue;
                }

                if ((snapshotIndex == 2 || snapshotIndex == 3) && stateIndex == 3 &&
                    value <= MinimumAttenuationDb)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InaudibleStateUi,
                        profilePath, mixerPath, relatedIndex);
                }
            }
        }

        private static void ValidateSourceBank(
            OrpheusAudioSourceBank bank,
            string profilePath,
            string prefabPath,
            List<OrpheusAudioValidationError> errors)
        {
            var serializedBank = new SerializedObject(bank);
            var expectedSources = new List<AudioSource>(OrpheusAudioSourceBank.TotalSourceCount);
            var flattenedIndex = 0;
            for (var roleIndex = 0; roleIndex < BankRoleProperties.Length; roleIndex++)
            {
                var role = serializedBank.FindProperty(BankRoleProperties[roleIndex]);
                if (role == null || !role.isArray || role.arraySize != BankRoleCounts[roleIndex])
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidSourceBankRoleCount,
                        profilePath, prefabPath, roleIndex,
                        role == null || !role.isArray ? uint.MaxValue : (uint)role.arraySize);
                }

                for (var localIndex = 0; localIndex < BankRoleCounts[roleIndex]; localIndex++)
                {
                    AudioSource source = null;
                    if (role != null && role.isArray && localIndex < role.arraySize)
                    {
                        source = role.GetArrayElementAtIndex(localIndex).objectReferenceValue
                            as AudioSource;
                    }

                    expectedSources.Add(source);
                    if (source == null)
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.MissingSourceBankLeaf,
                            profilePath, prefabPath, flattenedIndex);
                        flattenedIndex++;
                        continue;
                    }

                    var expectedName = BankRolePrefixes[roleIndex] + localIndex.ToString("00");
                    if (!string.Equals(source.name, expectedName, StringComparison.Ordinal))
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.InvalidSourceBankLeafName,
                            profilePath, prefabPath, flattenedIndex);
                    }

                    for (var previousIndex = 0; previousIndex < flattenedIndex; previousIndex++)
                    {
                        if (ReferenceEquals(expectedSources[previousIndex], source))
                        {
                            Add(errors, OrpheusAudioValidationErrorCode.DuplicateSourceBankLeaf,
                                profilePath, prefabPath, flattenedIndex, (uint)previousIndex);
                            break;
                        }
                    }

                    if (ReferenceEquals(source.transform, bank.transform) ||
                        !source.transform.IsChildOf(bank.transform))
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.SourceBankLeafOutsideBank,
                            profilePath, prefabPath, flattenedIndex);
                    }

                    var components = source.gameObject.GetComponents<AudioSource>();
                    if (components.Length != 1)
                    {
                        Add(errors,
                            OrpheusAudioValidationErrorCode.InvalidSourceBankLeafAudioSourceCount,
                            profilePath, prefabPath, flattenedIndex, (uint)components.Length);
                    }

                    if (source.playOnAwake)
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.SourceBankPlayOnAwake,
                            profilePath, prefabPath, flattenedIndex);
                    }

                    if (source.clip != null)
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.SourceBankPresetClip,
                            profilePath, prefabPath, flattenedIndex);
                    }

                    if (source.outputAudioMixerGroup != null)
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.SourceBankStaleRoute,
                            profilePath, prefabPath, flattenedIndex);
                    }

                    flattenedIndex++;
                }
            }

            var descendants = bank.GetComponentsInChildren<AudioSource>(true);
            for (var descendantIndex = 0; descendantIndex < descendants.Length; descendantIndex++)
            {
                var found = false;
                for (var expectedIndex = 0; expectedIndex < expectedSources.Count; expectedIndex++)
                {
                    if (ReferenceEquals(descendants[descendantIndex], expectedSources[expectedIndex]))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.UnexpectedSourceBankAudioSource,
                        profilePath, prefabPath,
                        OrpheusAudioSourceBank.TotalSourceCount + descendantIndex);
                }
            }
        }

        private static bool TryReadHashFloatMap(
            SerializedProperty property,
            out string[] parameters,
            out float[] values)
        {
            var parameterList = new List<string>();
            var valueList = new List<float>();
            if (property.isArray)
            {
                for (var index = 0; index < property.arraySize; index++)
                {
                    var element = property.GetArrayElementAtIndex(index);
                    var key = element.FindPropertyRelative("first");
                    var value = element.FindPropertyRelative("second");
                    if (key == null || value == null)
                    {
                        parameters = null;
                        values = null;
                        return false;
                    }

                    string identifier;
                    if (!TryReadIdentifier(key, out identifier))
                    {
                        parameters = null;
                        values = null;
                        return false;
                    }

                    parameterList.Add(identifier);
                    valueList.Add(value.floatValue);
                }
            }
            else
            {
                var iterator = property.Copy();
                var propertyDepth = property.depth;
                var enterChildren = true;
                var hasPendingKey = false;
                string pendingKey = null;
                while (iterator.Next(enterChildren))
                {
                    if (iterator.depth <= propertyDepth)
                    {
                        break;
                    }

                    enterChildren = true;
                    if (string.Equals(iterator.name, "first", StringComparison.Ordinal))
                    {
                        if (!TryReadIdentifier(iterator, out pendingKey))
                        {
                            parameters = null;
                            values = null;
                            return false;
                        }

                        hasPendingKey = true;
                    }
                    else if (string.Equals(iterator.name, "second", StringComparison.Ordinal) &&
                             hasPendingKey)
                    {
                        parameterList.Add(pendingKey);
                        valueList.Add(iterator.floatValue);
                        hasPendingKey = false;
                    }
                }

                if (hasPendingKey)
                {
                    parameters = null;
                    values = null;
                    return false;
                }
            }

            parameters = parameterList.ToArray();
            values = valueList.ToArray();
            return parameters.Length == values.Length;
        }

        private static bool TryReadIdentifier(
            SerializedProperty property,
            out string identifier)
        {
            identifier = null;
            if (property == null)
            {
                return false;
            }

            if (property.propertyType == SerializedPropertyType.Hash128)
            {
                identifier = property.hash128Value.ToString();
                return !string.IsNullOrEmpty(identifier);
            }

            if (property.propertyType == SerializedPropertyType.String)
            {
                identifier = property.stringValue;
                return !string.IsNullOrEmpty(identifier);
            }

            var builder = new StringBuilder(64);
            var iterator = property.Copy();
            var propertyDepth = property.depth;
            var enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                if (iterator.depth <= propertyDepth)
                {
                    break;
                }

                enterChildren = true;
                if (iterator.propertyType != SerializedPropertyType.Integer)
                {
                    continue;
                }

                builder.Append(unchecked((uint)iterator.longValue).ToString("X8"));
            }

            if (builder.Length == 0)
            {
                return false;
            }

            identifier = builder.ToString();
            return true;
        }

        private static void Add(
            List<OrpheusAudioValidationError> errors,
            OrpheusAudioValidationErrorCode code,
            string profilePath,
            string assetPath,
            int relatedIndex = -1,
            uint detail = 0)
        {
            errors.Add(new OrpheusAudioValidationError(
                code, profilePath, assetPath, -1, relatedIndex, detail));
        }
    }
}
