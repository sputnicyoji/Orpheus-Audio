using System;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Tests
{
    public sealed partial class OrpheusAudioSessionTests
    {
        private const int Ticket20FrameCount = 600;
        private const float Ticket20Delta = 1f / 60f;

        private static readonly int[] Ticket20CriticalFrames =
        {
            4,
            64,
            124,
            184,
            244,
            304,
            364,
            424
        };

        [Test]
        public void Ticket20_ProductionWorkloadRunsSixHundredFixedFramesAtZeroManagedBytes()
        {
            RunWithCleanRuntimeStatics(RunTicket20ProductionWorkload);
        }

        private static void RunTicket20ProductionWorkload()
        {
            using (var fixture = new SessionFixture())
            {
                var transient2D = fixture.CreateTicket20Event(
                    800,
                    OrpheusPlaybackKind.OneShot2D,
                    OrpheusLoadPolicy.BootstrapTransient,
                    OrpheusCategory.SfxWorld,
                    priority: 160,
                    polyphonyCap: 4);
                var transient3D = fixture.CreateTicket20Event(
                    801,
                    OrpheusPlaybackKind.OneShot3D,
                    OrpheusLoadPolicy.BootstrapTransient,
                    OrpheusCategory.SfxWorld,
                    priority: 160,
                    polyphonyCap: 12);
                var bgmA = fixture.CreateTicket20Event(
                    802,
                    OrpheusPlaybackKind.Bgm,
                    OrpheusLoadPolicy.PersistentStream,
                    OrpheusCategory.Music);
                var bgmB = fixture.CreateTicket20Event(
                    803,
                    OrpheusPlaybackKind.Bgm,
                    OrpheusLoadPolicy.PersistentStream,
                    OrpheusCategory.Music);
                var ambienceA = fixture.CreateTicket20Event(
                    804,
                    OrpheusPlaybackKind.ProfileAmbience,
                    OrpheusLoadPolicy.PersistentStream,
                    OrpheusCategory.Ambience);
                var ambienceB = fixture.CreateTicket20Event(
                    805,
                    OrpheusPlaybackKind.ProfileAmbience,
                    OrpheusLoadPolicy.PersistentStream,
                    OrpheusCategory.Ambience);
                var loopA = fixture.CreateTicket20Event(
                    806,
                    OrpheusPlaybackKind.GlobalLoop2D,
                    OrpheusLoadPolicy.PersistentStream,
                    OrpheusCategory.SfxWorld);
                var loopB = fixture.CreateTicket20Event(
                    807,
                    OrpheusPlaybackKind.GlobalLoop2D,
                    OrpheusLoadPolicy.PersistentStream,
                    OrpheusCategory.Ambience);
                var replacement = fixture.CreateTicket20Event(
                    808,
                    OrpheusPlaybackKind.OneShot2D,
                    OrpheusLoadPolicy.BootstrapTransient,
                    OrpheusCategory.SfxCombat,
                    priority: 0,
                    polyphonyCap: 4);
                fixture.SetEvents(
                    transient2D,
                    transient3D,
                    bgmA,
                    bgmB,
                    ambienceA,
                    ambienceB,
                    loopA,
                    loopB,
                    replacement);
                var manager = fixture.CreateReadyManager(out _);
                try
                {
                    fixture.Mixer.SuppressOperationOrder = true;
                    var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                    manager.Tick(realtime, 0f);
                    manager.ApplyProfile(PersistentIntent(1, OrpheusBaseState.Peace, 802, 804));
                    manager.PlayLoop(new OrpheusAudioKey(806));
                    manager.PlayLoop(new OrpheusAudioKey(807));
                    manager.Tick(realtime + 0.4d, 0.4f);

                    // Warm every measured path before the allocation interval.
                    manager.Play(new OrpheusAudioKey(800));
                    manager.PlayAt(new OrpheusAudioKey(801), new Vector3(1f, 0f, 0f));
                    for (var warmFrame = 1; warmFrame <= 60; warmFrame++)
                    {
                        manager.Tick(realtime + 0.4d + warmFrame * Ticket20Delta, Ticket20Delta);
                        manager.LateTick();
                    }
                    fixture.Host.HandleApplicationFocusChanged(true);

                    for (var fillIndex = 1;
                         fillIndex < OrpheusAudioSourceBank.Transient3DCount;
                         fillIndex++)
                    {
                        manager.PlayAt(
                            new OrpheusAudioKey(801),
                            new Vector3(fillIndex, 0f, 0f));
                    }
                    var fullBankRealtime = realtime + 1.4d;
                    for (var graceFrame = 1; graceFrame <= 4; graceFrame++)
                    {
                        manager.Tick(
                            fullBankRealtime + graceFrame * Ticket20Delta,
                            Ticket20Delta);
                    }

                    // Warm the exact eligible steal, pending fade and activation path.
                    manager.PlayAt(new OrpheusAudioKey(801), Vector3.zero);
                    var stealStart = fullBankRealtime + 5 * Ticket20Delta;
                    manager.Tick(stealStart, Ticket20Delta);
                    Assert.That(manager.TryGetDiagnostics(out var beforeMeasuredSteals), Is.True);
                    Assert.That(beforeMeasuredSteals.Transient3DActiveCount, Is.EqualTo(12));
                    Assert.That(beforeMeasuredSteals.PendingCount, Is.Zero);

                    var beforeStealBytes = GC.GetAllocatedBytesForCurrentThread();
                    for (var stealIndex = 1; stealIndex <= 8; stealIndex++)
                    {
                        manager.PlayAt(new OrpheusAudioKey(801), Vector3.zero);
                        manager.Tick(
                            stealStart + stealIndex * Ticket20Delta,
                            Ticket20Delta);
                        manager.LateTick();
                    }
                    var measuredStealBytes =
                        GC.GetAllocatedBytesForCurrentThread() - beforeStealBytes;
                    Assert.That(manager.TryGetDiagnostics(out var afterMeasuredSteals), Is.True);
                    Assert.That(measuredStealBytes, Is.Zero);
                    Assert.That(
                        afterMeasuredSteals.Counters.Stolen,
                        Is.EqualTo(beforeMeasuredSteals.Counters.Stolen + 8));
                    Assert.That(afterMeasuredSteals.Transient3DActiveCount, Is.EqualTo(12));
                    Assert.That(afterMeasuredSteals.PendingCount, Is.Zero);
                    Assert.That(afterMeasuredSteals.FadingCount, Is.Zero);

                    var workloadStart = stealStart + 8 * Ticket20Delta;
                    var maximumPhysicalCount = 0;
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    for (var frame = 1; frame <= Ticket20FrameCount; frame++)
                    {
                        if (frame == 24)
                        {
                            manager.ApplyProfile(PersistentIntent(
                                2,
                                OrpheusBaseState.Combat,
                                803,
                                805));
                        }

                        if (frame == 100)
                        {
                            fixture.Host.HandleApplicationFocusChanged(true);
                        }

                        if (frame == 300)
                        {
                            fixture.Host.RecordAudioConfigurationChanged(true);
                            fixture.Host.ConsumePendingAudioConfigurationChanges();
                        }

                        manager.Tick(workloadStart + frame * Ticket20Delta, Ticket20Delta);
                        if (frame % 120 == 0)
                        {
                            manager.Play(new OrpheusAudioKey(800));
                            manager.PlayAt(
                                new OrpheusAudioKey(801),
                                new Vector3(frame * 0.001f, 0f, 0f));
                        }
                        manager.LateTick();
                        manager.TryGetDiagnostics(out var frameDiagnostics);
                        var physicalCount = CountPhysicalSources(fixture, frameDiagnostics);
                        if (physicalCount > maximumPhysicalCount)
                        {
                            maximumPhysicalCount = physicalCount;
                        }
                    }
                    var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

                    Assert.That(allocatedBytes, Is.Zero);
                    Assert.That(maximumPhysicalCount, Is.LessThanOrEqualTo(24));
                    Assert.That(manager.TryGetDiagnostics(out var completed), Is.True);
                    Assert.That(completed.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
                    Assert.That(completed.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                    Assert.That(completed.GlobalLoopActiveCount, Is.EqualTo(2));

                    for (var fillAttempt = 0;
                         fillAttempt < 2 && completed.Transient2DActiveCount < 2;
                         fillAttempt++)
                    {
                        var activeBeforeFill = completed.Transient2DActiveCount;
                        manager.Play(new OrpheusAudioKey(800));
                        Assert.That(manager.TryGetDiagnostics(out completed), Is.True);
                        Assert.That(
                            completed.Transient2DActiveCount,
                            Is.EqualTo(activeBeforeFill + 1));
                    }
                    Assert.That(completed.Transient2DActiveCount, Is.EqualTo(2));
                    manager.Tick(workloadStart + 10.06d, 0.06f);
                    manager.Play(new OrpheusAudioKey(808));
                    Assert.That(manager.TryGetDiagnostics(out var pending), Is.True);
                    Assert.That(pending.PendingCount, Is.EqualTo(1));

                    fixture.Host.HandleApplicationFocusChanged(false);
                    Assert.That(manager.TryGetDiagnostics(out var suspended), Is.True);
                    Assert.That(suspended.Transient2DActiveCount, Is.Zero);
                    Assert.That(suspended.Transient3DActiveCount, Is.Zero);
                    Assert.That(suspended.PendingCount, Is.Zero);
                    Assert.That(suspended.FadingCount, Is.Zero);

                    fixture.Host.HandleApplicationFocusChanged(true);
                    manager.Tick(workloadStart + 10.16d, 0.1f);
                    Assert.That(manager.TryGetDiagnostics(out var recovered), Is.True);
                    Assert.That(recovered.TransportState, Is.EqualTo(OrpheusTransportState.Active));
                    Assert.That(recovered.Transient2DActiveCount, Is.Zero);
                    Assert.That(recovered.Transient3DActiveCount, Is.Zero);
                    Assert.That(recovered.PendingCount, Is.Zero);
                    Assert.That(recovered.FadingCount, Is.Zero);
                    for (var sourceIndex = 0;
                         sourceIndex < OrpheusAudioSourceBank.BgmOffset;
                         sourceIndex++)
                    {
                        AssertNormalized(fixture.Sources[sourceIndex]);
                    }

                }
                finally
                {
                    ReleaseTicket20Manager(fixture, manager);
                }
            }
        }

        [Test]
        public void Ticket20_CapacityRejectedProducerDoesNotCommitCooldown()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    const ushort firstFillKey = 1100;
                    const ushort probeKey = 1112;
                    var events = new OrpheusAudioEvent[13];
                    for (var index = 0; index < 12; index++)
                    {
                        events[index] = fixture.CreateTicket20Event(
                            (ushort)(firstFillKey + index),
                            OrpheusPlaybackKind.OneShot3D,
                            OrpheusLoadPolicy.BootstrapTransient,
                            OrpheusCategory.SfxWorld,
                            priority: 200,
                            maximumDistance: 100f);
                    }
                    events[12] = fixture.CreateTicket20Event(
                        probeKey,
                        OrpheusPlaybackKind.OneShot3D,
                        OrpheusLoadPolicy.BootstrapTransient,
                        OrpheusCategory.SfxWorld,
                        priority: 255,
                        maximumDistance: 100f);
                    fixture.SetTicket20Cooldown(events[12], 0.4f);
                    fixture.SetEvents(events);
                    var manager = fixture.CreateReadyManager(out _);
                    try
                    {
                        var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                        manager.Tick(realtime, 0f);
                        for (var index = 0; index < 12; index++)
                        {
                            manager.PlayAt(
                                new OrpheusAudioKey((ushort)(firstFillKey + index)),
                                new Vector3(index, 0f, 0f));
                        }

                        manager.PlayAt(new OrpheusAudioKey(probeKey), Vector3.zero);
                        Assert.That(manager.TryGetDiagnostics(out var firstRejected), Is.True);
                        AssertOnlyPoolCapacityRejected(firstRejected, 1);

                        manager.PlayAt(new OrpheusAudioKey(probeKey), Vector3.zero);
                        Assert.That(manager.TryGetDiagnostics(out var sameFrameRetry), Is.True);
                        AssertOnlyPoolCapacityRejected(sameFrameRetry, 2);

                        manager.Tick(realtime + 0.4d, 0.4f);
                        manager.PlayAt(new OrpheusAudioKey(probeKey), Vector3.zero);
                        Assert.That(manager.TryGetDiagnostics(out var exactBoundaryRetry), Is.True);
                        AssertOnlyPoolCapacityRejected(exactBoundaryRetry, 3);
                        Assert.That(exactBoundaryRetry.Transient3DActiveCount, Is.EqualTo(12));
                        Assert.That(exactBoundaryRetry.PendingCount, Is.Zero);
                        Assert.That(exactBoundaryRetry.FadingCount, Is.Zero);
                        for (var index = 0; index < 12; index++)
                        {
                            Assert.That(
                                fixture.Sources[index].clip,
                                Is.SameAs(events[index].GetClip(0)));
                        }
                    }
                    finally
                    {
                        ReleaseTicket20Manager(fixture, manager);
                    }
                }
            });
        }

        [Test]
        public void Ticket20_ExactTwentyFourSourceSaturationIsDeterministicForSixHundredFrames()
        {
            var withRejectedProducers = RunTicket20SaturationWithCleanStatics(true);
            var withoutRejectedProducers = RunTicket20SaturationWithCleanStatics(false);

            Assert.That(withRejectedProducers.PoolCapacityRejected, Is.EqualTo(3));
            Assert.That(withoutRejectedProducers.PoolCapacityRejected, Is.Zero);
            Assert.That(withRejectedProducers.Stolen, Is.EqualTo(8));
            Assert.That(withoutRejectedProducers.Stolen, Is.EqualTo(8));
            Assert.That(
                withRejectedProducers.CriticalClipIndices,
                Is.EqualTo(withoutRejectedProducers.CriticalClipIndices),
                "Rejected Worldmap work advanced the manager-local RNG.");
        }

        private static Ticket20SaturationResult RunTicket20SaturationWithCleanStatics(
            bool submitAllFifteen)
        {
            Ticket20SaturationResult result = default;
            RunWithCleanRuntimeStatics(() =>
            {
                result = RunTicket20Saturation(submitAllFifteen);
            });
            return result;
        }

        private static Ticket20SaturationResult RunTicket20Saturation(bool submitAllFifteen)
        {
            using (var fixture = new SessionFixture())
            {
                const ushort firstProducerKey = 1000;
                const ushort criticalKey = 1015;
                var events = new OrpheusAudioEvent[28];
                for (var index = 0; index < 15; index++)
                {
                    events[index] = fixture.CreateTicket20Event(
                        (ushort)(firstProducerKey + index),
                        OrpheusPlaybackKind.OneShot3D,
                        OrpheusLoadPolicy.BootstrapTransient,
                        OrpheusCategory.SfxWorld,
                        priority: 200,
                        polyphonyCap: 1,
                        maximumDistance: 100f);
                }
                var critical = fixture.CreateTicket20Event(
                    criticalKey,
                    OrpheusPlaybackKind.OneShot3D,
                    OrpheusLoadPolicy.BootstrapTransient,
                    OrpheusCategory.SfxCombat,
                    priority: 0,
                    polyphonyCap: 12,
                    maximumDistance: 100f,
                    clipCount: 3);
                events[15] = critical;
                events[16] = fixture.CreateTicket20Event(
                    1016,
                    OrpheusPlaybackKind.OneShot2D,
                    OrpheusLoadPolicy.BootstrapTransient,
                    OrpheusCategory.SfxCombat);
                events[17] = fixture.CreateTicket20Event(
                    1017,
                    OrpheusPlaybackKind.OneShot2D,
                    OrpheusLoadPolicy.BootstrapTransient,
                    OrpheusCategory.SfxWorld);
                events[18] = fixture.CreateTicket20Event(
                    1018,
                    OrpheusPlaybackKind.OneShot2D,
                    OrpheusLoadPolicy.BootstrapTransient,
                    OrpheusCategory.SfxUi);
                events[19] = fixture.CreateTicket20Event(
                    1019,
                    OrpheusPlaybackKind.OneShot2D,
                    OrpheusLoadPolicy.BootstrapTransient,
                    OrpheusCategory.SfxUi);
                events[20] = fixture.CreateTicket20Event(
                    1020,
                    OrpheusPlaybackKind.Bgm,
                    OrpheusLoadPolicy.PersistentStream,
                    OrpheusCategory.Music);
                events[21] = fixture.CreateTicket20Event(
                    1021,
                    OrpheusPlaybackKind.Bgm,
                    OrpheusLoadPolicy.PersistentStream,
                    OrpheusCategory.Music);
                events[22] = fixture.CreateTicket20Event(
                    1022,
                    OrpheusPlaybackKind.ProfileAmbience,
                    OrpheusLoadPolicy.PersistentStream,
                    OrpheusCategory.Ambience);
                events[23] = fixture.CreateTicket20Event(
                    1023,
                    OrpheusPlaybackKind.ProfileAmbience,
                    OrpheusLoadPolicy.PersistentStream,
                    OrpheusCategory.Ambience);
                for (var index = 0; index < 4; index++)
                {
                    events[24 + index] = fixture.CreateTicket20Event(
                        (ushort)(1024 + index),
                        OrpheusPlaybackKind.GlobalLoop2D,
                        OrpheusLoadPolicy.PersistentStream,
                        index == 3 ? OrpheusCategory.Ambience : OrpheusCategory.SfxWorld);
                }
                fixture.SetEvents(events);
                var manager = fixture.CreateReadyManager(out _);
                try
                {
                    var realtime = Time.realtimeSinceStartupAsDouble + 0.1d;
                    manager.Tick(realtime, 0f);
                    manager.ApplyProfile(PersistentIntent(1, OrpheusBaseState.Peace, 1020, 1022));
                    manager.Tick(realtime + 0.4d, 0.4f);
                    var workloadStart = realtime + 0.4d;

                    for (var loopIndex = 0; loopIndex < 4; loopIndex++)
                    {
                        manager.PlayLoop(new OrpheusAudioKey((ushort)(1024 + loopIndex)));
                    }
                    manager.Play(new OrpheusAudioKey(1016));
                    manager.Play(new OrpheusAudioKey(1017));
                    manager.Play(new OrpheusAudioKey(1018));
                    manager.Play(new OrpheusAudioKey(1019));
                    var producerCount = submitAllFifteen ? 15 : 12;
                    for (var producerIndex = 0; producerIndex < producerCount; producerIndex++)
                    {
                        manager.PlayAt(
                            new OrpheusAudioKey((ushort)(firstProducerKey + producerIndex)),
                            new Vector3(producerIndex, 0f, 0f));
                    }
                    manager.Tick(workloadStart, 0f);

                    Assert.That(manager.TryGetDiagnostics(out var initial), Is.True);
                    Assert.That(initial.Transient3DActiveCount, Is.EqualTo(12));
                    Assert.That(initial.Transient2DActiveCount, Is.EqualTo(4));
                    Assert.That(initial.GlobalLoopActiveCount, Is.EqualTo(4));
                    Assert.That(initial.Counters.PoolCapacityRejected, Is.EqualTo(
                        submitAllFifteen ? 3ul : 0ul));
                    Assert.That(initial.Counters.CooldownRejected, Is.Zero);
                    Assert.That(fixture.Sources[14].clip, Is.SameAs(events[18].GetClip(0)));
                    Assert.That(fixture.Sources[15].clip, Is.SameAs(events[19].GetClip(0)));

                    var criticalClipIndices = new int[Ticket20CriticalFrames.Length];
                    var maximumPhysicalCount = CountPhysicalSources(fixture, initial);
                    var criticalRequestIndex = 0;
                    var pendingVictimIndex = -1;
                    for (var frame = 1; frame <= Ticket20FrameCount; frame++)
                    {
                        manager.Tick(workloadStart + frame * Ticket20Delta, Ticket20Delta);
                        manager.LateTick();
                        if (frame == 24)
                        {
                            manager.ApplyProfile(PersistentIntent(
                                2,
                                OrpheusBaseState.Combat,
                                1021,
                                1023));
                        }
                        if (pendingVictimIndex >= 0)
                        {
                            var startedClip = fixture.Sources[pendingVictimIndex].clip;
                            var clipIndex = FindClipIndex(critical, startedClip);
                            Assert.That(clipIndex, Is.GreaterThanOrEqualTo(0));
                            criticalClipIndices[criticalRequestIndex - 1] = clipIndex;
                            Assert.That(manager.TryGetDiagnostics(out var started), Is.True);
                            Assert.That(started.PendingCount, Is.Zero);
                            Assert.That(started.FadingCount, Is.Zero);
                            pendingVictimIndex = -1;
                        }

                        if (criticalRequestIndex < Ticket20CriticalFrames.Length &&
                            frame == Ticket20CriticalFrames[criticalRequestIndex])
                        {
                            var expectedVictimIndex = 11 - criticalRequestIndex;
                            Assert.That(
                                fixture.Sources[expectedVictimIndex].clip,
                                Is.SameAs(events[expectedVictimIndex].GetClip(0)));
                            Assert.That(manager.TryGetDiagnostics(out var beforeCritical), Is.True);
                            manager.PlayAt(new OrpheusAudioKey(criticalKey), Vector3.zero);
                            Assert.That(manager.TryGetDiagnostics(out var afterCritical), Is.True);
                            AssertCriticalAcceptedOnly(beforeCritical, afterCritical);
                            Assert.That(afterCritical.PendingCount, Is.EqualTo(1));
                            Assert.That(afterCritical.FadingCount, Is.EqualTo(1));
                            pendingVictimIndex = expectedVictimIndex;
                            criticalRequestIndex++;
                        }

                        Assert.That(manager.TryGetDiagnostics(out var frameDiagnostics), Is.True);
                        var physicalCount = CountPhysicalSources(fixture, frameDiagnostics);
                        if (physicalCount > maximumPhysicalCount)
                        {
                            maximumPhysicalCount = physicalCount;
                        }
                        if (frame == 25)
                        {
                            Assert.That(frameDiagnostics.BgmActiveCount, Is.EqualTo(2));
                            Assert.That(frameDiagnostics.ProfileAmbienceActiveCount, Is.EqualTo(2));
                            Assert.That(frameDiagnostics.GlobalLoopActiveCount, Is.EqualTo(4));
                            Assert.That(physicalCount, Is.EqualTo(24));
                        }
                    }

                    Assert.That(criticalRequestIndex, Is.EqualTo(8));
                    Assert.That(pendingVictimIndex, Is.EqualTo(-1));
                    Assert.That(maximumPhysicalCount, Is.EqualTo(24));
                    Assert.That(manager.TryGetDiagnostics(out var completed), Is.True);
                    Assert.That(completed.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
                    Assert.That(completed.Transient3DActiveCount, Is.EqualTo(12));
                    Assert.That(completed.Transient2DActiveCount, Is.EqualTo(4));
                    Assert.That(completed.BgmActiveCount, Is.EqualTo(1));
                    Assert.That(completed.ProfileAmbienceActiveCount, Is.EqualTo(1));
                    Assert.That(completed.GlobalLoopActiveCount, Is.EqualTo(4));
                    Assert.That(completed.PendingCount, Is.Zero);
                    Assert.That(completed.Counters.Stolen, Is.EqualTo(8));
                    Assert.That(completed.Counters.CooldownRejected, Is.Zero);
                    Assert.That(completed.Counters.PolyphonyRejected, Is.Zero);
                    Assert.That(completed.Counters.DistanceRejected, Is.Zero);
                    Assert.That(completed.Counters.LoadNotReadyRejected, Is.Zero);
                    Assert.That(completed.Counters.LoadFailed, Is.Zero);
                    Assert.That(
                        completed.Counters.PoolCapacityRejected,
                        Is.EqualTo(submitAllFifteen ? 3ul : 0ul));
                    for (var loopIndex = 0; loopIndex < 4; loopIndex++)
                    {
                        Assert.That(fixture.GlobalLoop(loopIndex).clip, Is.Not.Null);
                    }

                    var result = new Ticket20SaturationResult(
                        criticalClipIndices,
                        completed.Counters.PoolCapacityRejected,
                        completed.Counters.Stolen);
                    return result;
                }
                finally
                {
                    ReleaseTicket20Manager(fixture, manager);
                }
            }
        }

        private static void ReleaseTicket20Manager(
            SessionFixture fixture,
            OrpheusAudioManager manager)
        {
            if (manager == null)
            {
                return;
            }

            manager.Dispose();
            fixture.Host.Unbind(manager);
        }

        private static void AssertOnlyPoolCapacityRejected(
            OrpheusAudioDiagnostics diagnostics,
            ulong expectedPoolCapacityRejected)
        {
            Assert.That(
                diagnostics.Counters.PoolCapacityRejected,
                Is.EqualTo(expectedPoolCapacityRejected));
            Assert.That(diagnostics.Counters.Stolen, Is.Zero);
            Assert.That(diagnostics.Counters.CooldownRejected, Is.Zero);
            Assert.That(diagnostics.Counters.PolyphonyRejected, Is.Zero);
            Assert.That(diagnostics.Counters.DistanceRejected, Is.Zero);
            Assert.That(diagnostics.Counters.PreReadyRejected, Is.Zero);
            Assert.That(diagnostics.Counters.SuspendedRejected, Is.Zero);
            Assert.That(diagnostics.Counters.UnavailableRejected, Is.Zero);
            Assert.That(diagnostics.Counters.WrongThreadRejected, Is.Zero);
            Assert.That(diagnostics.Counters.LoadNotReadyRejected, Is.Zero);
            Assert.That(diagnostics.Counters.LoadFailed, Is.Zero);
            Assert.That(diagnostics.Counters.LoadStalled, Is.Zero);
            Assert.That(diagnostics.Counters.InvalidKeyRejected, Is.Zero);
            Assert.That(diagnostics.Counters.InvalidRawKeyRejected, Is.Zero);
            Assert.That(diagnostics.Counters.InvalidPositionRejected, Is.Zero);
            Assert.That(diagnostics.Counters.InvalidValueRejected, Is.Zero);
            Assert.That(diagnostics.Counters.PlaybackKindRejected, Is.Zero);
            Assert.That(diagnostics.Counters.LoopRegistryFull, Is.Zero);
            Assert.That(diagnostics.Counters.SetFloatFailed, Is.Zero);
            Assert.That(diagnostics.Counters.RecoveryFailed, Is.Zero);
            Assert.That(diagnostics.Counters.StaleBootstrapTokenRejected, Is.Zero);
            Assert.That(diagnostics.Counters.UnexpectedException, Is.Zero);
        }

        private static void AssertCriticalAcceptedOnly(
            OrpheusAudioDiagnostics before,
            OrpheusAudioDiagnostics after)
        {
            Assert.That(after.Counters.Stolen, Is.EqualTo(before.Counters.Stolen + 1));
            Assert.That(
                after.Counters.PoolCapacityRejected,
                Is.EqualTo(before.Counters.PoolCapacityRejected));
            Assert.That(after.Counters.CooldownRejected, Is.EqualTo(before.Counters.CooldownRejected));
            Assert.That(after.Counters.PolyphonyRejected, Is.EqualTo(before.Counters.PolyphonyRejected));
            Assert.That(after.Counters.DistanceRejected, Is.EqualTo(before.Counters.DistanceRejected));
            Assert.That(after.Counters.PreReadyRejected, Is.EqualTo(before.Counters.PreReadyRejected));
            Assert.That(after.Counters.SuspendedRejected, Is.EqualTo(before.Counters.SuspendedRejected));
            Assert.That(after.Counters.UnavailableRejected, Is.EqualTo(before.Counters.UnavailableRejected));
            Assert.That(after.Counters.WrongThreadRejected, Is.EqualTo(before.Counters.WrongThreadRejected));
            Assert.That(after.Counters.LoadNotReadyRejected, Is.EqualTo(before.Counters.LoadNotReadyRejected));
            Assert.That(after.Counters.LoadFailed, Is.EqualTo(before.Counters.LoadFailed));
            Assert.That(after.Counters.LoadStalled, Is.EqualTo(before.Counters.LoadStalled));
            Assert.That(after.Counters.InvalidKeyRejected, Is.EqualTo(before.Counters.InvalidKeyRejected));
            Assert.That(after.Counters.InvalidRawKeyRejected, Is.EqualTo(before.Counters.InvalidRawKeyRejected));
            Assert.That(after.Counters.InvalidPositionRejected, Is.EqualTo(before.Counters.InvalidPositionRejected));
            Assert.That(after.Counters.InvalidValueRejected, Is.EqualTo(before.Counters.InvalidValueRejected));
            Assert.That(after.Counters.PlaybackKindRejected, Is.EqualTo(before.Counters.PlaybackKindRejected));
            Assert.That(after.Counters.LoopRegistryFull, Is.EqualTo(before.Counters.LoopRegistryFull));
            Assert.That(after.Counters.SetFloatFailed, Is.EqualTo(before.Counters.SetFloatFailed));
            Assert.That(after.Counters.RecoveryFailed, Is.EqualTo(before.Counters.RecoveryFailed));
            Assert.That(
                after.Counters.StaleBootstrapTokenRejected,
                Is.EqualTo(before.Counters.StaleBootstrapTokenRejected));
            Assert.That(after.Counters.UnexpectedException, Is.EqualTo(before.Counters.UnexpectedException));
        }

        private static int CountPhysicalSources(
            SessionFixture fixture,
            OrpheusAudioDiagnostics diagnostics)
        {
            var transient3DCount = CountOccupiedSources(
                fixture.Sources,
                0,
                OrpheusAudioSourceBank.Transient3DCount);
            var transient2DCount = CountOccupiedSources(
                fixture.Sources,
                OrpheusAudioSourceBank.Transient2DOffset,
                OrpheusAudioSourceBank.Transient2DCount);
            var bgmCount = CountOccupiedSources(
                fixture.Sources,
                OrpheusAudioSourceBank.BgmOffset,
                OrpheusAudioSourceBank.BgmCount);
            var profileAmbienceCount = CountOccupiedSources(
                fixture.Sources,
                OrpheusAudioSourceBank.ProfileAmbienceOffset,
                OrpheusAudioSourceBank.ProfileAmbienceCount);
            var globalLoopCount = CountOccupiedSources(
                fixture.Sources,
                OrpheusAudioSourceBank.GlobalLoopOffset,
                OrpheusAudioSourceBank.GlobalLoopCount);

            AssertObservedBankCount(
                transient3DCount,
                diagnostics.Transient3DActiveCount,
                OrpheusAudioSourceBank.Transient3DCount,
                "Transient3D");
            AssertObservedBankCount(
                transient2DCount,
                diagnostics.Transient2DActiveCount,
                OrpheusAudioSourceBank.Transient2DCount,
                "Transient2D");
            AssertObservedBankCount(
                bgmCount,
                diagnostics.BgmActiveCount,
                OrpheusAudioSourceBank.BgmCount,
                "Bgm");
            AssertObservedBankCount(
                profileAmbienceCount,
                diagnostics.ProfileAmbienceActiveCount,
                OrpheusAudioSourceBank.ProfileAmbienceCount,
                "ProfileAmbience");
            AssertObservedBankCount(
                globalLoopCount,
                diagnostics.GlobalLoopActiveCount,
                OrpheusAudioSourceBank.GlobalLoopCount,
                "GlobalLoop");

            return transient3DCount +
                   transient2DCount +
                   bgmCount +
                   profileAmbienceCount +
                   globalLoopCount;
        }

        private static int CountOccupiedSources(
            AudioSource[] sources,
            int offset,
            int count)
        {
            var occupiedCount = 0;
            var end = offset + count;
            for (var sourceIndex = offset; sourceIndex < end; sourceIndex++)
            {
                if (sources[sourceIndex].clip != null)
                {
                    occupiedCount++;
                }
            }

            return occupiedCount;
        }

        private static void AssertObservedBankCount(
            int observedCount,
            int diagnosticsCount,
            int capacity,
            string bankName)
        {
            if (observedCount != diagnosticsCount)
            {
                Assert.Fail(
                    bankName + " observed occupied Source count did not match Diagnostics.");
            }

            if (observedCount > capacity)
            {
                Assert.Fail(bankName + " observed occupied Source count exceeded capacity.");
            }
        }

        private static int FindClipIndex(OrpheusAudioEvent audioEvent, AudioClip clip)
        {
            for (var index = 0; index < audioEvent.ClipCount; index++)
            {
                if (ReferenceEquals(audioEvent.GetClip(index), clip))
                {
                    return index;
                }
            }

            return -1;
        }

        private readonly struct Ticket20SaturationResult
        {
            internal Ticket20SaturationResult(
                int[] criticalClipIndices,
                ulong poolCapacityRejected,
                ulong stolen)
            {
                CriticalClipIndices = criticalClipIndices;
                PoolCapacityRejected = poolCapacityRejected;
                Stolen = stolen;
            }

            internal int[] CriticalClipIndices { get; }
            internal ulong PoolCapacityRejected { get; }
            internal ulong Stolen { get; }
        }

        private sealed partial class SessionFixture
        {
            internal OrpheusAudioEvent CreateTicket20Event(
                ushort key,
                OrpheusPlaybackKind playbackKind,
                OrpheusLoadPolicy loadPolicy,
                OrpheusCategory category,
                byte priority = 128,
                byte polyphonyCap = 1,
                float maximumDistance = 100f,
                int clipCount = 1)
            {
                const int sampleRate = 48000;
                const int samples = sampleRate * 12;
                var clips = new AudioClip[clipCount];
                for (var clipIndex = 0; clipIndex < clipCount; clipIndex++)
                {
                    clips[clipIndex] = AudioClip.Create(
                        "Ticket20Clip" + key + "_" + clipIndex,
                        samples,
                        1,
                        sampleRate,
                        false);
                    _playbackTestObjects.Add(clips[clipIndex]);
                }

                var audioEvent = ScriptableObject.CreateInstance<OrpheusAudioEvent>();
                SetField(audioEvent, "_key", key);
                SetField(audioEvent, "_playbackKind", playbackKind);
                SetField(audioEvent, "_category", category);
                SetField(audioEvent, "_loadPolicy", loadPolicy);
                SetField(audioEvent, "_clips", clips);
                SetField(audioEvent, "_volumeMin", 1f);
                SetField(audioEvent, "_volumeMax", 1f);
                SetField(audioEvent, "_pitchMin", 1f);
                SetField(audioEvent, "_pitchMax", 1f);
                SetField(audioEvent, "_priority", priority);
                SetField(audioEvent, "_polyphonyCap", polyphonyCap);
                SetField(audioEvent, "_cooldownSeconds", 0f);
                SetField(audioEvent, "_minimumDistance", 1f);
                SetField(audioEvent, "_maximumDistance", maximumDistance);
                SetField(audioEvent, "_rolloffMode", OrpheusRolloffMode.Logarithmic);
                _playbackTestObjects.Add(audioEvent);
                return audioEvent;
            }

            internal void SetTicket20Cooldown(OrpheusAudioEvent audioEvent, float seconds)
            {
                SetField(audioEvent, "_cooldownSeconds", seconds);
            }
        }
    }
}
