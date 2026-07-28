using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Orpheus.Audio.Tests
{
    public sealed partial class OrpheusAudioSessionTests
    {
        [Test]
        public void Bridges_HaveExactLowAuthorityPublicSurface()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                AssertExactBridgeSurface(typeof(OrpheusAudioBridge), typeof(OrpheusAudioKey));
                AssertExactBridgeSurface(typeof(OrpheusAudioRawBridge), typeof(int));
            });
        }

        [Test]
        public void RuntimeAssembly_HasNoXLuaDependency()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                var references = typeof(OrpheusAudioBridge).Assembly.GetReferencedAssemblies();
                for (var index = 0; index < references.Length; index++)
                {
                    Assert.That(
                        references[index].Name.IndexOf("xLua", StringComparison.OrdinalIgnoreCase),
                        Is.EqualTo(-1),
                        references[index].FullName);
                }
            });
        }

        [Test]
        public void Bridges_BindIndependentlyToSameManagerAndUnboundPlayIsNoOp()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    fixture.SetEvents(fixture.CreatePlaybackEvent(200, polyphonyCap: 2));
                    var manager = fixture.CreateReadyManager(out _);

                    Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);

                    Assert.That(OrpheusAudioBridge.Unbind(manager), Is.True);
                    OrpheusAudioBridge.Play(new OrpheusAudioKey(200));
                    AssertNormalized(fixture.Sources[12]);
                    OrpheusAudioRawBridge.Play(200);
                    Assert.That(fixture.Sources[12].clip, Is.Not.Null);

                    Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Unbind(manager), Is.True);
                    OrpheusAudioRawBridge.Play(200);
                    AssertNormalized(fixture.Sources[13]);
                    OrpheusAudioBridge.Play(new OrpheusAudioKey(200));
                    Assert.That(fixture.Sources[13].clip, Is.Not.Null);

                    Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                    Assert.That(diagnostics.Transient2DActiveCount, Is.EqualTo(2));
                    DisposeBound(fixture, manager);
                }
            });
        }

        [Test]
        public void Bridges_PlayAtRemainIndependentAndPreservePosition()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    fixture.SetEvents(fixture.CreateSpatialPlaybackEvent(200, polyphonyCap: 2));
                    var manager = fixture.CreateReadyManager(out _);
                    var rawPosition = new Vector3(1f, 2f, 3f);
                    var typedPosition = new Vector3(-4f, 5f, -6f);

                    Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);
                    Assert.That(OrpheusAudioBridge.Unbind(manager), Is.True);
                    OrpheusAudioBridge.PlayAt(new OrpheusAudioKey(200), Vector3.one);
                    AssertNormalized(fixture.Sources[0]);
                    OrpheusAudioRawBridge.PlayAt(200, rawPosition);
                    Assert.That(fixture.Sources[0].transform.position, Is.EqualTo(rawPosition));

                    Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Unbind(manager), Is.True);
                    OrpheusAudioRawBridge.PlayAt(200, Vector3.one);
                    AssertNormalized(fixture.Sources[1]);
                    OrpheusAudioBridge.PlayAt(new OrpheusAudioKey(200), typedPosition);
                    Assert.That(fixture.Sources[1].transform.position, Is.EqualTo(typedPosition));

                    DisposeBound(fixture, manager);
                }
            });
        }

        [Test]
        public void Bridges_BindIsIdempotentAndRejectsForeignOrStaleIdentity()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var first = new SessionFixture(new object()))
                using (var second = new SessionFixture(new object()))
                {
                    Assert.That(first.Create(out var firstManager).Success, Is.True);
                    Assert.That(second.Create(out var secondManager).Success, Is.True);

                    AssertIdentityRulesForTypedBridge(firstManager, secondManager);
                    AssertIdentityRulesForRawBridge(firstManager, secondManager);

                    firstManager.Dispose();
                    secondManager.Dispose();
                }
            });
        }

        [Test]
        public void Bridges_WrongThreadMatrixOnlyIncrementsWrongThreadAndDoesNotUnbind()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    fixture.SetEvents(
                        fixture.CreatePlaybackEvent(200),
                        fixture.CreateSpatialPlaybackEvent(201));
                    Assert.That(fixture.Create(out var manager).Success, Is.True);
                    Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);
                    Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                    var typedBind = true;
                    var typedUnbind = true;
                    var rawBind = true;
                    var rawUnbind = true;

                    var worker = new Thread(() =>
                    {
                        typedBind = OrpheusAudioBridge.Bind(manager);
                        typedUnbind = OrpheusAudioBridge.Unbind(manager);
                        OrpheusAudioBridge.Bind(null);
                        OrpheusAudioBridge.Unbind(null);
                        OrpheusAudioBridge.Play(new OrpheusAudioKey(200));
                        OrpheusAudioBridge.PlayAt(new OrpheusAudioKey(201), Vector3.one);
                        rawBind = OrpheusAudioRawBridge.Bind(manager);
                        rawUnbind = OrpheusAudioRawBridge.Unbind(manager);
                        OrpheusAudioRawBridge.Bind(null);
                        OrpheusAudioRawBridge.Unbind(null);
                        OrpheusAudioRawBridge.Play(200);
                        OrpheusAudioRawBridge.PlayAt(201, Vector3.one);
                    });
                    worker.Start();
                    worker.Join();

                    Assert.That(typedBind, Is.False);
                    Assert.That(typedUnbind, Is.False);
                    Assert.That(rawBind, Is.False);
                    Assert.That(rawUnbind, Is.False);
                    Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                    Assert.That(after.Counters.WrongThreadRejected,
                        Is.EqualTo(before.Counters.WrongThreadRejected + 12));
                    AssertDiagnosticsEqualExceptWrongThread(before, after);
                    AssertNormalized(fixture.Sources[0]);
                    AssertNormalized(fixture.Sources[12]);
                    Assert.That(OrpheusAudioBridge.Unbind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Unbind(manager), Is.True);
                    manager.Dispose();
                }
            });
        }

        [Test]
        public void Bridges_WrongThreadForeignIdentityTargetsExplicitManagerAndLeavesBinding()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var first = new SessionFixture(new object()))
                using (var second = new SessionFixture(new object()))
                {
                    Assert.That(first.Create(out var boundManager).Success, Is.True);
                    Assert.That(second.Create(out var foreignManager).Success, Is.True);
                    Assert.That(OrpheusAudioBridge.Bind(boundManager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(boundManager), Is.True);
                    Assert.That(boundManager.TryGetDiagnostics(out var boundBefore), Is.True);
                    Assert.That(foreignManager.TryGetDiagnostics(out var foreignBefore), Is.True);

                    var worker = new Thread(() =>
                    {
                        OrpheusAudioBridge.Bind(foreignManager);
                        OrpheusAudioBridge.Unbind(foreignManager);
                        OrpheusAudioRawBridge.Bind(foreignManager);
                        OrpheusAudioRawBridge.Unbind(foreignManager);
                    });
                    worker.Start();
                    worker.Join();

                    Assert.That(boundManager.TryGetDiagnostics(out var boundAfter), Is.True);
                    Assert.That(foreignManager.TryGetDiagnostics(out var foreignAfter), Is.True);
                    Assert.That(boundAfter.Counters.WrongThreadRejected,
                        Is.EqualTo(boundBefore.Counters.WrongThreadRejected));
                    AssertDiagnosticsEqualExceptWrongThread(boundBefore, boundAfter);
                    Assert.That(foreignAfter.Counters.WrongThreadRejected,
                        Is.EqualTo(foreignBefore.Counters.WrongThreadRejected + 4));
                    AssertDiagnosticsEqualExceptWrongThread(foreignBefore, foreignAfter);
                    Assert.That(OrpheusAudioBridge.Bind(foreignManager), Is.False);
                    Assert.That(OrpheusAudioRawBridge.Bind(foreignManager), Is.False);
                    Assert.That(OrpheusAudioBridge.Unbind(boundManager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Unbind(boundManager), Is.True);
                    boundManager.Dispose();
                    foreignManager.Dispose();
                }
            });
        }

        [UnityTest]
        public IEnumerator Bridges_WrongThreadCallsAreNeverDeferred()
        {
            OrpheusAudioRuntimeStatics.Reset();
            try
            {
                using (var fixture = new SessionFixture())
                using (var foreignFixture = new SessionFixture(new object()))
                {
                    fixture.SetEvents(
                        fixture.CreatePlaybackEvent(200),
                        fixture.CreateSpatialPlaybackEvent(201));
                    var manager = fixture.CreateReadyManager(out _);
                    Assert.That(foreignFixture.Create(out var foreignManager).Success, Is.True);
                    Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);

                    var worker = new Thread(() =>
                    {
                        OrpheusAudioBridge.Bind(foreignManager);
                        OrpheusAudioBridge.Unbind(manager);
                        OrpheusAudioBridge.Play(new OrpheusAudioKey(200));
                        OrpheusAudioBridge.PlayAt(new OrpheusAudioKey(201), Vector3.one);
                        OrpheusAudioRawBridge.Bind(foreignManager);
                        OrpheusAudioRawBridge.Unbind(manager);
                        OrpheusAudioRawBridge.Play(200);
                        OrpheusAudioRawBridge.PlayAt(201, Vector3.one);
                    });
                    worker.Start();
                    worker.Join();

                    Assert.That(manager.TryGetDiagnostics(out var immediate), Is.True);
                    AssertNormalized(fixture.Sources[0]);
                    AssertNormalized(fixture.Sources[12]);

                    yield return null;

                    Assert.That(manager.TryGetDiagnostics(out var afterFrame), Is.True);
                    Assert.That(afterFrame.Counters.WrongThreadRejected,
                        Is.EqualTo(immediate.Counters.WrongThreadRejected));
                    AssertDiagnosticsEqualExceptWrongThread(immediate, afterFrame);
                    AssertNormalized(fixture.Sources[0]);
                    AssertNormalized(fixture.Sources[12]);
                    Assert.That(OrpheusAudioBridge.Unbind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Unbind(manager), Is.True);
                    Assert.That(OrpheusAudioBridge.Bind(foreignManager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(foreignManager), Is.True);
                    DisposeBound(fixture, manager);
                    foreignManager.Dispose();
                }
            }
            finally
            {
                OrpheusAudioRuntimeStatics.Reset();
            }
        }

        [Test]
        public void RawBridge_PlayValidatesRangeBeforeLookupAndCannotAliasKey200()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    fixture.SetEvents(fixture.CreatePlaybackEvent(200));
                    var manager = fixture.CreateReadyManager(out _);
                    Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);

                    OrpheusAudioRawBridge.Play(-1);
                    OrpheusAudioRawBridge.Play(0);
                    OrpheusAudioRawBridge.Play(65536);
                    OrpheusAudioRawBridge.Play(65736);
                    OrpheusAudioRawBridge.Play(65535);
                    AssertNormalized(fixture.Sources[12]);
                    OrpheusAudioRawBridge.Play(200);

                    Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                    Assert.That(diagnostics.Counters.InvalidRawKeyRejected, Is.EqualTo(4));
                    Assert.That(diagnostics.Counters.InvalidKeyRejected, Is.EqualTo(1));
                    Assert.That(diagnostics.Transient2DActiveCount, Is.EqualTo(1));
                    Assert.That(fixture.Sources[12].clip, Is.Not.Null);
                    DisposeBound(fixture, manager);
                }
            });
        }

        [Test]
        public void RawBridge_PlayAtUsesRangeThenCatalogThenPositionValidation()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    fixture.SetEvents(fixture.CreateSpatialPlaybackEvent(200));
                    var manager = fixture.CreateReadyManager(out _);
                    Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);
                    var nonFinite = new Vector3(float.NaN, float.PositiveInfinity, 0f);

                    OrpheusAudioRawBridge.PlayAt(-1, nonFinite);
                    OrpheusAudioRawBridge.PlayAt(65535, nonFinite);
                    OrpheusAudioRawBridge.PlayAt(200, nonFinite);
                    AssertNormalized(fixture.Sources[0]);
                    OrpheusAudioRawBridge.PlayAt(200, new Vector3(7f, 8f, 9f));

                    Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                    Assert.That(diagnostics.Counters.InvalidRawKeyRejected, Is.EqualTo(1));
                    Assert.That(diagnostics.Counters.InvalidKeyRejected, Is.EqualTo(1));
                    Assert.That(diagnostics.Counters.InvalidPositionRejected, Is.EqualTo(1));
                    Assert.That(diagnostics.Transient3DActiveCount, Is.EqualTo(1));
                    DisposeBound(fixture, manager);
                }
            });
        }

        [Test]
        public void RuntimeStaticsReset_ClearsBothBridgesAndBothLeaseRegistries()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var first = new SessionFixture())
                using (var second = new SessionFixture(first.Mixer.LeaseIdentity))
                {
                    second.SetSourceBank(first.Bank);
                    Assert.That(first.Create(out var firstManager).Success, Is.True);
                    Assert.That(OrpheusAudioBridge.Bind(firstManager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(firstManager), Is.True);

                    OrpheusAudioRuntimeStatics.Reset();

                    Assert.That(second.Create(out var secondManager).Success, Is.True);
                    Assert.That(OrpheusAudioBridge.Bind(secondManager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(secondManager), Is.True);
                    Assert.That(OrpheusAudioBridge.Unbind(firstManager), Is.False);
                    Assert.That(OrpheusAudioRawBridge.Unbind(firstManager), Is.False);
                    secondManager.Dispose();
                    firstManager.Dispose();
                }
            });
        }

        [Test]
        public void FailClosedAndDispose_DefensivelyUnbindWithoutClearingNewIdentity()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var first = new SessionFixture(new object()))
                using (var second = new SessionFixture(new object()))
                using (var third = new SessionFixture(new object()))
                {
                    Assert.That(first.Create(out var firstManager).Success, Is.True);
                    Assert.That(second.Create(out var secondManager).Success, Is.True);
                    Assert.That(third.Create(out var thirdManager).Success, Is.True);
                    Assert.That(OrpheusAudioBridge.Bind(firstManager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(firstManager), Is.True);

                    firstManager.FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);
                    Assert.That(OrpheusAudioBridge.Bind(firstManager), Is.False);
                    Assert.That(OrpheusAudioRawBridge.Bind(firstManager), Is.False);
                    Assert.That(OrpheusAudioBridge.Bind(secondManager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(secondManager), Is.True);
                    firstManager.Dispose();
                    Assert.That(OrpheusAudioBridge.Bind(thirdManager), Is.False);
                    Assert.That(OrpheusAudioRawBridge.Bind(thirdManager), Is.False);

                    secondManager.Dispose();
                    Assert.That(OrpheusAudioBridge.Bind(secondManager), Is.False);
                    Assert.That(OrpheusAudioRawBridge.Bind(secondManager), Is.False);
                    Assert.That(OrpheusAudioBridge.Bind(thirdManager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(thirdManager), Is.True);
                    thirdManager.Dispose();
                }
            });
        }

        [Test]
        public void BridgePlayback_SteadyStateAllocatesZeroBytes()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    fixture.SetEvents(
                        fixture.CreatePlaybackEvent(200, polyphonyCap: 4),
                        fixture.CreateSpatialPlaybackEvent(201, polyphonyCap: 4));
                    var manager = fixture.CreateReadyManager(out _);
                    Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);

                    OrpheusAudioBridge.Play(new OrpheusAudioKey(200));
                    OrpheusAudioRawBridge.Play(200);
                    OrpheusAudioBridge.PlayAt(new OrpheusAudioKey(201), Vector3.zero);
                    OrpheusAudioRawBridge.PlayAt(201, Vector3.one);

                    var before = GC.GetAllocatedBytesForCurrentThread();
                    OrpheusAudioBridge.Play(new OrpheusAudioKey(200));
                    OrpheusAudioRawBridge.Play(200);
                    OrpheusAudioBridge.PlayAt(new OrpheusAudioKey(201), Vector3.right);
                    OrpheusAudioRawBridge.PlayAt(201, Vector3.up);
                    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                    Assert.That(allocated, Is.Zero);
                    DisposeBound(fixture, manager);
                }
            });
        }

        [Test]
        public void BridgeRejectedWrongThreadAndUnboundPaths_AllocateZeroBytes()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    fixture.SetEvents(
                        fixture.CreatePlaybackEvent(200),
                        fixture.CreateSpatialPlaybackEvent(201));
                    var manager = fixture.CreateReadyManager(out _);
                    Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);
                    var invalidPosition = new Vector3(float.NaN, 0f, 0f);

                    OrpheusAudioBridge.Play(OrpheusAudioKey.Invalid);
                    OrpheusAudioBridge.PlayAt(new OrpheusAudioKey(201), invalidPosition);
                    OrpheusAudioRawBridge.Play(-1);
                    OrpheusAudioRawBridge.Play(65535);
                    OrpheusAudioRawBridge.PlayAt(201, invalidPosition);

                    var rejectedBefore = GC.GetAllocatedBytesForCurrentThread();
                    OrpheusAudioBridge.Play(OrpheusAudioKey.Invalid);
                    OrpheusAudioBridge.PlayAt(new OrpheusAudioKey(201), invalidPosition);
                    OrpheusAudioRawBridge.Play(-1);
                    OrpheusAudioRawBridge.Play(65535);
                    OrpheusAudioRawBridge.PlayAt(201, invalidPosition);
                    var rejectedAllocated =
                        GC.GetAllocatedBytesForCurrentThread() - rejectedBefore;

                    long wrongThreadAllocated = -1;
                    var worker = new Thread(() =>
                    {
                        OrpheusAudioBridge.Play(new OrpheusAudioKey(200));
                        OrpheusAudioRawBridge.Play(200);
                        var workerBefore = GC.GetAllocatedBytesForCurrentThread();
                        OrpheusAudioBridge.Bind(null);
                        OrpheusAudioBridge.Play(new OrpheusAudioKey(200));
                        OrpheusAudioBridge.PlayAt(new OrpheusAudioKey(201), Vector3.one);
                        OrpheusAudioRawBridge.Bind(null);
                        OrpheusAudioRawBridge.Play(200);
                        OrpheusAudioRawBridge.PlayAt(201, Vector3.one);
                        wrongThreadAllocated =
                            GC.GetAllocatedBytesForCurrentThread() - workerBefore;
                    });
                    worker.Start();
                    worker.Join();

                    Assert.That(OrpheusAudioBridge.Unbind(manager), Is.True);
                    Assert.That(OrpheusAudioRawBridge.Unbind(manager), Is.True);
                    OrpheusAudioBridge.Play(new OrpheusAudioKey(200));
                    OrpheusAudioRawBridge.Play(200);
                    var unboundBefore = GC.GetAllocatedBytesForCurrentThread();
                    OrpheusAudioBridge.Play(new OrpheusAudioKey(200));
                    OrpheusAudioBridge.PlayAt(new OrpheusAudioKey(201), Vector3.one);
                    OrpheusAudioRawBridge.Play(200);
                    OrpheusAudioRawBridge.PlayAt(201, Vector3.one);
                    var unboundAllocated = GC.GetAllocatedBytesForCurrentThread() - unboundBefore;

                    Assert.That(rejectedAllocated, Is.Zero);
                    Assert.That(wrongThreadAllocated, Is.Zero);
                    Assert.That(unboundAllocated, Is.Zero);
                    manager.Dispose();
                }
            });
        }

        private static void AssertExactBridgeSurface(Type bridgeType, Type keyType)
        {
            Assert.That(bridgeType.IsAbstract && bridgeType.IsSealed, Is.True);
            Assert.That(bridgeType.GetFields(BindingFlags.Public | BindingFlags.Static), Is.Empty);
            Assert.That(bridgeType.GetProperties(BindingFlags.Public | BindingFlags.Static), Is.Empty);
            Assert.That(bridgeType.GetEvents(BindingFlags.Public | BindingFlags.Static), Is.Empty);
            Assert.That(
                bridgeType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly),
                Has.Length.EqualTo(4));
            AssertMethod(bridgeType, "Bind", typeof(bool), typeof(OrpheusAudioManager));
            AssertMethod(bridgeType, "Unbind", typeof(bool), typeof(OrpheusAudioManager));
            AssertMethod(bridgeType, "Play", typeof(void), keyType);
            AssertMethod(bridgeType, "PlayAt", typeof(void), keyType, typeof(Vector3));
        }

        private static void AssertMethod(Type type, string name, Type returnType, params Type[] parameters)
        {
            var method = type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            Assert.That(method, Is.Not.Null, type.Name + "." + name);
            Assert.That(method.ReturnType, Is.EqualTo(returnType), type.Name + "." + name);
        }

        private static void AssertIdentityRulesForTypedBridge(
            OrpheusAudioManager first,
            OrpheusAudioManager second)
        {
            Assert.That(OrpheusAudioBridge.Bind(first), Is.True);
            Assert.That(OrpheusAudioBridge.Bind(first), Is.True);
            Assert.That(OrpheusAudioBridge.Bind(second), Is.False);
            Assert.That(OrpheusAudioBridge.Unbind(second), Is.False);
            Assert.That(OrpheusAudioBridge.Unbind(first), Is.True);
            Assert.That(OrpheusAudioBridge.Bind(second), Is.True);
            Assert.That(OrpheusAudioBridge.Unbind(first), Is.False);
            Assert.That(OrpheusAudioBridge.Unbind(second), Is.True);
        }

        private static void AssertIdentityRulesForRawBridge(
            OrpheusAudioManager first,
            OrpheusAudioManager second)
        {
            Assert.That(OrpheusAudioRawBridge.Bind(first), Is.True);
            Assert.That(OrpheusAudioRawBridge.Bind(first), Is.True);
            Assert.That(OrpheusAudioRawBridge.Bind(second), Is.False);
            Assert.That(OrpheusAudioRawBridge.Unbind(second), Is.False);
            Assert.That(OrpheusAudioRawBridge.Unbind(first), Is.True);
            Assert.That(OrpheusAudioRawBridge.Bind(second), Is.True);
            Assert.That(OrpheusAudioRawBridge.Unbind(first), Is.False);
            Assert.That(OrpheusAudioRawBridge.Unbind(second), Is.True);
        }

        private static void RunWithCleanRuntimeStatics(Action test)
        {
            OrpheusAudioRuntimeStatics.Reset();
            try
            {
                test();
            }
            finally
            {
                OrpheusAudioRuntimeStatics.Reset();
            }
        }
    }
}
