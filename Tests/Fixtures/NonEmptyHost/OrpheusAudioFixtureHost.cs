using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Tests.Fixtures
{
    public sealed class OrpheusAudioFixtureHost : MonoBehaviour
    {
        [SerializeField] private OrpheusAudioSettings _settings;
        [SerializeField] private OrpheusAudioCatalog _catalog;
        [SerializeField] private OrpheusAudioRuntimeHost _runtimeHost;
        [SerializeField] private OrpheusAudioFixtureSceneBinding _sceneBinding;

        private OrpheusAudioManager _manager;
        private OrpheusAudioInitResult _creationResult;
        private OrpheusCompleteHostReadyResult _hostReadyResult;
        private bool _initializeAttempted;

        public OrpheusAudioRuntimeHost RuntimeHost => _runtimeHost;
        public OrpheusAudioFixtureSceneBinding SceneBinding => _sceneBinding;
        public OrpheusAudioManager Manager => _manager;
        public OrpheusAudioInitResult CreationResult => _creationResult;
        public OrpheusCompleteHostReadyResult HostReadyResult => _hostReadyResult;

        public bool Initialize()
        {
            if (_initializeAttempted)
            {
                return _manager != null;
            }

            _initializeAttempted = true;
            if (_runtimeHost == null || _sceneBinding == null)
            {
                return false;
            }

            _creationResult = OrpheusAudioFactory.Create(
                _runtimeHost,
                _settings,
                _catalog,
                new OrpheusAudioUserGains(1f, 1f, 1f, 1f, 1f, 1f),
                out var manager);
            if (!_creationResult.Success)
            {
                return false;
            }

            _manager = manager;
            if (!_sceneBinding.BindListener(manager) || !_runtimeHost.Bind(manager))
            {
                Teardown();
                return false;
            }

            if (!manager.CompleteBootstrapHydration(manager.CaptureBootstrapAuthority()))
            {
                Teardown();
                return false;
            }

            _hostReadyResult = manager.CompleteHostReady();
            if (_hostReadyResult != OrpheusCompleteHostReadyResult.Completed)
            {
                Teardown();
                return false;
            }

            return true;
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
                    if (_runtimeHost != null)
                    {
                        _runtimeHost.Unbind(manager);
                    }

                    _manager = null;
                }
            }
        }
    }
}
