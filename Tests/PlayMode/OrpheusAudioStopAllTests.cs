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
        private const float StopAllDeclickSeconds = 0.015f;

        [Test]
        public void StopAll_PublicSurfaceIsExactAndBridgesExposeNone()
        {
            var members = typeof(OrpheusAudioManager).GetMember(
                "StopAll",
                MemberTypes.Method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

            Assert.That(members, Has.Length.EqualTo(1));
            var method = (MethodInfo)members[0];
            Assert.That(method.ReturnType, Is.EqualTo(typeof(void)));
            var parameters = method.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(1));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(OrpheusBus)));
            Assert.That(typeof(OrpheusAudioBridge).GetMethod("StopAll"), Is.Null);
            Assert.That(typeof(OrpheusAudioRawBridge).GetMethod("StopAll"), Is.Null);
        }

        [Test]
        public void StopAll_InvalidBusChangesOnlyInvalidValueRejected()
        {
            using (var fixture = new SessionFixture())
            {
                var cue = fixture.CreatePlaybackEvent(600, category: OrpheusCategory.SfxUi);
                fixture.SetEvents(cue);
                var manager = fixture.CreateReadyManager(out _);
                manager.Tick(StopAllRealtime(), 0f);
                manager.Play(new OrpheusAudioKey(600));
                var source = fixture.Sources[12];
                var clip = source.clip;
                var volume = source.volume;
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                manager.StopAll(OrpheusBus.Invalid);
                manager.StopAll((OrpheusBus)255);

                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                AssertDiagnosticsEqualExceptCounter(
                    before,
                    after,
                    nameof(OrpheusAudioDiagnosticsCounters.InvalidValueRejected),
                    2);
                Assert.That(source.clip, Is.SameAs(clip));
                Assert.That(source.volume, Is.EqualTo(volume));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void StopAll_WrongThreadInvalidBusChangesOnlyWrongThreadRejected()
        {
            using (var fixture = new SessionFixture())
            {
                var cue = fixture.CreatePlaybackEvent(610, category: OrpheusCategory.SfxWorld);
                var loop = fixture.CreateGlobalLoopEvent(611, OrpheusCategory.Ambience);
                fixture.SetEvents(cue, loop);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = StopAllRealtime();
                manager.Tick(realtime, 0f);
                manager.Play(new OrpheusAudioKey(610));
                manager.PlayLoop(new OrpheusAudioKey(611));
                manager.Tick(realtime + 0.01d, 0.01f);
                var cueSource = fixture.Sources[12];
                var loopSource = fixture.FindGlobalLoopSource(loop.GetClip(0));
                var cueClip = cueSource.clip;
                var loopClip = loopSource.clip;
                var cueVolume = cueSource.volume;
                var loopVolume = loopSource.volume;
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                var worker = new Thread(() => manager.StopAll((OrpheusBus)255));
                worker.Start();
                worker.Join();

                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                AssertDiagnosticsEqualExceptCounter(
                    before,
                    after,
                    nameof(OrpheusAudioDiagnosticsCounters.WrongThreadRejected),
                    1);
                Assert.That(cueSource.clip, Is.SameAs(cueClip));
                Assert.That(loopSource.clip, Is.SameAs(loopClip));
                Assert.That(cueSource.volume, Is.EqualTo(cueVolume));
                Assert.That(loopSource.volume, Is.EqualTo(loopVolume));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void StopAll_BusIsolationFadesMatching2DAnd3DForExactlyFifteenMilliseconds()
        {
            using (var fixture = new SessionFixture())
            {
                var combat2D = fixture.CreatePlaybackEvent(620, category: OrpheusCategory.SfxCombat);
                var world2D = fixture.CreatePlaybackEvent(621, category: OrpheusCategory.SfxWorld);
                var combat3D = fixture.CreateSpatialPlaybackEvent(622, category: OrpheusCategory.SfxCombat);
                var ambience3D = fixture.CreateSpatialPlaybackEvent(623, category: OrpheusCategory.Ambience);
                fixture.SetEvents(combat2D, world2D, combat3D, ambience3D);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = StopAllRealtime();
                manager.Tick(realtime, 0f);
                manager.Play(new OrpheusAudioKey(620));
                manager.Play(new OrpheusAudioKey(621));
                manager.PlayAt(new OrpheusAudioKey(622), Vector3.zero);
                manager.PlayAt(new OrpheusAudioKey(623), Vector3.one);

                manager.StopAll(OrpheusBus.SfxCombat);

                Assert.That(manager.TryGetDiagnostics(out var stopping), Is.True);
                Assert.That(stopping.Transient2DActiveCount, Is.EqualTo(2));
                Assert.That(stopping.Transient3DActiveCount, Is.EqualTo(2));
                Assert.That(stopping.FadingCount, Is.EqualTo(2));
                manager.Tick(realtime + 0.0075d, 0.0075f);
                Assert.That(fixture.Sources[12].volume, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(fixture.Sources[0].volume, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(fixture.Sources[13].clip, Is.SameAs(world2D.GetClip(0)));
                Assert.That(fixture.Sources[1].clip, Is.SameAs(ambience3D.GetClip(0)));
                manager.StopAll(OrpheusBus.SfxCombat);
                manager.Tick(realtime + StopAllDeclickSeconds, 0.0075f);

                AssertNormalized(fixture.Sources[12]);
                AssertNormalized(fixture.Sources[0]);
                Assert.That(fixture.Sources[13].clip, Is.SameAs(world2D.GetClip(0)));
                Assert.That(fixture.Sources[1].clip, Is.SameAs(ambience3D.GetClip(0)));
                Assert.That(manager.TryGetDiagnostics(out var released), Is.True);
                Assert.That(released.Transient2DActiveCount, Is.EqualTo(1));
                Assert.That(released.Transient3DActiveCount, Is.EqualTo(1));
                Assert.That(released.FadingCount, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void StopAll_PendingTargetCategoryCancelsWithoutRestartingVictimFade()
        {
            using (var fixture = new SessionFixture())
            {
                var victim = fixture.CreatePlaybackEvent(
                    630,
                    volume: 0.8f,
                    priority: 200,
                    category: OrpheusCategory.SfxWorld,
                    polyphonyCap: 4);
                var pending = fixture.CreatePlaybackEvent(
                    631,
                    volume: 0.6f,
                    priority: 100,
                    category: OrpheusCategory.SfxCombat);
                fixture.SetEvents(victim, pending);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = StopAllRealtime();
                manager.Tick(realtime, 0f);
                for (var index = 0; index < 4; index++)
                {
                    manager.Play(new OrpheusAudioKey(630));
                }

                manager.Tick(realtime + 0.05d, 0.05f);
                manager.Play(new OrpheusAudioKey(631));
                manager.Tick(realtime + 0.0575d, 0.0075f);
                var halfVolume = fixture.Sources[12].volume;

                manager.StopAll(OrpheusBus.SfxCombat);

                Assert.That(manager.TryGetDiagnostics(out var cancelled), Is.True);
                Assert.That(cancelled.PendingCount, Is.Zero);
                Assert.That(cancelled.FadingCount, Is.EqualTo(1));
                Assert.That(fixture.Sources[12].clip, Is.SameAs(victim.GetClip(0)));
                Assert.That(fixture.Sources[12].volume, Is.EqualTo(halfVolume));
                manager.Tick(realtime + 0.065d, 0.0075f);
                AssertNormalized(fixture.Sources[12]);
                for (var sourceIndex = 12; sourceIndex < 16; sourceIndex++)
                {
                    Assert.That(fixture.Sources[sourceIndex].clip, Is.Not.SameAs(pending.GetClip(0)));
                }

                Assert.That(manager.TryGetDiagnostics(out var completed), Is.True);
                Assert.That(completed.FadingCount, Is.Zero);
                Assert.That(completed.PendingCount, Is.Zero);
                Assert.That(completed.Transient2DActiveCount, Is.EqualTo(1));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void StopAll_MusicAndAmbienceClearOnlyOwnedProfileIntentAndReapplyRestoresIt()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetProfileDurations(0.05f, 0.05f, 0.1f);
                var bgm = fixture.CreateBgmEvent(640);
                var profileAmbience = fixture.CreateProfileAmbienceEvent(641);
                var combatLoop = fixture.CreateGlobalLoopEvent(642, OrpheusCategory.SfxCombat);
                var ambienceLoop = fixture.CreateGlobalLoopEvent(643, OrpheusCategory.Ambience);
                fixture.SetEvents(bgm, profileAmbience, combatLoop, ambienceLoop);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = StopAllRealtime();
                var original = new OrpheusAudioProfileIntent(
                    9,
                    OrpheusBaseState.Combat,
                    new OrpheusAudioKey(640),
                    new OrpheusAudioKey(641));
                manager.ApplyProfile(original);
                manager.SetOverlay(OrpheusOverlay.Menu, true);
                manager.SetOverlay(OrpheusOverlay.Pause, true);
                manager.PlayLoop(new OrpheusAudioKey(642));
                manager.PlayLoop(new OrpheusAudioKey(643));
                manager.Tick(realtime, 0f);
                manager.Tick(realtime + 0.05d, 0.05f);
                var combatSource = fixture.FindGlobalLoopSource(combatLoop.GetClip(0));

                manager.StopAll(OrpheusBus.Music);

                Assert.That(manager.TryGetDiagnostics(out var musicStopped), Is.True);
                Assert.That(musicStopped.ProfileId, Is.EqualTo(9));
                Assert.That(musicStopped.BaseState, Is.EqualTo(OrpheusBaseState.Combat));
                Assert.That(musicStopped.Overlay, Is.EqualTo(OrpheusOverlay.Menu | OrpheusOverlay.Pause));
                Assert.That(musicStopped.DesiredBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(musicStopped.DesiredProfileAmbienceKey, Is.EqualTo(new OrpheusAudioKey(641)));
                manager.Tick(realtime + 0.1d, 0.05f);
                manager.ApplyProfile(original);
                manager.Tick(realtime + 0.11d, 0.01f);
                manager.Tick(realtime + 0.16d, 0.05f);
                Assert.That(manager.TryGetDiagnostics(out var musicRestored), Is.True);
                Assert.That(musicRestored.DesiredBgmKey, Is.EqualTo(new OrpheusAudioKey(640)));
                Assert.That(musicRestored.BgmActiveCount, Is.EqualTo(1));

                manager.StopAll(OrpheusBus.Ambience);

                Assert.That(manager.TryGetDiagnostics(out var ambienceStopped), Is.True);
                Assert.That(ambienceStopped.DesiredBgmKey, Is.EqualTo(new OrpheusAudioKey(640)));
                Assert.That(ambienceStopped.DesiredProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(ambienceStopped.ProfileId, Is.EqualTo(9));
                Assert.That(ambienceStopped.BaseState, Is.EqualTo(OrpheusBaseState.Combat));
                Assert.That(combatSource.clip, Is.SameAs(combatLoop.GetClip(0)));
                manager.Tick(realtime + 0.175d, StopAllDeclickSeconds);
                Assert.That(fixture.FindGlobalLoopSourceOrNull(ambienceLoop.GetClip(0)), Is.Null);
                Assert.That(combatSource.clip, Is.SameAs(combatLoop.GetClip(0)));
                manager.Tick(realtime + 0.21d, 0.035f);
                manager.ApplyProfile(original);
                manager.Tick(realtime + 0.22d, 0.01f);
                manager.Tick(realtime + 0.27d, 0.05f);
                Assert.That(manager.TryGetDiagnostics(out var ambienceRestored), Is.True);
                Assert.That(ambienceRestored.DesiredProfileAmbienceKey, Is.EqualTo(new OrpheusAudioKey(641)));
                Assert.That(ambienceRestored.ProfileAmbienceActiveCount, Is.EqualTo(1));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void StopAll_AmbienceBeforeReadyCancelsLoadingLoopAndPreventsLateStart()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(645, OrpheusCategory.Ambience);
                fixture.SetEvents(loop);
                fixture.SetClipState(0, OrpheusClipLoadState.Unloaded);
                var manager = fixture.CreateManagerBeforeHostReady(out _);
                var realtime = StopAllRealtime();

                manager.PlayLoop(new OrpheusAudioKey(645));
                fixture.SetClipState(0, OrpheusClipLoadState.Loading);
                manager.Tick(realtime, 0f);
                manager.StopAll(OrpheusBus.Ambience);
                fixture.SetClipState(0, OrpheusClipLoadState.Loaded);
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                Assert.That(
                    manager.CompleteBootstrapHydration(manager.CaptureBootstrapAuthority()),
                    Is.True);
                manager.Tick(realtime + 0.01d, 0.01f);

                fixture.AssertAllGlobalLoopSourcesNormalized();
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.GlobalLoopActiveCount, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void StopAll_MasterUsesRoleSpecificDurationsAcrossMixedRepresentativeRoles()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetProfileDurations(0.2f, 0.3f, 0.1f);
                var cue2D = fixture.CreatePlaybackEvent(650, category: OrpheusCategory.SfxUi);
                var cue3D = fixture.CreateSpatialPlaybackEvent(651, category: OrpheusCategory.SfxWorld);
                var bgm = fixture.CreateBgmEvent(652);
                var ambience = fixture.CreateProfileAmbienceEvent(653);
                var loop = fixture.CreateGlobalLoopEvent(654, OrpheusCategory.SfxCombat);
                var victim = fixture.CreatePlaybackEvent(
                    655,
                    priority: 200,
                    category: OrpheusCategory.SfxWorld,
                    polyphonyCap: 4);
                var pending = fixture.CreatePlaybackEvent(
                    656,
                    priority: 100,
                    category: OrpheusCategory.SfxCombat);
                fixture.SetEvents(cue2D, cue3D, bgm, ambience, loop, victim, pending);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = StopAllRealtime();
                manager.Tick(realtime, 0f);
                manager.StopAll(OrpheusBus.SfxCombat);
                manager.ApplyProfile(new OrpheusAudioProfileIntent(
                    10,
                    OrpheusBaseState.Peace,
                    new OrpheusAudioKey(652),
                    new OrpheusAudioKey(653)));
                manager.PlayLoop(new OrpheusAudioKey(654));
                manager.Tick(realtime + 0.3d, 0.3f);
                manager.Play(new OrpheusAudioKey(650));
                manager.PlayAt(new OrpheusAudioKey(651), Vector3.zero);
                for (var index = 0; index < 3; index++)
                {
                    manager.Play(new OrpheusAudioKey(655));
                }

                manager.Tick(realtime + 0.35d, 0.05f);
                manager.Play(new OrpheusAudioKey(656));
                Assert.That(manager.TryGetDiagnostics(out var beforeMaster), Is.True);
                Assert.That(beforeMaster.Transient2DActiveCount, Is.EqualTo(3));
                Assert.That(beforeMaster.FadingCount, Is.EqualTo(1));
                Assert.That(beforeMaster.PendingCount, Is.EqualTo(1));

                var beforeStop = GC.GetAllocatedBytesForCurrentThread();
                manager.StopAll(OrpheusBus.Master);
                var stopBytes = GC.GetAllocatedBytesForCurrentThread() - beforeStop;
                Assert.That(manager.TryGetDiagnostics(out var stopping), Is.True);
                Assert.That(stopping.DesiredBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(stopping.DesiredProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
                Assert.That(stopping.Transient2DActiveCount, Is.EqualTo(3));
                Assert.That(stopping.Transient3DActiveCount, Is.EqualTo(1));
                Assert.That(stopping.BgmActiveCount, Is.EqualTo(1));
                Assert.That(stopping.ProfileAmbienceActiveCount, Is.EqualTo(1));
                Assert.That(stopping.GlobalLoopActiveCount, Is.EqualTo(1));
                Assert.That(stopping.FadingCount, Is.EqualTo(4));
                Assert.That(stopping.PendingCount, Is.Zero);

                var beforeDeclick = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.365d, StopAllDeclickSeconds);
                var declickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeDeclick;
                Assert.That(manager.TryGetDiagnostics(out var declicked), Is.True);
                Assert.That(declicked.Transient2DActiveCount, Is.Zero);
                Assert.That(declicked.Transient3DActiveCount, Is.Zero);
                Assert.That(declicked.GlobalLoopActiveCount, Is.Zero);
                Assert.That(declicked.BgmActiveCount, Is.EqualTo(1));
                Assert.That(declicked.ProfileAmbienceActiveCount, Is.EqualTo(1));

                var beforeBgmCompletion = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.55d, 0.185f);
                var bgmTickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBgmCompletion;
                Assert.That(manager.TryGetDiagnostics(out var bgmComplete), Is.True);
                Assert.That(bgmComplete.BgmActiveCount, Is.Zero);
                Assert.That(bgmComplete.ProfileAmbienceActiveCount, Is.EqualTo(1));

                var beforeAmbienceCompletion = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.65d, 0.1f);
                var ambienceTickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeAmbienceCompletion;
                Assert.That(manager.TryGetDiagnostics(out var complete), Is.True);
                Assert.That(complete.ProfileAmbienceActiveCount, Is.Zero);
                Assert.That(stopBytes, Is.Zero, "StopAll");
                Assert.That(declickBytes, Is.Zero, "15ms Tick");
                Assert.That(bgmTickBytes, Is.Zero, "BGM completion Tick");
                Assert.That(ambienceTickBytes, Is.Zero, "Profile Ambience completion Tick");
                for (var sourceIndex = 0; sourceIndex < fixture.Sources.Length; sourceIndex++)
                {
                    AssertNormalized(fixture.Sources[sourceIndex]);
                }

                DisposeBound(fixture, manager);
            }
        }

        private static double StopAllRealtime()
        {
            return Time.realtimeSinceStartupAsDouble + 0.1d;
        }

        private static void AssertDiagnosticsEqualExceptCounter(
            OrpheusAudioDiagnostics before,
            OrpheusAudioDiagnostics after,
            string changedCounter,
            ulong expectedDelta)
        {
            var diagnosticsFields = typeof(OrpheusAudioDiagnostics)
                .GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (var index = 0; index < diagnosticsFields.Length; index++)
            {
                var field = diagnosticsFields[index];
                if (field.Name != nameof(OrpheusAudioDiagnostics.Counters))
                {
                    Assert.That(field.GetValue(after), Is.EqualTo(field.GetValue(before)), field.Name);
                }
            }

            var counterFields = typeof(OrpheusAudioDiagnosticsCounters)
                .GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (var index = 0; index < counterFields.Length; index++)
            {
                var field = counterFields[index];
                var beforeValue = (ulong)field.GetValue(before.Counters);
                var afterValue = (ulong)field.GetValue(after.Counters);
                Assert.That(
                    afterValue,
                    Is.EqualTo(beforeValue + (field.Name == changedCounter ? expectedDelta : 0UL)),
                    field.Name);
            }
        }
    }
}
