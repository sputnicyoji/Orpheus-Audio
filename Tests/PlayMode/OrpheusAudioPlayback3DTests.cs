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
        public void PlayAt_HasOneExactFixedPositionPublicSurface()
        {
            var method = typeof(OrpheusAudioManager).GetMethod(
                "PlayAt",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(OrpheusAudioKey), typeof(Vector3) },
                null);
            var methods = typeof(OrpheusAudioManager).GetMethods(
                BindingFlags.Instance | BindingFlags.Public);
            var playAtCount = 0;
            for (var index = 0; index < methods.Length; index++)
            {
                if (methods[index].Name == "PlayAt")
                {
                    playAtCount++;
                }
            }

            Assert.That(method, Is.Not.Null);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(playAtCount, Is.EqualTo(1));
        }

        [Test]
        public void PlayAt_RejectionOrderStopsBeforeLaterMutation()
        {
            using (var wrongKind = new SessionFixture())
            {
                wrongKind.SetEvents(wrongKind.CreatePlaybackEvent(81));
                var manager = wrongKind.CreateReadyManager(out _);

                manager.PlayAt(new OrpheusAudioKey(81), new Vector3(float.NaN, 0f, 0f));

                AssertSingleCounter(manager, "PlaybackKindRejected");
                AssertNormalized(wrongKind.Sources[0]);
                DisposeBound(wrongKind, manager);
            }

            using (var invalidPosition = new SessionFixture())
            {
                invalidPosition.SetEvents(invalidPosition.CreateSpatialPlaybackEvent(82));
                var manager = invalidPosition.CreateReadyManager(out _);

                manager.PlayAt(new OrpheusAudioKey(82), new Vector3(0f, float.PositiveInfinity, 0f));

                AssertSingleCounter(manager, "InvalidPositionRejected");
                AssertNormalized(invalidPosition.Sources[0]);
                DisposeBound(invalidPosition, manager);
            }

            using (var preReady = new SessionFixture())
            {
                preReady.SetEvents(preReady.CreateSpatialPlaybackEvent(83));
                var manager = preReady.CreateManagerBeforeHostReady(out _);

                manager.PlayAt(new OrpheusAudioKey(83), Vector3.zero);

                AssertSingleCounter(manager, "PreReadyRejected");
                AssertNormalized(preReady.Sources[0]);
                DisposeBound(preReady, manager);
            }
        }

        [Test]
        public void PlayAt_WrongThreadOnlyIncrementsWrongThreadCounter()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateSpatialPlaybackEvent(80));
                var manager = fixture.CreateReadyManager(out _);
                fixture.Readiness.ResetCalls();
                Exception workerException = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        manager.PlayAt(new OrpheusAudioKey(80), Vector3.zero);
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
                AssertNormalized(fixture.Sources[0]);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PlayAt_InvalidPositionAndDistanceDoNotCommitVariationCooldownOrOccupancy()
        {
            using (var fixture = new SessionFixture())
            {
                var audioEvent = fixture.CreateSpatialPlaybackEvent(
                    79,
                    polyphonyCap: 3,
                    cooldownSeconds: 0.5f,
                    maximumDistance: 10f,
                    clipCount: 3);
                fixture.SetEvents(audioEvent);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);

                manager.PlayAt(new OrpheusAudioKey(79), new Vector3(float.NaN, 0f, 0f));
                manager.PlayAt(new OrpheusAudioKey(79), new Vector3(11f, 0f, 0f));
                manager.PlayAt(new OrpheusAudioKey(79), Vector3.zero);
                manager.PlayAt(new OrpheusAudioKey(79), Vector3.one);

                Assert.That(fixture.Sources[0].clip, Is.SameAs(audioEvent.GetClip(2)));
                AssertNormalized(fixture.Sources[1]);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Transient3DActiveCount, Is.EqualTo(1));
                Assert.That(diagnostics.Counters.InvalidPositionRejected, Is.EqualTo(1));
                Assert.That(diagnostics.Counters.DistanceRejected, Is.EqualTo(1));
                Assert.That(diagnostics.Counters.CooldownRejected, Is.EqualTo(1));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PlayAt_ConfiguresFixedSpatialSourceAndReports3DCount()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateSpatialPlaybackEvent(
                    84,
                    category: OrpheusCategory.SfxCombat,
                    minimumDistance: 2f,
                    maximumDistance: 20f,
                    rolloffMode: OrpheusRolloffMode.Linear));
                var manager = fixture.CreateReadyManager(out _);
                var position = new Vector3(3f, 4f, 5f);

                manager.PlayAt(new OrpheusAudioKey(84), position);

                var source = fixture.Sources[0];
                Assert.That(source.clip, Is.Not.Null);
                Assert.That(source.transform.position, Is.EqualTo(position));
                Assert.That(source.spatialBlend, Is.EqualTo(1f));
                Assert.That(source.minDistance, Is.EqualTo(2f));
                Assert.That(source.maxDistance, Is.EqualTo(20f));
                Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Transient3DActiveCount, Is.EqualTo(1));
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void LateTick_ReappliesFixedWorldPositionAfterCarrierMovementWithoutAllocation()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateSpatialPlaybackEvent(78));
                var manager = fixture.CreateReadyManager(out _);
                var fixedPosition = new Vector3(5f, 6f, 7f);
                manager.PlayAt(new OrpheusAudioKey(78), fixedPosition);
                var source = fixture.Sources[0];
                Assert.That(source.transform.parent, Is.Not.Null);
                source.transform.parent.position = new Vector3(100f, 0f, 0f);
                Assert.That(source.transform.position, Is.Not.EqualTo(fixedPosition));

                manager.LateTick();
                source.transform.parent.position = new Vector3(200f, 0f, 0f);
                var before = GC.GetAllocatedBytesForCurrentThread();
                manager.LateTick();
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(source.transform.position, Is.EqualTo(fixedPosition));
                Assert.That(allocated, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PlayAt_UsesCachedListenerAndAcceptsExactMaximumDistance()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreateSpatialPlaybackEvent(85, maximumDistance: 10f),
                    fixture.CreateSpatialPlaybackEvent(86, maximumDistance: 10f));
                var manager = fixture.CreateReadyManager(out var listener);

                manager.PlayAt(new OrpheusAudioKey(85), new Vector3(10f, 0f, 0f));
                listener.transform.position = new Vector3(10f, 0f, 0f);
                manager.PlayAt(new OrpheusAudioKey(86), new Vector3(15f, 0f, 0f));
                manager.LateTick();
                manager.PlayAt(new OrpheusAudioKey(86), new Vector3(15f, 0f, 0f));

                Assert.That(fixture.Sources[0].clip, Is.Not.Null);
                Assert.That(fixture.Sources[1].clip, Is.Not.Null);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Transient3DActiveCount, Is.EqualTo(2));
                Assert.That(diagnostics.Counters.DistanceRejected, Is.EqualTo(1));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PlayAt_UsesOnlyTwelve3DSlotsAndNeverBorrows2D()
        {
            using (var fixture = new SessionFixture())
            {
                var events = new OrpheusAudioEvent[13];
                for (var index = 0; index < events.Length; index++)
                {
                    events[index] = fixture.CreateSpatialPlaybackEvent(
                        (ushort)(87 + index),
                        priority: 100,
                        maximumDistance: 100f);
                }
                fixture.SetEvents(events);
                var manager = fixture.CreateReadyManager(out _);

                for (var index = 0; index < 12; index++)
                {
                    manager.PlayAt(
                        new OrpheusAudioKey((ushort)(87 + index)),
                        new Vector3(index, 0f, 0f));
                }
                manager.PlayAt(new OrpheusAudioKey(99), Vector3.zero);

                for (var index = 0; index < 12; index++)
                {
                    Assert.That(fixture.Sources[index].clip, Is.Not.Null, index.ToString());
                }
                AssertNormalized(fixture.Sources[12]);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Transient3DActiveCount, Is.EqualTo(12));
                Assert.That(diagnostics.Counters.PoolCapacityRejected, Is.EqualTo(1));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PlayAt_SameKeyReplacementStartsAfterExactDeclick()
        {
            using (var fixture = new SessionFixture())
            {
                var audioEvent = fixture.CreateSpatialPlaybackEvent(
                    88,
                    polyphonyCap: 1,
                    maximumDistance: 100f);
                fixture.SetEvents(audioEvent);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                manager.PlayAt(new OrpheusAudioKey(88), new Vector3(1f, 0f, 0f));
                manager.PlayAt(new OrpheusAudioKey(88), new Vector3(2f, 0f, 0f));
                AssertSingleCounter(manager, "PolyphonyRejected");
                Assert.That(manager.TryGetDiagnostics(out var initial), Is.True);
                Assert.That(initial.Transient3DActiveCount, Is.EqualTo(1));
                Assert.That(initial.PendingCount, Is.Zero);

                manager.Tick(realtime + 0.05d, 0.05f);
                manager.PlayAt(new OrpheusAudioKey(88), new Vector3(2f, 0f, 0f));

                Assert.That(manager.TryGetDiagnostics(out var fading), Is.True);
                Assert.That(fading.Transient3DActiveCount, Is.EqualTo(1));
                Assert.That(fading.FadingCount, Is.EqualTo(1));
                Assert.That(fading.PendingCount, Is.EqualTo(1));
                Assert.That(fading.Counters.Stolen, Is.EqualTo(1));
                manager.Tick(realtime + 0.0575d, 0.0075f);
                Assert.That(fixture.Sources[0].volume, Is.EqualTo(0.5f).Within(0.0001f));
                manager.Tick(realtime + 0.065d, 0.0075f);
                Assert.That(fixture.Sources[0].transform.position, Is.EqualTo(new Vector3(2f, 0f, 0f)));
                Assert.That(manager.TryGetDiagnostics(out var started), Is.True);
                Assert.That(started.FadingCount, Is.Zero);
                Assert.That(started.PendingCount, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PlayAt_OrdinaryStealUsesCurrentUnclampedVictimDistanceRatio()
        {
            using (var fixture = new SessionFixture())
            {
                var events = new OrpheusAudioEvent[13];
                for (var index = 0; index < 12; index++)
                {
                    events[index] = fixture.CreateSpatialPlaybackEvent(
                        (ushort)(100 + index),
                        priority: 200,
                        maximumDistance: 10f);
                }
                events[12] = fixture.CreateSpatialPlaybackEvent(
                    112,
                    priority: 100,
                    maximumDistance: 10f);
                fixture.SetEvents(events);
                var manager = fixture.CreateReadyManager(out var listener);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                for (var index = 0; index < 12; index++)
                {
                    var x = index == 7 ? 9f : index * 0.1f;
                    manager.PlayAt(new OrpheusAudioKey((ushort)(100 + index)), new Vector3(x, 0f, 0f));
                }
                manager.Tick(realtime + 0.05d, 0.05f);
                listener.transform.position = new Vector3(-20f, 0f, 0f);
                manager.LateTick();

                manager.PlayAt(new OrpheusAudioKey(112), new Vector3(-20f, 0f, 0f));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.FadingCount, Is.EqualTo(1));
                Assert.That(diagnostics.PendingCount, Is.EqualTo(1));
                Assert.That(diagnostics.Counters.Stolen, Is.EqualTo(1));
                Assert.That(fixture.Sources[7].clip, Is.SameAs(events[7].GetClip(0)));
                manager.Tick(realtime + 0.065d, 0.015f);
                Assert.That(fixture.Sources[7].clip, Is.SameAs(events[12].GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PlayAt_TwelveSlotRuntimeSelectsOnlyVictimAtExactFiftyMilliseconds()
        {
            using (var fixture = new SessionFixture())
            {
                var events = new OrpheusAudioEvent[13];
                for (var index = 0; index < events.Length; index++)
                {
                    events[index] = fixture.CreateSpatialPlaybackEvent(
                        (ushort)(140 + index),
                        priority: 200,
                        maximumDistance: 100f);
                }
                fixture.SetEvents(events);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                manager.PlayAt(new OrpheusAudioKey(140), Vector3.zero);
                manager.Tick(realtime + 0.05d, 0.05f);
                for (var index = 1; index < 12; index++)
                {
                    manager.PlayAt(
                        new OrpheusAudioKey((ushort)(140 + index)),
                        Vector3.zero);
                }

                manager.PlayAt(new OrpheusAudioKey(152), Vector3.zero);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.FadingCount, Is.EqualTo(1));
                Assert.That(fixture.Sources[0].clip, Is.SameAs(events[0].GetClip(0)));
                for (var index = 1; index < 12; index++)
                {
                    Assert.That(fixture.Sources[index].clip, Is.SameAs(events[index].GetClip(0)));
                }
                manager.Tick(realtime + 0.065d, 0.015f);
                Assert.That(fixture.Sources[0].clip, Is.SameAs(events[12].GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PlayAt_TwelveSlotRuntimePriorityDominatesDistance()
        {
            using (var fixture = new SessionFixture())
            {
                var events = new OrpheusAudioEvent[13];
                for (var index = 0; index < 12; index++)
                {
                    events[index] = fixture.CreateSpatialPlaybackEvent(
                        (ushort)(160 + index),
                        priority: index == 3 ? (byte)201 : (byte)200,
                        maximumDistance: 10f);
                }
                events[12] = fixture.CreateSpatialPlaybackEvent(
                    172,
                    priority: 100,
                    maximumDistance: 10f);
                fixture.SetEvents(events);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                for (var index = 0; index < 12; index++)
                {
                    var position = index == 7
                        ? new Vector3(9f, 0f, 0f)
                        : new Vector3(index * 0.1f, 0f, 0f);
                    manager.PlayAt(new OrpheusAudioKey((ushort)(160 + index)), position);
                }
                manager.Tick(realtime + 0.05d, 0.05f);

                manager.PlayAt(new OrpheusAudioKey(172), Vector3.zero);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.FadingCount, Is.EqualTo(1));
                Assert.That(fixture.Sources[3].clip, Is.SameAs(events[3].GetClip(0)));
                manager.Tick(realtime + 0.065d, 0.015f);
                Assert.That(fixture.Sources[3].clip, Is.SameAs(events[12].GetClip(0)));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PlayAt_DirectStealAndFadeLifecycleAllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateSpatialPlaybackEvent(
                    120,
                    polyphonyCap: 12,
                    maximumDistance: 100f));
                var manager = fixture.CreateReadyManager(out _);
                var key = new OrpheusAudioKey(120);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);

                manager.PlayAt(key, Vector3.zero);
                manager.Tick(realtime + 2d, 2f);
                var beforeDirect = GC.GetAllocatedBytesForCurrentThread();
                manager.PlayAt(key, Vector3.zero);
                var directBytes = GC.GetAllocatedBytesForCurrentThread() - beforeDirect;
                manager.Tick(realtime + 4d, 2f);

                FillSpatialBank(manager, key);
                manager.Tick(realtime + 4.05d, 0.05f);
                manager.PlayAt(key, Vector3.zero);
                manager.Tick(realtime + 4.0575d, 0.0075f);
                manager.Tick(realtime + 4.065d, 0.0075f);
                manager.Tick(realtime + 6.065d, 2f);

                FillSpatialBank(manager, key);
                manager.Tick(realtime + 6.115d, 0.05f);
                var beforeSteal = GC.GetAllocatedBytesForCurrentThread();
                manager.PlayAt(key, Vector3.zero);
                var stealBytes = GC.GetAllocatedBytesForCurrentThread() - beforeSteal;
                var beforePartialFade = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 6.1225d, 0.0075f);
                var partialFadeBytes = GC.GetAllocatedBytesForCurrentThread() - beforePartialFade;
                var beforePendingStart = GC.GetAllocatedBytesForCurrentThread();
                manager.Tick(realtime + 6.13d, 0.0075f);
                var pendingStartBytes = GC.GetAllocatedBytesForCurrentThread() - beforePendingStart;

                Assert.That(directBytes, Is.Zero);
                Assert.That(stealBytes, Is.Zero);
                Assert.That(partialFadeBytes, Is.Zero);
                Assert.That(pendingStartBytes, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PlayAt_OrdinaryDistanceStealAndActiveLateTickAllocateZeroBytes()
        {
            using (var fixture = new SessionFixture())
            {
                var events = new OrpheusAudioEvent[13];
                for (var index = 0; index < 12; index++)
                {
                    events[index] = fixture.CreateSpatialPlaybackEvent(
                        (ushort)(200 + index),
                        priority: 200,
                        maximumDistance: 100f);
                }
                events[12] = fixture.CreateSpatialPlaybackEvent(
                    212,
                    priority: 100,
                    maximumDistance: 100f);
                fixture.SetEvents(events);
                var manager = fixture.CreateReadyManager(out var listener);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);

                FillSpatialBank(manager, 200);
                manager.Tick(realtime + 0.05d, 0.05f);
                manager.PlayAt(new OrpheusAudioKey(212), Vector3.zero);
                manager.Tick(realtime + 0.0575d, 0.0075f);
                manager.Tick(realtime + 0.065d, 0.0075f);
                manager.Tick(realtime + 2.065d, 2f);

                FillSpatialBank(manager, 200);
                manager.Tick(realtime + 2.115d, 0.05f);
                listener.transform.position = Vector3.one;
                manager.LateTick();
                listener.transform.position = new Vector3(2f, 0f, 0f);
                var beforeLateTick = GC.GetAllocatedBytesForCurrentThread();
                manager.LateTick();
                var lateTickBytes = GC.GetAllocatedBytesForCurrentThread() - beforeLateTick;
                var beforeSteal = GC.GetAllocatedBytesForCurrentThread();
                manager.PlayAt(new OrpheusAudioKey(212), new Vector3(2f, 0f, 0f));
                var stealBytes = GC.GetAllocatedBytesForCurrentThread() - beforeSteal;

                Assert.That(lateTickBytes, Is.Zero);
                Assert.That(stealBytes, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void PlayAt_NormalizesDirtySourceBeforeStartAndAfterNaturalCompletion()
        {
            using (var fixture = new SessionFixture())
            {
                var audioEvent = fixture.CreateSpatialPlaybackEvent(
                    121,
                    minimumDistance: 3f,
                    maximumDistance: 30f,
                    rolloffMode: OrpheusRolloffMode.Linear);
                fixture.SetEvents(audioEvent);
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                fixture.DirtyAllSources(audioEvent.GetClip(0));

                manager.PlayAt(new OrpheusAudioKey(121), new Vector3(5f, 0f, 0f));

                var source = fixture.Sources[0];
                Assert.That(source.loop, Is.False);
                Assert.That(source.mute, Is.False);
                Assert.That(source.panStereo, Is.Zero);
                Assert.That(source.dopplerLevel, Is.Zero);
                Assert.That(source.spatialBlend, Is.EqualTo(1f));
                Assert.That(source.minDistance, Is.EqualTo(3f));
                Assert.That(source.maxDistance, Is.EqualTo(30f));
                Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Linear));

                manager.Tick(realtime + 1d, 1f);

                AssertNormalized(source);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Transient3DActiveCount, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Tick_Invalid3DPendingCancelsWithoutOrdinaryLoadRejection()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateSpatialPlaybackEvent(
                    122,
                    polyphonyCap: 1));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                manager.PlayAt(new OrpheusAudioKey(122), Vector3.zero);
                manager.Tick(realtime + 0.05d, 0.05f);
                manager.PlayAt(new OrpheusAudioKey(122), Vector3.one);
                fixture.Readiness.State = OrpheusClipLoadState.Failed;

                manager.Tick(realtime + 0.065d, 0.015f);

                AssertNormalized(fixture.Sources[0]);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Transient3DActiveCount, Is.Zero);
                Assert.That(diagnostics.FadingCount, Is.Zero);
                Assert.That(diagnostics.PendingCount, Is.Zero);
                Assert.That(diagnostics.Counters.LoadFailed, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Tick_Destroyed3DSourceFailClosesAndClearsBothBankCounters()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreatePlaybackEvent(180, polyphonyCap: 1),
                    fixture.CreateSpatialPlaybackEvent(181, polyphonyCap: 1));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                manager.Play(new OrpheusAudioKey(180));
                manager.PlayAt(new OrpheusAudioKey(181), Vector3.zero);
                manager.Tick(realtime + 0.05d, 0.05f);
                manager.Play(new OrpheusAudioKey(180));
                manager.PlayAt(new OrpheusAudioKey(181), Vector3.one);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                Assert.That(before.FadingCount, Is.EqualTo(2));
                Assert.That(before.PendingCount, Is.EqualTo(2));
                UnityEngine.Object.DestroyImmediate(fixture.Sources[0].gameObject);

                Assert.DoesNotThrow(() => manager.Tick(realtime + 0.0575d, 0.0075f));

                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                Assert.That(after.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(after.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.OwnedSourceDestroyed));
                Assert.That(after.Transient3DActiveCount, Is.Zero);
                Assert.That(after.Transient2DActiveCount, Is.Zero);
                Assert.That(after.FadingCount, Is.Zero);
                Assert.That(after.PendingCount, Is.Zero);
                AssertNormalized(fixture.Sources[12]);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Tick_Invalid3DMixerRouteDuringPendingCompletionFailCloses()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreateSpatialPlaybackEvent(182, polyphonyCap: 1));
                var manager = fixture.CreateReadyManager(out _);
                var realtime = Time.realtimeSinceStartupAsDouble;
                manager.Tick(realtime, 0f);
                manager.PlayAt(new OrpheusAudioKey(182), Vector3.zero);
                manager.Tick(realtime + 0.05d, 0.05f);
                manager.PlayAt(new OrpheusAudioKey(182), Vector3.one);
                SetField(manager, "_categoryRoutes", default(OrpheusAudioCategoryRoutes));

                manager.Tick(realtime + 0.065d, 0.015f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.MixerGroupReferenceInvalid));
                Assert.That(diagnostics.Transient3DActiveCount, Is.Zero);
                Assert.That(diagnostics.FadingCount, Is.Zero);
                Assert.That(diagnostics.PendingCount, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        private static void FillSpatialBank(
            OrpheusAudioManager manager,
            OrpheusAudioKey key)
        {
            for (var index = 0; index < 12; index++)
            {
                manager.PlayAt(key, new Vector3(index, 0f, 0f));
            }
        }

        private static void FillSpatialBank(
            OrpheusAudioManager manager,
            ushort firstKey)
        {
            for (var index = 0; index < 12; index++)
            {
                manager.PlayAt(
                    new OrpheusAudioKey((ushort)(firstKey + index)),
                    new Vector3(index, 0f, 0f));
            }
        }

        private sealed partial class SessionFixture
        {
            internal OrpheusAudioEvent CreateSpatialPlaybackEvent(
                ushort key,
                byte priority = 128,
                OrpheusCategory category = OrpheusCategory.SfxWorld,
                byte polyphonyCap = 1,
                float cooldownSeconds = 0f,
                float minimumDistance = 1f,
                float maximumDistance = 100f,
                OrpheusRolloffMode rolloffMode = OrpheusRolloffMode.Logarithmic,
                int clipCount = 1)
            {
                return CreatePlaybackEvent(
                    key,
                    OrpheusPlaybackKind.OneShot3D,
                    priority: priority,
                    category: category,
                    polyphonyCap: polyphonyCap,
                    cooldownSeconds: cooldownSeconds,
                    minimumDistance: minimumDistance,
                    maximumDistance: maximumDistance,
                    rolloffMode: rolloffMode,
                    clipCount: clipCount);
            }
        }
    }
}
