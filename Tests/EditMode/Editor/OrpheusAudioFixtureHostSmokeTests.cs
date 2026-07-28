using System.Collections;
using System.Linq;
using NUnit.Framework;
using Orpheus.Audio.Core;
using Orpheus.Audio.Tests.Fixtures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioFixtureHostSmokeTests
    {
        private static class FixtureContract
        {
            internal const string SceneSuffix = "/OrpheusIssue19/FixtureHost.unity";
            internal const ushort BootstrapOneShot2DKey = 100;
            internal const ushort BgmKey = 105;
            internal const ushort ProfileAmbienceKey = 106;
            internal const uint ProfileId = 1;
            internal const float TimeoutSeconds = 5f;
        }

        private Scene _loadedScene;
        private OrpheusAudioFixtureHost _fixtureHost;
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
                .Where(path => path.EndsWith(FixtureContract.SceneSuffix))
                .SingleOrDefault();
            Assert.That(scenePath, Is.Not.Null.And.Not.Empty, FixtureContract.SceneSuffix);
            EditorSceneManager.LoadSceneInPlayMode(
                scenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;

            _loadedScene = SceneManager.GetSceneByPath(scenePath);
            Assert.That(_loadedScene.IsValid(), Is.True, scenePath);
            Assert.That(_loadedScene.isLoaded, Is.True, scenePath);
            Assert.That(SceneManager.SetActiveScene(_loadedScene), Is.True, scenePath);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_fixtureHost != null && _fixtureHost.Manager != null)
            {
                _fixtureHost.Teardown();
            }
            else if (_manager != null &&
                     _manager.TryGetDiagnostics(out var diagnostics) &&
                     diagnostics.Lifecycle != OrpheusAudioLifecycle.Disposed)
            {
                _manager.Dispose();
                _runtimeHost?.Unbind(_manager);
            }

            _fixtureHost = null;
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
        [UnityPlatform(RuntimePlatform.WindowsEditor)]
        public IEnumerator NonEmptyFixture_PublicPathPlaysLoadedCueToNaturalCompletionAndDisposesCleanly()
        {
            var fixtureHosts = Object.FindObjectsOfType<OrpheusAudioFixtureHost>(true);
            Assert.That(fixtureHosts, Has.Length.EqualTo(1));
            _fixtureHost = fixtureHosts[0];
            Assert.That(_fixtureHost.gameObject.scene, Is.EqualTo(_loadedScene));
            Assert.That(_fixtureHost.Initialize(), Is.True,
                _fixtureHost.CreationResult.ErrorCode.ToString());

            _manager = _fixtureHost.Manager;
            _runtimeHost = _fixtureHost.RuntimeHost;
            Assert.That(_manager, Is.Not.Null);
            Assert.That(_fixtureHost.SceneBinding, Is.Not.Null);
            Assert.That(_runtimeHost.gameObject.scene, Is.EqualTo(_loadedScene));
            Assert.That(_fixtureHost.SceneBinding.gameObject.scene, Is.EqualTo(_loadedScene));
            Assert.That(
                _fixtureHost.HostReadyResult,
                Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));

            // Batchmode has no OS focus. Normalize the deterministic fixture to the
            // focused Windows Editor state required by this playback smoke.
            _runtimeHost.HandleApplicationFocusChanged(true);

            var liveHosts = Object.FindObjectsOfType<OrpheusAudioRuntimeHost>(true);
            Assert.That(liveHosts, Has.Length.EqualTo(1));
            Assert.That(liveHosts[0], Is.SameAs(_runtimeHost));
            Assert.That(_runtimeHost.isActiveAndEnabled, Is.True);

            Assert.That(_manager.TryGetDiagnostics(out var ready), Is.True);
            AssertReady(ready);

            var cueKey = new OrpheusAudioKey(FixtureContract.BootstrapOneShot2DKey);
            var loadDeadline = Time.realtimeSinceStartup + FixtureContract.TimeoutSeconds;
            while (_manager.GetLoadState(cueKey) != OrpheusClipLoadState.Loaded &&
                   Time.realtimeSinceStartup < loadDeadline)
            {
                yield return null;
            }

            Assert.That(
                _manager.GetLoadState(cueKey),
                Is.EqualTo(OrpheusClipLoadState.Loaded));

            _manager.ApplyProfile(new OrpheusAudioProfileIntent(
                FixtureContract.ProfileId,
                OrpheusBaseState.Combat,
                new OrpheusAudioKey(FixtureContract.BgmKey),
                new OrpheusAudioKey(FixtureContract.ProfileAmbienceKey)));
            _manager.SetOverlay(OrpheusOverlay.Menu, true);

            Assert.That(_manager.TryGetDiagnostics(out var applied), Is.True);
            Assert.That(applied.ProfileId, Is.EqualTo(FixtureContract.ProfileId));
            Assert.That(applied.BaseState, Is.EqualTo(OrpheusBaseState.Combat));
            Assert.That(applied.Overlay, Is.EqualTo(OrpheusOverlay.Menu));
            Assert.That(applied.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Menu));
            AssertCountersAreZero(applied.Counters);

            _manager.Play(cueKey);
            Assert.That(_manager.TryGetDiagnostics(out var started), Is.True);
            Assert.That(started.Transient2DActiveCount, Is.EqualTo(1));
            AssertCountersAreZero(started.Counters);

            var completionDeadline = Time.realtimeSinceStartup + FixtureContract.TimeoutSeconds;
            do
            {
                yield return null;
                Assert.That(_manager.TryGetDiagnostics(out started), Is.True);
            }
            while (started.Transient2DActiveCount != 0 &&
                   Time.realtimeSinceStartup < completionDeadline);

            Assert.That(started.Transient2DActiveCount, Is.Zero, "Fixture cue did not complete naturally.");
            AssertCountersAreZero(started.Counters);

            _fixtureHost.Teardown();
            Assert.That(_manager.TryGetDiagnostics(out var disposed), Is.True);
            AssertDisposed(disposed);

            var sources = _runtimeHost.GetComponentsInChildren<AudioSource>(true);
            Assert.That(sources, Has.Length.EqualTo(24));
            foreach (var source in sources.OrderBy(source => source.name))
            {
                AssertNormalized(source);
            }

            _manager = null;
            yield return null;
        }

        private static void AssertReady(OrpheusAudioDiagnostics diagnostics)
        {
            Assert.That(diagnostics.SchemaVersion, Is.EqualTo(1));
            Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
            Assert.That(diagnostics.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.None));
            Assert.That(diagnostics.TransportState, Is.EqualTo(OrpheusTransportState.Active));
            Assert.That(diagnostics.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.None));
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
            Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Peace));
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
            Assert.That(diagnostics.ProfileId, Is.EqualTo(FixtureContract.ProfileId));
            Assert.That(diagnostics.BaseState, Is.EqualTo(OrpheusBaseState.Combat));
            Assert.That(diagnostics.Overlay, Is.EqualTo(OrpheusOverlay.Menu));
            Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Menu));
            Assert.That(diagnostics.DesiredBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.CurrentBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.TargetBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.DesiredProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.CurrentProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.TargetProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
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
            AssertCountersAreZero(diagnostics.Counters);
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
    }
}
