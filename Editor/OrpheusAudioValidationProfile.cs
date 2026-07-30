using System;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor
{
    [CreateAssetMenu(fileName = "OrpheusAudioValidationProfile", menuName = "Orpheus/Audio Validation Profile")]
    public sealed class OrpheusAudioValidationProfile : ScriptableObject
    {
        [SerializeField] private int _schemaVersion = Core.OrpheusAudioAuthoringSchema.Current;
        [SerializeField] private bool _enabled;
        [SerializeField] private OrpheusAudioSettings _settings;
        [SerializeField] private OrpheusAudioCatalog _catalog;
        [SerializeField] private OrpheusAudioKeyManifest _keyManifest;
        [SerializeField] private GameObject _runtimeHostPrefab;
        [SerializeField] private SceneAsset[] _listenerScenes = Array.Empty<SceneAsset>();
        [SerializeField] private OrpheusAudioAuthoringProfile _authoringProfile;
        [SerializeField] private string _authoringEnrollmentGuid = string.Empty;

        internal int SchemaVersion => _schemaVersion;
        internal bool Enabled => _enabled;
        internal OrpheusAudioSettings Settings => _settings;
        internal OrpheusAudioCatalog Catalog => _catalog;
        internal OrpheusAudioKeyManifest KeyManifest => _keyManifest;
        internal GameObject RuntimeHostPrefab => _runtimeHostPrefab;
        internal int ListenerSceneCount => _listenerScenes == null ? 0 : _listenerScenes.Length;
        internal OrpheusAudioAuthoringProfile AuthoringProfile => _authoringProfile;
        internal string AuthoringEnrollmentGuid => _authoringEnrollmentGuid;

        internal SceneAsset GetListenerScene(int index)
        {
            return _listenerScenes[index];
        }
    }
}
