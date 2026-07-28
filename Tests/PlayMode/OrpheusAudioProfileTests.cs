using System;
using System.Linq;
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
        public void ManagerProfileSurface_IsExactAndDoesNotExposeCompetingWriters()
        {
            var methods = typeof(OrpheusAudioManager).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

            Assert.That(methods.Count(method => method.Name == "ApplyProfile"), Is.EqualTo(1));
            Assert.That(methods.Count(method => method.Name == "SetOverlay"), Is.EqualTo(1));
            Assert.That(methods.Any(method => method.Name == "PlayBgm"), Is.False);
            Assert.That(methods.Any(method => method.Name == "StopBgm"), Is.False);
            Assert.That(methods.Any(method => method.Name == "SetBaseState"), Is.False);

            var apply = methods.Single(method => method.Name == "ApplyProfile");
            Assert.That(apply.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(apply.GetParameters().Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[] { typeof(OrpheusAudioProfileIntent) }));
            var overlay = methods.Single(method => method.Name == "SetOverlay");
            Assert.That(overlay.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(overlay.GetParameters().Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[] { typeof(OrpheusOverlay), typeof(bool) }));
        }

        [Test]
        public void Factory_RejectsInvalidProfileDurationsBeforePublishingManager()
        {
            AssertInvalidDuration(float.NaN, 0.4f, 0.6f);
            AssertInvalidDuration(0.4f, 0.0149f, 0.6f);
            AssertInvalidDuration(0.4f, 0.4f, 5.0001f);

            using (var fixture = new SessionFixture())
            {
                fixture.SetProfileDurations(0.015f, 5f, 0.015f);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                manager.Dispose();
            }
        }

        [Test]
        public void PreActivationProfileAndOverlay_ApplyLatestSnapshotAtZeroInBothBarrierOrders()
        {
            AssertPreActivationSnapshot(hostReadyFirst: true);
            AssertPreActivationSnapshot(hostReadyFirst: false);
        }

        [Test]
        public void ActiveProfileAndOverlay_CommitAtomicallyAndUseConfiguredDuration()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetProfileDurations(0.4f, 0.4f, 0.73f);
                var bgm = fixture.CreatePlaybackEvent(
                    100,
                    OrpheusPlaybackKind.Bgm,
                    OrpheusLoadPolicy.PersistentStream,
                    category: OrpheusCategory.Music);
                var ambience = fixture.CreatePlaybackEvent(
                    200,
                    OrpheusPlaybackKind.ProfileAmbience,
                    OrpheusLoadPolicy.PersistentStream,
                    category: OrpheusCategory.Ambience);
                fixture.SetEvents(bgm, ambience);
                var manager = fixture.CreateReadyManager(out _);
                fixture.ResetMixerCalls();

                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    7,
                    OrpheusBaseState.Combat,
                    new OrpheusAudioKey(100),
                    new OrpheusAudioKey(200)));
                manager.SetOverlay(OrpheusOverlay.Menu, true);
                manager.SetOverlay(OrpheusOverlay.Pause, true);
                manager.SetOverlay(OrpheusOverlay.Pause, false);
                manager.SetOverlay(OrpheusOverlay.Menu, false);

                Assert.That(
                    fixture.Mixer.Snapshots,
                    Is.EqualTo(new[]
                    {
                        OrpheusEffectiveSnapshot.Combat,
                        OrpheusEffectiveSnapshot.Menu,
                        OrpheusEffectiveSnapshot.Pause,
                        OrpheusEffectiveSnapshot.Menu,
                        OrpheusEffectiveSnapshot.Combat
                    }));
                Assert.That(fixture.Mixer.TransitionSeconds,
                    Is.All.EqualTo(0.73f).Within(0.0001f));
                Assert.That(fixture.Mixer.ParameterNames, Is.Empty);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.ProfileId, Is.EqualTo(7));
                Assert.That(diagnostics.BaseState, Is.EqualTo(OrpheusBaseState.Combat));
                Assert.That(diagnostics.Overlay, Is.EqualTo(OrpheusOverlay.None));
                Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Combat));
                Assert.That(diagnostics.DesiredBgmKey, Is.EqualTo(new OrpheusAudioKey(100)));
                Assert.That(diagnostics.DesiredProfileAmbienceKey, Is.EqualTo(new OrpheusAudioKey(200)));

                fixture.ResetMixerCalls();
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    7,
                    OrpheusBaseState.Combat,
                    new OrpheusAudioKey(100),
                    new OrpheusAudioKey(200)));
                manager.SetOverlay(OrpheusOverlay.Menu, false);
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);

                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    0,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));
                manager.SetOverlay(OrpheusOverlay.None, true);
                manager.SetOverlay(OrpheusOverlay.Menu | OrpheusOverlay.Pause, true);
                manager.SetOverlay((OrpheusOverlay)4, true);
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);
                Assert.That(manager.TryGetDiagnostics(out var afterReject), Is.True);
                Assert.That(afterReject.ProfileId, Is.EqualTo(7));
                Assert.That(afterReject.Counters.InvalidValueRejected, Is.EqualTo(3));

                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void SameProfileIdChangedContent_IsRealUpdateAndPreservesHigherOverlays()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreatePlaybackEvent(
                        100,
                        OrpheusPlaybackKind.Bgm,
                        OrpheusLoadPolicy.PersistentStream,
                        category: OrpheusCategory.Music),
                    fixture.CreatePlaybackEvent(
                        101,
                        OrpheusPlaybackKind.Bgm,
                        OrpheusLoadPolicy.PersistentStream,
                        category: OrpheusCategory.Music),
                    fixture.CreatePlaybackEvent(
                        200,
                        OrpheusPlaybackKind.ProfileAmbience,
                        OrpheusLoadPolicy.PersistentStream,
                        category: OrpheusCategory.Ambience),
                    fixture.CreatePlaybackEvent(
                        201,
                        OrpheusPlaybackKind.ProfileAmbience,
                        OrpheusLoadPolicy.PersistentStream,
                        category: OrpheusCategory.Ambience));
                var manager = fixture.CreateReadyManager(out _);
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    7,
                    OrpheusBaseState.Peace,
                    new OrpheusAudioKey(100),
                    new OrpheusAudioKey(200)));
                manager.SetOverlay(OrpheusOverlay.Menu, true);
                manager.SetOverlay(OrpheusOverlay.Pause, true);
                fixture.ResetMixerCalls();

                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    7,
                    OrpheusBaseState.Combat,
                    new OrpheusAudioKey(101),
                    new OrpheusAudioKey(201)));
                manager.SetOverlay(OrpheusOverlay.Menu, false);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.ProfileId, Is.EqualTo(7));
                Assert.That(diagnostics.BaseState, Is.EqualTo(OrpheusBaseState.Combat));
                Assert.That(diagnostics.DesiredBgmKey, Is.EqualTo(new OrpheusAudioKey(101)));
                Assert.That(diagnostics.DesiredProfileAmbienceKey,
                    Is.EqualTo(new OrpheusAudioKey(201)));
                Assert.That(diagnostics.Overlay, Is.EqualTo(OrpheusOverlay.Pause));
                Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Pause));
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);

                manager.SetOverlay(OrpheusOverlay.Pause, false);

                Assert.That(fixture.Mixer.Snapshots,
                    Is.EqualTo(new[] { OrpheusEffectiveSnapshot.Combat }));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void InvalidProfileCatalogEntries_RejectAtomicallyAtManagerSurface()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreatePlaybackEvent(
                        300,
                        OrpheusPlaybackKind.ProfileAmbience,
                        OrpheusLoadPolicy.PersistentStream,
                        category: OrpheusCategory.Ambience),
                    fixture.CreatePlaybackEvent(
                        301,
                        OrpheusPlaybackKind.OneShot2D,
                        OrpheusLoadPolicy.BootstrapTransient,
                        category: OrpheusCategory.SfxUi),
                    fixture.CreatePlaybackEvent(
                        302,
                        OrpheusPlaybackKind.Bgm,
                        OrpheusLoadPolicy.PersistentStream,
                        category: OrpheusCategory.Music));
                var manager = fixture.CreateReadyManager(out _);
                manager.SetOverlay(OrpheusOverlay.Menu, true);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                fixture.ResetMixerCalls();

                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    1,
                    OrpheusBaseState.Combat,
                    new OrpheusAudioKey(300),
                    OrpheusAudioKey.Invalid));
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    1,
                    OrpheusBaseState.Combat,
                    new OrpheusAudioKey(301),
                    OrpheusAudioKey.Invalid));
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    1,
                    OrpheusBaseState.Combat,
                    OrpheusAudioKey.Invalid,
                    new OrpheusAudioKey(302)));
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    1,
                    OrpheusBaseState.Combat,
                    new OrpheusAudioKey(999),
                    OrpheusAudioKey.Invalid));

                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                Assert.That(after.ProfileId, Is.EqualTo(before.ProfileId));
                Assert.That(after.BaseState, Is.EqualTo(before.BaseState));
                Assert.That(after.Overlay, Is.EqualTo(before.Overlay));
                Assert.That(after.DesiredBgmKey, Is.EqualTo(before.DesiredBgmKey));
                Assert.That(after.DesiredProfileAmbienceKey,
                    Is.EqualTo(before.DesiredProfileAmbienceKey));
                Assert.That(after.EffectiveSnapshot, Is.EqualTo(before.EffectiveSnapshot));
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void IdenticalFailedProfile_BgmAndAmbienceEachConsumeExactlyOneRetry()
        {
            using (var fixture = new SessionFixture())
            {
                var bgm = fixture.CreatePlaybackEvent(
                    100,
                    OrpheusPlaybackKind.Bgm,
                    OrpheusLoadPolicy.PersistentStream,
                    category: OrpheusCategory.Music);
                var ambience = fixture.CreatePlaybackEvent(
                    200,
                    OrpheusPlaybackKind.ProfileAmbience,
                    OrpheusLoadPolicy.PersistentStream,
                    category: OrpheusCategory.Ambience);
                fixture.SetEvents(bgm, ambience);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                var intent = new OrpheusAudioProfileIntent(
                    1,
                    OrpheusBaseState.Peace,
                    new OrpheusAudioKey(100),
                    new OrpheusAudioKey(200));
                manager.ApplyProfile(intent);
                manager.MarkProfilePersistentFailed(
                    OrpheusPlaybackKind.Bgm,
                    new OrpheusAudioKey(100));
                manager.MarkProfilePersistentFailed(
                    OrpheusPlaybackKind.ProfileAmbience,
                    new OrpheusAudioKey(200));
                fixture.SetClipState(0, OrpheusClipLoadState.Failed);
                fixture.SetClipState(1, OrpheusClipLoadState.Failed);
                fixture.Readiness.ResetCalls();

                manager.ApplyProfile(intent);
                manager.ApplyProfile(intent);

                Assert.That(
                    manager.TryConsumeProfilePersistentRetry(
                        OrpheusPlaybackKind.Bgm,
                        new OrpheusAudioKey(100)),
                    Is.False);
                Assert.That(
                    manager.TryConsumeProfilePersistentRetry(
                        OrpheusPlaybackKind.ProfileAmbience,
                        new OrpheusAudioKey(200)),
                    Is.False);
                Assert.That(fixture.GetClipRequestCount(0), Is.EqualTo(1));
                Assert.That(fixture.GetClipRequestCount(1), Is.EqualTo(1));
                manager.Dispose();
            }
        }

        [Test]
        public void ProfileAndOverlayWrongThread_ChangeOnlyWrongThreadCounter()
        {
            using (var fixture = new SessionFixture())
            {
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                var worker = new Thread(() =>
                {
                    manager.ApplyProfile(new OrpheusAudioProfileIntent(
                        0,
                        OrpheusBaseState.Invalid,
                        OrpheusAudioKey.Invalid,
                        OrpheusAudioKey.Invalid));
                    manager.SetOverlay(OrpheusOverlay.None, true);
                });
                worker.Start();
                worker.Join();

                var nextRealtime = (double)GetField(manager, "_lastRealtime") + 0.016d;
                manager.Tick(nextRealtime, 0.016f);
                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                Assert.That(after.ProfileId, Is.EqualTo(before.ProfileId));
                Assert.That(after.Overlay, Is.EqualTo(before.Overlay));
                Assert.That(after.Counters.WrongThreadRejected,
                    Is.EqualTo(before.Counters.WrongThreadRejected + 2));
                Assert.That(after.Counters.InvalidValueRejected,
                    Is.EqualTo(before.Counters.InvalidValueRejected));
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);
                manager.Dispose();
            }
        }

        [TestCase(OrpheusAudioDisableReason.MixerReferenceInvalid, true, false)]
        [TestCase(OrpheusAudioDisableReason.MixerGroupReferenceInvalid, true, false)]
        [TestCase(OrpheusAudioDisableReason.SnapshotReferenceInvalid, false, false)]
        [TestCase(OrpheusAudioDisableReason.UnexpectedRuntimeException, false, true)]
        public void SnapshotFailure_RetainsHistoricalStateAndClearsLiveDesiredKeys(
            OrpheusAudioDisableReason failureReason,
            bool failCarrierValidation,
            bool throwUnexpected)
        {
            using (var fixture = new SessionFixture())
            {
                var bgm = fixture.CreatePlaybackEvent(
                    100,
                    OrpheusPlaybackKind.Bgm,
                    OrpheusLoadPolicy.PersistentStream,
                    category: OrpheusCategory.Music);
                fixture.SetEvents(bgm);
                var manager = fixture.CreateReadyManager(out _);
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    9,
                    OrpheusBaseState.Combat,
                    new OrpheusAudioKey(100),
                    OrpheusAudioKey.Invalid));
                if (failCarrierValidation)
                {
                    fixture.Mixer.FailLeaseIdentity(failureReason);
                }
                else if (throwUnexpected)
                {
                    fixture.Mixer.ThrowOnTransition = true;
                }
                else
                {
                    fixture.Mixer.TransitionFailureReason = failureReason;
                }

                manager.SetOverlay(OrpheusOverlay.Menu, true);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(diagnostics.DisableReason, Is.EqualTo(failureReason));
                Assert.That(diagnostics.ProfileId, Is.EqualTo(9));
                Assert.That(diagnostics.BaseState, Is.EqualTo(OrpheusBaseState.Combat));
                Assert.That(diagnostics.Overlay, Is.EqualTo(OrpheusOverlay.Menu));
                Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Menu));
                Assert.That(diagnostics.DesiredBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void TerminalProfileAndOverlayCalls_AreNoOps()
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    3,
                    OrpheusBaseState.Combat,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));
                manager.SetOverlay(OrpheusOverlay.Menu, true);
                DisposeBound(fixture, manager);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                fixture.ResetMixerCalls();

                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    4,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));
                manager.SetOverlay(OrpheusOverlay.Pause, true);

                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                Assert.That(after.ProfileId, Is.EqualTo(before.ProfileId));
                Assert.That(after.BaseState, Is.EqualTo(before.BaseState));
                Assert.That(after.Overlay, Is.EqualTo(before.Overlay));
                Assert.That(after.EffectiveSnapshot, Is.EqualTo(before.EffectiveSnapshot));
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);
            }
        }

        [Test]
        public void PreActivationProfileAndOverlayHotPaths_AllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateManagerBeforeHostReady(out _);
                var intent = new OrpheusAudioProfileIntent(
                    6,
                    OrpheusBaseState.Combat,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid);
                var alternateIntent = new OrpheusAudioProfileIntent(
                    7,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid);
                manager.ApplyProfile(intent);
                manager.SetOverlay(OrpheusOverlay.Pause, true);

                var before = GC.GetAllocatedBytesForCurrentThread();
                manager.ApplyProfile(alternateIntent);
                manager.SetOverlay(OrpheusOverlay.Pause, false);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.ProfileId, Is.EqualTo(7));
                Assert.That(diagnostics.BaseState, Is.EqualTo(OrpheusBaseState.Peace));
                Assert.That(diagnostics.Overlay, Is.EqualTo(OrpheusOverlay.None));
                Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Peace));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PauseOverlay_DoesNotSuspendLoadedUiOneShot()
        {
            using (var fixture = new SessionFixture())
            {
                var ui = fixture.CreatePlaybackEvent(10);
                fixture.SetEvents(ui);
                var manager = fixture.CreateReadyManager(out _);
                manager.SetOverlay(OrpheusOverlay.Pause, true);

                manager.Play(new OrpheusAudioKey(10));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Pause));
                Assert.That(diagnostics.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                Assert.That(diagnostics.Transient2DActiveCount, Is.EqualTo(1));
                Assert.That(fixture.Sources[12].outputAudioMixerGroup.name, Is.EqualTo("SFX_UI_State"));
                DisposeBound(fixture, manager);
            }
        }

        private static void AssertPreActivationSnapshot(bool hostReadyFirst)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateManagerBeforeHostReady(out _);
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    1,
                    OrpheusBaseState.Combat,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));
                manager.SetOverlay(OrpheusOverlay.Menu, true);
                manager.SetOverlay(OrpheusOverlay.Pause, true);
                var authority = manager.CaptureBootstrapAuthority();

                if (hostReadyFirst)
                {
                    Assert.That(manager.CompleteHostReady(),
                        Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                    Assert.That(fixture.Mixer.Snapshots, Is.Empty);
                    Assert.That(manager.CompleteBootstrapHydration(authority), Is.True);
                }
                else
                {
                    Assert.That(manager.CompleteBootstrapHydration(authority), Is.True);
                    Assert.That(fixture.Mixer.Snapshots, Is.Empty);
                    Assert.That(manager.CompleteHostReady(),
                        Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                }

                Assert.That(fixture.Mixer.Snapshots,
                    Is.EqualTo(new[] { OrpheusEffectiveSnapshot.Pause }));
                Assert.That(fixture.Mixer.TransitionSeconds, Is.EqualTo(new[] { 0f }));
                DisposeBound(fixture, manager);
            }
        }

        private static void AssertInvalidDuration(float bgm, float ambience, float snapshot)
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetProfileDurations(bgm, ambience, snapshot);
                var result = fixture.Create(out var manager);
                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidSettings));
                Assert.That(manager, Is.Null);
            }
        }

        private sealed partial class SessionFixture
        {
            internal void SetProfileDurations(float bgm, float ambience, float snapshot)
            {
                SetField(_settings, "_bgmCrossfadeSeconds", bgm);
                SetField(_settings, "_profileAmbienceCrossfadeSeconds", ambience);
                SetField(_settings, "_snapshotTransitionSeconds", snapshot);
            }

            internal void ResetMixerCalls()
            {
                _mixer.ResetCalls();
            }
        }
    }
}
