using System.Collections;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Samples.PlayableOneShot
{
    [DefaultExecutionOrder(-100)]
    public sealed class PlayableOneShotCompositionRoot : MonoBehaviour
    {
        private const ushort CueRawKey = 100;
        private const float LoadTimeoutSeconds = 5f;
        private static readonly Rect ReplayButtonRect =
            new Rect(16f, 16f, 180f, 40f);
        private static readonly GUIContent ReplayButtonContent =
            new GUIContent("Replay Orpheus Cue");

        private static readonly OrpheusAudioKey CueKey =
            new OrpheusAudioKey(CueRawKey);

        [SerializeField] private OrpheusAudioRuntimeHost _runtimeHost;
        [SerializeField] private OrpheusAudioSettings _settings;
        [SerializeField] private OrpheusAudioCatalog _catalog;
        [SerializeField] private PlayableOneShotSceneBinding _sceneBinding;

        private OrpheusAudioManager _manager;
        private OrpheusAudioInitResult _creationResult;
        private OrpheusCompleteHostReadyResult _hostReadyResult;
        private bool _createAttempted;
        private bool _autoPlayAttempted;

        public OrpheusAudioManager Manager => _manager;
        public OrpheusAudioInitResult CreationResult => _creationResult;
        public OrpheusCompleteHostReadyResult HostReadyResult => _hostReadyResult;
        public bool AutoPlayAttempted => _autoPlayAttempted;

        private void Awake()
        {
            Create();
        }

        private IEnumerator Start()
        {
            // Runtime Host must observe Start before the Host Ready barrier completes.
            yield return null;
            var result = CompleteHostReady();
            if (result != OrpheusCompleteHostReadyResult.Completed &&
                result != OrpheusCompleteHostReadyResult.AlreadyCompleted)
            {
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + LoadTimeoutSeconds;
            while (_manager != null &&
                   _manager.GetLoadState(CueKey) != OrpheusClipLoadState.Loaded &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (_manager == null ||
                _manager.GetLoadState(CueKey) != OrpheusClipLoadState.Loaded)
            {
                yield break;
            }

            _autoPlayAttempted = true;
            PlayCue();
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
                new OrpheusAudioUserGains(1f, 1f, 1f, 1f, 1f, 1f),
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

        public void PlayCue()
        {
            if (_manager != null)
            {
                _manager.Play(CueKey);
            }
        }

        private void OnGUI()
        {
            if (_manager != null &&
                GUI.Button(ReplayButtonRect, ReplayButtonContent))
            {
                PlayCue();
            }
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
