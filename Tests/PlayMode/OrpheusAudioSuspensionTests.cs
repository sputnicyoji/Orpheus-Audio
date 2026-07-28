using System;
using System.Threading;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Tests
{
    public sealed partial class OrpheusAudioSessionTests
    {
        [Test]
        public void InitialFocusAndListenerAvailability_DetermineTransportWithoutFirstFrameCallbacks()
        {
            using (var unfocused = new SessionFixture())
            {
                SetField(unfocused.Host, "_hasStarted", true);
                Assert.That(unfocused.CreateWithInitialFocus(false, out var manager).Success, Is.True);
                Assert.That(unfocused.Host.Bind(manager), Is.True);

                Assert.That(manager.TryGetDiagnostics(out var initial), Is.True);
                Assert.That(initial.TransportState, Is.EqualTo(OrpheusTransportState.Suspended));
                Assert.That(
                    initial.SuspensionReasons,
                    Is.EqualTo(OrpheusSuspensionReason.FocusLost | OrpheusSuspensionReason.ListenerMissing));
                Assert.That(
                    initial.SuspensionReasons & OrpheusSuspensionReason.ApplicationPaused,
                    Is.EqualTo(OrpheusSuspensionReason.None));

                var listener = CreateListener(unfocused, "Issue15UnfocusedListener");
                Assert.That(manager.BindListener(listener), Is.True);
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                Assert.That(
                    manager.CompleteBootstrapHydration(manager.CaptureBootstrapAuthority()),
                    Is.True);

                Assert.That(manager.TryGetDiagnostics(out var barrierReady), Is.True);
                Assert.That(barrierReady.ActivationReady, Is.True);
                Assert.That(barrierReady.PlaybackReady, Is.False);
                Assert.That(
                    barrierReady.SuspensionReasons,
                    Is.EqualTo(OrpheusSuspensionReason.FocusLost));

                unfocused.ResetMixerCalls();
                unfocused.Host.HandleApplicationFocusChanged(true);

                Assert.That(manager.TryGetDiagnostics(out var resumed), Is.True);
                Assert.That(resumed.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(resumed.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.None));
                Assert.That(resumed.PlaybackReady, Is.True);
                Assert.That(unfocused.Mixer.Snapshots, Is.EqualTo(new[] { OrpheusEffectiveSnapshot.Peace }));
                Assert.That(unfocused.Mixer.TransitionSeconds, Is.EqualTo(new[] { 0f }));
                DisposeBound(unfocused, manager);
            }

            using (var focused = new SessionFixture())
            {
                var manager = focused.CreateReadyManager(out _);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(diagnostics.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.None));
                Assert.That(diagnostics.PlaybackReady, Is.True);
                DisposeBound(focused, manager);
            }
        }

        [Test]
        public void SuspensionReasons_AreIdempotentAndRemainingReasonPreventsResume()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(410);
                fixture.SetEvents(loop);
                var manager = fixture.CreateReadyManager(out var listener);
                manager.PlayLoop(new OrpheusAudioKey(410));
                manager.Tick(Time.realtimeSinceStartupAsDouble + 0.1d, 0f);
                var loopSource = fixture.FindGlobalLoopSource(loop.GetClip(0));
                fixture.ResetMixerCalls();

                fixture.Host.HandleApplicationFocusChanged(false);
                AssertSuspension(manager, OrpheusSuspensionReason.FocusLost);
                var retainedClip = loopSource.clip;
                var retainedVolume = loopSource.volume;

                fixture.Host.HandleApplicationFocusChanged(false);
                AssertSuspension(manager, OrpheusSuspensionReason.FocusLost);
                Assert.That(loopSource.clip, Is.SameAs(retainedClip));
                Assert.That(loopSource.volume, Is.EqualTo(retainedVolume));

                fixture.Host.HandleApplicationPauseChanged(true);
                AssertSuspension(
                    manager,
                    OrpheusSuspensionReason.FocusLost | OrpheusSuspensionReason.ApplicationPaused);
                fixture.Host.HandleApplicationPauseChanged(true);
                AssertSuspension(
                    manager,
                    OrpheusSuspensionReason.FocusLost | OrpheusSuspensionReason.ApplicationPaused);

                fixture.Host.HandleApplicationFocusChanged(true);
                AssertSuspension(manager, OrpheusSuspensionReason.ApplicationPaused);
                Assert.That(manager.RemoveListener(listener), Is.True);
                AssertSuspension(
                    manager,
                    OrpheusSuspensionReason.ApplicationPaused | OrpheusSuspensionReason.ListenerMissing);

                fixture.Host.HandleApplicationPauseChanged(false);
                AssertSuspension(manager, OrpheusSuspensionReason.ListenerMissing);
                Assert.That(fixture.Mixer.Snapshots, Is.Empty);

                Assert.That(manager.BindListener(listener), Is.True);
                Assert.That(manager.TryGetDiagnostics(out var active), Is.True);
                Assert.That(active.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(active.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.None));
                Assert.That(active.PlaybackReady, Is.True);
                Assert.That(fixture.Mixer.Snapshots.Count, Is.EqualTo(1));
                Assert.That(fixture.Mixer.TransitionSeconds[0], Is.EqualTo(0f));

                fixture.Host.HandleApplicationPauseChanged(false);
                fixture.Host.HandleApplicationFocusChanged(true);
                Assert.That(fixture.Mixer.Snapshots.Count, Is.EqualTo(1));
                Assert.That(loopSource.clip, Is.SameAs(retainedClip));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void AndroidObservedBackgroundSequences_ResumeOnlyAfterApplicationPauseClears()
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                Assert.That(manager.TryGetEvidenceState(out var initial), Is.True);
                Assert.That(initial.RecoveryGeneration, Is.Zero);

                manager.HandleRuntimeHostSuspensionInput(
                    fixture.Host,
                    OrpheusSuspensionReason.ApplicationPaused,
                    true,
                    true);
                AssertSuspension(manager, OrpheusSuspensionReason.ApplicationPaused);
                manager.HandleRuntimeHostSuspensionInput(
                    fixture.Host,
                    OrpheusSuspensionReason.ApplicationPaused,
                    false,
                    true);

                Assert.That(manager.TryGetDiagnostics(out var keyboardHomeResume), Is.True);
                Assert.That(
                    keyboardHomeResume.TransportState,
                    Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(
                    keyboardHomeResume.SuspensionReasons,
                    Is.EqualTo(OrpheusSuspensionReason.None));

                manager.HandleRuntimeHostSuspensionInput(
                    fixture.Host,
                    OrpheusSuspensionReason.ApplicationPaused,
                    true,
                    true);
                manager.HandleRuntimeHostSuspensionInput(
                    fixture.Host,
                    OrpheusSuspensionReason.FocusLost,
                    true,
                    true);
                manager.HandleRuntimeHostSuspensionInput(
                    fixture.Host,
                    OrpheusSuspensionReason.FocusLost,
                    false,
                    true);
                AssertSuspension(manager, OrpheusSuspensionReason.ApplicationPaused);
                manager.HandleRuntimeHostSuspensionInput(
                    fixture.Host,
                    OrpheusSuspensionReason.ApplicationPaused,
                    false,
                    true);

                Assert.That(manager.TryGetDiagnostics(out var ordinaryHomeResume), Is.True);
                Assert.That(
                    ordinaryHomeResume.TransportState,
                    Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(
                    ordinaryHomeResume.SuspensionReasons,
                    Is.EqualTo(OrpheusSuspensionReason.None));
                Assert.That(manager.TryGetEvidenceState(out var finalEvidence), Is.True);
                Assert.That(finalEvidence.RecoveryGeneration, Is.Zero);
                Assert.That(fixture.AudioSystem.ResetCallCount, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void FocusAndPauseWrongThread_OnlyIncrementWrongThreadRejectedAndNeverQueueWork()
        {
            using (var fixture = new SessionFixture())
            {
                var oneShot = fixture.CreatePlaybackEvent(420);
                var bgm = fixture.CreateBgmEvent(421);
                var ambience = fixture.CreateProfileAmbienceEvent(422);
                var loop = fixture.CreateGlobalLoopEvent(423);
                fixture.SetEvents(oneShot, bgm, ambience, loop);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.ApplyProfile(PersistentIntent(1, OrpheusBaseState.Peace, 421, 422));
                manager.PlayLoop(new OrpheusAudioKey(423));
                manager.Play(new OrpheusAudioKey(420));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                fixture.Readiness.ResetCalls();
                fixture.ResetMixerCalls();
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                PauseOccupiedSources(fixture.Sources);
                var sourceStates = CaptureSources(fixture.Sources);
                Exception workerException = null;

                var thread = new Thread(() =>
                {
                    try
                    {
                        fixture.Host.HandleApplicationFocusChanged(false);
                        fixture.Host.HandleApplicationPauseChanged(true);
                    }
                    catch (Exception exception)
                    {
                        workerException = exception;
                    }
                });
                thread.Start();
                thread.Join();

                Assert.That(workerException, Is.Null);
                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                AssertDiagnosticsEqualExceptWrongThread(before, after);
                Assert.That(
                    after.Counters.WrongThreadRejected,
                    Is.EqualTo(before.Counters.WrongThreadRejected + 2));
                Assert.That(after.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(after.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.None));
                AssertSourcesEqual(sourceStates, fixture.Sources);
                Assert.That(fixture.Readiness.GetCallCount, Is.Zero);
                Assert.That(fixture.Readiness.RequestCallCount, Is.Zero);
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);

                manager.Tick(realtime + 0.5d, 0.1f);
                Assert.That(manager.TryGetDiagnostics(out var afterTick), Is.True);
                Assert.That(afterTick.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(afterTick.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.None));
                DisposeBound(fixture, manager);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TransportSuspension_ChangesOnlyCapturedOwnedSourcesAndNeverWritesAudioListenerPause(
            bool listenerPauseSentinel)
        {
            var previousPause = AudioListener.pause;
            var foreignObject = new GameObject("Issue15ForeignAudioSource");
            var foreignClip = AudioClip.Create("Issue15ForeignClip", 48000, 1, 48000, false);
            try
            {
                using (var fixture = new SessionFixture())
                {
                    var oneShot = fixture.CreatePlaybackEvent(430);
                    var bgm = fixture.CreateBgmEvent(431);
                    var ambience = fixture.CreateProfileAmbienceEvent(432);
                    var loop = fixture.CreateGlobalLoopEvent(433);
                    fixture.SetEvents(oneShot, bgm, ambience, loop);
                    var manager = fixture.CreateReadyManager(out _);
                    manager.ApplyProfile(PersistentIntent(1, OrpheusBaseState.Peace, 431, 432));
                    manager.PlayLoop(new OrpheusAudioKey(433));
                    manager.Play(new OrpheusAudioKey(430));
                    var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                    manager.Tick(realtime, 0f);
                    manager.Tick(realtime + 0.4d, 0.4f);

                    var foreign = foreignObject.AddComponent<AudioSource>();
                    foreign.clip = foreignClip;
                    foreign.volume = 0.37f;
                    foreign.pitch = 0.83f;
                    foreign.loop = true;
                    foreign.spatialBlend = 0.25f;
                    foreign.timeSamples = 137;
                    AudioListener.pause = listenerPauseSentinel;

                    fixture.Host.HandleApplicationFocusChanged(false);

                    Assert.That(AudioListener.pause, Is.EqualTo(listenerPauseSentinel));
                    Assert.That(foreign.clip, Is.SameAs(foreignClip));
                    Assert.That(foreign.volume, Is.EqualTo(0.37f));
                    Assert.That(foreign.pitch, Is.EqualTo(0.83f));
                    Assert.That(foreign.loop, Is.True);
                    Assert.That(foreign.spatialBlend, Is.EqualTo(0.25f));
                    Assert.That(foreign.timeSamples, Is.EqualTo(137));
                    Assert.That(manager.TryGetDiagnostics(out var suspended), Is.True);
                    Assert.That(suspended.TransportState, Is.EqualTo(OrpheusTransportState.Suspended));
                    Assert.That(suspended.Transient2DActiveCount, Is.Zero);
                    Assert.That(suspended.BgmActiveCount, Is.EqualTo(1));
                    Assert.That(suspended.ProfileAmbienceActiveCount, Is.EqualTo(1));
                    Assert.That(suspended.GlobalLoopActiveCount, Is.EqualTo(1));
                    DisposeBound(fixture, manager);
                }
            }
            finally
            {
                AudioListener.pause = previousPause;
                UnityEngine.Object.DestroyImmediate(foreignObject);
                UnityEngine.Object.DestroyImmediate(foreignClip);
            }
        }

        [Test]
        public void EnteringSuspended_AtomicallyClearsPlayingFadingAndPendingTransientsAndNeverReplaysThem()
        {
            using (var fixture = new SessionFixture())
            {
                var victim = fixture.CreatePlaybackEvent(
                    440,
                    volume: 0.8f,
                    priority: 200,
                    polyphonyCap: 4);
                var pending = fixture.CreatePlaybackEvent(441, priority: 100);
                var spatial = fixture.CreateSpatialPlaybackEvent(442);
                var fresh = fixture.CreatePlaybackEvent(443);
                fixture.SetEvents(victim, pending, spatial, fresh);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.Tick(realtime, 0f);
                for (var index = 0; index < 4; index++)
                {
                    manager.Play(new OrpheusAudioKey(440));
                }
                manager.PlayAt(new OrpheusAudioKey(442), Vector3.zero);
                manager.Tick(realtime + 0.05d, 0.05f);
                manager.Play(new OrpheusAudioKey(441));

                Assert.That(manager.TryGetDiagnostics(out var populated), Is.True);
                Assert.That(populated.Transient3DActiveCount, Is.EqualTo(1));
                Assert.That(populated.Transient2DActiveCount, Is.EqualTo(4));
                Assert.That(populated.FadingCount, Is.EqualTo(1));
                Assert.That(populated.PendingCount, Is.EqualTo(1));

                fixture.Host.HandleApplicationFocusChanged(false);

                AssertTransientStateEmpty(manager, fixture);
                var getCalls = fixture.Readiness.GetCallCount;
                var requestCalls = fixture.Readiness.RequestCallCount;
                Assert.That(manager.TryGetDiagnostics(out var beforeRejected), Is.True);
                manager.Play(new OrpheusAudioKey(443));
                Assert.That(manager.TryGetDiagnostics(out var afterRejected), Is.True);
                Assert.That(
                    afterRejected.Counters.SuspendedRejected,
                    Is.EqualTo(beforeRejected.Counters.SuspendedRejected + 1));
                Assert.That(fixture.Readiness.GetCallCount, Is.EqualTo(getCalls));
                Assert.That(fixture.Readiness.RequestCallCount, Is.EqualTo(requestCalls));

                manager.Tick(realtime + 1d, 1f);
                fixture.Host.HandleApplicationFocusChanged(true);
                manager.Tick(realtime + 2d, 1f);
                manager.Tick(realtime + 3d, 1f);
                AssertTransientStateEmpty(manager, fixture);

                manager.Play(new OrpheusAudioKey(443));
                Assert.That(manager.TryGetDiagnostics(out var accepted), Is.True);
                Assert.That(accepted.Transient2DActiveCount, Is.EqualTo(1));
                Assert.That(fixture.Sources[12].clip, Is.SameAs(fresh.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Suspension_NormalizesBothDirectorsToDeterministicSurvivorsAndResumesPersistentSourcesWithoutReplay()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetProfileDurations(0.8f, 0.4f, 0.2f);
                var bgmA = fixture.CreateBgmEvent(450);
                var bgmB = fixture.CreateBgmEvent(451);
                var ambienceX = fixture.CreateProfileAmbienceEvent(452);
                var ambienceY = fixture.CreateProfileAmbienceEvent(453);
                var loopOne = fixture.CreateGlobalLoopEvent(454);
                var loopTwo = fixture.CreateGlobalLoopEvent(455);
                fixture.SetEvents(bgmA, bgmB, ambienceX, ambienceY, loopOne, loopTwo);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(PersistentIntent(1, OrpheusBaseState.Peace, 450, 452));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.8d, 0.8f);
                manager.PlayLoop(new OrpheusAudioKey(454));
                manager.PlayLoop(new OrpheusAudioKey(455));
                manager.ApplyProfile(PersistentIntent(2, OrpheusBaseState.Peace, 451, 453));
                manager.Tick(realtime + 0.81d, 0.01f);
                manager.Tick(realtime + 1.11d, 0.3f);

                var loopOneSource = fixture.FindGlobalLoopSource(loopOne.GetClip(0));
                var loopTwoSource = fixture.FindGlobalLoopSource(loopTwo.GetClip(0));
                fixture.BgmZero.timeSamples = 1000;
                fixture.ProfileAmbienceOne.timeSamples = 2000;
                loopOneSource.timeSamples = 3000;
                loopTwoSource.timeSamples = 4000;
                fixture.ResetMixerCalls();

                fixture.Host.HandleApplicationFocusChanged(false);

                Assert.That(manager.TryGetDiagnostics(out var suspended), Is.True);
                Assert.That(suspended.DesiredBgmKey, Is.EqualTo(new OrpheusAudioKey(451)));
                Assert.That(suspended.CurrentBgmKey, Is.EqualTo(new OrpheusAudioKey(450)));
                Assert.That(suspended.TargetBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(suspended.BgmActiveCount, Is.EqualTo(1));
                Assert.That(suspended.DesiredProfileAmbienceKey, Is.EqualTo(new OrpheusAudioKey(453)));
                Assert.That(suspended.CurrentProfileAmbienceKey, Is.EqualTo(new OrpheusAudioKey(453)));
                Assert.That(suspended.TargetProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(suspended.ProfileAmbienceActiveCount, Is.EqualTo(1));
                Assert.That(suspended.GlobalLoopActiveCount, Is.EqualTo(2));
                Assert.That(fixture.BgmZero.clip, Is.SameAs(bgmA.GetClip(0)));
                AssertNormalized(fixture.BgmOne);
                AssertNormalized(fixture.ProfileAmbienceZero);
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.SameAs(ambienceY.GetClip(0)));
                Assert.That(fixture.BgmZero.timeSamples, Is.EqualTo(1000));
                Assert.That(fixture.ProfileAmbienceOne.timeSamples, Is.EqualTo(2000));
                Assert.That(loopOneSource.timeSamples, Is.EqualTo(3000));
                Assert.That(loopTwoSource.timeSamples, Is.EqualTo(4000));

                fixture.Host.HandleApplicationFocusChanged(false);
                Assert.That(fixture.BgmZero.timeSamples, Is.EqualTo(1000));
                Assert.That(fixture.ProfileAmbienceOne.timeSamples, Is.EqualTo(2000));

                fixture.Host.HandleApplicationFocusChanged(true);

                Assert.That(fixture.Mixer.Snapshots, Is.EqualTo(new[] { OrpheusEffectiveSnapshot.Peace }));
                Assert.That(fixture.Mixer.TransitionSeconds, Is.EqualTo(new[] { 0f }));
                Assert.That(manager.TryGetDiagnostics(out var resumed), Is.True);
                Assert.That(resumed.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(resumed.BgmActiveCount, Is.EqualTo(1));
                Assert.That(resumed.TargetBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(resumed.ProfileAmbienceActiveCount, Is.EqualTo(1));
                Assert.That(resumed.CurrentProfileAmbienceKey, Is.EqualTo(new OrpheusAudioKey(453)));
                Assert.That(fixture.BgmZero.timeSamples, Is.EqualTo(1000));
                Assert.That(fixture.ProfileAmbienceOne.timeSamples, Is.EqualTo(2000));
                Assert.That(loopOneSource.timeSamples, Is.EqualTo(3000));
                Assert.That(loopTwoSource.timeSamples, Is.EqualTo(4000));

                manager.Tick(realtime + 2d, 0f);
                Assert.That(manager.TryGetDiagnostics(out var firstActive), Is.True);
                Assert.That(firstActive.BgmActiveCount, Is.EqualTo(2));
                Assert.That(firstActive.TargetBgmKey, Is.EqualTo(new OrpheusAudioKey(451)));
                Assert.That(firstActive.ProfileAmbienceActiveCount, Is.EqualTo(1));
                Assert.That(fixture.BgmZero.timeSamples, Is.EqualTo(1000));
                Assert.That(fixture.ProfileAmbienceOne.timeSamples, Is.EqualTo(2000));
                Assert.That(loopOneSource.timeSamples, Is.EqualTo(3000));
                Assert.That(loopTwoSource.timeSamples, Is.EqualTo(4000));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void SuspendedIntentUpdatesLatestProfileOverlayAndLoopsWithoutStartingNewPersistentSources()
        {
            using (var fixture = new SessionFixture())
            {
                var bgmA = fixture.CreateBgmEvent(460);
                var ambienceX = fixture.CreateProfileAmbienceEvent(461);
                var loopOne = fixture.CreateGlobalLoopEvent(462);
                var bgmC = fixture.CreateBgmEvent(463);
                var ambienceZ = fixture.CreateProfileAmbienceEvent(464);
                var loopTwo = fixture.CreateGlobalLoopEvent(465);
                fixture.SetEvents(bgmA, ambienceX, loopOne, bgmC, ambienceZ, loopTwo);
                fixture.SetClipState(3, OrpheusClipLoadState.Unloaded);
                fixture.SetClipState(4, OrpheusClipLoadState.Unloaded);
                fixture.SetClipState(5, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.ApplyProfile(PersistentIntent(1, OrpheusBaseState.Peace, 460, 461));
                manager.PlayLoop(new OrpheusAudioKey(462));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                fixture.Host.HandleApplicationPauseChanged(true);

                var latest = PersistentIntent(9, OrpheusBaseState.Combat, 463, 464);
                manager.ApplyProfile(latest);
                manager.SetOverlay(OrpheusOverlay.Pause, true);
                manager.PlayLoop(new OrpheusAudioKey(465));
                manager.StopLoop(new OrpheusAudioKey(462));
                manager.ApplyProfile(latest);
                manager.PlayLoop(new OrpheusAudioKey(465));

                Assert.That(manager.TryGetDiagnostics(out var intent), Is.True);
                Assert.That(intent.ProfileId, Is.EqualTo(9));
                Assert.That(intent.BaseState, Is.EqualTo(OrpheusBaseState.Combat));
                Assert.That(intent.Overlay, Is.EqualTo(OrpheusOverlay.Pause));
                Assert.That(intent.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Pause));
                Assert.That(intent.DesiredBgmKey, Is.EqualTo(new OrpheusAudioKey(463)));
                Assert.That(intent.DesiredProfileAmbienceKey, Is.EqualTo(new OrpheusAudioKey(464)));
                Assert.That(fixture.GetClipRequestCount(3), Is.EqualTo(1));
                Assert.That(fixture.GetClipRequestCount(4), Is.EqualTo(1));
                Assert.That(fixture.GetClipRequestCount(5), Is.EqualTo(1));

                fixture.SetClipState(3, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(4, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(5, OrpheusClipLoadState.Loaded);
                manager.Tick(realtime + 1d, 0.6f);
                manager.Tick(realtime + 2d, 1f);
                Assert.That(FindSourceWithClip(fixture.Sources, bgmC.GetClip(0)), Is.Null);
                Assert.That(FindSourceWithClip(fixture.Sources, ambienceZ.GetClip(0)), Is.Null);
                Assert.That(FindSourceWithClip(fixture.Sources, loopTwo.GetClip(0)), Is.Null);

                fixture.ResetMixerCalls();
                fixture.Host.HandleApplicationPauseChanged(false);

                Assert.That(fixture.Mixer.Snapshots, Is.EqualTo(new[] { OrpheusEffectiveSnapshot.Pause }));
                Assert.That(fixture.Mixer.TransitionSeconds, Is.EqualTo(new[] { 0f }));
                Assert.That(FindSourceWithClip(fixture.Sources, bgmC.GetClip(0)), Is.Null);
                Assert.That(FindSourceWithClip(fixture.Sources, ambienceZ.GetClip(0)), Is.Null);
                Assert.That(FindSourceWithClip(fixture.Sources, loopTwo.GetClip(0)), Is.Null);
                Assert.That(FindSourceWithClip(fixture.Sources, loopOne.GetClip(0)), Is.Not.Null);

                manager.Tick(realtime + 3d, 0f);
                Assert.That(CountSourcesWithClip(fixture.Sources, bgmC.GetClip(0)), Is.EqualTo(1));
                Assert.That(CountSourcesWithClip(fixture.Sources, ambienceZ.GetClip(0)), Is.EqualTo(1));
                Assert.That(CountSourcesWithClip(fixture.Sources, loopTwo.GetClip(0)), Is.EqualTo(1));
                Assert.That(CountSourcesWithClip(fixture.Sources, loopOne.GetClip(0)), Is.EqualTo(1));
                fixture.Host.HandleApplicationPauseChanged(false);
                Assert.That(CountSourcesWithClip(fixture.Sources, bgmC.GetClip(0)), Is.EqualTo(1));
                Assert.That(CountSourcesWithClip(fixture.Sources, ambienceZ.GetClip(0)), Is.EqualTo(1));
                Assert.That(CountSourcesWithClip(fixture.Sources, loopTwo.GetClip(0)), Is.EqualTo(1));
                manager.Tick(realtime + 3.015d, 0.015f);
                Assert.That(FindSourceWithClip(fixture.Sources, loopOne.GetClip(0)), Is.Null);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Suspension_FreezesPersistentFadeAndFirstResumeTickConsumesZeroActiveDelta()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(470, volume: 0.8f);
                fixture.SetEvents(loop);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.Tick(realtime, 0f);
                manager.PlayLoop(new OrpheusAudioKey(470));
                manager.Tick(realtime + 0.001d, 0f);
                var source = fixture.FindGlobalLoopSource(loop.GetClip(0));
                manager.StopLoop(new OrpheusAudioKey(470));
                manager.Tick(realtime + 0.0075d, 0.0075f);
                var halfVolume = source.volume;
                Assert.That(halfVolume, Is.EqualTo(0.4f).Within(0.0001f));

                fixture.Host.HandleApplicationFocusChanged(false);
                manager.Tick(realtime + 1d, 1f);
                Assert.That(source.clip, Is.SameAs(loop.GetClip(0)));
                Assert.That(source.volume, Is.EqualTo(halfVolume));

                fixture.Host.HandleApplicationFocusChanged(true);
                manager.Tick(realtime + 2d, 1f);
                Assert.That(source.clip, Is.SameAs(loop.GetClip(0)));
                Assert.That(source.volume, Is.EqualTo(halfVolume));
                Assert.That(manager.TryGetDiagnostics(out var firstActive), Is.True);
                Assert.That(firstActive.GlobalLoopActiveCount, Is.EqualTo(1));

                manager.Tick(realtime + 2.0075d, 0.0075f);
                AssertNormalized(source);
                Assert.That(manager.TryGetDiagnostics(out var completed), Is.True);
                Assert.That(completed.GlobalLoopActiveCount, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Suspension_FreezesPlaybackTimeButContinuesCooldownAndLoadStallRealtime()
        {
            using (var fixture = new SessionFixture())
            {
                var cooldown = fixture.CreatePlaybackEvent(480, cooldownSeconds: 2f);
                var loadingBgm = fixture.CreateBgmEvent(481);
                fixture.SetEvents(cooldown, loadingBgm);
                fixture.SetClipState(1, OrpheusClipLoadState.Loading);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.Tick(realtime, 0f);
                manager.Play(new OrpheusAudioKey(480));
                manager.ApplyProfile(BgmIntent(1, 481));
                fixture.Host.HandleApplicationFocusChanged(false);

                manager.Tick(realtime + 2.1d, 2.1f);

                Assert.That(manager.TryGetDiagnostics(out var suspended), Is.True);
                Assert.That(suspended.TransportState, Is.EqualTo(OrpheusTransportState.Suspended));
                Assert.That(suspended.Counters.LoadStalled, Is.EqualTo(1));
                Assert.That(suspended.BgmActiveCount, Is.Zero);
                fixture.Host.HandleApplicationFocusChanged(true);
                manager.Play(new OrpheusAudioKey(480));
                Assert.That(manager.TryGetDiagnostics(out var resumed), Is.True);
                Assert.That(resumed.Transient2DActiveCount, Is.EqualTo(1));
                Assert.That(resumed.Counters.CooldownRejected, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PauseOverlay_DoesNotSuspendLoadedUiOneShotOrClearOwnedPlayback()
        {
            using (var fixture = new SessionFixture())
            {
                var ui = fixture.CreatePlaybackEvent(490);
                fixture.SetEvents(ui);
                var manager = fixture.CreateReadyManager(out _);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                manager.SetOverlay(OrpheusOverlay.Pause, true);
                manager.Play(new OrpheusAudioKey(490));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Pause));
                Assert.That(diagnostics.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(diagnostics.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.None));
                Assert.That(diagnostics.PlaybackReady, Is.True);
                Assert.That(
                    diagnostics.Counters.SuspendedRejected,
                    Is.EqualTo(before.Counters.SuspendedRejected));
                Assert.That(diagnostics.Transient2DActiveCount, Is.EqualTo(1));
                Assert.That(fixture.Sources[12].outputAudioMixerGroup.name, Is.EqualTo("SFX_UI_State"));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void BindListener_WhenResumeFails_DoesNotReportCommittedBinding()
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out var listener);
                Assert.That(manager.RemoveListener(listener), Is.True);
                fixture.Mixer.TransitionFailureReason =
                    OrpheusAudioDisableReason.SnapshotReferenceInvalid;

                Assert.That(manager.BindListener(listener), Is.False);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.SnapshotReferenceInvalid));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ReplaceListener_WhenResumeFails_DoesNotReportCommittedBinding()
        {
            var replacementObject = new GameObject("Issue15ReplacementListener");
            try
            {
                using (var fixture = new SessionFixture())
                {
                    var manager = fixture.CreateReadyManager(out var listener);
                    listener.enabled = false;
                    manager.LateTick();
                    var replacement = replacementObject.AddComponent<AudioListener>();
                    fixture.Mixer.TransitionFailureReason =
                        OrpheusAudioDisableReason.SnapshotReferenceInvalid;

                    Assert.That(manager.ReplaceListener(listener, replacement), Is.False);
                    Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                    Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                    Assert.That(
                        diagnostics.DisableReason,
                        Is.EqualTo(OrpheusAudioDisableReason.SnapshotReferenceInvalid));
                    DisposeBound(fixture, manager);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(replacementObject);
            }
        }

        [Test]
        public void FocusPauseSuspensionResumeAndFrozenTicks_AllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                var bgm = fixture.CreateBgmEvent(500);
                var ambience = fixture.CreateProfileAmbienceEvent(501);
                var loop = fixture.CreateGlobalLoopEvent(502);
                var oneShot = fixture.CreatePlaybackEvent(503);
                fixture.SetEvents(bgm, ambience, loop, oneShot);
                var manager = fixture.CreateReadyManager(out _);
                var intent = PersistentIntent(1, OrpheusBaseState.Peace, 500, 501);
                var loopKey = new OrpheusAudioKey(502);
                var oneShotKey = new OrpheusAudioKey(503);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.ApplyProfile(intent);
                manager.PlayLoop(loopKey);
                manager.Play(oneShotKey);
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);

                fixture.Host.HandleApplicationFocusChanged(false);
                fixture.Host.HandleApplicationFocusChanged(true);
                fixture.Host.HandleApplicationPauseChanged(true);
                manager.Tick(realtime + 0.5d, 0f);
                fixture.Host.HandleApplicationPauseChanged(false);
                fixture.Mixer.SuppressOperationOrder = true;
                fixture.ResetMixerCalls();
                manager.Play(oneShotKey);

                var beforeEnter = GC.GetAllocatedBytesForCurrentThread();
                fixture.Host.HandleApplicationFocusChanged(false);
                var enterBytes = GC.GetAllocatedBytesForCurrentThread() - beforeEnter;
                var beforeDuplicate = GC.GetAllocatedBytesForCurrentThread();
                fixture.Host.HandleApplicationFocusChanged(false);
                var duplicateBytes = GC.GetAllocatedBytesForCurrentThread() - beforeDuplicate;
                var beforeCombined = GC.GetAllocatedBytesForCurrentThread();
                fixture.Host.HandleApplicationPauseChanged(true);
                fixture.Host.HandleApplicationFocusChanged(true);
                var combinedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeCombined;
                var beforeFrozen = GC.GetAllocatedBytesForCurrentThread();
                manager.ApplyProfile(intent);
                manager.PlayLoop(loopKey);
                manager.Tick(realtime + 1d, 1f);
                var frozenBytes = GC.GetAllocatedBytesForCurrentThread() - beforeFrozen;
                var beforeResume = GC.GetAllocatedBytesForCurrentThread();
                fixture.Host.HandleApplicationPauseChanged(false);
                var resumeBytes = GC.GetAllocatedBytesForCurrentThread() - beforeResume;
                var beforeFirstTick = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 2d, 1f);
                var firstTickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeFirstTick;
                var beforeDuplicateResume = GC.GetAllocatedBytesForCurrentThread();
                fixture.Host.HandleApplicationPauseChanged(false);
                var duplicateResumeBytes = GC.GetAllocatedBytesForCurrentThread() - beforeDuplicateResume;
                var beforeAndroidEnter = GC.GetAllocatedBytesForCurrentThread();
                manager.HandleRuntimeHostSuspensionInput(
                    fixture.Host,
                    OrpheusSuspensionReason.ApplicationPaused,
                    true,
                    true);
                var androidEnterBytes =
                    GC.GetAllocatedBytesForCurrentThread() - beforeAndroidEnter;
                var beforeAndroidExit = GC.GetAllocatedBytesForCurrentThread();
                manager.HandleRuntimeHostSuspensionInput(
                    fixture.Host,
                    OrpheusSuspensionReason.ApplicationPaused,
                    false,
                    true);
                var androidExitBytes =
                    GC.GetAllocatedBytesForCurrentThread() - beforeAndroidExit;

                Assert.That(enterBytes, Is.Zero);
                Assert.That(duplicateBytes, Is.Zero);
                Assert.That(combinedBytes, Is.Zero);
                Assert.That(frozenBytes, Is.Zero);
                Assert.That(resumeBytes, Is.Zero);
                Assert.That(firstTickBytes, Is.Zero);
                Assert.That(duplicateResumeBytes, Is.Zero);
                Assert.That(androidEnterBytes, Is.Zero);
                Assert.That(androidExitBytes, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        private static void AssertSuspension(
            OrpheusAudioManager manager,
            OrpheusSuspensionReason expectedReasons)
        {
            Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
            Assert.That(diagnostics.TransportState, Is.EqualTo(OrpheusTransportState.Suspended));
            Assert.That(diagnostics.SuspensionReasons, Is.EqualTo(expectedReasons));
            Assert.That(diagnostics.PlaybackReady, Is.False);
        }

        private static void AssertTransientStateEmpty(
            OrpheusAudioManager manager,
            SessionFixture fixture)
        {
            Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
            Assert.That(diagnostics.Transient3DActiveCount, Is.Zero);
            Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
            Assert.That(diagnostics.FadingCount, Is.Zero);
            Assert.That(diagnostics.PendingCount, Is.Zero);
            for (var sourceIndex = 0; sourceIndex < 16; sourceIndex++)
            {
                AssertNormalized(fixture.Sources[sourceIndex]);
            }
        }

        private static SourceState[] CaptureSources(AudioSource[] sources)
        {
            var states = new SourceState[sources.Length];
            for (var index = 0; index < sources.Length; index++)
            {
                states[index] = new SourceState(sources[index]);
            }
            return states;
        }

        private static void PauseOccupiedSources(AudioSource[] sources)
        {
            for (var index = 0; index < sources.Length; index++)
            {
                if (sources[index].clip != null)
                {
                    sources[index].Pause();
                }
            }
        }

        private static void AssertSourcesEqual(SourceState[] expected, AudioSource[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (var index = 0; index < expected.Length; index++)
            {
                expected[index].AssertMatches(actual[index]);
            }
        }

        private static void AssertSourcesResumedWithoutRestart(
            SourceState[] expected,
            AudioSource[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (var index = 0; index < expected.Length; index++)
            {
                expected[index].AssertResumedWithoutRestart(actual[index]);
            }
        }

        private static AudioSource FindSourceWithClip(AudioSource[] sources, AudioClip clip)
        {
            for (var index = 0; index < sources.Length; index++)
            {
                if (ReferenceEquals(sources[index].clip, clip))
                {
                    return sources[index];
                }
            }
            return null;
        }

        private static int CountSourcesWithClip(AudioSource[] sources, AudioClip clip)
        {
            var count = 0;
            for (var index = 0; index < sources.Length; index++)
            {
                if (ReferenceEquals(sources[index].clip, clip))
                {
                    count++;
                }
            }
            return count;
        }

        private readonly struct SourceState
        {
            private readonly AudioClip _clip;
            private readonly float _volume;
            private readonly float _pitch;
            private readonly int _timeSamples;
            private readonly bool _loop;
            private readonly UnityEngine.Audio.AudioMixerGroup _route;

            internal SourceState(AudioSource source)
            {
                _clip = source.clip;
                _volume = source.volume;
                _pitch = source.pitch;
                _timeSamples = source.timeSamples;
                _loop = source.loop;
                _route = source.outputAudioMixerGroup;
            }

            internal void AssertMatches(AudioSource source)
            {
                AssertConfigurationMatches(source);
                Assert.That(source.timeSamples, Is.EqualTo(_timeSamples));
            }

            internal void AssertConfigurationMatches(AudioSource source)
            {
                Assert.That(source.clip, Is.SameAs(_clip));
                Assert.That(source.volume, Is.EqualTo(_volume));
                Assert.That(source.pitch, Is.EqualTo(_pitch));
                Assert.That(source.loop, Is.EqualTo(_loop));
                Assert.That(source.outputAudioMixerGroup, Is.SameAs(_route));
            }

            internal void AssertResumedWithoutRestart(AudioSource source)
            {
                AssertConfigurationMatches(source);
                if (_clip == null)
                {
                    Assert.That(source.isPlaying, Is.False);
                    Assert.That(source.timeSamples, Is.EqualTo(_timeSamples));
                    return;
                }

                Assert.That(source.isPlaying, Is.True);
                var actualSamples = source.timeSamples;
                var forwardSamples = actualSamples >= _timeSamples
                    ? actualSamples - _timeSamples
                    : _clip.samples - _timeSamples + actualSamples;
                var restartDistanceSamples = _clip.samples - _timeSamples;
                Assert.That(forwardSamples, Is.LessThan(restartDistanceSamples));
            }
        }

        private sealed partial class SessionFixture
        {
            internal OrpheusAudioInitResult CreateWithInitialFocus(
                bool initialFocus,
                out OrpheusAudioManager manager)
            {
                return OrpheusAudioTestFactory.Create(
                    Host,
                    _settings,
                    _catalog,
                    DefaultGains,
                    1,
                    initialFocus,
                    _clipReadiness,
                    _mixer,
                    AudioSystem,
                    out manager);
            }
        }
    }
}
