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
        private const float GlobalLoopFadeSeconds = 0.015f;

        [Test]
        public void GlobalLoop_PublicSurfaceHasOnlyVoidKeyControl()
        {
            AssertVoidKeyMethod(typeof(OrpheusAudioManager), "PlayLoop");
            AssertVoidKeyMethod(typeof(OrpheusAudioManager), "StopLoop");
            Assert.That(typeof(OrpheusAudioBridge).GetMethod("PlayLoop"), Is.Null);
            Assert.That(typeof(OrpheusAudioBridge).GetMethod("StopLoop"), Is.Null);
            Assert.That(typeof(OrpheusAudioRawBridge).GetMethod("PlayLoop"), Is.Null);
            Assert.That(typeof(OrpheusAudioRawBridge).GetMethod("StopLoop"), Is.Null);
        }

        [Test]
        public void GlobalLoop_FourAllowedCategoriesUseFourDedicatedSourcesAndRoutes()
        {
            using (var fixture = new SessionFixture())
            {
                var combat = fixture.CreateGlobalLoopEvent(400, OrpheusCategory.SfxCombat);
                var world = fixture.CreateGlobalLoopEvent(401, OrpheusCategory.SfxWorld);
                var ui = fixture.CreateGlobalLoopEvent(402, OrpheusCategory.SfxUi);
                var ambience = fixture.CreateGlobalLoopEvent(403, OrpheusCategory.Ambience);
                fixture.SetEvents(combat, world, ui, ambience);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();

                manager.PlayLoop(new OrpheusAudioKey(400));
                manager.PlayLoop(new OrpheusAudioKey(401));
                manager.PlayLoop(new OrpheusAudioKey(402));
                manager.PlayLoop(new OrpheusAudioKey(403));
                manager.Tick(realtime, 0f);

                AssertLoopSource(fixture.FindGlobalLoopSource(combat.GetClip(0)), "SFX_Combat_State");
                AssertLoopSource(fixture.FindGlobalLoopSource(world.GetClip(0)), "SFX_World_State");
                AssertLoopSource(fixture.FindGlobalLoopSource(ui.GetClip(0)), "SFX_UI_State");
                AssertLoopSource(fixture.FindGlobalLoopSource(ambience.GetClip(0)), "Ambience_State");
                for (var sourceIndex = 0; sourceIndex < SessionFixture.FirstGlobalLoopSourceIndex; sourceIndex++)
                {
                    AssertNormalized(fixture.Sources[sourceIndex]);
                }

                AssertGlobalLoopDiagnostics(manager, 4, 0, 0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_MissingOrWrongKindKeyCannotReserveLoadOrStart()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreatePlaybackEvent(410),
                    fixture.CreateBgmEvent(411));
                var manager = fixture.CreateReadyManager(out _);

                manager.PlayLoop(OrpheusAudioKey.Invalid);
                manager.PlayLoop(new OrpheusAudioKey(999));
                manager.PlayLoop(new OrpheusAudioKey(410));
                manager.PlayLoop(new OrpheusAudioKey(411));
                manager.StopLoop(OrpheusAudioKey.Invalid);
                manager.StopLoop(new OrpheusAudioKey(999));
                manager.StopLoop(new OrpheusAudioKey(410));
                manager.StopLoop(new OrpheusAudioKey(411));
                manager.Tick(GlobalLoopRealtime(), 0f);

                Assert.That(fixture.GetClipRequestCount(0), Is.Zero);
                Assert.That(fixture.GetClipRequestCount(1), Is.Zero);
                fixture.AssertAllGlobalLoopSourcesNormalized();
                AssertGlobalLoopDiagnostics(manager, 0, 0, 0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_RecordsIntentAndLoadsBeforeReadyButStartsOnlyWhenLoadedAndPlaybackReady()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(420);
                fixture.SetEvents(loop);
                fixture.SetClipState(0, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateManagerBeforeHostReady(out _);
                var realtime = GlobalLoopRealtime();

                manager.PlayLoop(new OrpheusAudioKey(420));
                manager.PlayLoop(new OrpheusAudioKey(420));
                Assert.That(fixture.GetClipRequestCount(0), Is.EqualTo(1));
                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                manager.Tick(realtime, 0f);
                fixture.AssertAllGlobalLoopSourcesNormalized();
                AssertGlobalLoopDiagnostics(manager, 0, 0, 0);

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                manager.Tick(realtime + 0.01d, 0.01f);
                fixture.AssertAllGlobalLoopSourcesNormalized();

                Assert.That(
                    manager.CompleteBootstrapHydration(manager.CaptureBootstrapAuthority()),
                    Is.True);
                manager.Tick(realtime + 0.02d, 0.01f);

                Assert.That(fixture.GlobalLoop(0).clip, Is.SameAs(loop.GetClip(0)));
                AssertGlobalLoopDiagnostics(manager, 1, 0, 0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_RepeatingActiveKeyDoesNotRestartOrDuplicate()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(430);
                fixture.SetEvents(loop);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();
                manager.PlayLoop(new OrpheusAudioKey(430));
                manager.Tick(realtime, 0f);
                var source = fixture.FindGlobalLoopSource(loop.GetClip(0));
                source.timeSamples = 2048;

                manager.PlayLoop(new OrpheusAudioKey(430));
                manager.PlayLoop(new OrpheusAudioKey(430));

                Assert.That(source.clip, Is.SameAs(loop.GetClip(0)));
                Assert.That(source.timeSamples, Is.EqualTo(2048));
                Assert.That(source.volume, Is.EqualTo(1f));
                Assert.That(fixture.GetClipRequestCount(0), Is.Zero);
                AssertGlobalLoopDiagnostics(manager, 1, 0, 0);
                fixture.AssertOtherGlobalLoopSourcesNormalized(source);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_FailedLoadCountsOnceAndExplicitRetryRequestsExactlyOnce()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateGlobalLoopEvent(440));
                fixture.SetClipState(0, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();

                manager.PlayLoop(new OrpheusAudioKey(440));
                Assert.That(fixture.GetClipRequestCount(0), Is.EqualTo(1));
                fixture.SetClipState(0, OrpheusClipLoadState.Loading);
                manager.Tick(realtime, 0f);
                fixture.SetClipState(0, OrpheusClipLoadState.Failed);
                manager.Tick(realtime + 0.01d, 0.01f);
                manager.Tick(realtime + 0.02d, 0.01f);
                manager.Tick(realtime + 0.03d, 0.01f);
                AssertGlobalLoopDiagnostics(manager, 0, 1, 0);

                manager.PlayLoop(new OrpheusAudioKey(440));
                Assert.That(fixture.GetClipRequestCount(0), Is.EqualTo(2));
                fixture.SetClipState(0, OrpheusClipLoadState.Loading);
                manager.PlayLoop(new OrpheusAudioKey(440));
                Assert.That(fixture.GetClipRequestCount(0), Is.EqualTo(2));
                manager.Tick(realtime + 0.04d, 0.01f);
                AssertGlobalLoopDiagnostics(manager, 0, 1, 0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_FailedKeyCannotStartAfterLateLoadedWithoutExplicitRetry()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(450);
                fixture.SetEvents(loop);
                fixture.SetClipState(0, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();
                manager.PlayLoop(new OrpheusAudioKey(450));
                fixture.SetClipState(0, OrpheusClipLoadState.Failed);
                manager.Tick(realtime, 0f);

                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                manager.Tick(realtime + 0.01d, 0.01f);
                fixture.AssertAllGlobalLoopSourcesNormalized();
                AssertGlobalLoopDiagnostics(manager, 0, 1, 0);

                manager.PlayLoop(new OrpheusAudioKey(450));
                manager.Tick(realtime + 0.02d, 0.01f);

                Assert.That(fixture.GlobalLoop(0).clip, Is.SameAs(loop.GetClip(0)));
                AssertGlobalLoopDiagnostics(manager, 1, 1, 0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_FifthDistinctKeyRejectsOnceWhileFourEntriesAreOccupied()
        {
            using (var fixture = new SessionFixture())
            {
                var active = fixture.CreateGlobalLoopEvent(460);
                var loading = fixture.CreateGlobalLoopEvent(461);
                var failed = fixture.CreateGlobalLoopEvent(462);
                var stopping = fixture.CreateGlobalLoopEvent(463);
                var rejected = fixture.CreateGlobalLoopEvent(464);
                fixture.SetEvents(active, loading, failed, stopping, rejected);
                fixture.SetClipState(1, OrpheusClipLoadState.Unloaded);
                fixture.SetClipState(2, OrpheusClipLoadState.Unloaded);
                fixture.SetClipState(4, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();

                manager.PlayLoop(new OrpheusAudioKey(460));
                manager.PlayLoop(new OrpheusAudioKey(461));
                manager.PlayLoop(new OrpheusAudioKey(462));
                manager.PlayLoop(new OrpheusAudioKey(463));
                fixture.SetClipState(1, OrpheusClipLoadState.Loading);
                fixture.SetClipState(2, OrpheusClipLoadState.Failed);
                manager.Tick(realtime, 0f);
                manager.StopLoop(new OrpheusAudioKey(463));

                manager.PlayLoop(new OrpheusAudioKey(464));

                Assert.That(fixture.GetClipRequestCount(4), Is.Zero);
                AssertGlobalLoopDiagnostics(manager, 2, 1, 1);
                manager.PlayLoop(new OrpheusAudioKey(460));
                manager.PlayLoop(new OrpheusAudioKey(461));
                manager.PlayLoop(new OrpheusAudioKey(462));
                manager.PlayLoop(new OrpheusAudioKey(463));
                Assert.That(manager.TryGetDiagnostics(out var afterSameKeys), Is.True);
                Assert.That(afterSameKeys.Counters.LoopRegistryFull, Is.EqualTo(1));
                Assert.That(fixture.GetClipRequestCount(2), Is.EqualTo(2));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_StopLoadingFreesIntentAndRejectsLateCompletionGeneration()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateGlobalLoopEvent(470);
                var b = fixture.CreateGlobalLoopEvent(471);
                fixture.SetEvents(a, b);
                fixture.SetClipState(0, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();

                manager.PlayLoop(new OrpheusAudioKey(470));
                fixture.SetClipState(0, OrpheusClipLoadState.Loading);
                manager.Tick(realtime, 0f);
                manager.StopLoop(new OrpheusAudioKey(470));
                manager.PlayLoop(new OrpheusAudioKey(471));
                manager.Tick(realtime + 0.01d, 0.01f);

                Assert.That(fixture.GlobalLoop(0).clip, Is.SameAs(b.GetClip(0)));
                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                manager.Tick(realtime + 0.02d, 0.01f);

                Assert.That(fixture.GlobalLoop(0).clip, Is.SameAs(b.GetClip(0)));
                Assert.That(fixture.FindGlobalLoopSourceOrNull(a.GetClip(0)), Is.Null);
                AssertGlobalLoopDiagnostics(manager, 1, 0, 0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_StoppingRetainsPhysicalAndRegistryCapacityUntilExactFifteenMilliseconds()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateGlobalLoopEvent(480);
                var b = fixture.CreateGlobalLoopEvent(481);
                var c = fixture.CreateGlobalLoopEvent(482);
                var d = fixture.CreateGlobalLoopEvent(483);
                var e = fixture.CreateGlobalLoopEvent(484);
                fixture.SetEvents(a, b, c, d, e);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();
                PlayFourLoops(manager, 480);
                manager.Tick(realtime, 0f);
                var source = fixture.FindGlobalLoopSource(a.GetClip(0));

                manager.StopLoop(new OrpheusAudioKey(480));
                manager.PlayLoop(new OrpheusAudioKey(484));
                manager.Tick(realtime + 0.0149d, 0.0149f);

                Assert.That(source.clip, Is.SameAs(a.GetClip(0)));
                Assert.That(source.volume, Is.GreaterThan(0f));
                AssertGlobalLoopDiagnostics(manager, 4, 0, 1);

                manager.Tick(realtime + 0.0151d, 0.0002f);

                AssertNormalized(source);
                AssertGlobalLoopDiagnostics(manager, 3, 0, 1);
                manager.PlayLoop(new OrpheusAudioKey(484));
                manager.Tick(realtime + 0.016d, 0.0009f);
                Assert.That(source.clip, Is.SameAs(e.GetClip(0)));
                AssertGlobalLoopDiagnostics(manager, 4, 0, 1);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_PlayDuringStoppingResumesGainWithoutClipRestartOrStaleRelease()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(490);
                fixture.SetEvents(loop);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();
                manager.PlayLoop(new OrpheusAudioKey(490));
                manager.Tick(realtime, 0f);
                var source = fixture.FindGlobalLoopSource(loop.GetClip(0));
                source.timeSamples = 4096;
                manager.StopLoop(new OrpheusAudioKey(490));
                manager.Tick(realtime + 0.0075d, 0.0075f);
                var gain = source.volume;
                var sample = source.timeSamples;

                manager.PlayLoop(new OrpheusAudioKey(490));

                Assert.That(source.clip, Is.SameAs(loop.GetClip(0)));
                Assert.That(source.volume, Is.EqualTo(gain));
                Assert.That(source.timeSamples, Is.EqualTo(sample));
                manager.Tick(realtime + 0.015d, 0.0075f);
                Assert.That(source.clip, Is.SameAs(loop.GetClip(0)));
                Assert.That(source.timeSamples, Is.GreaterThan(0));
                Assert.That(source.volume, Is.GreaterThan(gain));
                manager.Tick(realtime + 0.03d, 0.015f);
                Assert.That(source.clip, Is.SameAs(loop.GetClip(0)));
                Assert.That(source.volume, Is.EqualTo(1f));
                AssertGlobalLoopDiagnostics(manager, 1, 0, 0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_LatestIntentWinsDuringStoppingReactivation()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(495);
                fixture.SetEvents(loop);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();
                var key = new OrpheusAudioKey(495);
                manager.PlayLoop(key);
                manager.Tick(realtime, 0f);
                var source = fixture.FindGlobalLoopSource(loop.GetClip(0));
                source.timeSamples = 2048;

                manager.StopLoop(key);
                manager.Tick(realtime + 0.0075d, 0.0075f);
                manager.PlayLoop(key);
                manager.Tick(realtime + 0.0125d, 0.005f);
                var firstRecoveryGain = source.volume;

                manager.PlayLoop(key);
                manager.Tick(realtime + 0.0175d, 0.005f);

                Assert.That(source.volume, Is.GreaterThan(0.8f));
                Assert.That(source.volume, Is.GreaterThan(firstRecoveryGain));
                Assert.That(source.timeSamples, Is.GreaterThan(0));

                manager.StopLoop(key);
                manager.Tick(realtime + 0.0324d, 0.0149f);
                Assert.That(source.clip, Is.SameAs(loop.GetClip(0)));
                Assert.That(source.volume, Is.GreaterThan(0f));
                AssertGlobalLoopDiagnostics(manager, 1, 0, 0);

                manager.Tick(realtime + 0.0325d, 0.0001f);
                AssertNormalized(source);
                AssertGlobalLoopDiagnostics(manager, 0, 0, 0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_SlotReuseRejectsStaleStartAndReleaseAcrossDifferentKeys()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateGlobalLoopEvent(500);
                var b = fixture.CreateGlobalLoopEvent(501);
                fixture.SetEvents(a, b);
                fixture.SetClipState(0, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();
                manager.PlayLoop(new OrpheusAudioKey(500));
                fixture.SetClipState(0, OrpheusClipLoadState.Loading);
                manager.Tick(realtime, 0f);
                manager.StopLoop(new OrpheusAudioKey(500));
                manager.PlayLoop(new OrpheusAudioKey(501));
                manager.Tick(realtime + 0.001d, 0.001f);
                var source = fixture.FindGlobalLoopSource(b.GetClip(0));
                source.timeSamples = 1024;

                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                manager.StopLoop(new OrpheusAudioKey(501));
                manager.Tick(realtime + 0.0085d, 0.0075f);
                manager.PlayLoop(new OrpheusAudioKey(501));
                manager.Tick(realtime + 0.016d, 0.0075f);
                manager.Tick(realtime + 0.031d, 0.015f);

                Assert.That(source.clip, Is.SameAs(b.GetClip(0)));
                Assert.That(source.timeSamples, Is.GreaterThan(0));
                Assert.That(fixture.FindGlobalLoopSourceOrNull(a.GetClip(0)), Is.Null);
                AssertGlobalLoopDiagnostics(manager, 1, 0, 0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_WrongThreadPlayAndStopOnlyIncrementWrongThreadRejected()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateGlobalLoopEvent(510);
                var b = fixture.CreateGlobalLoopEvent(511);
                var c = fixture.CreateGlobalLoopEvent(512);
                fixture.SetEvents(a, b, c);
                fixture.SetClipState(1, OrpheusClipLoadState.Unloaded);
                fixture.SetClipState(2, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();
                manager.PlayLoop(new OrpheusAudioKey(510));
                manager.PlayLoop(new OrpheusAudioKey(511));
                fixture.SetClipState(1, OrpheusClipLoadState.Loading);
                manager.Tick(realtime, 0f);
                var source = fixture.FindGlobalLoopSource(a.GetClip(0));
                source.timeSamples = 2048;
                var clipBefore = source.clip;
                var gainBefore = source.volume;
                var bRequests = fixture.GetClipRequestCount(1);
                var cRequests = fixture.GetClipRequestCount(2);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                var worker = new Thread(() =>
                {
                    manager.StopLoop(new OrpheusAudioKey(510));
                    manager.PlayLoop(new OrpheusAudioKey(512));
                });
                worker.Start();
                worker.Join();

                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                Assert.That(
                    after.Counters.WrongThreadRejected,
                    Is.EqualTo(before.Counters.WrongThreadRejected + 2));
                AssertDiagnosticsEqualExceptWrongThread(before, after);
                Assert.That(source.clip, Is.SameAs(clipBefore));
                Assert.That(source.volume, Is.EqualTo(gainBefore));
                Assert.That(fixture.GetClipRequestCount(1), Is.EqualTo(bRequests));
                Assert.That(fixture.GetClipRequestCount(2), Is.EqualTo(cRequests));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_DiagnosticsTrackPhysicalSourcesFailuresAndFullRejection()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreateGlobalLoopEvent(520),
                    fixture.CreateGlobalLoopEvent(521),
                    fixture.CreateGlobalLoopEvent(522),
                    fixture.CreateGlobalLoopEvent(523),
                    fixture.CreateGlobalLoopEvent(524));
                fixture.SetClipState(1, OrpheusClipLoadState.Unloaded);
                fixture.SetClipState(2, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();
                manager.PlayLoop(new OrpheusAudioKey(520));
                manager.PlayLoop(new OrpheusAudioKey(521));
                manager.PlayLoop(new OrpheusAudioKey(522));
                manager.PlayLoop(new OrpheusAudioKey(523));
                fixture.SetClipState(1, OrpheusClipLoadState.Loading);
                fixture.SetClipState(2, OrpheusClipLoadState.Failed);
                manager.Tick(realtime, 0f);
                manager.StopLoop(new OrpheusAudioKey(523));
                manager.PlayLoop(new OrpheusAudioKey(524));

                AssertGlobalLoopDiagnostics(manager, 2, 1, 1);
                manager.Tick(realtime + GlobalLoopFadeSeconds, GlobalLoopFadeSeconds);
                AssertGlobalLoopDiagnostics(manager, 1, 1, 1);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void GlobalLoop_DisposeImmediatelyInvalidatesIntentAndNormalizesAllDedicatedSources()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreateGlobalLoopEvent(530),
                    fixture.CreateGlobalLoopEvent(531),
                    fixture.CreateGlobalLoopEvent(532),
                    fixture.CreateGlobalLoopEvent(533));
                fixture.SetClipState(1, OrpheusClipLoadState.Unloaded);
                fixture.SetClipState(2, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();
                manager.PlayLoop(new OrpheusAudioKey(530));
                manager.PlayLoop(new OrpheusAudioKey(531));
                manager.PlayLoop(new OrpheusAudioKey(532));
                manager.PlayLoop(new OrpheusAudioKey(533));
                fixture.SetClipState(1, OrpheusClipLoadState.Loading);
                fixture.SetClipState(2, OrpheusClipLoadState.Failed);
                manager.Tick(realtime, 0f);
                manager.StopLoop(new OrpheusAudioKey(533));
                var requestCount = fixture.GetClipRequestCount(1);

                manager.Dispose();
                manager.PlayLoop(new OrpheusAudioKey(531));
                manager.StopLoop(new OrpheusAudioKey(530));
                fixture.SetClipState(1, OrpheusClipLoadState.Loaded);
                manager.Tick(realtime + 1d, 1f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disposed));
                Assert.That(diagnostics.TransportState, Is.EqualTo(OrpheusTransportState.Disposed));
                Assert.That(diagnostics.GlobalLoopActiveCount, Is.Zero);
                Assert.That(fixture.GetClipRequestCount(1), Is.EqualTo(requestCount));
                fixture.AssertAllGlobalLoopSourcesNormalized();
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void GlobalLoop_SourceReferenceChangeFailClosesAndBlocksLateLoad()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateGlobalLoopEvent(540);
                var b = fixture.CreateGlobalLoopEvent(541);
                fixture.SetEvents(a, b);
                fixture.SetClipState(1, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();
                manager.PlayLoop(new OrpheusAudioKey(540));
                manager.PlayLoop(new OrpheusAudioKey(541));
                fixture.SetClipState(1, OrpheusClipLoadState.Loading);
                manager.Tick(realtime, 0f);
                manager.StopLoop(new OrpheusAudioKey(540));
                fixture.ReplaceGlobalLoopZeroReference();

                manager.Tick(realtime + 0.01d, 0.01f);

                Assert.That(manager.TryGetDiagnostics(out var disabled), Is.True);
                Assert.That(disabled.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    disabled.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.OwnedSourceReferenceChanged));
                Assert.That(disabled.GlobalLoopActiveCount, Is.Zero);
                fixture.AssertAllGlobalLoopSourcesNormalized();
                fixture.SetClipState(1, OrpheusClipLoadState.Loaded);
                manager.Tick(realtime + 0.02d, 0.01f);
                fixture.AssertAllGlobalLoopSourcesNormalized();

                manager.Dispose();
                Assert.That(manager.TryGetDiagnostics(out var disposed), Is.True);
                Assert.That(disposed.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disposed));
                Assert.That(
                    disposed.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.OwnedSourceReferenceChanged));
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void GlobalLoopAcceptedRejectedStableAndFadeHotPathsAllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreateGlobalLoopEvent(550),
                    fixture.CreateGlobalLoopEvent(551),
                    fixture.CreateGlobalLoopEvent(552),
                    fixture.CreateGlobalLoopEvent(553),
                    fixture.CreateGlobalLoopEvent(554),
                    fixture.CreateGlobalLoopEvent(555));
                fixture.SetClipState(1, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = GlobalLoopRealtime();

                manager.PlayLoop(new OrpheusAudioKey(555));
                manager.Tick(realtime, 0f);
                manager.StopLoop(new OrpheusAudioKey(555));
                manager.Tick(realtime + 0.02d, 0.02f);

                var beforeNewPlay = GC.GetAllocatedBytesForCurrentThread();
                manager.PlayLoop(new OrpheusAudioKey(550));
                var newPlayBytes = GC.GetAllocatedBytesForCurrentThread() - beforeNewPlay;
                manager.Tick(realtime + 0.03d, 0.01f);
                manager.PlayLoop(new OrpheusAudioKey(551));
                fixture.SetClipState(1, OrpheusClipLoadState.Loading);

                var beforeLoadingDuplicate = GC.GetAllocatedBytesForCurrentThread();
                manager.PlayLoop(new OrpheusAudioKey(551));
                var loadingDuplicateBytes =
                    GC.GetAllocatedBytesForCurrentThread() - beforeLoadingDuplicate;
                var beforeActiveDuplicate = GC.GetAllocatedBytesForCurrentThread();
                manager.PlayLoop(new OrpheusAudioKey(550));
                var activeDuplicateBytes =
                    GC.GetAllocatedBytesForCurrentThread() - beforeActiveDuplicate;

                manager.PlayLoop(new OrpheusAudioKey(552));
                manager.PlayLoop(new OrpheusAudioKey(553));
                manager.Tick(realtime + 0.04d, 0.01f);
                var beforeFull = GC.GetAllocatedBytesForCurrentThread();
                manager.PlayLoop(new OrpheusAudioKey(554));
                var fullBytes = GC.GetAllocatedBytesForCurrentThread() - beforeFull;
                var beforeStop = GC.GetAllocatedBytesForCurrentThread();
                manager.StopLoop(new OrpheusAudioKey(550));
                var stopBytes = GC.GetAllocatedBytesForCurrentThread() - beforeStop;
                var beforeStoppingTick = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.0475d, 0.0075f);
                var stoppingTickBytes =
                    GC.GetAllocatedBytesForCurrentThread() - beforeStoppingTick;
                var beforeReactivate = GC.GetAllocatedBytesForCurrentThread();
                manager.PlayLoop(new OrpheusAudioKey(550));
                var reactivateBytes = GC.GetAllocatedBytesForCurrentThread() - beforeReactivate;
                var beforeReactivationTick = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.055d, 0.0075f);
                var reactivationTickBytes =
                    GC.GetAllocatedBytesForCurrentThread() - beforeReactivationTick;
                manager.Tick(realtime + 0.07d, 0.015f);
                var beforeActiveTick = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.08d, 0.01f);
                var activeTickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeActiveTick;

                Assert.That(newPlayBytes, Is.Zero, "new PlayLoop");
                Assert.That(loadingDuplicateBytes, Is.Zero, "Loading duplicate");
                Assert.That(activeDuplicateBytes, Is.Zero, "active duplicate");
                Assert.That(fullBytes, Is.Zero, "full rejection");
                Assert.That(stopBytes, Is.Zero, "StopLoop");
                Assert.That(stoppingTickBytes, Is.Zero, "Stopping Tick");
                Assert.That(reactivateBytes, Is.Zero, "Stopping reactivation");
                Assert.That(reactivationTickBytes, Is.Zero, "reactivation Tick");
                Assert.That(activeTickBytes, Is.Zero, "active Tick");
                DisposeBound(fixture, manager);
            }
        }

        private static double GlobalLoopRealtime()
        {
            return Time.realtimeSinceStartupAsDouble + 0.1d;
        }

        private static void AssertVoidKeyMethod(Type type, string name)
        {
            var members = type.GetMember(
                name,
                MemberTypes.Method,
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(members, Has.Length.EqualTo(1), name);
            var method = (MethodInfo)members[0];
            Assert.That(method.ReturnType, Is.EqualTo(typeof(void)), name);
            var parameters = method.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(1), name);
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(OrpheusAudioKey)), name);
        }

        private static void AssertLoopSource(AudioSource source, string expectedGroup)
        {
            Assert.That(source, Is.Not.Null);
            Assert.That(source.loop, Is.True);
            Assert.That(source.pitch, Is.EqualTo(1f));
            Assert.That(source.spatialBlend, Is.EqualTo(0f));
            Assert.That(source.outputAudioMixerGroup, Is.Not.Null);
            Assert.That(source.outputAudioMixerGroup.name, Is.EqualTo(expectedGroup));
        }

        private static void AssertGlobalLoopDiagnostics(
            OrpheusAudioManager manager,
            byte expectedPhysicalCount,
            ulong expectedLoadFailed,
            ulong expectedRegistryFull)
        {
            Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
            Assert.That(diagnostics.GlobalLoopActiveCount, Is.EqualTo(expectedPhysicalCount));
            Assert.That(diagnostics.Counters.LoadFailed, Is.EqualTo(expectedLoadFailed));
            Assert.That(diagnostics.Counters.LoopRegistryFull, Is.EqualTo(expectedRegistryFull));
        }

        private static void PlayFourLoops(OrpheusAudioManager manager, ushort firstKey)
        {
            manager.PlayLoop(new OrpheusAudioKey(firstKey));
            manager.PlayLoop(new OrpheusAudioKey((ushort)(firstKey + 1)));
            manager.PlayLoop(new OrpheusAudioKey((ushort)(firstKey + 2)));
            manager.PlayLoop(new OrpheusAudioKey((ushort)(firstKey + 3)));
        }

        private sealed partial class SessionFixture
        {
            internal const int FirstGlobalLoopSourceIndex = 20;

            internal AudioSource GlobalLoop(int index)
            {
                return Sources[FirstGlobalLoopSourceIndex + index];
            }

            internal OrpheusAudioEvent CreateGlobalLoopEvent(
                ushort key,
                OrpheusCategory category = OrpheusCategory.SfxWorld,
                float volume = 1f)
            {
                return CreatePlaybackEvent(
                    key,
                    OrpheusPlaybackKind.GlobalLoop2D,
                    OrpheusLoadPolicy.PersistentStream,
                    volume: volume,
                    category: category);
            }

            internal AudioSource FindGlobalLoopSource(AudioClip clip)
            {
                var source = FindGlobalLoopSourceOrNull(clip);
                Assert.That(source, Is.Not.Null);
                return source;
            }

            internal AudioSource FindGlobalLoopSourceOrNull(AudioClip clip)
            {
                for (var index = 0; index < OrpheusAudioSourceBank.GlobalLoopCount; index++)
                {
                    var source = GlobalLoop(index);
                    if (ReferenceEquals(source.clip, clip))
                    {
                        return source;
                    }
                }

                return null;
            }

            internal void AssertAllGlobalLoopSourcesNormalized()
            {
                for (var index = 0; index < OrpheusAudioSourceBank.GlobalLoopCount; index++)
                {
                    AssertNormalized(GlobalLoop(index));
                }
            }

            internal void AssertOtherGlobalLoopSourcesNormalized(AudioSource active)
            {
                for (var index = 0; index < OrpheusAudioSourceBank.GlobalLoopCount; index++)
                {
                    var source = GlobalLoop(index);
                    if (!ReferenceEquals(source, active))
                    {
                        AssertNormalized(source);
                    }
                }
            }

            internal void ReplaceGlobalLoopZeroReference()
            {
                var replacementObject = new GameObject("GlobalLoop_00");
                replacementObject.transform.SetParent(Host.transform, false);
                var replacement = replacementObject.AddComponent<AudioSource>();
                SetField(
                    Bank,
                    "_globalLoop",
                    new[] { replacement, GlobalLoop(1), GlobalLoop(2), GlobalLoop(3) });
            }
        }
    }
}
