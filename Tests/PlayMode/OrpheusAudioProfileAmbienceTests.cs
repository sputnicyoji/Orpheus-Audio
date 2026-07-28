using System;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Tests
{
    public sealed partial class OrpheusAudioSessionTests
    {
        [Test]
        public void ProfileAmbience_LoadsBeforeReadyAndStartsOnlyAfterPlaybackReadyAndLoaded()
        {
            using (var fixture = new SessionFixture())
            {
                var ambience = fixture.CreateProfileAmbienceEvent(100);
                fixture.SetEvents(ambience);
                fixture.SetClipState(0, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateManagerBeforeHostReady(out _);
                var intent = AmbienceIntent(1, 100);

                manager.ApplyProfile(intent);
                manager.ApplyProfile(intent);

                Assert.That(fixture.GetClipRequestCount(0), Is.EqualTo(1));
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.Null);
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.Null);
                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(100),
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid,
                    0);

                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.Tick(realtime, 0.016f);
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.Null);

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                Assert.That(
                    manager.CompleteBootstrapHydration(manager.CaptureBootstrapAuthority()),
                    Is.True);
                manager.Tick(realtime + 0.1d, 0.1f);

                Assert.That(fixture.ProfileAmbienceZero.clip, Is.SameAs(ambience.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceZero.loop, Is.True);
                Assert.That(fixture.ProfileAmbienceZero.pitch, Is.EqualTo(1f));
                Assert.That(fixture.ProfileAmbienceZero.spatialBlend, Is.Zero);
                Assert.That(fixture.ProfileAmbienceZero.outputAudioMixerGroup.name, Is.EqualTo("Ambience_State"));
                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(100),
                    new OrpheusAudioKey(100),
                    OrpheusAudioKey.Invalid,
                    1);
                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void ProfileAmbience_CrossfadesEqualPowerAndPromotesOnlyLatestQueuedTarget()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateProfileAmbienceEvent(100);
                var b = fixture.CreateProfileAmbienceEvent(101);
                var c = fixture.CreateProfileAmbienceEvent(102);
                var d = fixture.CreateProfileAmbienceEvent(103);
                fixture.SetEvents(a, b, c, d);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(AmbienceIntent(1, 100));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                Assert.That(fixture.ProfileAmbienceZero.volume, Is.EqualTo(1f).Within(0.000001f));

                manager.ApplyProfile(AmbienceIntent(2, 101));
                manager.Tick(realtime + 0.41d, 0.01f);
                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(101),
                    new OrpheusAudioKey(100),
                    new OrpheusAudioKey(101),
                    2);

                manager.Tick(realtime + 0.61d, 0.2f);
                var midpoint = (float)Math.Sqrt(0.5d);
                Assert.That(fixture.ProfileAmbienceZero.volume, Is.EqualTo(midpoint).Within(0.001f));
                Assert.That(fixture.ProfileAmbienceOne.volume, Is.EqualTo(midpoint).Within(0.001f));

                manager.ApplyProfile(AmbienceIntent(3, 102));
                manager.ApplyProfile(AmbienceIntent(4, 103));
                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(103),
                    new OrpheusAudioKey(100),
                    new OrpheusAudioKey(101),
                    2);

                manager.Tick(realtime + 0.81d, 0.2f);

                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(103),
                    new OrpheusAudioKey(101),
                    new OrpheusAudioKey(103),
                    2);
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.SameAs(d.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.SameAs(b.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.Not.SameAs(c.GetClip(0)));
                for (var sourceIndex = 0; sourceIndex < fixture.Sources.Length; sourceIndex++)
                {
                    if (sourceIndex != SessionFixture.FirstProfileAmbienceSourceIndex &&
                        sourceIndex != SessionFixture.FirstProfileAmbienceSourceIndex + 1)
                    {
                        AssertNormalized(fixture.Sources[sourceIndex]);
                    }
                }

                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void ProfileAmbience_RetargetToOutgoingReversesWithoutGainJumpOrRestart()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateProfileAmbienceEvent(110);
                var b = fixture.CreateProfileAmbienceEvent(111);
                var c = fixture.CreateProfileAmbienceEvent(112);
                fixture.SetEvents(a, b, c);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(AmbienceIntent(1, 110));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(AmbienceIntent(2, 111));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.61d, 0.2f);
                manager.ApplyProfile(AmbienceIntent(3, 112));
                fixture.ProfileAmbienceZero.timeSamples = 400;
                var beforeZero = fixture.ProfileAmbienceZero.volume;
                var beforeOne = fixture.ProfileAmbienceOne.volume;

                manager.ApplyProfile(AmbienceIntent(4, 110));

                Assert.That(fixture.ProfileAmbienceZero.volume, Is.EqualTo(beforeZero).Within(0.000001f));
                Assert.That(fixture.ProfileAmbienceOne.volume, Is.EqualTo(beforeOne).Within(0.000001f));
                Assert.That(fixture.ProfileAmbienceZero.timeSamples, Is.EqualTo(400));
                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(110),
                    new OrpheusAudioKey(111),
                    new OrpheusAudioKey(110),
                    2);

                manager.Tick(realtime + 0.81d, 0.2f);

                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(110),
                    new OrpheusAudioKey(110),
                    OrpheusAudioKey.Invalid,
                    1);
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.SameAs(a.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.Null);
                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void ProfileAmbience_LoadFailureAndStallKeepCurrentAndExplicitRetryRunsOnce()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateProfileAmbienceEvent(120);
                var b = fixture.CreateProfileAmbienceEvent(121);
                fixture.SetEvents(a, b);
                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(1, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(AmbienceIntent(1, 120));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                var failedIntent = AmbienceIntent(2, 121);
                manager.ApplyProfile(failedIntent);
                Assert.That(fixture.GetClipRequestCount(1), Is.EqualTo(1));

                fixture.SetClipState(1, OrpheusClipLoadState.Loading);
                manager.Tick(realtime + 2.5d, 0.1f);
                fixture.SetClipState(1, OrpheusClipLoadState.Failed);
                manager.Tick(realtime + 2.6d, 0.1f);

                AssertProfileAmbienceDiagnostics(
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
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.SameAs(a.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.Null);
                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void ProfileAmbience_RequestingCurrentCancelsPendingLoadAndRejectsLateLoadedGeneration()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateProfileAmbienceEvent(125);
                fixture.SetEvents(a, fixture.CreateProfileAmbienceEvent(126));
                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(1, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(AmbienceIntent(1, 125));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(AmbienceIntent(2, 126));
                Assert.That(fixture.GetClipRequestCount(1), Is.EqualTo(1));

                manager.ApplyProfile(AmbienceIntent(3, 125));
                fixture.SetClipState(1, OrpheusClipLoadState.Loaded);
                manager.Tick(realtime + 1d, 0.1f);
                manager.Tick(realtime + 2d, 0.1f);
                manager.Tick(realtime + 3d, 0.1f);

                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(125),
                    new OrpheusAudioKey(125),
                    OrpheusAudioKey.Invalid,
                    1);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Counters.LoadStalled, Is.Zero);
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.SameAs(a.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.Null);
                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void ProfileAmbience_QueuedFailedRetryDoesNotStaleActiveCrossfade()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateProfileAmbienceEvent(122);
                var b = fixture.CreateProfileAmbienceEvent(123);
                var c = fixture.CreateProfileAmbienceEvent(124);
                fixture.SetEvents(a, b, c);
                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(1, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(2, OrpheusClipLoadState.Failed);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(AmbienceIntent(1, 122));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(AmbienceIntent(2, 123));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.51d, 0.1f);

                var queued = AmbienceIntent(3, 124);
                manager.ApplyProfile(queued);
                manager.Tick(realtime + 0.51d, 0f);
                manager.ApplyProfile(queued);
                Assert.That(fixture.GetClipRequestCount(2), Is.EqualTo(2));
                fixture.SetClipState(2, OrpheusClipLoadState.Loaded);

                manager.Tick(realtime + 0.81d, 0.3f);

                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(124),
                    new OrpheusAudioKey(123),
                    new OrpheusAudioKey(124),
                    2);
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.SameAs(c.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.SameAs(b.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void ProfileAmbience_FailedTargetCannotStartAfterLateLoadedWithoutExplicitRetry()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateProfileAmbienceEvent(141);
                var b = fixture.CreateProfileAmbienceEvent(142);
                fixture.SetEvents(a, b);
                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                fixture.SetClipState(1, OrpheusClipLoadState.Failed);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(AmbienceIntent(1, 141));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                var failedIntent = AmbienceIntent(2, 142);
                manager.ApplyProfile(failedIntent);
                manager.Tick(realtime + 0.41d, 0.01f);
                fixture.SetClipState(1, OrpheusClipLoadState.Loaded);

                manager.Tick(realtime + 0.5d, 0.09f);

                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(142),
                    new OrpheusAudioKey(141),
                    OrpheusAudioKey.Invalid,
                    1);
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.Null);

                manager.ApplyProfile(failedIntent);
                manager.Tick(realtime + 0.51d, 0.01f);

                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(142),
                    new OrpheusAudioKey(141),
                    new OrpheusAudioKey(142),
                    2);
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.SameAs(b.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void ProfileAmbience_DifferentIntentDuringTwoSourceStopUsesTieBreakSurvivor()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateProfileAmbienceEvent(127);
                var b = fixture.CreateProfileAmbienceEvent(128);
                var c = fixture.CreateProfileAmbienceEvent(129);
                fixture.SetEvents(a, b, c);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(AmbienceIntent(1, 127));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(AmbienceIntent(2, 128));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.61d, 0.2f);
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    3,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));

                manager.ApplyProfile(AmbienceIntent(4, 129));
                manager.Tick(realtime + 1.01d, 0.4f);

                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(129),
                    new OrpheusAudioKey(127),
                    new OrpheusAudioKey(129),
                    2);
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.SameAs(a.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.SameAs(c.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.Not.SameAs(b.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void ProfileAmbience_MatchingSourceDuringStopSurvivesWithoutRestart()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateProfileAmbienceEvent(132);
                var b = fixture.CreateProfileAmbienceEvent(133);
                fixture.SetEvents(a, b);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(AmbienceIntent(1, 132));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(AmbienceIntent(2, 133));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.51d, 0.1f);
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    3,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));
                fixture.ProfileAmbienceOne.timeSamples = 400;

                manager.ApplyProfile(AmbienceIntent(4, 133));

                Assert.That(fixture.ProfileAmbienceOne.clip, Is.SameAs(b.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceOne.timeSamples, Is.EqualTo(400));
                manager.Tick(realtime + 0.91d, 0.4f);
                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(133),
                    new OrpheusAudioKey(133),
                    OrpheusAudioKey.Invalid,
                    1);
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.Null);
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.SameAs(b.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void ProfileAmbience_DifferentIntentDuringStopPreservesLargerGainSource()
        {
            using (var fixture = new SessionFixture())
            {
                var a = fixture.CreateProfileAmbienceEvent(134);
                var b = fixture.CreateProfileAmbienceEvent(135);
                var c = fixture.CreateProfileAmbienceEvent(136);
                fixture.SetEvents(a, b, c);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(AmbienceIntent(1, 134));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(AmbienceIntent(2, 135));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.51d, 0.1f);
                Assert.That(fixture.ProfileAmbienceZero.volume, Is.GreaterThan(fixture.ProfileAmbienceOne.volume));
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    3,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));

                manager.ApplyProfile(AmbienceIntent(4, 136));
                manager.Tick(realtime + 0.91d, 0.4f);

                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(136),
                    new OrpheusAudioKey(134),
                    new OrpheusAudioKey(136),
                    2);
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.SameAs(a.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.SameAs(c.GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void InvalidProfileAmbience_FadesEveryOccupiedSourceToZeroAndReleasesThem()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateProfileAmbienceEvent(130), fixture.CreateProfileAmbienceEvent(131));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(AmbienceIntent(1, 130));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(AmbienceIntent(2, 131));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.61d, 0.2f);
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    3,
                    OrpheusBaseState.Peace,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid));

                AssertProfileAmbienceDiagnostics(
                    manager,
                    OrpheusAudioKey.Invalid,
                    new OrpheusAudioKey(130),
                    new OrpheusAudioKey(131),
                    2);
                manager.Tick(realtime + 1.01d, 0.4f);

                AssertProfileAmbienceDiagnostics(
                    manager,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid,
                    OrpheusAudioKey.Invalid,
                    0);
                AssertNormalized(fixture.ProfileAmbienceZero);
                AssertNormalized(fixture.ProfileAmbienceOne);
                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void ProfileAmbience_ReferenceChangeDuringCrossfadeFailClosesBeforeGainMutation()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateProfileAmbienceEvent(145), fixture.CreateProfileAmbienceEvent(146));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(AmbienceIntent(1, 145));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(AmbienceIntent(2, 146));
                manager.Tick(realtime + 0.41d, 0.01f);
                fixture.ReplaceProfileAmbienceZeroReference();

                manager.Tick(realtime + 0.51d, 0.1f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.OwnedSourceReferenceChanged));
                Assert.That(diagnostics.DesiredProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(diagnostics.CurrentProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(diagnostics.TargetProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(diagnostics.ProfileAmbienceActiveCount, Is.Zero);
                AssertNormalized(fixture.ProfileAmbienceZero);
                AssertNormalized(fixture.ProfileAmbienceOne);
                DisposeBound(fixture, manager);
            }
        }


        [Test]
        public void BaseStateAndBgmOnlyChanges_DoNotRestartIdenticalProfileAmbience()
        {
            using (var fixture = new SessionFixture())
            {
                var bgmA = fixture.CreateBgmEvent(200);
                var bgmB = fixture.CreateBgmEvent(201);
                var ambience = fixture.CreateProfileAmbienceEvent(300);
                fixture.SetEvents(bgmA, bgmB, ambience);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(PersistentIntent(1, OrpheusBaseState.Peace, 200, 300));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                var source = fixture.ProfileAmbienceZero;
                source.timeSamples = 400;
                var clip = source.clip;
                var volume = source.volume;
                var requestCount = fixture.GetClipRequestCount(2);

                manager.ApplyProfile(PersistentIntent(2, OrpheusBaseState.Combat, 200, 300));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.ApplyProfile(PersistentIntent(3, OrpheusBaseState.Combat, 201, 300));
                manager.Tick(realtime + 0.42d, 0.01f);

                Assert.That(source.clip, Is.SameAs(clip));
                Assert.That(source.timeSamples, Is.EqualTo(400));
                Assert.That(source.volume, Is.EqualTo(volume));
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.Null);
                Assert.That(fixture.GetClipRequestCount(2), Is.EqualTo(requestCount));
                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(300),
                    new OrpheusAudioKey(300),
                    OrpheusAudioKey.Invalid,
                    1);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.BgmActiveCount, Is.EqualTo(2));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileAmbienceOnlyChange_DoesNotDisturbBgmExecution()
        {
            using (var fixture = new SessionFixture())
            {
                var bgm = fixture.CreateBgmEvent(210);
                var ambienceX = fixture.CreateProfileAmbienceEvent(310);
                var ambienceY = fixture.CreateProfileAmbienceEvent(311);
                fixture.SetEvents(bgm, ambienceX, ambienceY);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(PersistentIntent(1, OrpheusBaseState.Peace, 210, 310));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                var source = fixture.BgmZero;
                source.timeSamples = 400;
                var clip = source.clip;
                var volume = source.volume;

                manager.ApplyProfile(PersistentIntent(2, OrpheusBaseState.Peace, 210, 311));
                manager.Tick(realtime + 0.41d, 0.01f);

                Assert.That(source.clip, Is.SameAs(clip));
                Assert.That(source.timeSamples, Is.EqualTo(400));
                Assert.That(source.volume, Is.EqualTo(volume));
                Assert.That(fixture.BgmOne.clip, Is.Null);
                AssertBgmDiagnostics(
                    manager,
                    new OrpheusAudioKey(210),
                    new OrpheusAudioKey(210),
                    OrpheusAudioKey.Invalid,
                    1);
                AssertProfileAmbienceDiagnostics(
                    manager,
                    new OrpheusAudioKey(311),
                    new OrpheusAudioKey(310),
                    new OrpheusAudioKey(311),
                    2);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void SimultaneousBgmAndProfileAmbienceCrossfadesUseExactlyTwoPlusTwoDedicatedSources()
        {
            using (var fixture = new SessionFixture())
            {
                var bgmA = fixture.CreateBgmEvent(220);
                var bgmB = fixture.CreateBgmEvent(221);
                var ambienceX = fixture.CreateProfileAmbienceEvent(320);
                var ambienceY = fixture.CreateProfileAmbienceEvent(321);
                fixture.SetEvents(bgmA, bgmB, ambienceX, ambienceY);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;

                manager.ApplyProfile(PersistentIntent(1, OrpheusBaseState.Peace, 220, 320));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(PersistentIntent(2, OrpheusBaseState.Peace, 221, 321));
                manager.Tick(realtime + 0.41d, 0.01f);
                manager.Tick(realtime + 0.61d, 0.2f);

                var midpoint = (float)Math.Sqrt(0.5d);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.BgmActiveCount, Is.EqualTo(2));
                Assert.That(diagnostics.ProfileAmbienceActiveCount, Is.EqualTo(2));
                Assert.That(fixture.BgmZero.clip, Is.SameAs(bgmA.GetClip(0)));
                Assert.That(fixture.BgmOne.clip, Is.SameAs(bgmB.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceZero.clip, Is.SameAs(ambienceX.GetClip(0)));
                Assert.That(fixture.ProfileAmbienceOne.clip, Is.SameAs(ambienceY.GetClip(0)));
                Assert.That(fixture.BgmZero.outputAudioMixerGroup.name, Is.EqualTo("Music_State"));
                Assert.That(fixture.BgmOne.outputAudioMixerGroup.name, Is.EqualTo("Music_State"));
                Assert.That(fixture.ProfileAmbienceZero.outputAudioMixerGroup.name, Is.EqualTo("Ambience_State"));
                Assert.That(fixture.ProfileAmbienceOne.outputAudioMixerGroup.name, Is.EqualTo("Ambience_State"));
                Assert.That(fixture.BgmZero.volume, Is.EqualTo(midpoint).Within(0.001f));
                Assert.That(fixture.BgmOne.volume, Is.EqualTo(midpoint).Within(0.001f));
                Assert.That(fixture.ProfileAmbienceZero.volume, Is.EqualTo(midpoint).Within(0.001f));
                Assert.That(fixture.ProfileAmbienceOne.volume, Is.EqualTo(midpoint).Within(0.001f));
                for (var sourceIndex = 0; sourceIndex < fixture.Sources.Length; sourceIndex++)
                {
                    if (sourceIndex < OrpheusAudioSourceBank.BgmOffset ||
                        sourceIndex >= SessionFixture.FirstProfileAmbienceSourceIndex +
                        OrpheusAudioSourceBank.ProfileAmbienceCount)
                    {
                        AssertNormalized(fixture.Sources[sourceIndex]);
                    }
                }
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void ProfileAmbience_DisposeInvalidatesDirectorAndNormalizesDedicatedSources()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreateProfileAmbienceEvent(330),
                    fixture.CreateProfileAmbienceEvent(331));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                manager.ApplyProfile(AmbienceIntent(1, 330));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(AmbienceIntent(2, 331));
                manager.Tick(realtime + 0.41d, 0.01f);

                manager.Dispose();

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disposed));
                Assert.That(diagnostics.TransportState, Is.EqualTo(OrpheusTransportState.Disposed));
                Assert.That(diagnostics.DesiredBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(diagnostics.CurrentBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(diagnostics.TargetBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(diagnostics.DesiredProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(diagnostics.CurrentProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(diagnostics.TargetProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(diagnostics.BgmActiveCount, Is.Zero);
                Assert.That(diagnostics.ProfileAmbienceActiveCount, Is.Zero);
                AssertNormalized(fixture.ProfileAmbienceZero);
                AssertNormalized(fixture.ProfileAmbienceOne);
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void ProfileAmbienceAndDualDirectorHotPaths_AllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreateBgmEvent(240),
                    fixture.CreateBgmEvent(241),
                    fixture.CreateProfileAmbienceEvent(340),
                    fixture.CreateProfileAmbienceEvent(341));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                var stable = PersistentIntent(1, OrpheusBaseState.Peace, 240, 340);
                manager.ApplyProfile(stable);
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.4d, 0.4f);
                manager.ApplyProfile(stable);
                manager.Tick(realtime + 0.41d, 0.01f);

                var beforeIdentical = GC.GetAllocatedBytesForCurrentThread();
                manager.ApplyProfile(stable);
                var identicalBytes = GC.GetAllocatedBytesForCurrentThread() - beforeIdentical;
                var accepted = PersistentIntent(2, OrpheusBaseState.Peace, 240, 341);
                var beforeAccepted = GC.GetAllocatedBytesForCurrentThread();
                manager.ApplyProfile(accepted);
                var acceptedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAccepted;
                var rejected = PersistentIntent(3, OrpheusBaseState.Peace, 240, 999);
                var beforeRejected = GC.GetAllocatedBytesForCurrentThread();
                manager.ApplyProfile(rejected);
                var rejectedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeRejected;
                manager.Tick(realtime + 0.42d, 0.01f);
                manager.Tick(realtime + 0.47d, 0.05f);
                var beforeActive = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.52d, 0.05f);
                var activeBytes = GC.GetAllocatedBytesForCurrentThread() - beforeActive;
                manager.ApplyProfile(PersistentIntent(4, OrpheusBaseState.Peace, 241, 341));
                manager.Tick(realtime + 0.53d, 0.01f);
                var beforeDual = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.58d, 0.05f);
                var dualBytes = GC.GetAllocatedBytesForCurrentThread() - beforeDual;

                Assert.That(identicalBytes, Is.Zero);
                Assert.That(acceptedBytes, Is.Zero);
                Assert.That(rejectedBytes, Is.Zero);
                Assert.That(activeBytes, Is.Zero);
                Assert.That(dualBytes, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        private static OrpheusAudioProfileIntent AmbienceIntent(uint profileId, ushort ambienceKey)
        {
            return PersistentIntent(
                profileId,
                OrpheusBaseState.Peace,
                OrpheusAudioKey.Invalid.Value,
                ambienceKey);
        }

        private static OrpheusAudioProfileIntent PersistentIntent(
            uint profileId,
            OrpheusBaseState baseState,
            ushort bgmKey,
            ushort ambienceKey)
        {
            return new OrpheusAudioProfileIntent(
                profileId,
                baseState,
                new OrpheusAudioKey(bgmKey),
                new OrpheusAudioKey(ambienceKey));
        }

        private static void AssertProfileAmbienceDiagnostics(
            OrpheusAudioManager manager,
            OrpheusAudioKey desired,
            OrpheusAudioKey current,
            OrpheusAudioKey target,
            byte activeCount)
        {
            Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
            Assert.That(diagnostics.DesiredProfileAmbienceKey, Is.EqualTo(desired));
            Assert.That(diagnostics.CurrentProfileAmbienceKey, Is.EqualTo(current));
            Assert.That(diagnostics.TargetProfileAmbienceKey, Is.EqualTo(target));
            Assert.That(diagnostics.ProfileAmbienceActiveCount, Is.EqualTo(activeCount));
        }

        private sealed partial class SessionFixture
        {
            internal const int FirstProfileAmbienceSourceIndex =
                OrpheusAudioSourceBank.BgmOffset + OrpheusAudioSourceBank.BgmCount;

            internal AudioSource ProfileAmbienceZero => Sources[FirstProfileAmbienceSourceIndex];
            internal AudioSource ProfileAmbienceOne => Sources[FirstProfileAmbienceSourceIndex + 1];

            internal OrpheusAudioEvent CreateProfileAmbienceEvent(ushort key, float volume = 1f)
            {
                return CreatePlaybackEvent(
                    key,
                    OrpheusPlaybackKind.ProfileAmbience,
                    OrpheusLoadPolicy.PersistentStream,
                    volume: volume,
                    category: OrpheusCategory.Ambience);
            }

            internal void ReplaceProfileAmbienceZeroReference()
            {
                var replacementObject = new GameObject("ProfileAmbience_00");
                replacementObject.transform.SetParent(Host.transform, false);
                var replacement = replacementObject.AddComponent<AudioSource>();
                SetField(Bank, "_profileAmbience", new[] { replacement, ProfileAmbienceOne });
            }
        }    }
}
