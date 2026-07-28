using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Tests
{
    public sealed partial class OrpheusAudioSessionTests
    {
        [Test]
        public void RuntimeHost_AudioConfigurationSubscriptionMatchesBoundLifetimeExactly()
        {
            using (var fixture = new SessionFixture())
            {
                var baseline = CountAudioConfigurationSubscriptions(fixture.Host);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(CountAudioConfigurationSubscriptions(fixture.Host), Is.EqualTo(baseline));

                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(CountAudioConfigurationSubscriptions(fixture.Host), Is.EqualTo(baseline + 1));
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(CountAudioConfigurationSubscriptions(fixture.Host), Is.EqualTo(baseline + 1));

                manager.Dispose();
                Assert.That(CountAudioConfigurationSubscriptions(fixture.Host), Is.EqualTo(baseline + 1));
                Assert.That(fixture.Host.Unbind(manager), Is.True);
                Assert.That(CountAudioConfigurationSubscriptions(fixture.Host), Is.EqualTo(baseline));
                Assert.That(fixture.Host.Unbind(manager), Is.True);
                Assert.That(CountAudioConfigurationSubscriptions(fixture.Host), Is.EqualTo(baseline));
            }

            using (var destroyed = new SessionFixture())
            {
                var manager = destroyed.CreateReadyManager(out _);
                Assert.That(CountAudioConfigurationSubscriptions(destroyed.Host), Is.EqualTo(1));

                destroyed.DestroyHostOnly();

                Assert.That(CountAudioConfigurationSubscriptions(destroyed.Host), Is.Zero);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.RuntimeHostDestroyed));
                manager.Dispose();
            }
        }

        [Test]
        public void ConfigurationCallbacks_CoalesceAndDeferUntilHostConsumption()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(600);
                fixture.SetEvents(loop);
                var manager = fixture.CreateReadyManager(out _);
                manager.PlayLoop(new OrpheusAudioKey(600));
                manager.Tick(Time.realtimeSinceStartupAsDouble + 0.1d, 0f);
                fixture.ResetMixerCalls();
                fixture.Readiness.ResetCalls();

                fixture.Host.RecordAudioConfigurationChanged(false);
                fixture.Host.RecordAudioConfigurationChanged(false);
                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.RecordAudioConfigurationChanged(true);

                Assert.That(
                    fixture.Host.PeekAudioConfigurationPending(),
                    Is.EqualTo(OrpheusRecoveryPendingReason.ExternalConfiguration));
                Assert.That(manager.TryGetDiagnostics(out var pending), Is.True);
                Assert.That(
                    pending.RecoveryPendingReasons,
                    Is.EqualTo(OrpheusRecoveryPendingReason.ExternalConfiguration));
                Assert.That(pending.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(pending.PlaybackReady, Is.True);
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);
                Assert.That(fixture.Readiness.GetCallCount, Is.Zero);
                Assert.That(fixture.AudioSystem.ResetCallCount, Is.Zero);
                Assert.That(fixture.FindGlobalLoopSource(loop.GetClip(0)), Is.Not.Null);

                fixture.Host.ConsumePendingAudioConfigurationChanges();

                Assert.That(
                    fixture.Host.PeekAudioConfigurationPending(),
                    Is.EqualTo(OrpheusRecoveryPendingReason.None));
                Assert.That(fixture.Mixer.ParameterNames, Has.Count.EqualTo(6));
                Assert.That(fixture.Mixer.Snapshots, Is.EqualTo(new[] { OrpheusEffectiveSnapshot.Peace }));
                Assert.That(fixture.Mixer.TransitionSeconds, Is.EqualTo(new[] { 0f }));
                Assert.That(fixture.AudioSystem.ResetCallCount, Is.Zero);
                Assert.That(fixture.FindGlobalLoopSource(loop.GetClip(0)), Is.Not.Null);
                Assert.That(manager.TryGetDiagnostics(out var recovered), Is.True);
                Assert.That(recovered.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(recovered.RecoveryPendingReasons, Is.EqualTo(OrpheusRecoveryPendingReason.None));

                fixture.Host.ConsumePendingAudioConfigurationChanges();
                Assert.That(fixture.Mixer.ParameterNames, Has.Count.EqualTo(6));
                Assert.That(fixture.Mixer.Snapshots, Has.Count.EqualTo(1));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void EvidenceState_ReportsExactRecoveryGenerationAndRequestedLoops()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(600);
                var transient = fixture.CreatePlaybackEvent(601);
                fixture.SetEvents(loop, transient);
                var manager = fixture.CreateReadyManager(out _);

                Assert.That(
                    manager.TryGetEvidenceState(out var initial),
                    Is.True);
                Assert.That(initial.RecoveryGeneration, Is.Zero);
                Assert.That(initial.RequestedGlobalLoopMask, Is.Zero);

                manager.Play(new OrpheusAudioKey(601));
                Assert.That(manager.TryGetDiagnostics(out var transientStarted), Is.True);
                Assert.That(transientStarted.Transient2DActiveCount, Is.EqualTo(1));

                manager.PlayLoop(new OrpheusAudioKey(600));
                Assert.That(
                    manager.TryGetEvidenceState(out var requested),
                    Is.True);
                Assert.That(requested.RequestedGlobalLoopMask, Is.EqualTo(1));
                Assert.That(
                    requested.GlobalLoop0Key,
                    Is.EqualTo(new OrpheusAudioKey(600)));

                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.ConsumePendingAudioConfigurationChanges();

                Assert.That(
                    manager.TryGetEvidenceState(out var recovered),
                    Is.True);
                Assert.That(recovered.RecoveryGeneration, Is.EqualTo(1u));
                Assert.That(recovered.RequestedGlobalLoopMask, Is.EqualTo(1));
                Assert.That(
                    recovered.GlobalLoop0Key,
                    Is.EqualTo(new OrpheusAudioKey(600)));

                fixture.Host.ConsumePendingAudioConfigurationChanges();
                Assert.That(
                    manager.TryGetEvidenceState(out var unchanged),
                    Is.True);
                Assert.That(unchanged.RecoveryGeneration, Is.EqualTo(1u));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void SuccessfulManualReset_ClearsBeforeSynchronousSelfCallbackAndCannotRecurse()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetAndroidManualResetEnabled(true);
                var manager = fixture.CreateReadyManager(out _);
                fixture.AudioSystem.OnReset = () => fixture.Host.RecordAudioConfigurationChanged(false);
                fixture.ResetMixerCalls();

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

                Assert.That(fixture.AudioSystem.ResetCallCount, Is.EqualTo(1));
                Assert.That(fixture.Mixer.ParameterNames, Has.Count.EqualTo(6));
                Assert.That(fixture.Mixer.Snapshots, Has.Count.EqualTo(1));
                Assert.That(
                    fixture.Host.PeekAudioConfigurationPending(),
                    Is.EqualTo(OrpheusRecoveryPendingReason.SelfResetNotification));

                fixture.Host.ConsumePendingAudioConfigurationChanges();

                Assert.That(fixture.AudioSystem.ResetCallCount, Is.EqualTo(1));
                Assert.That(fixture.Mixer.ParameterNames, Has.Count.EqualTo(6));
                Assert.That(fixture.Mixer.Snapshots, Has.Count.EqualTo(1));
                Assert.That(
                    fixture.Host.PeekAudioConfigurationPending(),
                    Is.EqualTo(OrpheusRecoveryPendingReason.None));

                fixture.Host.RecordAudioConfigurationChanged(false);
                Assert.That(
                    fixture.Host.PeekAudioConfigurationPending(),
                    Is.EqualTo(OrpheusRecoveryPendingReason.ExternalConfiguration));
                fixture.Host.ConsumePendingAudioConfigurationChanges();

                Assert.That(fixture.AudioSystem.ResetCallCount, Is.EqualTo(1));
                Assert.That(fixture.Mixer.ParameterNames, Has.Count.EqualTo(12));
                Assert.That(fixture.Mixer.Snapshots, Has.Count.EqualTo(2));
                Assert.That(
                    fixture.Host.PeekAudioConfigurationPending(),
                    Is.EqualTo(OrpheusRecoveryPendingReason.None));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void SuccessfulManualReset_CombinedSelfAndExternalPreservesExternalTruth()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetAndroidManualResetEnabled(true);
                var manager = fixture.CreateReadyManager(out _);
                fixture.AudioSystem.OnReset = () =>
                {
                    fixture.Host.RecordAudioConfigurationChanged(false);
                    fixture.Host.RecordAudioConfigurationChanged(true);
                };
                fixture.ResetMixerCalls();

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

                Assert.That(fixture.AudioSystem.ResetCallCount, Is.EqualTo(1));
                Assert.That(fixture.Mixer.ParameterNames, Has.Count.EqualTo(6));
                Assert.That(
                    fixture.Host.PeekAudioConfigurationPending(),
                    Is.EqualTo(
                        OrpheusRecoveryPendingReason.ExternalConfiguration |
                        OrpheusRecoveryPendingReason.SelfResetNotification));

                fixture.Host.ConsumePendingAudioConfigurationChanges();

                Assert.That(fixture.AudioSystem.ResetCallCount, Is.EqualTo(1));
                Assert.That(fixture.Mixer.ParameterNames, Has.Count.EqualTo(12));
                Assert.That(fixture.Mixer.Snapshots, Has.Count.EqualTo(2));
                Assert.That(
                    fixture.Host.PeekAudioConfigurationPending(),
                    Is.EqualTo(OrpheusRecoveryPendingReason.None));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ExternalRecoveryDuringSuspension_RemainsPendingUntilEveryReasonClears()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(610);
                fixture.SetEvents(loop);
                var manager = fixture.CreateReadyManager(out _);
                manager.PlayLoop(new OrpheusAudioKey(610));
                manager.Tick(Time.realtimeSinceStartupAsDouble + 0.1d, 0f);
                fixture.Host.HandleApplicationFocusChanged(false);
                fixture.Host.HandleApplicationPauseChanged(true);
                fixture.ResetMixerCalls();

                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.ConsumePendingAudioConfigurationChanges();

                Assert.That(manager.TryGetDiagnostics(out var suspended), Is.True);
                Assert.That(suspended.TransportState, Is.EqualTo(OrpheusTransportState.Suspended));
                Assert.That(
                    suspended.RecoveryPendingReasons,
                    Is.EqualTo(OrpheusRecoveryPendingReason.ExternalConfiguration));
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);

                fixture.Host.HandleApplicationFocusChanged(true);
                Assert.That(manager.TryGetDiagnostics(out var stillSuspended), Is.True);
                Assert.That(stillSuspended.TransportState, Is.EqualTo(OrpheusTransportState.Suspended));
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);

                fixture.Host.HandleApplicationPauseChanged(false);

                Assert.That(manager.TryGetDiagnostics(out var active), Is.True);
                Assert.That(active.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(active.PlaybackReady, Is.True);
                Assert.That(active.RecoveryPendingReasons, Is.EqualTo(OrpheusRecoveryPendingReason.None));
                Assert.That(fixture.Mixer.ParameterNames, Has.Count.EqualTo(6));
                Assert.That(fixture.Mixer.TransitionSeconds, Is.EqualTo(new[] { 0f }));
                Assert.That(fixture.FindGlobalLoopSource(loop.GetClip(0)), Is.Not.Null);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void CommonRecovery_NormalizesEveryOwnedSourceBeforeMixerWorkAndBlocksAdmission()
        {
            using (var fixture = new SessionFixture())
            {
                var oneShot = fixture.CreatePlaybackEvent(620);
                fixture.SetEvents(oneShot);
                var manager = fixture.CreateReadyManager(out _);
                fixture.DirtyAllSources(oneShot.GetClip(0));
                fixture.ResetMixerCalls();
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                var observed = false;
                OrpheusAudioDiagnostics during = default;
                fixture.Mixer.OnBeforeMixerOperation = () =>
                {
                    if (observed)
                    {
                        return;
                    }

                    observed = true;
                    for (var sourceIndex = 0; sourceIndex < fixture.Sources.Length; sourceIndex++)
                    {
                        AssertNormalized(fixture.Sources[sourceIndex]);
                    }

                    Assert.That(manager.TryGetDiagnostics(out during), Is.True);
                    manager.Play(new OrpheusAudioKey(620));
                    fixture.Host.RecordAudioConfigurationChanged(true);
                };

                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.ConsumePendingAudioConfigurationChanges();
                fixture.Mixer.OnBeforeMixerOperation = null;

                Assert.That(observed, Is.True);
                Assert.That(during.TransportState, Is.EqualTo(OrpheusTransportState.Recovering));
                Assert.That(during.PlaybackReady, Is.False);
                Assert.That(
                    fixture.Host.PeekAudioConfigurationPending(),
                    Is.EqualTo(OrpheusRecoveryPendingReason.ExternalConfiguration));
                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                Assert.That(
                    after.Counters.SuspendedRejected,
                    Is.EqualTo(before.Counters.SuspendedRejected + 1));
                Assert.That(fixture.Mixer.ParameterNames, Has.Count.EqualTo(6));
                Assert.That(fixture.Mixer.TransitionSeconds, Is.EqualTo(new[] { 0f }));
                for (var sourceIndex = 0; sourceIndex < fixture.Sources.Length; sourceIndex++)
                {
                    AssertNormalized(fixture.Sources[sourceIndex]);
                }

                fixture.Host.ConsumePendingAudioConfigurationChanges();
                Assert.That(fixture.Mixer.ParameterNames, Has.Count.EqualTo(12));
                Assert.That(fixture.Mixer.Snapshots, Has.Count.EqualTo(2));

                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Recovery_RestartsOnlyLatestPersistentIntentAndNeverTransientHistoryOrProgress()
        {
            using (var fixture = new SessionFixture())
            {
                var transient = fixture.CreatePlaybackEvent(630);
                var oldBgm = fixture.CreateBgmEvent(631);
                var latestBgm = fixture.CreateBgmEvent(632);
                var oldAmbience = fixture.CreateProfileAmbienceEvent(633);
                var latestAmbience = fixture.CreateProfileAmbienceEvent(634);
                var requestedLoop = fixture.CreateGlobalLoopEvent(635);
                var stoppedLoop = fixture.CreateGlobalLoopEvent(636);
                fixture.SetEvents(
                    transient,
                    oldBgm,
                    latestBgm,
                    oldAmbience,
                    latestAmbience,
                    requestedLoop,
                    stoppedLoop);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.ApplyProfile(PersistentIntent(1, OrpheusBaseState.Peace, 631, 633));
                manager.PlayLoop(new OrpheusAudioKey(635));
                manager.PlayLoop(new OrpheusAudioKey(636));
                manager.Play(new OrpheusAudioKey(630));
                manager.Tick(realtime, 0f);
                manager.ApplyProfile(PersistentIntent(2, OrpheusBaseState.Peace, 632, 634));
                manager.Tick(realtime + 0.01d, 0.01f);
                manager.StopLoop(new OrpheusAudioKey(636));
                SetOccupiedPersistentProgress(fixture.Sources, 1234);

                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.ConsumePendingAudioConfigurationChanges();

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.DesiredBgmKey, Is.EqualTo(new OrpheusAudioKey(632)));
                Assert.That(diagnostics.CurrentBgmKey, Is.EqualTo(new OrpheusAudioKey(632)));
                Assert.That(diagnostics.TargetBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(diagnostics.DesiredProfileAmbienceKey, Is.EqualTo(new OrpheusAudioKey(634)));
                Assert.That(diagnostics.CurrentProfileAmbienceKey, Is.EqualTo(new OrpheusAudioKey(634)));
                Assert.That(diagnostics.TargetProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                Assert.That(diagnostics.Transient3DActiveCount, Is.Zero);
                Assert.That(diagnostics.FadingCount, Is.Zero);
                Assert.That(diagnostics.PendingCount, Is.Zero);
                Assert.That(CountSourcesWithClip(fixture.Sources, transient.GetClip(0)), Is.Zero);
                Assert.That(CountSourcesWithClip(fixture.Sources, oldBgm.GetClip(0)), Is.Zero);
                Assert.That(CountSourcesWithClip(fixture.Sources, latestBgm.GetClip(0)), Is.EqualTo(1));
                Assert.That(CountSourcesWithClip(fixture.Sources, oldAmbience.GetClip(0)), Is.Zero);
                Assert.That(CountSourcesWithClip(fixture.Sources, latestAmbience.GetClip(0)), Is.EqualTo(1));
                Assert.That(fixture.FindGlobalLoopSource(requestedLoop.GetClip(0)), Is.Not.Null);
                Assert.That(fixture.FindGlobalLoopSourceOrNull(stoppedLoop.GetClip(0)), Is.Null);
                AssertPersistentProgressWasNotRestored(fixture.Sources, 1234);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Recovery_RetriesEachDesiredFailedPersistentRoleOncePerGeneration()
        {
            using (var fixture = new SessionFixture())
            {
                var bgm = fixture.CreateBgmEvent(640);
                var ambience = fixture.CreateProfileAmbienceEvent(641);
                var loop0 = fixture.CreateGlobalLoopEvent(642);
                var loop1 = fixture.CreateGlobalLoopEvent(643);
                var loop2 = fixture.CreateGlobalLoopEvent(644);
                var loop3 = fixture.CreateGlobalLoopEvent(645);
                fixture.SetEvents(bgm, ambience, loop0, loop1, loop2, loop3);
                for (var clipIndex = 0; clipIndex < 6; clipIndex++)
                {
                    fixture.SetClipState(clipIndex, OrpheusClipLoadState.Failed);
                }

                var manager = fixture.CreateReadyManager(out _);
                manager.ApplyProfile(PersistentIntent(1, OrpheusBaseState.Peace, 640, 641));
                manager.PlayLoop(new OrpheusAudioKey(642));
                manager.PlayLoop(new OrpheusAudioKey(643));
                manager.PlayLoop(new OrpheusAudioKey(644));
                manager.PlayLoop(new OrpheusAudioKey(645));
                fixture.Readiness.ResetCalls();

                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.ConsumePendingAudioConfigurationChanges();
                AssertRequestCounts(fixture, 1);

                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.ConsumePendingAudioConfigurationChanges();
                AssertRequestCounts(fixture, 2);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ManualResetPolicy_DefaultAndWindowsPathsNeverResetAndFalseResultIsNonDestructive()
        {
            using (var defaults = new SessionFixture())
            {
                var manager = defaults.CreateReadyManager(out _);
                manager.HandleRuntimeHostSuspensionInput(
                    defaults.Host,
                    OrpheusSuspensionReason.FocusLost,
                    true,
                    true);
                manager.HandleRuntimeHostSuspensionInput(
                    defaults.Host,
                    OrpheusSuspensionReason.FocusLost,
                    false,
                    true);
                Assert.That(defaults.AudioSystem.ResetCallCount, Is.Zero);
                DisposeBound(defaults, manager);
            }

            using (var windows = new SessionFixture())
            {
                windows.SetAndroidManualResetEnabled(true);
                var manager = windows.CreateReadyManager(out _);
                manager.HandleRuntimeHostSuspensionInput(
                    windows.Host,
                    OrpheusSuspensionReason.FocusLost,
                    true,
                    false);
                manager.HandleRuntimeHostSuspensionInput(
                    windows.Host,
                    OrpheusSuspensionReason.FocusLost,
                    false,
                    false);
                Assert.That(windows.AudioSystem.ResetCallCount, Is.Zero);
                DisposeBound(windows, manager);
            }

            using (var failed = new SessionFixture())
            {
                failed.SetAndroidManualResetEnabled(true);
                var loop = failed.CreateGlobalLoopEvent(650);
                failed.SetEvents(loop);
                var manager = failed.CreateReadyManager(out _);
                manager.PlayLoop(new OrpheusAudioKey(650));
                manager.Tick(Time.realtimeSinceStartupAsDouble + 0.1d, 0f);
                var loopSource = failed.FindGlobalLoopSource(loop.GetClip(0));
                loopSource.timeSamples = loopSource.clip.samples / 2;
                failed.AudioSystem.ResetResult = false;
                manager.HandleRuntimeHostSuspensionInput(
                    failed.Host,
                    OrpheusSuspensionReason.FocusLost,
                    true,
                    true);
                var suspendedSources = CaptureSources(failed.Sources);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                manager.HandleRuntimeHostSuspensionInput(
                    failed.Host,
                    OrpheusSuspensionReason.FocusLost,
                    false,
                    true);

                Assert.That(failed.AudioSystem.ResetCallCount, Is.EqualTo(1));
                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                Assert.That(after.Counters.RecoveryFailed, Is.EqualTo(before.Counters.RecoveryFailed + 1));
                Assert.That(after.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(after.DesiredBgmKey, Is.EqualTo(before.DesiredBgmKey));
                Assert.That(after.DesiredProfileAmbienceKey, Is.EqualTo(before.DesiredProfileAmbienceKey));
                AssertSourcesResumedWithoutRestart(suspendedSources, failed.Sources);

                failed.Host.ConsumePendingAudioConfigurationChanges();
                Assert.That(failed.AudioSystem.ResetCallCount, Is.EqualTo(1));
                Assert.That(manager.TryGetDiagnostics(out var duplicate), Is.True);
                Assert.That(duplicate.Counters.RecoveryFailed, Is.EqualTo(after.Counters.RecoveryFailed));
                DisposeBound(failed, manager);
            }

            using (var failedWithExternal = new SessionFixture())
            {
                failedWithExternal.SetAndroidManualResetEnabled(true);
                var loop = failedWithExternal.CreateGlobalLoopEvent(651);
                failedWithExternal.SetEvents(loop);
                var manager = failedWithExternal.CreateReadyManager(out _);
                manager.PlayLoop(new OrpheusAudioKey(651));
                manager.Tick(Time.realtimeSinceStartupAsDouble + 0.1d, 0f);
                failedWithExternal.AudioSystem.ResetResult = false;
                failedWithExternal.AudioSystem.OnReset = () =>
                    failedWithExternal.Host.RecordAudioConfigurationChanged(true);
                failedWithExternal.ResetMixerCalls();
                manager.HandleRuntimeHostSuspensionInput(
                    failedWithExternal.Host,
                    OrpheusSuspensionReason.FocusLost,
                    true,
                    true);
                var suspendedSources = CaptureSources(failedWithExternal.Sources);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                manager.HandleRuntimeHostSuspensionInput(
                    failedWithExternal.Host,
                    OrpheusSuspensionReason.FocusLost,
                    false,
                    true);

                Assert.That(failedWithExternal.AudioSystem.ResetCallCount, Is.EqualTo(1));
                Assert.That(manager.TryGetDiagnostics(out var pending), Is.True);
                Assert.That(
                    pending.Counters.RecoveryFailed,
                    Is.EqualTo(before.Counters.RecoveryFailed + 1));
                Assert.That(pending.TransportState, Is.EqualTo(OrpheusTransportState.Recovering));
                Assert.That(
                    pending.RecoveryPendingReasons,
                    Is.EqualTo(OrpheusRecoveryPendingReason.ExternalConfiguration));
                Assert.That(failedWithExternal.Mixer.OperationOrder, Is.Empty);
                AssertSourcesEqual(suspendedSources, failedWithExternal.Sources);

                failedWithExternal.Host.ConsumePendingAudioConfigurationChanges();

                Assert.That(failedWithExternal.AudioSystem.ResetCallCount, Is.EqualTo(1));
                Assert.That(manager.TryGetDiagnostics(out var recovered), Is.True);
                Assert.That(recovered.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(recovered.RecoveryPendingReasons, Is.EqualTo(OrpheusRecoveryPendingReason.None));
                Assert.That(failedWithExternal.Mixer.ParameterNames, Has.Count.EqualTo(6));
                Assert.That(failedWithExternal.Mixer.Snapshots, Has.Count.EqualTo(1));
                Assert.That(failedWithExternal.FindGlobalLoopSource(loop.GetClip(0)), Is.Not.Null);
                DisposeBound(failedWithExternal, manager);
            }
        }

        [Test]
        public void PendingConsumerWrongThread_ChangesOnlyCounterAndConsumesNothing()
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.ResetMixerCalls();
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                Exception workerException = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        fixture.Host.ConsumePendingAudioConfigurationChanges();
                    }
                    catch (Exception exception)
                    {
                        workerException = exception;
                    }
                });

                worker.Start();
                worker.Join();

                Assert.That(workerException, Is.Null);
                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                AssertDiagnosticsEqualExceptWrongThread(before, after);
                Assert.That(
                    after.Counters.WrongThreadRejected,
                    Is.EqualTo(before.Counters.WrongThreadRejected + 1));
                Assert.That(
                    fixture.Host.PeekAudioConfigurationPending(),
                    Is.EqualTo(OrpheusRecoveryPendingReason.ExternalConfiguration));
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);

                fixture.Host.ConsumePendingAudioConfigurationChanges();
                Assert.That(
                    fixture.Host.PeekAudioConfigurationPending(),
                    Is.EqualTo(OrpheusRecoveryPendingReason.None));
                DisposeBound(fixture, manager);
            }
        }

        [TestCase(false, OrpheusAudioDisableReason.MixerSetFloatFailed)]
        [TestCase(true, OrpheusAudioDisableReason.UnexpectedRuntimeException)]
        public void RecoveryMixerFailure_PreservesUnderlyingReasonWithoutDoubleCounting(
            bool throws,
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                fixture.ResetMixerCalls();
                fixture.Mixer.FailOnSetFloatCall = throws ? -1 : 1;
                fixture.Mixer.ThrowOnSetFloatCall = throws ? 1 : -1;

                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.ConsumePendingAudioConfigurationChanges();

                Assert.That(manager.TryGetDiagnostics(out var failed), Is.True);
                Assert.That(failed.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(failed.DisableReason, Is.EqualTo(expectedReason));
                Assert.That(failed.Counters.RecoveryFailed, Is.Zero);
                Assert.That(
                    failed.Counters.SetFloatFailed,
                    Is.EqualTo(throws ? 0ul : 1ul));
                Assert.That(
                    failed.Counters.UnexpectedException,
                    Is.EqualTo(throws ? 1ul : 0ul));

                manager.Dispose();
                Assert.That(manager.TryGetDiagnostics(out var disposed), Is.True);
                Assert.That(disposed.Counters.SetFloatFailed, Is.EqualTo(failed.Counters.SetFloatFailed));
                Assert.That(disposed.Counters.UnexpectedException, Is.EqualTo(failed.Counters.UnexpectedException));
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void RecoveryCarrierFailure_PreservesReferenceReasonWithoutSyntheticCounters()
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                fixture.ReplaceBgmZeroReference();

                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.ConsumePendingAudioConfigurationChanges();

                Assert.That(manager.TryGetDiagnostics(out var failed), Is.True);
                Assert.That(failed.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    failed.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.OwnedSourceReferenceChanged));
                Assert.That(failed.Counters.RecoveryFailed, Is.Zero);
                Assert.That(failed.Counters.UnexpectedException, Is.Zero);
                manager.Dispose();
                Assert.That(manager.TryGetDiagnostics(out var disposed), Is.True);
                Assert.That(disposed.Counters.RecoveryFailed, Is.Zero);
                Assert.That(disposed.Counters.UnexpectedException, Is.Zero);
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void FirstTickAfterRecovery_UsesZeroActiveDelta()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(660);
                fixture.SetEvents(loop);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.PlayLoop(new OrpheusAudioKey(660));
                manager.Tick(realtime, 0f);
                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.ConsumePendingAudioConfigurationChanges();
                var source = fixture.FindGlobalLoopSource(loop.GetClip(0));

                manager.StopLoop(new OrpheusAudioKey(660));
                manager.Tick(realtime + 100d, 1f);
                Assert.That(source.clip, Is.SameAs(loop.GetClip(0)));
                Assert.That(source.volume, Is.EqualTo(1f).Within(0.000001f));

                manager.Tick(realtime + 100.0075d, 0.0075f);
                Assert.That(source.volume, Is.EqualTo(0.5f).Within(0.001f));
                manager.Tick(realtime + 100.015d, 0.0075f);
                AssertNormalized(source);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ConfigurationCallbackConsumptionRecoveryAndFirstTick_AllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                fixture.Mixer.SuppressOperationOrder = true;
                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.ConsumePendingAudioConfigurationChanges();
                manager.Tick(Time.realtimeSinceStartupAsDouble + 0.1d, 0f);
                fixture.ResetMixerCalls();

                fixture.Host.HandleApplicationFocusChanged(false);
                fixture.Host.RecordAudioConfigurationChanged(true);
                var beforeDeferred = GC.GetAllocatedBytesForCurrentThread();
                fixture.Host.ConsumePendingAudioConfigurationChanges();
                var deferredBytes = GC.GetAllocatedBytesForCurrentThread() - beforeDeferred;
                fixture.Host.HandleApplicationFocusChanged(true);

                var beforeEmpty = GC.GetAllocatedBytesForCurrentThread();
                fixture.Host.ConsumePendingAudioConfigurationChanges();
                var emptyBytes = GC.GetAllocatedBytesForCurrentThread() - beforeEmpty;
                fixture.ResetMixerCalls();

                var beforeRecord = GC.GetAllocatedBytesForCurrentThread();
                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.RecordAudioConfigurationChanged(false);
                var recordBytes = GC.GetAllocatedBytesForCurrentThread() - beforeRecord;

                var beforeConsume = GC.GetAllocatedBytesForCurrentThread();
                fixture.Host.ConsumePendingAudioConfigurationChanges();
                var consumeBytes = GC.GetAllocatedBytesForCurrentThread() - beforeConsume;

                var beforeTick = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(Time.realtimeSinceStartupAsDouble + 0.2d, 0.1f);
                var tickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeTick;

                Assert.That(deferredBytes, Is.Zero);
                Assert.That(emptyBytes, Is.Zero);
                Assert.That(recordBytes, Is.Zero);
                Assert.That(consumeBytes, Is.Zero);
                Assert.That(tickBytes, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        private static int CountAudioConfigurationSubscriptions(OrpheusAudioRuntimeHost host)
        {
            var eventField = typeof(AudioSettings).GetField(
                "OnAudioConfigurationChanged",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(eventField, Is.Not.Null, "Unity event backing field was not found.");
            var callback = eventField.GetValue(null) as Delegate;
            if (callback == null)
            {
                return 0;
            }

            var count = 0;
            var invocationList = callback.GetInvocationList();
            for (var index = 0; index < invocationList.Length; index++)
            {
                if (ReferenceEquals(invocationList[index].Target, host))
                {
                    count++;
                }
            }

            return count;
        }

        private static void SetOccupiedPersistentProgress(AudioSource[] sources, int timeSamples)
        {
            for (var sourceIndex = OrpheusAudioSourceBank.BgmOffset;
                 sourceIndex < sources.Length;
                 sourceIndex++)
            {
                if (sources[sourceIndex].clip != null)
                {
                    sources[sourceIndex].timeSamples = timeSamples;
                }
            }
        }

        private static void AssertPersistentProgressWasNotRestored(
            AudioSource[] sources,
            int forbiddenTimeSamples)
        {
            for (var sourceIndex = OrpheusAudioSourceBank.BgmOffset;
                 sourceIndex < sources.Length;
                 sourceIndex++)
            {
                if (sources[sourceIndex].clip != null)
                {
                    Assert.That(sources[sourceIndex].timeSamples, Is.Not.EqualTo(forbiddenTimeSamples));
                }
            }
        }

        private static void AssertRequestCounts(SessionFixture fixture, int expected)
        {
            for (var flattenedClipIndex = 0; flattenedClipIndex < 6; flattenedClipIndex++)
            {
                Assert.That(
                    fixture.GetClipRequestCount(flattenedClipIndex),
                    Is.EqualTo(expected),
                    flattenedClipIndex.ToString());
            }
        }

        private sealed partial class SessionFixture
        {
            internal void SetAndroidManualResetEnabled(bool enabled)
            {
                SetField(_settings, "_androidManualResetEnabled", enabled);
            }
        }
    }
}
