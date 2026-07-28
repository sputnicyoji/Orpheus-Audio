using System.Collections;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Samples.MinimalSetup
{
    [DefaultExecutionOrder(-100)]
    public sealed class MinimalCompositionRoot : MonoBehaviour
    {
        [SerializeField] private OrpheusAudioRuntimeHost _runtimeHost;
        [SerializeField] private OrpheusAudioSettings _settings;
        [SerializeField] private OrpheusAudioCatalog _catalog;
        [SerializeField] private MinimalSceneBinding _sceneBinding;

        private OrpheusAudioManager _manager;
        private OrpheusAudioInitResult _creationResult;
        private OrpheusCompleteHostReadyResult _hostReadyResult;
        private bool _createAttempted;

        public OrpheusAudioManager Manager => _manager;
        public OrpheusAudioInitResult CreationResult => _creationResult;
        public OrpheusCompleteHostReadyResult HostReadyResult => _hostReadyResult;

        private void Awake()
        {
            Create();
        }

        private IEnumerator Start()
        {
            // Runtime Host must observe Start before the Host Ready barrier completes.
            yield return null;
            CompleteHostReady();
        }

        public bool Create()
        {
            if (_createAttempted)
            {
                return _manager != null;
            }

            _createAttempted = true;
            _creationResult = OrpheusAudioFactory.Create(
                _runtimeHost,
                _settings,
                _catalog,
                PlayerPrefsGainStore.Load(),
                out var manager);
            if (!_creationResult.Success)
            {
                return false;
            }

            _manager = manager;
            if (!_runtimeHost.Bind(manager))
            {
                DisposeUnboundManager(manager);
                return false;
            }

            if (_sceneBinding == null || !_sceneBinding.BindListener(manager))
            {
                Teardown();
                return false;
            }

            var authority = manager.CaptureBootstrapAuthority();
            if (!manager.CompleteBootstrapHydration(authority))
            {
                Teardown();
            }

            return _manager != null;
        }

        public OrpheusCompleteHostReadyResult CompleteHostReady()
        {
            if (_manager == null)
            {
                _hostReadyResult = OrpheusCompleteHostReadyResult.Invalid;
                return _hostReadyResult;
            }

            _hostReadyResult = _manager.CompleteHostReady();
            return _hostReadyResult;
        }

        public bool ReplaceListener(AudioListener replacementListener)
        {
            if (_manager == null || _sceneBinding == null ||
                !_sceneBinding.ReplaceListener(_manager, replacementListener))
            {
                return false;
            }

            return true;
        }

        public bool RemoveListener()
        {
            if (_manager == null || _sceneBinding == null ||
                !_sceneBinding.RemoveListener(_manager))
            {
                return false;
            }

            return true;
        }

        public void SetMenuOverlay(bool enabled)
        {
            if (_manager != null)
            {
                _manager.SetOverlay(OrpheusOverlay.Menu, enabled);
            }
        }

        public void SetPauseOverlay(bool enabled)
        {
            if (_manager != null)
            {
                _manager.SetOverlay(OrpheusOverlay.Pause, enabled);
            }
        }

        public bool SaveGains()
        {
            if (_manager == null ||
                !_manager.TryGetUserGain(OrpheusBus.Master, out var master) ||
                !_manager.TryGetUserGain(OrpheusBus.Music, out var music) ||
                !_manager.TryGetUserGain(OrpheusBus.SfxCombat, out var sfxCombat) ||
                !_manager.TryGetUserGain(OrpheusBus.SfxWorld, out var sfxWorld) ||
                !_manager.TryGetUserGain(OrpheusBus.SfxUi, out var sfxUi) ||
                !_manager.TryGetUserGain(OrpheusBus.Ambience, out var ambience))
            {
                return false;
            }

            return PlayerPrefsGainStore.Save(new OrpheusAudioUserGains(
                master,
                music,
                sfxCombat,
                sfxWorld,
                sfxUi,
                ambience));
        }

        public void Teardown()
        {
            var manager = _manager;
            if (manager == null)
            {
                return;
            }

            try
            {
                SaveGains();
            }
            finally
            {
                try
                {
                    if (_sceneBinding != null)
                    {
                        _sceneBinding.RemoveListener(manager);
                    }
                }
                finally
                {
                    try
                    {
                        manager.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            if (_runtimeHost != null)
                            {
                                _runtimeHost.Unbind(manager);
                            }
                        }
                        finally
                        {
                            _manager = null;
                        }
                    }
                }
            }
        }

        private void OnDestroy()
        {
            Teardown();
        }

        private void DisposeUnboundManager(OrpheusAudioManager manager)
        {
            manager.Dispose();
            _manager = null;
        }
    }
}
