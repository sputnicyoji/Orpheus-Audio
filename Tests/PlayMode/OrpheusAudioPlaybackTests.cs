using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio.Tests
{
    public sealed partial class OrpheusAudioSessionTests
    {
        [Test]
        public void Play_HasExactFireAndForgetPublicSurface()
        {
            var method = typeof(OrpheusAudioManager).GetMethod(
                "Play",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(OrpheusAudioKey) },
                null);

            Assert.That(method, Is.Not.Null);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(void)));
        }

        [Test]
        public void Play_RejectionOrderStopsAtFirstFailedGuard()
        {
            using (var unavailable = new SessionFixture())
            {
                unavailable.SetEvents(unavailable.CreatePlaybackEvent(11));
                Assert.That(unavailable.Create(out var manager).Success, Is.True);
                unavailable.Readiness.ResetCalls();

                manager.Play(OrpheusAudioKey.Invalid);

                AssertSingleCounter(manager, "UnavailableRejected");
                AssertNoReadinessSideEffects(unavailable);
                AssertNormalized(unavailable.Sources[12]);
                manager.Dispose();
            }

            using (var invalidKey = new SessionFixture())
            {
                invalidKey.SetEvents(invalidKey.CreatePlaybackEvent(11));
                Assert.That(invalidKey.Create(out var manager).Success, Is.True);
                Assert.That(invalidKey.Host.Bind(manager), Is.True);
                invalidKey.Readiness.ResetCalls();

                manager.Play(OrpheusAudioKey.Invalid);

                AssertSingleCounter(manager, "InvalidKeyRejected");
                AssertNoReadinessSideEffects(invalidKey);
                AssertNormalized(invalidKey.Sources[12]);
                DisposeBound(invalidKey, manager);
            }

            using (var wrongKind = new SessionFixture())
            {
                wrongKind.SetEvents(wrongKind.CreatePlaybackEvent(
                    11,
                    OrpheusPlaybackKind.GlobalLoop2D,
                    OrpheusLoadPolicy.PersistentStream,
                    category: OrpheusCategory.SfxWorld));
                Assert.That(wrongKind.Create(out var manager).Success, Is.True);
                Assert.That(wrongKind.Host.Bind(manager), Is.True);
                wrongKind.Readiness.ResetCalls();

                manager.Play(new OrpheusAudioKey(11));

                AssertSingleCounter(manager, "PlaybackKindRejected");
                AssertNoReadinessSideEffects(wrongKind);
                AssertNormalized(wrongKind.Sources[12]);
                DisposeBound(wrongKind, manager);
            }

            using (var preReady = new SessionFixture())
            {
                preReady.SetEvents(preReady.CreatePlaybackEvent(11));
                Assert.That(preReady.Create(out var manager).Success, Is.True);
                Assert.That(preReady.Host.Bind(manager), Is.True);
                preReady.Readiness.ResetCalls();

                manager.Play(new OrpheusAudioKey(11));

                AssertSingleCounter(manager, "PreReadyRejected");
                AssertNoReadinessSideEffects(preReady);
                AssertNormalized(preReady.Sources[12]);
                DisposeBound(preReady, manager);
            }
        }

        [Test]
        public void Play_BootstrapTransientRejectsEveryNonLoadedStateWithoutRetry()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreatePlaybackEvent(23));
                var manager = fixture.CreateReadyManager(out _);
                fixture.Readiness.ResetCalls();

                fixture.Readiness.State = OrpheusClipLoadState.Unloaded;
                manager.Play(new OrpheusAudioKey(23));
                fixture.Readiness.State = OrpheusClipLoadState.Loading;
                manager.Play(new OrpheusAudioKey(23));
                fixture.Readiness.State = OrpheusClipLoadState.Failed;
                manager.Play(new OrpheusAudioKey(23));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Counters.LoadNotReadyRejected, Is.EqualTo(2));
                Assert.That(diagnostics.Counters.LoadFailed, Is.EqualTo(1));
                Assert.That(fixture.Readiness.GetCallCount, Is.EqualTo(3));
                Assert.That(fixture.Readiness.RequestCallCount, Is.Zero);
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                AssertNormalized(fixture.Sources[12]);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Play_SuspendedPrecedesLoadAndLoadStatesNeverRequestOrQueue()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreatePlaybackEvent(
                    22,
                    OrpheusPlaybackKind.OneShot2D,
                    OrpheusLoadPolicy.ExplicitTransient));
                var manager = fixture.CreateReadyManager(out var listener);
                fixture.Readiness.State = OrpheusClipLoadState.Unloaded;
                Assert.That(manager.RemoveListener(listener), Is.True);

                manager.Play(new OrpheusAudioKey(22));

                AssertSingleCounter(manager, "SuspendedRejected");
                Assert.That(fixture.Readiness.GetCallCount, Is.Zero);
                Assert.That(fixture.Readiness.RequestCallCount, Is.Zero);

                Assert.That(manager.BindListener(listener), Is.True);
                manager.Play(new OrpheusAudioKey(22));
                fixture.Readiness.State = OrpheusClipLoadState.Loading;
                manager.Play(new OrpheusAudioKey(22));
                fixture.Readiness.State = OrpheusClipLoadState.Failed;
                manager.Play(new OrpheusAudioKey(22));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Counters.SuspendedRejected, Is.EqualTo(1));
                Assert.That(diagnostics.Counters.LoadNotReadyRejected, Is.EqualTo(2));
                Assert.That(diagnostics.Counters.LoadFailed, Is.EqualTo(1));
                Assert.That(fixture.Readiness.RequestCallCount, Is.Zero);
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                AssertNormalized(fixture.Sources[12]);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Play_AcceptsFirstFreeSourceAndFifthYoungRequestRejectsWithoutSteal()
        {
            using (var fixture = new SessionFixture())
            {
                var group = LoadSfxUiStateGroup();
                fixture.SetStateGroup(OrpheusCategory.SfxUi, group);
                var firstEvent = fixture.CreatePlaybackEvent(
                    33,
                    OrpheusPlaybackKind.OneShot2D,
                    OrpheusLoadPolicy.BootstrapTransient,
                    volume: 0.375f,
                    pitch: 2f,
                    priority: 17);
                fixture.SetEvents(
                    firstEvent,
                    fixture.CreatePlaybackEvent(34),
                    fixture.CreatePlaybackEvent(35),
                    fixture.CreatePlaybackEvent(36),
                    fixture.CreatePlaybackEvent(37));
                var manager = fixture.CreateReadyManager(out _);

                for (var index = 0; index < 5; index++)
                {
                    manager.Play(new OrpheusAudioKey((ushort)(33 + index)));
                }

                var first = fixture.Sources[12];
                Assert.That(first.clip, Is.SameAs(firstEvent.GetClip(0)));
                Assert.That(first.loop, Is.False);
                Assert.That(first.spatialBlend, Is.Zero);
                Assert.That(first.volume, Is.EqualTo(0.375f));
                Assert.That(first.pitch, Is.EqualTo(2f));
                Assert.That(first.priority, Is.EqualTo(17));
                Assert.That(first.outputAudioMixerGroup, Is.SameAs(group));
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Transient2DActiveCount, Is.EqualTo(4));
                Assert.That(diagnostics.Counters.PoolCapacityRejected, Is.EqualTo(1));
                Assert.That(diagnostics.Counters.Stolen, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Play_CooldownRejectsBeforeExactBoundaryAndAcceptsAtEquality()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreatePlaybackEvent(
                    38,
                    polyphonyCap: 4,
                    cooldownSeconds: 0.5f));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);

                manager.Play(new OrpheusAudioKey(38));
                manager.Tick(realtime + 0.499d, 0.499f);
                manager.Play(new OrpheusAudioKey(38));
                manager.Tick(realtime + 0.5d, 0.001f);
                manager.Play(new OrpheusAudioKey(38));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Counters.CooldownRejected, Is.EqualTo(1));
                Assert.That(diagnostics.Transient2DActiveCount, Is.EqualTo(2));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Play_SeededVariationUsesShuffleThenVolumeThenPitch()
        {
            using (var fixture = new SessionFixture())
            {
                var audioEvent = fixture.CreatePlaybackEvent(
                    39,
                    volume: 0.5f,
                    pitch: 0.75f,
                    clipCount: 3,
                    volumeMaximum: 1f,
                    pitchMaximum: 1.25f,
                    polyphonyCap: 3);
                fixture.SetEvents(audioEvent);
                var manager = fixture.CreateReadyManager(out _);

                manager.Play(new OrpheusAudioKey(39));

                var source = fixture.Sources[12];
                var expectedVolume = 0.5f +
                                     (0.5f * ((0x9DCCA8C5u >> 8) * (1f / 16777216f)));
                var expectedPitch = 0.75f +
                                    (0.5f * ((0x1255994Fu >> 8) * (1f / 16777216f)));
                Assert.That(source.clip, Is.SameAs(audioEvent.GetClip(2)));
                Assert.That(source.volume, Is.EqualTo(expectedVolume));
                Assert.That(source.pitch, Is.EqualTo(expectedPitch));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Play_NonUiReservationLeavesTwoFreeSlotsForUi()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreatePlaybackEvent(40, category: OrpheusCategory.SfxCombat),
                    fixture.CreatePlaybackEvent(41, category: OrpheusCategory.SfxWorld),
                    fixture.CreatePlaybackEvent(42, category: OrpheusCategory.SfxCombat),
                    fixture.CreatePlaybackEvent(43, category: OrpheusCategory.SfxUi),
                    fixture.CreatePlaybackEvent(44, category: OrpheusCategory.SfxUi));
                var manager = fixture.CreateReadyManager(out _);

                manager.Play(new OrpheusAudioKey(40));
                manager.Play(new OrpheusAudioKey(41));
                manager.Play(new OrpheusAudioKey(42));
                manager.Play(new OrpheusAudioKey(43));
                manager.Play(new OrpheusAudioKey(44));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Transient2DActiveCount, Is.EqualTo(4));
                Assert.That(diagnostics.Counters.PoolCapacityRejected, Is.EqualTo(1));
                Assert.That(fixture.Sources[14].clip, Is.Not.Null);
                Assert.That(fixture.Sources[15].clip, Is.Not.Null);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Play_RejectionDoesNotAdvanceShuffleOrMutateSource()
        {
            using (var fixture = new SessionFixture())
            {
                var audioEvent = fixture.CreatePlaybackEvent(
                    50,
                    clipCount: 3,
                    polyphonyCap: 3,
                    cooldownSeconds: 0.5f);
                fixture.SetEvents(audioEvent);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);

                manager.Play(new OrpheusAudioKey(50));
                var firstSource = fixture.Sources[12];
                var firstClip = firstSource.clip;
                manager.Play(new OrpheusAudioKey(50));

                Assert.That(firstSource.clip, Is.SameAs(firstClip));
                Assert.That(firstSource.clip, Is.SameAs(audioEvent.GetClip(2)));
                AssertNormalized(fixture.Sources[13]);

                manager.Tick(realtime + 0.5d, 0.5f);
                manager.Play(new OrpheusAudioKey(50));

                Assert.That(fixture.Sources[13].clip, Is.SameAs(audioEvent.GetClip(1)));
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Counters.CooldownRejected, Is.EqualTo(1));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Play_OrdinaryStealFadesForFifteenMillisecondsThenStartsPending()
        {
            using (var fixture = new SessionFixture())
            {
                var victim = fixture.CreatePlaybackEvent(
                    46,
                    volume: 0.8f,
                    priority: 200,
                    polyphonyCap: 4);
                var replacement = fixture.CreatePlaybackEvent(
                    47,
                    volume: 0.6f,
                    priority: 100,
                    cooldownSeconds: 1f);
                fixture.SetEvents(victim, replacement);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                for (var index = 0; index < 4; index++)
                {
                    manager.Play(new OrpheusAudioKey(46));
                }
                Assert.That(manager.TryGetDiagnostics(out var filled), Is.True);
                Assert.That(filled.Transient2DActiveCount, Is.EqualTo(4));
                Assert.That(filled.PendingCount, Is.Zero);

                manager.Tick(realtime + 0.05d, 0.05f);
                manager.Play(new OrpheusAudioKey(47));

                Assert.That(manager.TryGetDiagnostics(out var accepted), Is.True);
                Assert.That(accepted.Transient2DActiveCount, Is.EqualTo(4));
                Assert.That(accepted.FadingCount, Is.EqualTo(1));
                Assert.That(accepted.PendingCount, Is.EqualTo(1));
                Assert.That(accepted.Counters.Stolen, Is.EqualTo(1));
                Assert.That(fixture.Sources[12].clip, Is.SameAs(victim.GetClip(0)));

                manager.Play(new OrpheusAudioKey(47));
                Assert.That(manager.TryGetDiagnostics(out var pendingRejected), Is.True);
                Assert.That(pendingRejected.Counters.CooldownRejected, Is.EqualTo(1));
                Assert.That(pendingRejected.Counters.Stolen, Is.EqualTo(1));

                manager.Tick(realtime + 1.051d, 0f);
                manager.Play(new OrpheusAudioKey(47));
                Assert.That(manager.TryGetDiagnostics(out var cannotOverwrite), Is.True);
                Assert.That(cannotOverwrite.Counters.PolyphonyRejected, Is.EqualTo(1));
                Assert.That(cannotOverwrite.PendingCount, Is.EqualTo(1));
                Assert.That(cannotOverwrite.Counters.Stolen, Is.EqualTo(1));

                manager.Tick(realtime + 1.0575d, 0.0075f);
                Assert.That(fixture.Sources[12].volume, Is.EqualTo(0.4f).Within(0.0001f));
                manager.Tick(realtime + 1.065d, 0.0075f);

                Assert.That(manager.TryGetDiagnostics(out var started), Is.True);
                Assert.That(started.Transient2DActiveCount, Is.EqualTo(4));
                Assert.That(started.FadingCount, Is.Zero);
                Assert.That(started.PendingCount, Is.Zero);
                Assert.That(fixture.Sources[12].clip, Is.SameAs(replacement.GetClip(0)));
                Assert.That(fixture.Sources[12].volume, Is.EqualTo(0.6f));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Play_SameKeyCapRejectsBeforeGraceThenReplacesOldestAtBoundary()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreatePlaybackEvent(51));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);

                manager.Play(new OrpheusAudioKey(51));
                manager.Play(new OrpheusAudioKey(51));
                Assert.That(manager.TryGetDiagnostics(out var tooYoung), Is.True);
                Assert.That(tooYoung.Counters.PolyphonyRejected, Is.EqualTo(1));
                Assert.That(tooYoung.Counters.Stolen, Is.Zero);

                manager.Tick(realtime + 0.05d, 0.05f);
                manager.Play(new OrpheusAudioKey(51));

                Assert.That(manager.TryGetDiagnostics(out var accepted), Is.True);
                Assert.That(accepted.Transient2DActiveCount, Is.EqualTo(1));
                Assert.That(accepted.FadingCount, Is.EqualTo(1));
                Assert.That(accepted.PendingCount, Is.EqualTo(1));
                Assert.That(accepted.Counters.PolyphonyRejected, Is.EqualTo(1));
                Assert.That(accepted.Counters.Stolen, Is.EqualTo(1));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Tick_InvalidPendingAfterFadeCancelsWithoutOrdinaryLoadRejection()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreatePlaybackEvent(48, priority: 200, polyphonyCap: 4),
                    fixture.CreatePlaybackEvent(49, priority: 100));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                for (var index = 0; index < 4; index++)
                {
                    manager.Play(new OrpheusAudioKey(48));
                }

                manager.Tick(realtime + 0.05d, 0.05f);
                manager.Play(new OrpheusAudioKey(49));
                fixture.Readiness.State = OrpheusClipLoadState.Failed;
                manager.Tick(realtime + 0.065d, 0.015f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Transient2DActiveCount, Is.EqualTo(3));
                Assert.That(diagnostics.FadingCount, Is.Zero);
                Assert.That(diagnostics.PendingCount, Is.Zero);
                Assert.That(diagnostics.Counters.Stolen, Is.EqualTo(1));
                Assert.That(diagnostics.Counters.LoadFailed, Is.Zero);
                AssertNormalized(fixture.Sources[12]);
                DisposeBound(fixture, manager);
            }
        }

        [TestCase(0.0075f, OrpheusClipLoadState.Loaded)]
        [TestCase(0.015f, OrpheusClipLoadState.Loaded)]
        [TestCase(0.015f, OrpheusClipLoadState.Failed)]
        public void Tick_DestroyedSourceDuringFadeUsesStableRootCause(
            float activeDelta,
            OrpheusClipLoadState pendingLoadState)
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreatePlaybackEvent(52, priority: 200, polyphonyCap: 4),
                    fixture.CreatePlaybackEvent(53, priority: 100));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                for (var index = 0; index < 4; index++)
                {
                    manager.Play(new OrpheusAudioKey(52));
                }

                manager.Tick(realtime + 0.05d, 0.05f);
                manager.Play(new OrpheusAudioKey(53));
                UnityEngine.Object.DestroyImmediate(fixture.Sources[12].gameObject);
                fixture.Readiness.State = pendingLoadState;

                Assert.DoesNotThrow(
                    () => manager.Tick(realtime + 0.05d + activeDelta, activeDelta));
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.OwnedSourceDestroyed));
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                Assert.That(diagnostics.FadingCount, Is.Zero);
                Assert.That(diagnostics.PendingCount, Is.Zero);
                Assert.That(diagnostics.Counters.UnexpectedException, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Tick_InvalidMixerGroupDuringPendingCompletionFailCloses()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreatePlaybackEvent(54, priority: 200, polyphonyCap: 4),
                    fixture.CreatePlaybackEvent(55, priority: 100));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                for (var index = 0; index < 4; index++)
                {
                    manager.Play(new OrpheusAudioKey(54));
                }

                manager.Tick(realtime + 0.05d, 0.05f);
                manager.Play(new OrpheusAudioKey(55));
                SetField(manager, "_categoryRoutes", default(OrpheusAudioCategoryRoutes));
                manager.Tick(realtime + 0.065d, 0.015f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.MixerGroupReferenceInvalid));
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                Assert.That(diagnostics.FadingCount, Is.Zero);
                Assert.That(diagnostics.PendingCount, Is.Zero);
                Assert.That(diagnostics.Counters.UnexpectedException, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [TestCase(OrpheusCategory.Music, "Music_State")]
        [TestCase(OrpheusCategory.SfxCombat, "SFX_Combat_State")]
        [TestCase(OrpheusCategory.SfxWorld, "SFX_World_State")]
        [TestCase(OrpheusCategory.SfxUi, "SFX_UI_State")]
        [TestCase(OrpheusCategory.Ambience, "Ambience_State")]
        public void Play_RoutesEveryCategoryToItsDirectStateGroup(
            object categoryValue,
            string groupName)
        {
            var category = (OrpheusCategory)categoryValue;
            using (var fixture = new SessionFixture())
            {
                var group = LoadStateGroup(groupName);
                fixture.SetStateGroup(category, group);
                fixture.SetEvents(fixture.CreatePlaybackEvent(34, category: category));
                var manager = fixture.CreateReadyManager(out _);

                manager.Play(new OrpheusAudioKey(34));

                Assert.That(fixture.Sources[12].outputAudioMixerGroup, Is.SameAs(group));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void CompleteHostReady_MissingCategoryRouteFailClosesBeforePlayback()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.ClearStateGroup(OrpheusCategory.SfxUi);
                fixture.SetEvents(fixture.CreatePlaybackEvent(35));
                var manager = fixture.CreateManagerBeforeHostReady(out _);

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.DisabledByValidationFailure));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.MixerGroupReferenceInvalid));
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                AssertNormalized(fixture.Sources[12]);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void CompleteHostReady_ForeignCategoryRouteMixerFailClosesBeforePlayback()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.UseForeignSettingsMixerIdentity();
                fixture.SetEvents(fixture.CreatePlaybackEvent(36));
                var manager = fixture.CreateManagerBeforeHostReady(out _);

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.DisabledByValidationFailure));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.MixerGroupReferenceInvalid));
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                AssertNormalized(fixture.Sources[12]);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Play_ReadinessExceptionDoesNotAddOrdinaryLoadRejection()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreatePlaybackEvent(
                    36,
                    loadPolicy: OrpheusLoadPolicy.ExplicitTransient));
                var manager = fixture.CreateReadyManager(out _);
                fixture.Readiness.ThrowOnGet = true;

                manager.Play(new OrpheusAudioKey(36));

                AssertSingleCounter(manager, "UnexpectedException");
                AssertNormalized(fixture.Sources[12]);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Tick_UsesLogicalActiveTimeAndIgnoresPhysicalIsPlaying()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetStateGroup(OrpheusCategory.SfxUi, LoadSfxUiStateGroup());
                fixture.SetEvents(fixture.CreatePlaybackEvent(44, pitch: 2f));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                manager.Play(new OrpheusAudioKey(44));
                var source = fixture.Sources[12];
                source.Stop();

                manager.Tick(realtime + 0.1d, 0.25f);
                Assert.That(manager.TryGetDiagnostics(out var beforeBoundary), Is.True);
                Assert.That(beforeBoundary.Transient2DActiveCount, Is.EqualTo(1));
                Assert.That(source.clip, Is.Not.Null);

                manager.Tick(realtime + 0.2d, 0.25f);

                Assert.That(manager.TryGetDiagnostics(out var completed), Is.True);
                Assert.That(completed.Transient2DActiveCount, Is.Zero);
                AssertNormalized(source);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Tick_RemoveAndRebindBetweenTicksStillZerosFirstResumeDelta()
        {
            using (var fixture = new SessionFixture())
            {
                var loop = fixture.CreateGlobalLoopEvent(45, volume: 0.8f);
                fixture.SetEvents(loop);
                var manager = fixture.CreateReadyManager(out var listener);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                manager.PlayLoop(new OrpheusAudioKey(45));
                manager.Tick(realtime + 0.001d, 0f);
                var source = fixture.FindGlobalLoopSource(loop.GetClip(0));
                manager.StopLoop(new OrpheusAudioKey(45));
                manager.Tick(realtime + 0.0075d, 0.0075f);
                var halfVolume = source.volume;
                Assert.That(halfVolume, Is.EqualTo(0.4f).Within(0.0001f));

                Assert.That(manager.RemoveListener(listener), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                manager.Tick(realtime + 1d, 1f);

                Assert.That(manager.TryGetDiagnostics(out var resumed), Is.True);
                Assert.That(resumed.GlobalLoopActiveCount, Is.EqualTo(1));
                Assert.That(source.clip, Is.SameAs(loop.GetClip(0)));
                Assert.That(source.volume, Is.EqualTo(halfVolume));

                manager.Tick(realtime + 1.0075d, 0.0075f);
                Assert.That(manager.TryGetDiagnostics(out var completed), Is.True);
                Assert.That(completed.GlobalLoopActiveCount, Is.Zero);
                AssertNormalized(source);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Play_WrongThreadOnlyIncrementsWrongThreadCounter()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreatePlaybackEvent(55));
                var manager = fixture.CreateReadyManager(out _);
                fixture.Readiness.ResetCalls();
                Exception workerException = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        manager.Play(new OrpheusAudioKey(55));
                    }
                    catch (Exception exception)
                    {
                        workerException = exception;
                    }
                });

                worker.Start();
                Assert.That(worker.Join(5000), Is.True);

                Assert.That(workerException, Is.Null);
                AssertSingleCounter(manager, "WrongThreadRejected");
                AssertNoReadinessSideEffects(fixture);
                AssertNormalized(fixture.Sources[12]);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void InvalidClock_FailClosesAndNormalizesActivePlayback()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetStateGroup(OrpheusCategory.SfxUi, LoadSfxUiStateGroup());
                fixture.SetEvents(fixture.CreatePlaybackEvent(66));
                var manager = fixture.CreateReadyManager(out _);
                manager.Play(new OrpheusAudioKey(66));

                manager.Tick(double.NaN, 0.016f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(diagnostics.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.RuntimeTimeInvalid));
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                AssertNormalized(fixture.Sources[12]);
                DisposeBound(fixture, manager);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void InvalidClockMatrix_FailClosesAndNormalizesActivePlayback(int caseIndex)
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetStateGroup(OrpheusCategory.SfxUi, LoadSfxUiStateGroup());
                fixture.SetEvents(fixture.CreatePlaybackEvent(67));
                var manager = fixture.CreateReadyManager(out _);
                manager.Play(new OrpheusAudioKey(67));
                var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                var delta = 0.016f;
                switch (caseIndex)
                {
                    case 0:
                        realtime = double.NaN;
                        break;
                    case 1:
                        realtime = double.PositiveInfinity;
                        break;
                    case 2:
                        realtime = double.NegativeInfinity;
                        break;
                    case 3:
                        realtime = -1d;
                        break;
                    case 4:
                        delta = float.NaN;
                        break;
                    case 5:
                        delta = float.PositiveInfinity;
                        break;
                    case 6:
                        delta = -0.001f;
                        break;
                    case 7:
                        delta = float.NegativeInfinity;
                        break;
                }

                manager.Tick(realtime, delta);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(diagnostics.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.RuntimeTimeInvalid));
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                AssertNormalized(fixture.Sources[12]);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void BackwardsRealtime_FailClosesAndNormalizesActivePlayback()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetStateGroup(OrpheusCategory.SfxUi, LoadSfxUiStateGroup());
                fixture.SetEvents(fixture.CreatePlaybackEvent(68));
                var manager = fixture.CreateReadyManager(out _);
                manager.Play(new OrpheusAudioKey(68));
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime + 0.2d, 0.016f);

                manager.Tick(realtime + 0.1d, 0.016f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.RealtimeMovedBackwards));
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                AssertNormalized(fixture.Sources[12]);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void SteadyStatePlayTickAndLateTick_AllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetStateGroup(OrpheusCategory.SfxUi, LoadSfxUiStateGroup());
                fixture.SetEvents(fixture.CreatePlaybackEvent(77));
                var manager = fixture.CreateReadyManager(out _);
                var key = new OrpheusAudioKey(77);
                var realtime = Time.realtimeSinceStartupAsDouble;

                manager.Play(key);
                manager.Tick(realtime + 0.1d, 0f);
                manager.Tick(realtime + 0.2d, 2f);
                manager.LateTick();

                var beforePlay = GC.GetAllocatedBytesForCurrentThread();
                manager.Play(key);
                var playBytes = GC.GetAllocatedBytesForCurrentThread() - beforePlay;

                var beforeTick = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.3d, 0.01f);
                var tickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeTick;

                var beforeLateTick = GC.GetAllocatedBytesForCurrentThread();
                manager.LateTick();
                var lateTickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeLateTick;

                Assert.That(playBytes, Is.Zero);
                Assert.That(tickBytes, Is.Zero);
                Assert.That(lateTickBytes, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void AcceptedRandomizedStealAndFadeTicks_AllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreatePlaybackEvent(78, priority: 200, polyphonyCap: 4),
                    fixture.CreatePlaybackEvent(
                        79,
                        volume: 0.5f,
                        pitch: 0.75f,
                        priority: 150,
                        clipCount: 3,
                        volumeMaximum: 1f,
                        pitchMaximum: 1.25f),
                    fixture.CreatePlaybackEvent(
                        80,
                        volume: 0.5f,
                        pitch: 0.75f,
                        priority: 100,
                        clipCount: 3,
                        volumeMaximum: 1f,
                        pitchMaximum: 1.25f));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                for (var index = 0; index < 4; index++)
                {
                    manager.Play(new OrpheusAudioKey(78));
                }

                manager.Tick(realtime + 0.05d, 0.05f);
                manager.Play(new OrpheusAudioKey(79));
                manager.Tick(realtime + 0.0575d, 0.0075f);
                manager.Tick(realtime + 0.065d, 0.0075f);
                manager.Tick(realtime + 0.115d, 0.05f);

                var beforeSteal = GC.GetAllocatedBytesForCurrentThread();
                manager.Play(new OrpheusAudioKey(80));
                var stealBytes = GC.GetAllocatedBytesForCurrentThread() - beforeSteal;

                var beforePartialFade = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.1225d, 0.0075f);
                var partialFadeBytes =
                    GC.GetAllocatedBytesForCurrentThread() - beforePartialFade;

                var beforePendingStart = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 0.13d, 0.0075f);
                var pendingStartBytes =
                    GC.GetAllocatedBytesForCurrentThread() - beforePendingStart;

                Assert.That(stealBytes, Is.Zero);
                Assert.That(partialFadeBytes, Is.Zero);
                Assert.That(pendingStartBytes, Is.Zero);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Transient2DActiveCount, Is.EqualTo(4));
                Assert.That(diagnostics.FadingCount, Is.Zero);
                Assert.That(diagnostics.PendingCount, Is.Zero);
                Assert.That(diagnostics.Counters.Stolen, Is.EqualTo(2));
                DisposeBound(fixture, manager);
            }
        }

        private static void AssertSingleCounter(OrpheusAudioManager manager, string counterName)
        {
            Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
            var fields = typeof(OrpheusAudioDiagnosticsCounters).GetFields(
                BindingFlags.Instance | BindingFlags.Public);
            for (var index = 0; index < fields.Length; index++)
            {
                var field = fields[index];
                var expected = field.Name == counterName ? 1UL : 0UL;
                Assert.That((ulong)field.GetValue(diagnostics.Counters), Is.EqualTo(expected), field.Name);
            }
        }

        private static void AssertNoReadinessSideEffects(SessionFixture fixture)
        {
            Assert.That(fixture.Readiness.GetCallCount, Is.Zero);
            Assert.That(fixture.Readiness.RequestCallCount, Is.Zero);
        }

        private static AudioMixerGroup LoadSfxUiStateGroup()
        {
            return LoadStateGroup("SFX_UI_State");
        }

        private static AudioMixerGroup LoadStateGroup(string groupName)
        {
            var mixer = Resources.Load<AudioMixer>("OrpheusAudioTest");
            Assert.That(mixer, Is.Not.Null);
            var groups = mixer.FindMatchingGroups(groupName);
            Assert.That(groups, Has.Length.EqualTo(1));
            return groups[0];
        }

        private sealed partial class SessionFixture
        {
            private readonly List<UnityEngine.Object> _playbackTestObjects =
                new List<UnityEngine.Object>();

            internal FakeClipReadiness Readiness => _clipReadiness;

            internal OrpheusAudioEvent CreatePlaybackEvent(
                ushort key,
                OrpheusPlaybackKind playbackKind = OrpheusPlaybackKind.OneShot2D,
                OrpheusLoadPolicy loadPolicy = OrpheusLoadPolicy.BootstrapTransient,
                float volume = 1f,
                float pitch = 1f,
                byte priority = 128,
                OrpheusCategory category = OrpheusCategory.SfxUi,
                int clipCount = 1,
                float volumeMaximum = float.NaN,
                float pitchMaximum = float.NaN,
                byte polyphonyCap = 1,
                float cooldownSeconds = 0f,
                float minimumDistance = 1f,
                float maximumDistance = 100f,
                OrpheusRolloffMode rolloffMode = OrpheusRolloffMode.Logarithmic)
            {
                var clips = new AudioClip[clipCount];
                for (var clipIndex = 0; clipIndex < clipCount; clipIndex++)
                {
                    clips[clipIndex] = AudioClip.Create(
                        "Issue07Clip" + key + "_" + clipIndex,
                        48000,
                        1,
                        48000,
                        false);
                    _playbackTestObjects.Add(clips[clipIndex]);
                }

                var audioEvent = ScriptableObject.CreateInstance<OrpheusAudioEvent>();
                SetField(audioEvent, "_key", key);
                SetField(audioEvent, "_playbackKind", playbackKind);
                SetField(audioEvent, "_category", category);
                SetField(audioEvent, "_loadPolicy", loadPolicy);
                SetField(audioEvent, "_clips", clips);
                SetField(audioEvent, "_volumeMin", volume);
                SetField(audioEvent, "_volumeMax", float.IsNaN(volumeMaximum) ? volume : volumeMaximum);
                SetField(audioEvent, "_pitchMin", pitch);
                SetField(audioEvent, "_pitchMax", float.IsNaN(pitchMaximum) ? pitch : pitchMaximum);
                SetField(audioEvent, "_priority", priority);
                SetField(audioEvent, "_polyphonyCap", polyphonyCap);
                SetField(audioEvent, "_cooldownSeconds", cooldownSeconds);
                SetField(audioEvent, "_minimumDistance", minimumDistance);
                SetField(audioEvent, "_maximumDistance", maximumDistance);
                SetField(audioEvent, "_rolloffMode", rolloffMode);
                _playbackTestObjects.Add(audioEvent);
                return audioEvent;
            }

            internal void SetStateGroup(OrpheusCategory category, AudioMixerGroup group)
            {
                var fieldName = GetStateGroupFieldName(category);
                Assert.That(fieldName, Is.Not.Null);
                SetField(_settings, fieldName, group);
                SetField(_settings, "_mixer", group.audioMixer);
                _mixer.SetLeaseIdentity(group.audioMixer);
            }

            internal void ClearStateGroup(OrpheusCategory category)
            {
                var fieldName = GetStateGroupFieldName(category);
                Assert.That(fieldName, Is.Not.Null);
                SetField(_settings, fieldName, null);
            }

            internal void UseForeignSettingsMixerIdentity()
            {
                var mixer = Resources.Load<AudioMixer>("OrpheusAudioTest");
                Assert.That(mixer, Is.Not.Null);
                var foreignMixer = UnityEngine.Object.Instantiate(mixer);
                Assert.That(foreignMixer, Is.Not.Null);
                _playbackTestObjects.Add(foreignMixer);
                SetField(_settings, "_mixer", foreignMixer);
                _mixer.SetLeaseIdentity(foreignMixer);
            }

            internal OrpheusAudioManager CreateManagerBeforeHostReady(out AudioListener listener)
            {
                SetField(Host, "_hasStarted", true);
                listener = CreateListener(this, "Issue06Listener");
                Assert.That(Create(out var manager).Success, Is.True);
                Assert.That(Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                return manager;
            }

            internal OrpheusAudioManager CreateReadyManager(out AudioListener listener)
            {
                var manager = CreateManagerBeforeHostReady(out listener);
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                Assert.That(
                    manager.CompleteBootstrapHydration(manager.CaptureBootstrapAuthority()),
                    Is.True);
                return manager;
            }

            private static AudioMixer ConfigureValidationMixer(OrpheusAudioSettings settings)
            {
                var mixer = Resources.Load<AudioMixer>("OrpheusAudioTest");
                Assert.That(mixer, Is.Not.Null);
                SetField(settings, "_mixer", mixer);
                SetField(settings, "_master", GetOnlyGroup(mixer, "Master"));
                SetField(settings, "_musicUser", GetOnlyGroup(mixer, "Music_User"));
                SetField(settings, "_musicState", GetOnlyGroup(mixer, "Music_State"));
                SetField(settings, "_sfxCombatUser", GetOnlyGroup(mixer, "SFX_Combat_User"));
                SetField(settings, "_sfxCombatState", GetOnlyGroup(mixer, "SFX_Combat_State"));
                SetField(settings, "_sfxWorldUser", GetOnlyGroup(mixer, "SFX_World_User"));
                SetField(settings, "_sfxWorldState", GetOnlyGroup(mixer, "SFX_World_State"));
                SetField(settings, "_sfxUiUser", GetOnlyGroup(mixer, "SFX_UI_User"));
                SetField(settings, "_sfxUiState", GetOnlyGroup(mixer, "SFX_UI_State"));
                SetField(settings, "_ambienceUser", GetOnlyGroup(mixer, "Ambience_User"));
                SetField(settings, "_ambienceState", GetOnlyGroup(mixer, "Ambience_State"));
                SetField(settings, "_peace", mixer.FindSnapshot("Peace"));
                SetField(settings, "_combat", mixer.FindSnapshot("Combat"));
                SetField(settings, "_menu", mixer.FindSnapshot("Menu"));
                SetField(settings, "_pause", mixer.FindSnapshot("Pause"));
                return mixer;
            }

            private static AudioMixerGroup GetOnlyGroup(AudioMixer mixer, string groupName)
            {
                var groups = mixer.FindMatchingGroups(string.Empty);
                AudioMixerGroup match = null;
                var matchCount = 0;
                for (var index = 0; index < groups.Length; index++)
                {
                    if (groups[index].name == groupName)
                    {
                        match = groups[index];
                        matchCount++;
                    }
                }

                Assert.That(matchCount, Is.EqualTo(1), groupName);
                return match;
            }

            private static string GetStateGroupFieldName(OrpheusCategory category)
            {
                switch (category)
                {
                    case OrpheusCategory.Music:
                        return "_musicState";
                    case OrpheusCategory.SfxCombat:
                        return "_sfxCombatState";
                    case OrpheusCategory.SfxWorld:
                        return "_sfxWorldState";
                    case OrpheusCategory.SfxUi:
                        return "_sfxUiState";
                    case OrpheusCategory.Ambience:
                        return "_ambienceState";
                    default:
                        return null;
                }
            }

            private void DestroyPlaybackTestObjects()
            {
                for (var index = _playbackTestObjects.Count - 1; index >= 0; index--)
                {
                    var owned = _playbackTestObjects[index];
                    if (owned != null)
                    {
                        UnityEngine.Object.DestroyImmediate(owned);
                    }
                }

                _playbackTestObjects.Clear();
            }
        }
    }
}
