using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio.Editor.Tests
{
    internal static class OrpheusAudioEditorTestAssets
    {
        internal const string MixerPath =
            "Packages/com.orpheus.audio/Tests/Fixtures/Resources/OrpheusAudioTest.mixer";
        internal const string EmptyScenePath =
            "Packages/com.orpheus.audio/Tests/EditMode/Editor/Fixtures/EmptyListener.unity";

        internal static void ConfigureValidSettings(
            OrpheusAudioSettings settings,
            AudioMixer mixer = null,
            bool resolveFromSubAssets = false)
        {
            if (settings == null)
            {
                return;
            }

            mixer = mixer == null
                ? AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath)
                : mixer;
            Assert.That(mixer, Is.Not.Null);
            var assets = resolveFromSubAssets
                ? AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(mixer))
                : null;

            SetSerializedReference(settings, "_mixer", mixer);
            SetGroup(settings, mixer, assets, "_master", "Master");
            SetGroup(settings, mixer, assets, "_musicUser", "Music_User");
            SetGroup(settings, mixer, assets, "_musicState", "Music_State");
            SetGroup(settings, mixer, assets, "_sfxCombatUser", "SFX_Combat_User");
            SetGroup(settings, mixer, assets, "_sfxCombatState", "SFX_Combat_State");
            SetGroup(settings, mixer, assets, "_sfxWorldUser", "SFX_World_User");
            SetGroup(settings, mixer, assets, "_sfxWorldState", "SFX_World_State");
            SetGroup(settings, mixer, assets, "_sfxUiUser", "SFX_UI_User");
            SetGroup(settings, mixer, assets, "_sfxUiState", "SFX_UI_State");
            SetGroup(settings, mixer, assets, "_ambienceUser", "Ambience_User");
            SetGroup(settings, mixer, assets, "_ambienceState", "Ambience_State");
            SetSerializedReference(settings, "_peace", FindSnapshot(mixer, assets, "Peace"));
            SetSerializedReference(settings, "_combat", FindSnapshot(mixer, assets, "Combat"));
            SetSerializedReference(settings, "_menu", FindSnapshot(mixer, assets, "Menu"));
            SetSerializedReference(settings, "_pause", FindSnapshot(mixer, assets, "Pause"));
        }

        internal static AudioSource[] CreateSourceLeaves(Transform parent)
        {
            var sources = new AudioSource[OrpheusAudioSourceBank.TotalSourceCount];
            for (var index = 0; index < sources.Length; index++)
            {
                var leaf = new GameObject(ExpectedSourceName(index));
                leaf.transform.SetParent(parent, false);
                var source = leaf.AddComponent<AudioSource>();
                source.playOnAwake = false;
                sources[index] = source;
            }

            return sources;
        }

        internal static void SetBankSources(
            OrpheusAudioSourceBank bank,
            AudioSource[] sources,
            int oneShot3DCount = 12)
        {
            OrpheusAudioEditorContractTests.SetField(
                bank, "_oneShot3D", sources.Take(oneShot3DCount).ToArray());
            OrpheusAudioEditorContractTests.SetField(
                bank, "_oneShot2D", sources.Skip(12).Take(4).ToArray());
            OrpheusAudioEditorContractTests.SetField(
                bank, "_bgm", sources.Skip(16).Take(2).ToArray());
            OrpheusAudioEditorContractTests.SetField(
                bank, "_profileAmbience", sources.Skip(18).Take(2).ToArray());
            OrpheusAudioEditorContractTests.SetField(
                bank, "_globalLoop", sources.Skip(20).Take(4).ToArray());
        }

        internal static GameObject GetOrCreateValidRuntimeHostPrefab(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing;
            }

            var root = new GameObject("OrpheusRuntimeHost");
            try
            {
                var host = root.AddComponent<OrpheusAudioRuntimeHost>();
                var bank = root.AddComponent<OrpheusAudioSourceBank>();
                SetBankSources(bank, CreateSourceLeaves(root.transform));
                OrpheusAudioEditorContractTests.SetField(host, "_sourceBank", bank);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                Assert.That(prefab, Is.Not.Null);
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        internal static SceneAsset CopyEmptyScene(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (existing != null)
            {
                return existing;
            }

            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(EmptyScenePath), Is.Not.Null);
            Assert.That(AssetDatabase.CopyAsset(EmptyScenePath, path), Is.True);
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        }

        private static void SetGroup(
            OrpheusAudioSettings settings,
            AudioMixer mixer,
            UnityEngine.Object[] assets,
            string fieldName,
            string groupName)
        {
            var matches = assets == null
                ? mixer.FindMatchingGroups(string.Empty)
                    .Where(group => string.Equals(group.name, groupName, StringComparison.Ordinal))
                    .ToArray()
                : assets.OfType<AudioMixerGroup>()
                    .Where(group => string.Equals(group.name, groupName, StringComparison.Ordinal))
                    .ToArray();
            Assert.That(matches.Length, Is.EqualTo(1), groupName);
            SetSerializedReference(settings, fieldName, matches[0]);
        }

        private static AudioMixerSnapshot FindSnapshot(
            AudioMixer mixer,
            UnityEngine.Object[] assets,
            string name)
        {
            return assets == null
                ? mixer.FindSnapshot(name)
                : assets.OfType<AudioMixerSnapshot>().Single(
                    snapshot => string.Equals(snapshot.name, name, StringComparison.Ordinal));
        }

        private static void SetSerializedReference(
            UnityEngine.Object target,
            string fieldName,
            UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(fieldName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string ExpectedSourceName(int index)
        {
            if (index < 12)
            {
                return "OneShot3D_" + index.ToString("00");
            }

            if (index < 16)
            {
                return "OneShot2D_" + (index - 12).ToString("00");
            }

            if (index < 18)
            {
                return "Bgm_" + (index - 16).ToString("00");
            }

            if (index < 20)
            {
                return "ProfileAmbience_" + (index - 18).ToString("00");
            }

            return "GlobalLoop_" + (index - 20).ToString("00");
        }
    }
}
