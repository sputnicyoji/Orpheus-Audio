using System.Collections;
using System.Linq;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Orpheus.Audio.Samples.PlayableOneShot.Tests
{
    public sealed class OrpheusAudioPlayableOneShotSmokeTests
    {
        private const string SceneSuffix = "/Scenes/PlayableOneShot.unity";
        private const float TimeoutSeconds = 5f;

        private Scene _loadedScene;
        private PlayableOneShotCompositionRoot _compositionRoot;
        private OrpheusAudioRuntimeHost _runtimeHost;
        private OrpheusAudioManager _manager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return new EnterPlayMode();

            foreach (var listener in Object.FindObjectsOfType<AudioListener>(true))
            {
                listener.enabled = false;
            }

            var scenePath = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.EndsWith(SceneSuffix))
                .SingleOrDefault();
            Assert.That(scenePath, Is.Not.Null.And.Not.Empty, SceneSuffix);

            EditorSceneManager.LoadSceneInPlayMode(
                scenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;

            _loadedScene = SceneManager.GetSceneByPath(scenePath);
            Assert.That(_loadedScene.IsValid(), Is.True, scenePath);
            Assert.That(_loadedScene.isLoaded, Is.True, scenePath);
            Assert.That(SceneManager.SetActiveScene(_loadedScene), Is.True, scenePath);

            _runtimeHost = Object.FindObjectsOfType<OrpheusAudioRuntimeHost>(true)
                .Single(host => host.gameObject.scene == _loadedScene);
            _runtimeHost.SendMessage(
                "OnApplicationFocus",
                true,
                SendMessageOptions.RequireReceiver);

            _compositionRoot =
                Object.FindObjectsOfType<PlayableOneShotCompositionRoot>(true)
                    .Single(root => root.gameObject.scene == _loadedScene);

            var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while ((_compositionRoot.HostReadyResult !=
                    OrpheusCompleteHostReadyResult.Completed ||
                    !_compositionRoot.AutoPlayAttempted) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_compositionRoot != null && _compositionRoot.Manager != null)
            {
                _compositionRoot.Teardown();
            }
            else if (_manager != null &&
                     _manager.TryGetDiagnostics(out var diagnostics) &&
                     diagnostics.Lifecycle != OrpheusAudioLifecycle.Disposed)
            {
                _manager.Dispose();
                _runtimeHost?.Unbind(_manager);
            }

            _compositionRoot = null;
            _runtimeHost = null;
            _manager = null;

            if (_loadedScene.IsValid() && _loadedScene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_loadedScene);
            }

            _loadedScene = default;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ImportedScene_StartsCueAndDisposesCleanly()
        {
            Assert.That(
                _compositionRoot.HostReadyResult,
                Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
            Assert.That(_compositionRoot.AutoPlayAttempted, Is.True);
            Assert.That(_compositionRoot.CreationResult.Success, Is.True,
                _compositionRoot.CreationResult.ErrorCode.ToString());

            _manager = _compositionRoot.Manager;
            Assert.That(_manager, Is.Not.Null);
            Assert.That(_manager.TryGetDiagnostics(out var ready), Is.True);
            Assert.That(ready.PlaybackReady, Is.True);
            Assert.That(ready.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.None));

            var completionDeadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (ready.Transient2DActiveCount != 0 &&
                   Time.realtimeSinceStartup < completionDeadline)
            {
                yield return null;
                Assert.That(_manager.TryGetDiagnostics(out ready), Is.True);
            }

            Assert.That(ready.Transient2DActiveCount, Is.Zero);

            _compositionRoot.PlayCue();
            Assert.That(_manager.TryGetDiagnostics(out var started), Is.True);
            Assert.That(started.Transient2DActiveCount, Is.EqualTo(1));
            Assert.That(
                _runtimeHost.GetComponentsInChildren<AudioSource>(true)
                    .Count(source => source.isPlaying),
                Is.EqualTo(1));

            completionDeadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (started.Transient2DActiveCount != 0 &&
                   Time.realtimeSinceStartup < completionDeadline)
            {
                yield return null;
                Assert.That(_manager.TryGetDiagnostics(out started), Is.True);
            }

            Assert.That(started.Transient2DActiveCount, Is.Zero);

            var ownedSources = _runtimeHost.GetComponentsInChildren<AudioSource>(true);
            Assert.That(ownedSources, Has.Length.EqualTo(24));
            _compositionRoot.Teardown();

            Assert.That(_manager.TryGetDiagnostics(out var disposed), Is.True);
            Assert.That(
                disposed.Lifecycle,
                Is.EqualTo(OrpheusAudioLifecycle.Disposed));
            Assert.That(
                disposed.DisableReason,
                Is.EqualTo(OrpheusAudioDisableReason.None));

            foreach (var source in ownedSources)
            {
                Assert.That(source.isPlaying, Is.False, source.name);
                Assert.That(source.clip, Is.Null, source.name);
                Assert.That(source.volume, Is.Zero, source.name);
                Assert.That(source.outputAudioMixerGroup, Is.Null, source.name);
            }

            _manager = null;
        }
    }
}
