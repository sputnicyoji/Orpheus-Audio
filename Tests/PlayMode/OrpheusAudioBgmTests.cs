using System;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Tests
{
    public sealed partial class OrpheusAudioSessionTests
    {
        [Test]
        public void ProfileBgm_LoadsBeforeReadyAndStartsOnlyAfterPlaybackReadyAndLoaded()
        {
            using (var fixture = new SessionFixture())
            {
                var bgm = fixture.CreateBgmEvent(100);
                fixture.SetEvents(bgm);
                fixture.SetClipState(0, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateManagerBeforeHostReady(out _);
                var intent = BgmIntent(1, 100);

                manager.ApplyProfile(intent);
                manager.ApplyProfile(intent);

                Assert.That(fixture.GetClipRequestCount(0), Is.EqualTo(1));
                Assert.That(fixture.BgmZero.clip, Is.Null);
                Assert.That(fixture.BgmOne.clip, Is.Null);
                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(100),
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid,
                    0);

                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.Tick(realtime, 0.016f);
                Assert.That(fixture.BgmZero.clip, Is.Null);

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                Assert.That(
                    manager.CompleteBootstrapHydration(manager.CaptureBootstrapAuthority()),
                    Is.True);
                manager.Tick(realtime + 0.1d, 0.1f);

                Assert.That(fixture.BgmZero.clip, Is.SameAs(bgm.GetClip(0)));
                Assert.That(fixture.BgmZero.loop, Is.True);
                Assert.That(fixture.BgmZero.pitch, Is.EqualTo(1f));
                Assert.That(fixture.BgmZero.outputAudioMixerGroup.name, Is.EqualTo("Music_State"));
                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(100),
                    new OrpheusAudioKey(100),
                    OrpheusAudioKey.Invalid,
                    1);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileBgm_CrossfadesEqualPowerAndPromotesOnlyLatestQueuedTarget()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateBgmEvent(100);
                var b = fixture.CreateBgmEvent(101);
                var c = fixture.CreateBgmEvent(102);
                var d = fixture.CreateBgmEvent(103);
                fixture.SetEvents(a, b, c, d);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(BgmIntent(1, 100));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                Assert.That(fixture.BgmZero.volume, Is.EqualTo(1f).Within(0.000001f));

                manager.ApplyProfile(BgmIntent(2, 101));
                manager.Tick(realtime + 0.41d, 0.01f);
                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(101),
                    new OrpheusAudioKey(100),
                    new OrpheusAudioKey(101),
                    2);

                manager.Tick(realtime + 0.61d, 0.2f);
                var midpoint = (float)Math.Sqrt(0.5d);
                Assert.That(fixture.BgmZero.volume, Is.EqualTo(midpoint).Within(0.001f));
                Assert.That(fixture.BgmOne.volume, Is.EqualTo(midpoint).Within(0.001f));

                manager.ApplyProfile(BgmIntent(3, 102));
                manager.ApplyProfile(BgmIntent(4, 103));
                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(103),
                    new OrpheusAudioKey(100),
                    new OrpheusAudioKey(101),
                    2);

                manager.Tick(realtime + 0.81d, 0.2f);

                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(103),
                    new OrpheusAudioKey(101),
                    new OrpheusAudioKey(103),
                    2);
                Assert.That(fixture.BgmZero.clip, Is.SameAs(d.GetClip(0)));
                Assert.That(fixture.BgmOne.clip, Is.SameAs(b.GetClip(0)));
                Assert.That(fixture.BgmZero.clip, Is.Not.SameAs(c.GetClip(0)));
                for (var sourceIndex = 0; sourceIndex < fixture.Sources.Length; sourceIndex++)
                {
                    if (sourceIndex != OrpheusAudioSourceBank.BgmOffset &&
                        sourceIndex != OrpheusAudioSourceBank.BgmOffset + 1)
                    {
                        AssertNormalized(fixture.Sources[sourceIndex]);
                    }
                }

                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileBgm_RetargetToOutgoingReversesWithoutGainJumpOrRestart()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateBgmEvent(110);
                var b = fixture.CreateBgmEvent(111);
                var c = fixture.CreateBgmEvent(112);
                fixture.SetEvents(a, b, c);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(BgmIntent(1, 110));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(BgmIntent(2, 111));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.61d, 0.2f);
                manager.ApplyProfile(BgmIntent(3, 112));
                var beforeZero = fixture.BgmZero.volume;
                var beforeOne = fixture.BgmOne.volume;

                manager.ApplyProfile(BgmIntent(4, 110));

                Assert.That(fixture.BgmZero.volume, Is.EqualTo(beforeZero).Within(0.000001f));
                Assert.That(fixture.BgmOne.volume, Is.EqualTo(beforeOne).Within(0.000001f));
                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(110),
                    new OrpheusAudioKey(111),
                    new OrpheusAudioKey(110),
                    2);

                manager.Tick(realtime + 0.81d, 0.2f);

                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(110),
                    new OrpheusAudioKey(110),
                    OrpheusAudioKey.Invalid,
                    1);
                Assert.That(fixture.BgmZero.clip, Is.SameAs(a.GetClip(0)));
                Assert.That(fixture.BgmOne.clip, Is.Null);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileBgm_LoadFailureAndStallKeepCurrentAndExplicitRetryRunsOnce()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateBgmEvent(120);
                var b = fixture.CreateBgmEvent(121);
                fixture.SetEvents(a, b);
                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(1, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(BgmIntent(1, 120));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                var failedIntent = BgmIntent(2, 121);
                manager.ApplyProfile(failedIntent);
                Assert.That(fixture.GetClipRequestCount(1), Is.EqualTo(1));

                fixture.SetClipState(1, OrpheusClipLoadState.Loading);
                manager.Tick(realtime + 2.5d, 0.1f);
                fixture.SetClipState(1, OrpheusClipLoadState.Failed);
                manager.Tick(realtime + 2.6d, 0.1f);

                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(121),
                    new OrpheusAudioKey(120),
                    OrpheusAudioKey.Invalid,
                    1);
                Assert.That(manager.TryGetDiagnostics(out var failed), Is.True);
                Assert.That(failed.Counters.LoadStalled, Is.EqualTo(1));
                Assert.That(failed.Counters.LoadFailed, Is.EqualTo(1));

                manager.ApplyProfile(failedIntent);
                manager.ApplyProfile(failedIntent);

                Assert.That(fixture.GetClipRequestCount(1), Is.EqualTo(2));
                Assert.That(fixture.BgmZero.clip, Is.SameAs(a.GetClip(0)));
                Assert.That(fixture.BgmOne.clip, Is.Null);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileBgm_RequestingCurrentCancelsPendingLoadAndItsStallObservation()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateBgmEvent(125);
                fixture.SetEvents(a, fixture.CreateBgmEvent(126));
                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(1, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(BgmIntent(1, 125));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(BgmIntent(2, 126));
                Assert.That(fixture.GetClipRequestCount(1), Is.EqualTo(1));

                manager.ApplyProfile(BgmIntent(3, 125));
                fixture.SetClipState(1, OrpheusClipLoadState.Loading);
                manager.Tick(realtime + 3d, 0.1f);

                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(125),
                    new OrpheusAudioKey(125),
                    OrpheusAudioKey.Invalid,
                    1);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Counters.LoadStalled, Is.Zero);
                Assert.That(fixture.BgmZero.clip, Is.SameAs(a.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileBgm_QueuedFailedRetryDoesNotStaleTheActiveCrossfade()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateBgmEvent(122);
                var b = fixture.CreateBgmEvent(123);
                var c = fixture.CreateBgmEvent(124);
                fixture.SetEvents(a, b, c);
                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(1, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(2, OrpheusClipLoadState.Failed);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(BgmIntent(1, 122));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(BgmIntent(2, 123));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.51d, 0.1f);

                var queued = BgmIntent(3, 124);
                manager.ApplyProfile(queued);
                manager.Tick(realtime + 0.51d, 0f);
                manager.ApplyProfile(queued);
                Assert.That(fixture.GetClipRequestCount(2), Is.EqualTo(2));
                fixture.SetClipState(2, OrpheusClipLoadState.Loaded);

                manager.Tick(realtime + 0.81d, 0.3f);

                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(124),
                    new OrpheusAudioKey(123),
                    new OrpheusAudioKey(124),
                    2);
                Assert.That(fixture.BgmZero.clip, Is.SameAs(c.GetClip(0)));
                Assert.That(fixture.BgmOne.clip, Is.SameAs(b.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileBgm_FailedTargetCannotStartAfterLateLoadedWithoutExplicitRetry()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateBgmEvent(141);
                var b = fixture.CreateBgmEvent(142);
                fixture.SetEvents(a, b);
                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(1, OrpheusClipLoadState.Failed);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(BgmIntent(1, 141));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                var failedIntent = BgmIntent(2, 142);
                manager.ApplyProfile(failedIntent);
                manager.Tick(realtime + 0.41d, 0.01f);
                fixture.SetClipState(1, OrpheusClipLoadState.Loaded);

                manager.Tick(realtime + 0.5d, 0.09f);

                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(142),
                    new OrpheusAudioKey(141),
                    OrpheusAudioKey.Invalid,
                    1);
                Assert.That(fixture.BgmOne.clip, Is.Null);

                manager.ApplyProfile(failedIntent);
                manager.Tick(realtime + 0.51d, 0.01f);

                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(142),
                    new OrpheusAudioKey(141),
                    new OrpheusAudioKey(142),
                    2);
                Assert.That(fixture.BgmOne.clip, Is.SameAs(b.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileBgm_PersistentFailureAndRetryDoNotAdvanceOneShotRng()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetStateGroup(OrpheusCategory.SfxUi, LoadSfxUiStateGroup());
                var bgm = fixture.CreateBgmEvent(143);
                var oneShot = fixture.CreatePlaybackEvent(
                    144,
                    volume: 0.5f,
                    pitch: 0.75f,
                    clipCount: 3,
                    volumeMaximum: 1f,
                    pitchMaximum: 1.25f,
                    polyphonyCap: 3);
                fixture.SetEvents(bgm, oneShot);
                fixture.SetClipState(0, OrpheusClipLoadState.Failed);
                var manager = fixture.CreateReadyManager(out _);
                var intent = BgmIntent(1, 143);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(intent);
                manager.Tick(realtime, 0f);
                manager.ApplyProfile(intent);
                manager.Play(new OrpheusAudioKey(144));

                var source = fixture.Sources[OrpheusAudioSourceBank.Transient2DOffset];
                var expectedVolume = 0.5f +
                                     (0.5f * ((0x9DCCA8C5u >> 8) * (1f / 16777216f)));
                var expectedPitch = 0.75f +
                                    (0.5f * ((0x1255994Fu >> 8) * (1f / 16777216f)));
                Assert.That(source.clip, Is.SameAs(oneShot.GetClip(2)));
                Assert.That(source.volume, Is.EqualTo(expectedVolume));
                Assert.That(source.pitch, Is.EqualTo(expectedPitch));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileBgm_DifferentIntentDuringTwoSourceStopUsesTieBreakSurvivor()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateBgmEvent(127);
                var b = fixture.CreateBgmEvent(128);
                var c = fixture.CreateBgmEvent(129);
                fixture.SetEvents(a, b, c);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(BgmIntent(1, 127));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(BgmIntent(2, 128));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.61d, 0.2f);
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    3,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));

                manager.ApplyProfile(BgmIntent(4, 129));
                manager.Tick(realtime + 1.01d, 0.4f);

                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(129),
                    new OrpheusAudioKey(127),
                    new OrpheusAudioKey(129),
                    2);
                Assert.That(fixture.BgmZero.clip, Is.SameAs(a.GetClip(0)));
                Assert.That(fixture.BgmOne.clip, Is.SameAs(c.GetClip(0)));
                Assert.That(fixture.BgmOne.clip, Is.Not.SameAs(b.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileBgm_MatchingSourceDuringStopSurvivesWithoutRestart()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateBgmEvent(132);
                var b = fixture.CreateBgmEvent(133);
                fixture.SetEvents(a, b);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(BgmIntent(1, 132));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(BgmIntent(2, 133));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.51d, 0.1f);
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    3,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));
                fixture.BgmOne.timeSamples = 400;

                manager.ApplyProfile(BgmIntent(4, 133));

                Assert.That(fixture.BgmOne.clip, Is.SameAs(b.GetClip(0)));
                Assert.That(fixture.BgmOne.timeSamples, Is.EqualTo(400));
                manager.Tick(realtime + 0.91d, 0.4f);
                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(133),
                    new OrpheusAudioKey(133),
                    OrpheusAudioKey.Invalid,
                    1);
                Assert.That(fixture.BgmZero.clip, Is.Null);
                Assert.That(fixture.BgmOne.clip, Is.SameAs(b.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileBgm_DifferentIntentDuringStopPreservesTheLargerGainSource()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateBgmEvent(134);
                var b = fixture.CreateBgmEvent(135);
                var c = fixture.CreateBgmEvent(136);
                fixture.SetEvents(a, b, c);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(BgmIntent(1, 134));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(BgmIntent(2, 135));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.51d, 0.1f);
                Assert.That(fixture.BgmZero.volume, Is.GreaterThan(fixture.BgmOne.volume));
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    3,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));

                manager.ApplyProfile(BgmIntent(4, 136));
                manager.Tick(realtime + 0.91d, 0.4f);

                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(136),
                    new OrpheusAudioKey(134),
                    new OrpheusAudioKey(136),
                    2);
                Assert.That(fixture.BgmZero.clip, Is.SameAs(a.GetClip(0)));
                Assert.That(fixture.BgmOne.clip, Is.SameAs(c.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void InvalidProfileBgm_FadesEveryOccupiedSourceToZeroAndReleasesThem()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateBgmEvent(130), fixture.CreateBgmEvent(131));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(BgmIntent(1, 130));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(BgmIntent(2, 131));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.61d, 0.2f);
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    3,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));

                AssertBgmDiagnostics(
                    manager,
                    OrpheusAudioKey.Invalid,
                    new OrpheusAudioKey(130),
                    new OrpheusAudioKey(131),
                    2);
                manager.Tick(realtime + 1.01d, 0.4f);

                AssertBgmDiagnostics(
                    manager,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid,
                    0);
                AssertNormalized(fixture.BgmZero);
                AssertNormalized(fixture.BgmOne);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void StableBgmProfileAndTickHotPaths_AllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateBgmEvent(140));
                var manager = fixture.CreateReadyManager(out _);
                var intent = BgmIntent(1, 140);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.ApplyProfile(intent);
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(intent);
                manager.Tick(realtime + 0.41d, 0.01f);

                var beforeProfile = GC.GetAllocatedBytesForCurrentThread();
                manager.ApplyProfile(intent);
                var profileBytes = GC.GetAllocatedBytesForCurrentThread() - beforeProfile;
                var beforeTick = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.42d, 0.01f);
                var tickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeTick;

                Assert.That(profileBytes, Is.Zero);
                Assert.That(tickBytes, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void AcceptedRejectedBgmProfilesAndActiveCrossfadeTick_AllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateBgmEvent(147), fixture.CreateBgmEvent(148));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.ApplyProfile(BgmIntent(1, 147));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                var accepted = BgmIntent(2, 148);
                var rejected = BgmIntent(3, 999);

                var beforeAccepted = GC.GetAllocatedBytesForCurrentThread();
                manager.ApplyProfile(accepted);
                var acceptedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAccepted;
                var beforeRejected = GC.GetAllocatedBytesForCurrentThread();
                manager.ApplyProfile(rejected);
                var rejectedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeRejected;
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.46d, 0.05f);

                var beforeCrossfade = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.51d, 0.05f);
                var crossfadeBytes = GC.GetAllocatedBytesForCurrentThread() - beforeCrossfade;

                Assert.That(acceptedBytes, Is.Zero);
                Assert.That(rejectedBytes, Is.Zero);
                Assert.That(crossfadeBytes, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileBgm_ReferenceChangeDuringCrossfadeFailClosesBeforeGainMutation()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateBgmEvent(145), fixture.CreateBgmEvent(146));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(BgmIntent(1, 145));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(BgmIntent(2, 146));
                manager.Tick(realtime + 0.41d, 0.01f);
                fixture.ReplaceBgmZeroReference();

                manager.Tick(realtime + 0.51d, 0.1f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.OwnedSourceReferenceChanged));
                Assert.That(diagnostics.BgmActiveCount, Is.Zero);
                AssertNormalized(fixture.BgmZero);
                AssertNormalized(fixture.BgmOne);
                DisposeBound(fixture, manager);
            }
        }

        private static OrpheusAudioProfileIntent BgmIntent(uint profileId, ushort key)
        {
            return new OrpheusAudioProfileIntent(
                profileId,
                OrpheusBaseState.Peace,
                new OrpheusAudioKey(key),
                OrpheusAudioKey.Invalid);
        }

        private static void AssertBgmDiagnostics(
            OrpheusAudioManager manager,
            OrpheusAudioKey desired,
            OrpheusAudioKey current,
            OrpheusAudioKey target,
            byte activeCount)
        {
            Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
            Assert.That(diagnostics.DesiredBgmKey, Is.EqualTo(desired));
            Assert.That(diagnostics.CurrentBgmKey, Is.EqualTo(current));
            Assert.That(diagnostics.TargetBgmKey, Is.EqualTo(target));
            Assert.That(diagnostics.BgmActiveCount, Is.EqualTo(activeCount));
        }

        private sealed partial class SessionFixture
        {
            internal AudioSource BgmZero => Sources[OrpheusAudioSourceBank.BgmOffset];
            internal AudioSource BgmOne => Sources[OrpheusAudioSourceBank.BgmOffset + 1];

            internal OrpheusAudioEvent CreateBgmEvent(ushort key)
            {
                return CreatePlaybackEvent(
                    key,
                    OrpheusPlaybackKind.Bgm,
                    OrpheusLoadPolicy.PersistentStream,
                    category: OrpheusCategory.Music);
            }

            internal void SetClipState(int flattenedClipIndex, OrpheusClipLoadState state)
            {
                _clipReadiness.SetState(flattenedClipIndex, state);
            }

            internal int GetClipRequestCount(int flattenedClipIndex)
            {
                return _clipReadiness.GetRequestCount(flattenedClipIndex);
            }

            internal void ReplaceBgmZeroReference()
            {
                var replacementObject = new GameObject("Bgm_00");
                replacementObject.transform.SetParent(Host.transform, false);
                var replacement = replacementObject.AddComponent<AudioSource>();
                SetField(Bank, "_bgm", new[] { replacement, BgmOne });
            }
        }
    }
}
