using System;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Tests
{
    public sealed partial class OrpheusAudioSessionTests
    {
        [Test]
        public void LiveIntegrity_DestroyedOwnedSource_DisablesOnNextTick()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    var manager = CreateBridgeBoundIntegrityManager(fixture);
                    var dirtyClip = AudioClip.Create("DestroyedOwnedSource", 4800, 1, 48000, false);
                    fixture.DirtyAllSources(dirtyClip);
                    Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                    UnityEngine.Object.DestroyImmediate(fixture.Sources[0].gameObject);
                    manager.Tick(IntegrityRealtime(), 0f);

                    AssertIntegrityTerminalDiagnostics(
                        before,
                        manager,
                        OrpheusAudioDisableReason.OwnedSourceDestroyed,
                        OrpheusAudioLifecycle.Disabled);
                    for (var sourceIndex = 1; sourceIndex < fixture.Sources.Length; sourceIndex++)
                    {
                        AssertNormalized(fixture.Sources[sourceIndex]);
                    }
                    AssertBridgesUnbound(manager);

                    DisposeIntegrityManager(fixture, manager);
                    UnityEngine.Object.DestroyImmediate(dirtyClip);
                }
            });
        }

        [Test]
        public void LiveIntegrity_ReplacedOwnedSource_DisablesAndLeavesReplacementUntouched()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    var manager = CreateBridgeBoundIntegrityManager(fixture);
                    var dirtyClip = AudioClip.Create("ReplacedOwnedSource", 4800, 1, 48000, false);
                    fixture.DirtyAllSources(dirtyClip);
                    var replacement = fixture.ReplaceFirst3DSource();
                    DirtySentinel(replacement, 0.37f);
                    Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                    manager.Tick(IntegrityRealtime(), 0f);

                    AssertIntegrityTerminalDiagnostics(
                        before,
                        manager,
                        OrpheusAudioDisableReason.OwnedSourceReferenceChanged,
                        OrpheusAudioLifecycle.Disabled);
                    AssertAllNormalized(fixture.Sources, 0);
                    AssertSentinel(replacement, 0.37f);
                    AssertBridgesUnbound(manager);

                    DisposeIntegrityManager(fixture, manager);
                    UnityEngine.Object.DestroyImmediate(dirtyClip);
                }
            });
        }

        [Test]
        public void LiveIntegrity_DuplicateSourceReference_PreservesFirstReasonAndCounters()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    var manager = CreateBridgeBoundIntegrityManager(fixture);
                    var dirtyClip = AudioClip.Create("DuplicateOwnedSource", 4800, 1, 48000, false);
                    fixture.DirtyAllSources(dirtyClip);
                    fixture.SetDuplicateSource();
                    Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                    var realtime = IntegrityRealtime();

                    manager.Tick(realtime, 0f);

                    AssertIntegrityTerminalDiagnostics(
                        before,
                        manager,
                        OrpheusAudioDisableReason.SourceBankDuplicateReference,
                        OrpheusAudioLifecycle.Disabled);
                    AssertAllNormalized(fixture.Sources, 0);
                    Assert.That(manager.TryGetDiagnostics(out var firstFailure), Is.True);

                    OrpheusAudioLeaseRegistry.ReleaseSourceBank(manager);
                    manager.Tick(realtime + 0.1d, 0.1f);
                    manager.FailClosed(OrpheusAudioDisableReason.MixerLeaseLost);

                    Assert.That(manager.TryGetDiagnostics(out var repeated), Is.True);
                    Assert.That(
                        repeated.DisableReason,
                        Is.EqualTo(OrpheusAudioDisableReason.SourceBankDuplicateReference));
                    AssertCountersEqual(firstFailure.Counters, repeated.Counters);
                    AssertBridgesUnbound(manager);

                    DisposeIntegrityManager(fixture, manager);
                    UnityEngine.Object.DestroyImmediate(dirtyClip);
                }
            });
        }

        [Test]
        public void LiveIntegrity_SourceLeaseLoss_DisablesWithoutTouchingReleasedSources()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    var manager = CreateBridgeBoundIntegrityManager(fixture);
                    DirtySentinel(fixture.Sources[0], 0.41f);
                    Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                    OrpheusAudioLeaseRegistry.ReleaseSourceBank(manager);
                    manager.Tick(IntegrityRealtime(), 0f);

                    AssertIntegrityTerminalDiagnostics(
                        before,
                        manager,
                        OrpheusAudioDisableReason.SourceBankLeaseLost,
                        OrpheusAudioLifecycle.Disabled);
                    AssertSentinel(fixture.Sources[0], 0.41f);
                    AssertBridgesUnbound(manager);

                    manager.Dispose();
                    AssertSentinel(fixture.Sources[0], 0.41f);
                    AssertIntegrityTerminalDiagnostics(
                        before,
                        manager,
                        OrpheusAudioDisableReason.SourceBankLeaseLost,
                        OrpheusAudioLifecycle.Disposed);
                    Assert.That(fixture.Host.Unbind(manager), Is.True);
                    AssertSentinel(fixture.Sources[0], 0.41f);
                }
            });
        }

        [Test]
        public void LiveIntegrity_MixerLeaseLoss_CleansWhileSourceLeaseRemainsHeld()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    var manager = CreateBridgeBoundIntegrityManager(fixture);
                    var dirtyClip = AudioClip.Create("MixerLeaseLost", 4800, 1, 48000, false);
                    fixture.DirtyAllSources(dirtyClip);
                    Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                    OrpheusAudioLeaseRegistry.ReleaseMixerAndHost(manager);
                    manager.Tick(IntegrityRealtime(), 0f);

                    AssertIntegrityTerminalDiagnostics(
                        before,
                        manager,
                        OrpheusAudioDisableReason.MixerLeaseLost,
                        OrpheusAudioLifecycle.Disabled);
                    AssertAllNormalized(fixture.Sources, 0);
                    AssertBridgesUnbound(manager);

                    DirtySentinel(fixture.Sources[0], 0.43f);
                    manager.Dispose();
                    AssertNormalized(fixture.Sources[0]);
                    AssertIntegrityTerminalDiagnostics(
                        before,
                        manager,
                        OrpheusAudioDisableReason.MixerLeaseLost,
                        OrpheusAudioLifecycle.Disposed);
                    Assert.That(fixture.Host.Unbind(manager), Is.True);
                    UnityEngine.Object.DestroyImmediate(dirtyClip);
                }
            });
        }

        [Test]
        public void IntegrityFailure_RetainsHistoricalDiagnosticsAndFinalDisabledDisposeCleanup()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    var manager = CreateBridgeBoundIntegrityManager(fixture);
                    manager.ApplyProfile(new OrpheusAudioProfileIntent(
                        77,
                        OrpheusBaseState.Combat,
                        OrpheusAudioKey.Invalid,
                        OrpheusAudioKey.Invalid));
                    manager.SetOverlay(OrpheusOverlay.Menu, true);
                    manager.Play(new OrpheusAudioKey(65000));
                    Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                    Assert.That(before.ProfileId, Is.EqualTo(77));
                    Assert.That(before.BaseState, Is.EqualTo(OrpheusBaseState.Combat));
                    Assert.That(before.Overlay, Is.EqualTo(OrpheusOverlay.Menu));
                    Assert.That(before.Counters.InvalidKeyRejected, Is.EqualTo(1));

                    fixture.SetDuplicateSource();
                    manager.Tick(IntegrityRealtime(), 0f);

                    AssertIntegrityTerminalDiagnostics(
                        before,
                        manager,
                        OrpheusAudioDisableReason.SourceBankDuplicateReference,
                        OrpheusAudioLifecycle.Disabled);
                    DirtySentinel(fixture.Sources[0], 0.47f);
                    manager.Dispose();
                    AssertNormalized(fixture.Sources[0]);
                    AssertIntegrityTerminalDiagnostics(
                        before,
                        manager,
                        OrpheusAudioDisableReason.SourceBankDuplicateReference,
                        OrpheusAudioLifecycle.Disposed);

                    Assert.That(fixture.Host.Unbind(manager), Is.True);
                    DirtySentinel(fixture.Sources[0], 0.49f);
                    manager.Dispose();
                    Assert.That(fixture.Host.Unbind(manager), Is.True);
                    AssertSentinel(fixture.Sources[0], 0.49f);
                }
            });
        }

        [Test]
        public void FailingManager_CannotUnbindForeignBridgeReplacement()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var failedFixture = new SessionFixture())
                using (var foreignFixture = new SessionFixture())
                {
                    foreignFixture.SetEvents(foreignFixture.CreatePlaybackEvent(700, polyphonyCap: 2));
                    var failedManager = CreateBridgeBoundIntegrityManager(failedFixture);
                    failedFixture.SetDuplicateSource();

                    failedManager.Tick(IntegrityRealtime(), 0f);
                    AssertBridgesUnbound(failedManager);
                    failedManager.Dispose();
                    Assert.That(failedFixture.Host.Unbind(failedManager), Is.True);
                    var foreignManager = foreignFixture.CreateReadyManager(out _);
                    Assert.That(OrpheusAudioBridge.Bind(foreignManager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(foreignManager), Is.True);

                    failedManager.Dispose();
                    OrpheusAudioBridge.Play(new OrpheusAudioKey(700));
                    OrpheusAudioRawBridge.Play(700);

                    Assert.That(foreignManager.TryGetDiagnostics(out var foreign), Is.True);
                    Assert.That(foreign.Transient2DActiveCount, Is.EqualTo(2));
                    Assert.That(OrpheusAudioBridge.Unbind(foreignManager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Unbind(foreignManager), Is.True);
                    foreignManager.Dispose();
                    Assert.That(foreignFixture.Host.Unbind(foreignManager), Is.True);
                }
            });
        }

        [Test]
        public void IntegrityFailure_InvalidatesDelayedTransientDirectorsLoopsAndRecovery()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    var victim = fixture.CreatePlaybackEvent(
                        700,
                        volume: 0.8f,
                        priority: 200,
                        polyphonyCap: 4);
                    var pending = fixture.CreatePlaybackEvent(
                        701,
                        volume: 0.6f,
                        priority: 100);
                    var bgmA = fixture.CreateBgmEvent(710);
                    var bgmB = fixture.CreateBgmEvent(711);
                    var ambienceA = fixture.CreateProfileAmbienceEvent(720);
                    var ambienceB = fixture.CreateProfileAmbienceEvent(721);
                    var loopA = fixture.CreateGlobalLoopEvent(730);
                    var loopB = fixture.CreateGlobalLoopEvent(731);
                    fixture.SetEvents(
                        victim,
                        pending,
                        bgmA,
                        bgmB,
                        ambienceA,
                        ambienceB,
                        loopA,
                        loopB);
                    fixture.SetClipState(7, OrpheusClipLoadState.Unloaded);
                    var manager = CreateBridgeBoundIntegrityManager(fixture);
                    var realtime = IntegrityRealtime();
                    manager.Tick(realtime, 0f);

                    manager.ApplyProfile(new OrpheusAudioProfileIntent(
                        1,
                        OrpheusBaseState.Peace,
                        new OrpheusAudioKey(710),
                        new OrpheusAudioKey(720)));
                    manager.Tick(realtime + 0.06d, 0.01f);
                    manager.Tick(realtime + 0.46d, 0.4f);
                    manager.ApplyProfile(new OrpheusAudioProfileIntent(
                        2,
                        OrpheusBaseState.Combat,
                        new OrpheusAudioKey(711),
                        new OrpheusAudioKey(721)));
                    manager.Tick(realtime + 0.47d, 0.01f);

                    for (var playIndex = 0; playIndex < 4; playIndex++)
                    {
                        manager.Play(new OrpheusAudioKey(700));
                    }
                    manager.Tick(realtime + 0.52d, 0.05f);
                    manager.Play(new OrpheusAudioKey(701));
                    manager.PlayLoop(new OrpheusAudioKey(730));
                    manager.Tick(realtime + 0.521d, 0.001f);
                    manager.StopLoop(new OrpheusAudioKey(730));
                    manager.PlayLoop(new OrpheusAudioKey(731));
                    fixture.SetClipState(7, OrpheusClipLoadState.Loading);
                    fixture.Host.RecordAudioConfigurationChanged(true);
                    Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                    Assert.That(before.PendingCount, Is.EqualTo(1));
                    Assert.That(before.FadingCount, Is.EqualTo(1));
                    Assert.That(before.BgmActiveCount, Is.EqualTo(2));
                    Assert.That(before.ProfileAmbienceActiveCount, Is.EqualTo(2));
                    Assert.That(before.GlobalLoopActiveCount, Is.EqualTo(1));

                    fixture.ReplaceFirst3DSource();
                    manager.Tick(realtime + 0.522d, 0.001f);

                    AssertIntegrityTerminalDiagnostics(
                        before,
                        manager,
                        OrpheusAudioDisableReason.OwnedSourceReferenceChanged,
                        OrpheusAudioLifecycle.Disabled);
                    for (var sourceIndex = 0; sourceIndex < fixture.Sources.Length; sourceIndex++)
                    {
                        DirtySentinel(fixture.Sources[sourceIndex], 0.51f);
                    }

                    fixture.SetClipState(7, OrpheusClipLoadState.Loaded);
                    fixture.Host.ConsumePendingAudioConfigurationChanges();
                    manager.Tick(realtime + 5d, 1f);
                    manager.LateTick();
                    manager.Play(new OrpheusAudioKey(701));
                    manager.ApplyProfile(new OrpheusAudioProfileIntent(
                        3,
                        OrpheusBaseState.Peace,
                        new OrpheusAudioKey(710),
                        new OrpheusAudioKey(720)));
                    manager.PlayLoop(new OrpheusAudioKey(731));
                    for (var sourceIndex = 0; sourceIndex < fixture.Sources.Length; sourceIndex++)
                    {
                        AssertSentinel(fixture.Sources[sourceIndex], 0.51f);
                    }

                    DisposeIntegrityManager(fixture, manager);
                }
            });
        }

        [Test]
        public void IntegrityFailure_InvalidatesCapturedBootstrapAuthority()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    var manager = fixture.CreateManagerBeforeHostReady(out _);
                    var authority = manager.CaptureBootstrapAuthority();
                    Assert.That(authority, Is.Not.EqualTo(default(OrpheusBootstrapAuthority)));
                    Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);
                    fixture.SetDuplicateSource();

                    manager.Tick(IntegrityRealtime(), 0f);
                    DirtySentinel(fixture.Sources[0], 0.53f);

                    Assert.That(manager.CompleteBootstrapHydration(authority), Is.False);
                    AssertSentinel(fixture.Sources[0], 0.53f);
                    Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                    Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                    Assert.That(
                        diagnostics.DisableReason,
                        Is.EqualTo(OrpheusAudioDisableReason.SourceBankDuplicateReference));
                    Assert.That(diagnostics.Counters.StaleBootstrapTokenRejected, Is.Zero);
                    AssertBridgesUnbound(manager);
                    DisposeIntegrityManager(fixture, manager);
                }
            });
        }

        [Test]
        public void IntegrityFailure_DoesNotTouchForeignAudioOrGlobalListenerPause()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                var listenerPause = AudioListener.pause;
                var foreignObject = new GameObject("ForeignAudio");
                var foreign = foreignObject.AddComponent<AudioSource>();
                try
                {
                    AudioListener.pause = !listenerPause;
                    DirtySentinel(foreign, 0.57f);
                    using (var fixture = new SessionFixture())
                    {
                        var manager = CreateBridgeBoundIntegrityManager(fixture);
                        var replacement = fixture.ReplaceFirst3DSource();
                        DirtySentinel(replacement, 0.59f);

                        manager.Tick(IntegrityRealtime(), 0f);

                        AssertSentinel(foreign, 0.57f);
                        AssertSentinel(replacement, 0.59f);
                        Assert.That(AudioListener.pause, Is.EqualTo(!listenerPause));
                        DisposeIntegrityManager(fixture, manager);
                    }
                }
                finally
                {
                    AudioListener.pause = listenerPause;
                    UnityEngine.Object.DestroyImmediate(foreignObject);
                }
            });
        }

        [Test]
        public void LiveIntegrity_StableTickAndLateTickAllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                var realtime = IntegrityRealtime();
                manager.Tick(realtime, 0f);
                manager.LateTick();

                var beforeTick = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.1d, 0.1f);
                var tickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeTick;
                var beforeLateTick = GC.GetAllocatedBytesForCurrentThread();
                manager.LateTick();
                var lateTickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeLateTick;

                Assert.That(tickBytes, Is.Zero);
                Assert.That(lateTickBytes, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void LiveIntegrity_FailingProbeAndFailCloseAllocateZeroBytes()
        {
            using (var warmFixture = new SessionFixture())
            {
                var warmManager = warmFixture.CreateReadyManager(out _);
                warmFixture.SetDuplicateSource();
                warmManager.Tick(IntegrityRealtime(), 0f);
                DisposeIntegrityManager(warmFixture, warmManager);
            }

            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                fixture.SetDuplicateSource();

                var before = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(IntegrityRealtime(), 0f);
                var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocatedBytes, Is.Zero);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.SourceBankDuplicateReference));
                DisposeIntegrityManager(fixture, manager);
            }
        }

        private static OrpheusAudioManager CreateBridgeBoundIntegrityManager(SessionFixture fixture)
        {
            var manager = fixture.CreateReadyManager(out _);
            Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
            Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);
            return manager;
        }

        private static void DisposeIntegrityManager(
            SessionFixture fixture,
            OrpheusAudioManager manager)
        {
            manager.Dispose();
            Assert.That(fixture.Host.Unbind(manager), Is.True);
        }

        private static void AssertBridgesUnbound(OrpheusAudioManager manager)
        {
            Assert.That(OrpheusAudioBridge.Unbind(manager), Is.False);
            Assert.That(OrpheusAudioRawBridge.Unbind(manager), Is.False);
        }

        private static void AssertIntegrityTerminalDiagnostics(
            OrpheusAudioDiagnostics before,
            OrpheusAudioManager manager,
            OrpheusAudioDisableReason reason,
            OrpheusAudioLifecycle lifecycle)
        {
            Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
            Assert.That(after.SchemaVersion, Is.EqualTo(1));
            Assert.That(after.Lifecycle, Is.EqualTo(lifecycle));
            Assert.That(after.DisableReason, Is.EqualTo(reason));
            Assert.That(
                after.TransportState,
                Is.EqualTo(
                    lifecycle == OrpheusAudioLifecycle.Disabled
                        ? OrpheusTransportState.Invalid
                        : OrpheusTransportState.Disposed));
            Assert.That(after.Readiness, Is.EqualTo(OrpheusReadiness.None));
            Assert.That(after.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.None));
            Assert.That(after.RecoveryPendingReasons, Is.EqualTo(OrpheusRecoveryPendingReason.None));
            Assert.That(after.DesiredBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(after.CurrentBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(after.TargetBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(after.DesiredProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(after.CurrentProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(after.TargetProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(after.Transient3DActiveCount, Is.Zero);
            Assert.That(after.Transient2DActiveCount, Is.Zero);
            Assert.That(after.FadingCount, Is.Zero);
            Assert.That(after.PendingCount, Is.Zero);
            Assert.That(after.BgmActiveCount, Is.Zero);
            Assert.That(after.ProfileAmbienceActiveCount, Is.Zero);
            Assert.That(after.GlobalLoopActiveCount, Is.Zero);
            Assert.That(after.ProfileId, Is.EqualTo(before.ProfileId));
            Assert.That(after.BaseState, Is.EqualTo(before.BaseState));
            Assert.That(after.Overlay, Is.EqualTo(before.Overlay));
            Assert.That(after.EffectiveSnapshot, Is.EqualTo(before.EffectiveSnapshot));
            Assert.That(after.ConfiguredRealVoiceLimit, Is.EqualTo(before.ConfiguredRealVoiceLimit));
            Assert.That(after.ConfiguredVirtualVoiceLimit, Is.EqualTo(before.ConfiguredVirtualVoiceLimit));
            Assert.That(after.VoiceBudgetDegraded, Is.EqualTo(before.VoiceBudgetDegraded));
            Assert.That(after.Reserved0, Is.Zero);
            Assert.That(after.Reserved1, Is.Zero);
            Assert.That(after.Reserved2, Is.Zero);
            AssertCountersEqual(before.Counters, after.Counters);
        }

        private static void AssertCountersEqual(
            OrpheusAudioDiagnosticsCounters expected,
            OrpheusAudioDiagnosticsCounters actual)
        {
            Assert.That(actual.PoolCapacityRejected, Is.EqualTo(expected.PoolCapacityRejected));
            Assert.That(actual.Stolen, Is.EqualTo(expected.Stolen));
            Assert.That(actual.CooldownRejected, Is.EqualTo(expected.CooldownRejected));
            Assert.That(actual.PolyphonyRejected, Is.EqualTo(expected.PolyphonyRejected));
            Assert.That(actual.DistanceRejected, Is.EqualTo(expected.DistanceRejected));
            Assert.That(actual.PreReadyRejected, Is.EqualTo(expected.PreReadyRejected));
            Assert.That(actual.SuspendedRejected, Is.EqualTo(expected.SuspendedRejected));
            Assert.That(actual.UnavailableRejected, Is.EqualTo(expected.UnavailableRejected));
            Assert.That(actual.WrongThreadRejected, Is.EqualTo(expected.WrongThreadRejected));
            Assert.That(actual.LoadNotReadyRejected, Is.EqualTo(expected.LoadNotReadyRejected));
            Assert.That(actual.LoadFailed, Is.EqualTo(expected.LoadFailed));
            Assert.That(actual.LoadStalled, Is.EqualTo(expected.LoadStalled));
            Assert.That(actual.InvalidKeyRejected, Is.EqualTo(expected.InvalidKeyRejected));
            Assert.That(actual.InvalidRawKeyRejected, Is.EqualTo(expected.InvalidRawKeyRejected));
            Assert.That(actual.InvalidPositionRejected, Is.EqualTo(expected.InvalidPositionRejected));
            Assert.That(actual.InvalidValueRejected, Is.EqualTo(expected.InvalidValueRejected));
            Assert.That(actual.PlaybackKindRejected, Is.EqualTo(expected.PlaybackKindRejected));
            Assert.That(actual.LoopRegistryFull, Is.EqualTo(expected.LoopRegistryFull));
            Assert.That(actual.SetFloatFailed, Is.EqualTo(expected.SetFloatFailed));
            Assert.That(actual.RecoveryFailed, Is.EqualTo(expected.RecoveryFailed));
            Assert.That(actual.StaleBootstrapTokenRejected, Is.EqualTo(expected.StaleBootstrapTokenRejected));
            Assert.That(actual.UnexpectedException, Is.EqualTo(expected.UnexpectedException));
        }

        private static void AssertAllNormalized(AudioSource[] sources, int firstIndex)
        {
            for (var sourceIndex = firstIndex; sourceIndex < sources.Length; sourceIndex++)
            {
                AssertNormalized(sources[sourceIndex]);
            }
        }

        private static void DirtySentinel(AudioSource source, float volume)
        {
            source.loop = true;
            source.mute = true;
            source.volume = volume;
            source.pitch = 0.61f;
        }

        private static void AssertSentinel(AudioSource source, float volume)
        {
            Assert.That(source.loop, Is.True);
            Assert.That(source.mute, Is.True);
            Assert.That(source.volume, Is.EqualTo(volume));
            Assert.That(source.pitch, Is.EqualTo(0.61f));
        }

        private static double IntegrityRealtime()
        {
            return Time.realtimeSinceStartupAsDouble + 0.1d;
        }

    }
}
