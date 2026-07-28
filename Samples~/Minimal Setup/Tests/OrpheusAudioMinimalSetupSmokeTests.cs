using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Orpheus.Audio.Samples.MinimalSetup.Tests
{
    public sealed class OrpheusAudioMinimalSetupSmokeTests
    {
        private const string SceneSuffix = "/Minimal Setup/Scenes/MinimalSetup.unity";
        private const float HostReadyTimeoutSeconds = 5f;

        private Scene _loadedScene;
        private MinimalCompositionRoot _compositionRoot;
        private OrpheusAudioRuntimeHost _runtimeHost;
        private OrpheusAudioManager _manager;
        private GameObject _replacementListenerObject;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DeletePlayerPrefs();
            yield return new EnterPlayMode();

            foreach (var listener in UnityEngine.Object.FindObjectsOfType<AudioListener>(true))
            {
                listener.enabled = false;
            }

            var scenePath = FindImportedSampleScene();
            Assert.That(scenePath, Is.Not.Null.And.Not.Empty, SceneSuffix);

            EditorSceneManager.LoadSceneInPlayMode(
                scenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;

            _loadedScene = SceneManager.GetSceneByPath(scenePath);
            Assert.That(_loadedScene.IsValid(), Is.True, scenePath);
            Assert.That(_loadedScene.isLoaded, Is.True, scenePath);
            Assert.That(SceneManager.SetActiveScene(_loadedScene), Is.True, scenePath);

            yield return null;
            var compositionRoot = UnityEngine.Object.FindObjectsOfType<MinimalCompositionRoot>(true)
                .Single();
            var deadline = Time.realtimeSinceStartup + HostReadyTimeoutSeconds;
            while (compositionRoot.HostReadyResult != OrpheusCompleteHostReadyResult.Completed &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                compositionRoot.HostReadyResult,
                Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));

            // Batchmode starts without OS focus. Exercise the real Unity callback entry
            // so the sample smoke begins from the same focused state as an interactive Host.
            var runtimeHost = UnityEngine.Object.FindObjectsOfType<OrpheusAudioRuntimeHost>(true)
                .Single();
            runtimeHost.SendMessage(
                "OnApplicationFocus",
                true,
                SendMessageOptions.RequireReceiver);
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
                if (_runtimeHost != null)
                {
                    _runtimeHost.Unbind(_manager);
                }
            }

            if (_replacementListenerObject != null)
            {
                UnityEngine.Object.Destroy(_replacementListenerObject);
            }

            _replacementListenerObject = null;
            _compositionRoot = null;
            _runtimeHost = null;
            _manager = null;

            if (_loadedScene.IsValid() && _loadedScene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_loadedScene);
            }

            _loadedScene = default;
            DeletePlayerPrefs();
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ImportedScene_ActivatesRebindsAndDisposesThroughCompositionRoot()
        {
            var compositionRoots = UnityEngine.Object.FindObjectsOfType<MinimalCompositionRoot>(true);
            Assert.That(compositionRoots, Has.Length.EqualTo(1));
            _compositionRoot = compositionRoots[0];
            Assert.That(_compositionRoot.gameObject.scene, Is.EqualTo(_loadedScene));

            var runtimeHosts = UnityEngine.Object.FindObjectsOfType<OrpheusAudioRuntimeHost>(true);
            Assert.That(runtimeHosts, Has.Length.EqualTo(1));
            _runtimeHost = runtimeHosts[0];
            Assert.That(_runtimeHost.gameObject.scene, Is.EqualTo(_loadedScene));
            Assert.That(_runtimeHost.isActiveAndEnabled, Is.True);

            Assert.That(_compositionRoot.CreationResult.Success, Is.True,
                _compositionRoot.CreationResult.ErrorCode.ToString());
            Assert.That(_compositionRoot.Create(), Is.True);
            Assert.That(
                _compositionRoot.HostReadyResult,
                Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));

            _manager = _compositionRoot.Manager;
            Assert.That(_manager, Is.Not.Null);
            var sceneBinding = _compositionRoot.GetComponent<MinimalSceneBinding>();
            Assert.That(sceneBinding, Is.Not.Null);
            Assert.That(sceneBinding.BindListener(_manager), Is.True);
            Assert.That(_manager.TryGetDiagnostics(out var activated), Is.True);
            AssertActivated(activated);

            _compositionRoot.SetMenuOverlay(true);
            AssertOverlay(OrpheusOverlay.Menu, OrpheusEffectiveSnapshot.Menu);
            _compositionRoot.SetPauseOverlay(true);
            AssertOverlay(
                OrpheusOverlay.Menu | OrpheusOverlay.Pause,
                OrpheusEffectiveSnapshot.Pause);
            _compositionRoot.SetMenuOverlay(false);
            AssertOverlay(OrpheusOverlay.Pause, OrpheusEffectiveSnapshot.Pause);
            _compositionRoot.SetPauseOverlay(false);
            AssertOverlay(OrpheusOverlay.None, OrpheusEffectiveSnapshot.Peace);

            var originalListener = _loadedScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AudioListener>(true))
                .Single(listener => listener.isActiveAndEnabled);
            originalListener.enabled = false;
            _replacementListenerObject = new GameObject("OrpheusMinimalSetupReplacementListener");
            SceneManager.MoveGameObjectToScene(_replacementListenerObject, _loadedScene);
            var replacementListener = _replacementListenerObject.AddComponent<AudioListener>();

            Assert.That(_compositionRoot.ReplaceListener(replacementListener), Is.True);
            Assert.That(_manager.TryGetDiagnostics(out var replaced), Is.True);
            AssertActivated(replaced);

            Assert.That(_compositionRoot.RemoveListener(), Is.True);
            replacementListener.enabled = false;
            Assert.That(_manager.TryGetDiagnostics(out var removed), Is.True);
            Assert.That(removed.ActivationReady, Is.True);
            Assert.That(removed.PlaybackReady, Is.False);
            Assert.That(removed.TransportState, Is.EqualTo(OrpheusTransportState.Suspended));
            Assert.That(
                removed.SuspensionReasons,
                Is.EqualTo(OrpheusSuspensionReason.ListenerMissing));
            AssertCountersAreZero(removed.Counters);

            var sources = _runtimeHost.GetComponentsInChildren<AudioSource>(true);
            Assert.That(sources, Has.Length.EqualTo(24));

            _compositionRoot.Teardown();
            Assert.That(_compositionRoot.Manager, Is.Null);
            Assert.That(_manager.TryGetDiagnostics(out var disposed), Is.True);
            AssertDisposed(disposed);

            foreach (var source in sources.OrderBy(source => source.name, StringComparer.Ordinal))
            {
                AssertNormalized(source);
            }

            yield return null;
        }

        private void AssertOverlay(
            OrpheusOverlay expectedOverlay,
            OrpheusEffectiveSnapshot expectedSnapshot)
        {
            Assert.That(_manager.TryGetDiagnostics(out var diagnostics), Is.True);
            Assert.That(diagnostics.Overlay, Is.EqualTo(expectedOverlay));
            Assert.That(
                diagnostics.MenuOverlayEnabled,
                Is.EqualTo((expectedOverlay & OrpheusOverlay.Menu) != 0));
            Assert.That(
                diagnostics.PauseOverlayEnabled,
                Is.EqualTo((expectedOverlay & OrpheusOverlay.Pause) != 0));
            Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(expectedSnapshot));
            AssertCountersAreZero(diagnostics.Counters);
        }

        private static string FindImportedSampleScene()
        {
            return AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
                .Where(path => path.EndsWith(SceneSuffix, StringComparison.Ordinal))
                .SingleOrDefault();
        }

        private static void AssertActivated(OrpheusAudioDiagnostics diagnostics)
        {
            Assert.That(diagnostics.SchemaVersion, Is.EqualTo(1));
            Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
            Assert.That(diagnostics.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.None));
            Assert.That(diagnostics.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.None));
            Assert.That(diagnostics.TransportState, Is.EqualTo(OrpheusTransportState.Active));
            Assert.That(
                diagnostics.Readiness,
                Is.EqualTo(
                    OrpheusReadiness.HostReady |
                    OrpheusReadiness.BootstrapHydrated |
                    OrpheusReadiness.ActivationReady |
                    OrpheusReadiness.PlaybackReady));
            Assert.That(diagnostics.HostReady, Is.True);
            Assert.That(diagnostics.BootstrapHydrated, Is.True);
            Assert.That(diagnostics.ActivationReady, Is.True);
            Assert.That(diagnostics.PlaybackReady, Is.True);
            Assert.That(diagnostics.ProfileId, Is.Zero);
            Assert.That(diagnostics.BaseState, Is.EqualTo(OrpheusBaseState.Peace));
            Assert.That(diagnostics.Overlay, Is.EqualTo(OrpheusOverlay.None));
            Assert.That(diagnostics.MenuOverlayEnabled, Is.False);
            Assert.That(diagnostics.PauseOverlayEnabled, Is.False);
            Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Peace));
            Assert.That(diagnostics.RecoveryPendingReasons, Is.EqualTo(OrpheusRecoveryPendingReason.None));
            AssertPersistentKeysInvalid(diagnostics);
            AssertExecutionCountsAreZero(diagnostics);
            AssertCountersAreZero(diagnostics.Counters);
        }

        private static void AssertDisposed(OrpheusAudioDiagnostics diagnostics)
        {
            Assert.That(diagnostics.SchemaVersion, Is.EqualTo(1));
            Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disposed));
            Assert.That(diagnostics.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.None));
            Assert.That(diagnostics.TransportState, Is.EqualTo(OrpheusTransportState.Disposed));
            Assert.That(diagnostics.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.None));
            Assert.That(diagnostics.Readiness, Is.EqualTo(OrpheusReadiness.None));
            Assert.That(diagnostics.RecoveryPendingReasons, Is.EqualTo(OrpheusRecoveryPendingReason.None));
            Assert.That(diagnostics.ProfileId, Is.Zero);
            Assert.That(diagnostics.BaseState, Is.EqualTo(OrpheusBaseState.Peace));
            Assert.That(diagnostics.Overlay, Is.EqualTo(OrpheusOverlay.None));
            Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Peace));
            AssertPersistentKeysInvalid(diagnostics);
            AssertExecutionCountsAreZero(diagnostics);
            AssertCountersAreZero(diagnostics.Counters);
        }

        private static void AssertPersistentKeysInvalid(OrpheusAudioDiagnostics diagnostics)
        {
            Assert.That(diagnostics.DesiredBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.CurrentBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.TargetBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.DesiredProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.CurrentProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.TargetProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
        }

        private static void AssertExecutionCountsAreZero(OrpheusAudioDiagnostics diagnostics)
        {
            Assert.That(diagnostics.Transient3DActiveCount, Is.Zero);
            Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
            Assert.That(diagnostics.FadingCount, Is.Zero);
            Assert.That(diagnostics.PendingCount, Is.Zero);
            Assert.That(diagnostics.BgmActiveCount, Is.Zero);
            Assert.That(diagnostics.ProfileAmbienceActiveCount, Is.Zero);
            Assert.That(diagnostics.GlobalLoopActiveCount, Is.Zero);
            Assert.That(diagnostics.Reserved0, Is.Zero);
            Assert.That(diagnostics.Reserved1, Is.Zero);
            Assert.That(diagnostics.Reserved2, Is.Zero);
        }

        private static void AssertCountersAreZero(OrpheusAudioDiagnosticsCounters counters)
        {
            Assert.That(counters.PoolCapacityRejected, Is.Zero);
            Assert.That(counters.Stolen, Is.Zero);
            Assert.That(counters.CooldownRejected, Is.Zero);
            Assert.That(counters.PolyphonyRejected, Is.Zero);
            Assert.That(counters.DistanceRejected, Is.Zero);
            Assert.That(counters.PreReadyRejected, Is.Zero);
            Assert.That(counters.SuspendedRejected, Is.Zero);
            Assert.That(counters.UnavailableRejected, Is.Zero);
            Assert.That(counters.WrongThreadRejected, Is.Zero);
            Assert.That(counters.LoadNotReadyRejected, Is.Zero);
            Assert.That(counters.LoadFailed, Is.Zero);
            Assert.That(counters.LoadStalled, Is.Zero);
            Assert.That(counters.InvalidKeyRejected, Is.Zero);
            Assert.That(counters.InvalidRawKeyRejected, Is.Zero);
            Assert.That(counters.InvalidPositionRejected, Is.Zero);
            Assert.That(counters.InvalidValueRejected, Is.Zero);
            Assert.That(counters.PlaybackKindRejected, Is.Zero);
            Assert.That(counters.LoopRegistryFull, Is.Zero);
            Assert.That(counters.SetFloatFailed, Is.Zero);
            Assert.That(counters.RecoveryFailed, Is.Zero);
            Assert.That(counters.StaleBootstrapTokenRejected, Is.Zero);
            Assert.That(counters.UnexpectedException, Is.Zero);
        }

        private static void AssertNormalized(AudioSource source)
        {
            Assert.That(source.isPlaying, Is.False, source.name);
            Assert.That(source.clip, Is.Null, source.name);
            Assert.That(source.time, Is.Zero, source.name);
            Assert.That(source.playOnAwake, Is.False, source.name);
            Assert.That(source.loop, Is.False, source.name);
            Assert.That(source.mute, Is.False, source.name);
            Assert.That(source.volume, Is.Zero, source.name);
            Assert.That(source.pitch, Is.EqualTo(1f), source.name);
            Assert.That(source.priority, Is.EqualTo(128), source.name);
            Assert.That(source.outputAudioMixerGroup, Is.Null, source.name);
            Assert.That(source.spatialBlend, Is.Zero, source.name);
            Assert.That(source.panStereo, Is.Zero, source.name);
            Assert.That(source.spread, Is.Zero, source.name);
            Assert.That(source.dopplerLevel, Is.Zero, source.name);
            Assert.That(source.reverbZoneMix, Is.EqualTo(1f), source.name);
            Assert.That(source.minDistance, Is.EqualTo(1f), source.name);
            Assert.That(source.maxDistance, Is.EqualTo(500f), source.name);
            Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Logarithmic), source.name);
            Assert.That(source.bypassEffects, Is.False, source.name);
            Assert.That(source.bypassListenerEffects, Is.False, source.name);
            Assert.That(source.bypassReverbZones, Is.False, source.name);
            Assert.That(source.ignoreListenerPause, Is.False, source.name);
            Assert.That(source.ignoreListenerVolume, Is.False, source.name);
            Assert.That(source.spatialize, Is.False, source.name);
            Assert.That(source.spatializePostEffects, Is.False, source.name);
            Assert.That(source.velocityUpdateMode, Is.EqualTo(AudioVelocityUpdateMode.Fixed), source.name);
        }

        private static void DeletePlayerPrefs()
        {
            PlayerPrefs.DeleteKey(PlayerPrefsGainStore.MasterKey);
            PlayerPrefs.DeleteKey(PlayerPrefsGainStore.MusicKey);
            PlayerPrefs.DeleteKey(PlayerPrefsGainStore.SfxCombatKey);
            PlayerPrefs.DeleteKey(PlayerPrefsGainStore.SfxWorldKey);
            PlayerPrefs.DeleteKey(PlayerPrefsGainStore.SfxUiKey);
            PlayerPrefs.DeleteKey(PlayerPrefsGainStore.AmbienceKey);
            PlayerPrefs.Save();
        }
    }
}
