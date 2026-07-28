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
    public sealed class OrpheusAudioLoadingTests
    {
        [Test]
        public void Create_PreloadsEveryNonLoadedBootstrapClipOnce()
        {
            using (var fixture = new LoadingFixture())
            {
                var bootstrapClips = fixture.CreateClips(
                    "BootstrapLoaded",
                    "BootstrapUnloaded",
                    "BootstrapLoading",
                    "BootstrapFailed");
                var explicitClip = fixture.CreateClip("ExplicitUnloaded");
                var persistentClip = fixture.CreateClip("PersistentUnloaded");
                fixture.SetEvents(
                    fixture.CreateTransientEvent(100, OrpheusLoadPolicy.BootstrapTransient, bootstrapClips),
                    fixture.CreateTransientEvent(200, OrpheusLoadPolicy.ExplicitTransient, explicitClip),
                    fixture.CreatePersistentEvent(300, persistentClip));
                fixture.Readiness.SetState(0, OrpheusClipLoadState.Loaded);
                fixture.Readiness.SetState(1, OrpheusClipLoadState.Unloaded);
                fixture.Readiness.SetState(2, OrpheusClipLoadState.Loading);
                fixture.Readiness.SetState(3, OrpheusClipLoadState.Failed);
                fixture.Readiness.SetState(4, OrpheusClipLoadState.Unloaded);
                fixture.Readiness.SetState(5, OrpheusClipLoadState.Unloaded);
                fixture.Readiness.SetRequestResult(3, false);

                var result = fixture.Create(out var manager);

                Assert.That(result.Success, Is.True);
                Assert.That(manager, Is.Not.Null);
                Assert.That(fixture.Readiness.RequestCalls, Is.EqualTo(new[] { 1, 2, 3 }));
                Assert.That(fixture.Readiness.GetRequestCount(1), Is.EqualTo(1));
                Assert.That(fixture.Readiness.GetRequestCount(2), Is.EqualTo(1));
                Assert.That(fixture.Readiness.GetRequestCount(3), Is.EqualTo(1));
                Assert.That(fixture.Readiness.GetRequestCount(4), Is.Zero);
                Assert.That(fixture.Readiness.GetRequestCount(5), Is.Zero);

                manager.Dispose();
            }
        }

        [Test]
        public void Prepare_ReturnsEveryFixedResultAndRequestsOnlyRequestableClips()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(
                    fixture.CreateTransientEvent(
                        100,
                        OrpheusLoadPolicy.BootstrapTransient,
                        fixture.CreateClip("BootstrapLoaded")),
                    fixture.CreateTransientEvent(
                        200,
                        OrpheusLoadPolicy.ExplicitTransient,
                        fixture.CreateClip("ExplicitLoaded")),
                    fixture.CreateTransientEvent(
                        300,
                        OrpheusLoadPolicy.ExplicitTransient,
                        fixture.CreateClip("ExplicitLoading")),
                    fixture.CreateTransientEvent(
                        400,
                        OrpheusLoadPolicy.ExplicitTransient,
                        fixture.CreateClips("ExplicitUnloaded", "ExplicitFailed", "ExplicitLoadedTail")),
                    fixture.CreateTransientEvent(
                        500,
                        OrpheusLoadPolicy.ExplicitTransient,
                        fixture.CreateClip("ExplicitRequestFailure")),
                    fixture.CreatePersistentEvent(600, fixture.CreateClip("PersistentUnloaded")));
                fixture.Readiness.SetState(0, OrpheusClipLoadState.Loaded);
                fixture.Readiness.SetState(1, OrpheusClipLoadState.Loaded);
                fixture.Readiness.SetState(2, OrpheusClipLoadState.Loading);
                fixture.Readiness.SetState(3, OrpheusClipLoadState.Unloaded);
                fixture.Readiness.SetState(4, OrpheusClipLoadState.Failed);
                fixture.Readiness.SetState(5, OrpheusClipLoadState.Loaded);
                fixture.Readiness.SetState(6, OrpheusClipLoadState.Unloaded);
                fixture.Readiness.SetState(7, OrpheusClipLoadState.Unloaded);
                fixture.Readiness.SetRequestResult(6, false);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                fixture.Readiness.ClearCalls();

                Assert.That(manager.Prepare(OrpheusAudioKey.Invalid),
                    Is.EqualTo(OrpheusAudioPrepareResult.RejectedInvalidKey));
                Assert.That(manager.Prepare(new OrpheusAudioKey(999)),
                    Is.EqualTo(OrpheusAudioPrepareResult.RejectedInvalidKey));
                Assert.That(manager.Prepare(new OrpheusAudioKey(100)),
                    Is.EqualTo(OrpheusAudioPrepareResult.RejectedPolicy));
                Assert.That(manager.Prepare(new OrpheusAudioKey(600)),
                    Is.EqualTo(OrpheusAudioPrepareResult.RejectedPolicy));
                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.AlreadyLoaded));
                Assert.That(manager.Prepare(new OrpheusAudioKey(300)),
                    Is.EqualTo(OrpheusAudioPrepareResult.AlreadyLoading));
                Assert.That(manager.Prepare(new OrpheusAudioKey(400)),
                    Is.EqualTo(OrpheusAudioPrepareResult.LoadRequested));
                Assert.That(manager.Prepare(new OrpheusAudioKey(500)),
                    Is.EqualTo(OrpheusAudioPrepareResult.FailedToRequest));
                Assert.That(fixture.Readiness.RequestCalls, Is.EqualTo(new[] { 3, 4, 6 }));

                manager.Dispose();
                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.Invalid));
            }
        }

        [Test]
        public void Prepare_RetriesFailedClipOnlyOnANewExplicitCall()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(fixture.CreateTransientEvent(
                    200,
                    OrpheusLoadPolicy.ExplicitTransient,
                    fixture.CreateClip("ExplicitFailed")));
                fixture.Readiness.SetState(0, OrpheusClipLoadState.Failed);
                fixture.Readiness.SetRequestResult(0, false);
                Assert.That(fixture.Create(out var manager).Success, Is.True);

                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.FailedToRequest));
                Assert.That(fixture.Readiness.GetRequestCount(0), Is.EqualTo(1));

                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.FailedToRequest));
                Assert.That(fixture.Readiness.GetRequestCount(0), Is.EqualTo(2));

                manager.Dispose();
            }
        }

        [Test]
        public void Prepare_MixedLoadingAndUnloaded_RequestsOnlyUnloadedClip()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(fixture.CreateTransientEvent(
                    200,
                    OrpheusLoadPolicy.ExplicitTransient,
                    fixture.CreateClips("MixedLoading", "MixedUnloaded")));
                fixture.Readiness.SetState(0, OrpheusClipLoadState.Loading);
                fixture.Readiness.SetState(1, OrpheusClipLoadState.Unloaded);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                fixture.Readiness.ClearCalls();

                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.LoadRequested));
                Assert.That(fixture.Readiness.GetCalls, Is.EqualTo(new[] { 0, 1 }));
                Assert.That(fixture.Readiness.RequestCalls, Is.EqualTo(new[] { 1 }));

                manager.Dispose();
            }
        }

        [Test]
        public void Prepare_FirstRequestFailure_DoesNotSkipLaterRequestableClips()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(fixture.CreateTransientEvent(
                    200,
                    OrpheusLoadPolicy.ExplicitTransient,
                    fixture.CreateClips("FirstRequestFails", "SecondRequestSucceeds")));
                fixture.Readiness.SetState(0, OrpheusClipLoadState.Unloaded);
                fixture.Readiness.SetState(1, OrpheusClipLoadState.Failed);
                fixture.Readiness.SetRequestResult(0, false);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                fixture.Readiness.ClearCalls();

                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.FailedToRequest));
                Assert.That(fixture.Readiness.RequestCalls, Is.EqualTo(new[] { 0, 1 }));
                Assert.That(fixture.Readiness.GetRequestCount(0), Is.EqualTo(1));
                Assert.That(fixture.Readiness.GetRequestCount(1), Is.EqualTo(1));

                manager.Dispose();
            }
        }

        [Test]
        public void GetLoadState_UsesFixedAggregatePrecedenceWithoutRequesting()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(fixture.CreateTransientEvent(
                    200,
                    OrpheusLoadPolicy.ExplicitTransient,
                    fixture.CreateClips("AggregateA", "AggregateB", "AggregateC")));
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                fixture.Readiness.ClearCalls();

                fixture.Readiness.SetStates(
                    OrpheusClipLoadState.Loading,
                    OrpheusClipLoadState.Loaded,
                    OrpheusClipLoadState.Failed);
                Assert.That(manager.GetLoadState(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusClipLoadState.Failed));

                fixture.Readiness.SetStates(
                    OrpheusClipLoadState.Loaded,
                    OrpheusClipLoadState.Loaded,
                    OrpheusClipLoadState.Loaded);
                Assert.That(manager.GetLoadState(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusClipLoadState.Loaded));

                fixture.Readiness.SetStates(
                    OrpheusClipLoadState.Loaded,
                    OrpheusClipLoadState.Loading,
                    OrpheusClipLoadState.Loaded);
                Assert.That(manager.GetLoadState(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusClipLoadState.Loading));

                fixture.Readiness.SetStates(
                    OrpheusClipLoadState.Loaded,
                    OrpheusClipLoadState.Unloaded,
                    OrpheusClipLoadState.Loaded);
                Assert.That(manager.GetLoadState(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusClipLoadState.Unloaded));
                Assert.That(manager.GetLoadState(new OrpheusAudioKey(999)),
                    Is.EqualTo(OrpheusClipLoadState.Invalid));
                Assert.That(fixture.Readiness.RequestCalls, Is.Empty);

                manager.Dispose();
                Assert.That(manager.GetLoadState(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusClipLoadState.Invalid));
            }
        }

        [Test]
        public void GetLoadState_PollsAllThreePoliciesWithoutRequesting()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(
                    fixture.CreateTransientEvent(
                        100,
                        OrpheusLoadPolicy.BootstrapTransient,
                        fixture.CreateClip("BootstrapPolicy")),
                    fixture.CreateTransientEvent(
                        200,
                        OrpheusLoadPolicy.ExplicitTransient,
                        fixture.CreateClip("ExplicitPolicy")),
                    fixture.CreatePersistentEvent(300, fixture.CreateClip("PersistentPolicy")));
                fixture.Readiness.SetStates(
                    OrpheusClipLoadState.Loaded,
                    OrpheusClipLoadState.Loaded,
                    OrpheusClipLoadState.Loaded);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                fixture.Readiness.ClearCalls();
                fixture.Readiness.SetStates(
                    OrpheusClipLoadState.Unloaded,
                    OrpheusClipLoadState.Loading,
                    OrpheusClipLoadState.Failed);

                Assert.That(manager.GetLoadState(new OrpheusAudioKey(100)),
                    Is.EqualTo(OrpheusClipLoadState.Unloaded));
                Assert.That(manager.GetLoadState(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusClipLoadState.Loading));
                Assert.That(manager.GetLoadState(new OrpheusAudioKey(300)),
                    Is.EqualTo(OrpheusClipLoadState.Failed));
                Assert.That(fixture.Readiness.GetCalls, Is.EqualTo(new[] { 0, 1, 2 }));
                Assert.That(fixture.Readiness.RequestCalls, Is.Empty);

                manager.Dispose();
            }
        }

        [Test]
        public void DisabledLoadingApis_ReturnInvalidWithoutTouchingReadiness()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(fixture.CreateTransientEvent(
                    200,
                    OrpheusLoadPolicy.ExplicitTransient,
                    fixture.CreateClip("DisabledExplicit")));
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                fixture.Readiness.ClearCalls();

                manager.FailClosed(OrpheusAudioDisableReason.UnexpectedRuntimeException);

                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.Invalid));
                Assert.That(manager.GetLoadState(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusClipLoadState.Invalid));
                Assert.That(fixture.Readiness.GetCalls, Is.Empty);
                Assert.That(fixture.Readiness.RequestCalls, Is.Empty);

                manager.Dispose();
            }
        }

        [Test]
        public void WrongThreadPrepareAndGetLoadState_RejectBeforeTouchingReadiness()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(fixture.CreateTransientEvent(
                    200,
                    OrpheusLoadPolicy.ExplicitTransient,
                    fixture.CreateClip("WrongThreadLoading")));
                fixture.Readiness.SetState(0, OrpheusClipLoadState.Loading);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                fixture.Readiness.ClearCalls();
                var prepareResult = OrpheusAudioPrepareResult.LoadRequested;
                var loadState = OrpheusClipLoadState.Loaded;
                Exception workerException = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        prepareResult = manager.Prepare(new OrpheusAudioKey(200));
                        loadState = manager.GetLoadState(new OrpheusAudioKey(200));
                    }
                    catch (Exception exception)
                    {
                        workerException = exception;
                    }
                });

                worker.Start();
                Assert.That(worker.Join(5000), Is.True);

                Assert.That(workerException, Is.Null);
                Assert.That(prepareResult, Is.EqualTo(OrpheusAudioPrepareResult.Invalid));
                Assert.That(loadState, Is.EqualTo(OrpheusClipLoadState.Invalid));
                Assert.That(fixture.Readiness.GetCalls, Is.Empty);
                Assert.That(fixture.Readiness.RequestCalls, Is.Empty);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                var baseline = Time.realtimeSinceStartupAsDouble;
                manager.Tick(baseline + 0.1d, 0.016f);
                manager.Tick(baseline + 3d, 0.016f);
                Assert.That(fixture.Readiness.GetCalls, Is.Empty);
                Assert.That(fixture.Readiness.RequestCalls, Is.Empty);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Counters.WrongThreadRejected, Is.EqualTo(2));
                Assert.That(diagnostics.Counters.LoadStalled, Is.Zero);
                Assert.That(diagnostics.Counters.UnexpectedException, Is.Zero);
                AssertOnlyCounterIs(diagnostics.Counters, "WrongThreadRejected", 2);

                manager.Dispose();
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void Tick_ReportsOneStallPerExplicitIntentGenerationWithoutChangingLoadState()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(fixture.CreateTransientEvent(
                    200,
                    OrpheusLoadPolicy.ExplicitTransient,
                    fixture.CreateClip("StalledExplicit")));
                fixture.Readiness.SetState(0, OrpheusClipLoadState.Unloaded);
                fixture.Readiness.AutoTransitionSuccessfulRequestsToLoading = true;
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                var baseline = Time.realtimeSinceStartupAsDouble;

                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.LoadRequested));
                manager.Tick(baseline + 0.1d, 0.016f);
                manager.Tick(baseline + 2.2d, 0.016f);
                Assert.That(manager.TryGetDiagnostics(out var first), Is.True);
                Assert.That(first.Counters.LoadStalled, Is.EqualTo(1));
                Assert.That(fixture.Readiness.GetState(0), Is.EqualTo(OrpheusClipLoadState.Loading));

                manager.Tick(baseline + 4.4d, 0.016f);
                Assert.That(manager.TryGetDiagnostics(out var repeated), Is.True);
                Assert.That(repeated.Counters.LoadStalled, Is.EqualTo(1));

                fixture.Readiness.SetState(0, OrpheusClipLoadState.Failed);
                manager.Tick(baseline + 4.5d, 0.016f);
                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.LoadRequested));
                manager.Tick(baseline + 4.6d, 0.016f);
                manager.Tick(baseline + 6.8d, 0.016f);
                Assert.That(manager.TryGetDiagnostics(out var retried), Is.True);
                Assert.That(retried.Counters.LoadStalled, Is.EqualTo(2));
                Assert.That(fixture.Readiness.GetState(0), Is.EqualTo(OrpheusClipLoadState.Loading));
                Assert.That(fixture.Readiness.GetRequestCount(0), Is.EqualTo(2));

                manager.Dispose();
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void GetLoadStateDoesNotCreateIntent_ButFirstAlreadyLoadingPrepareDoes()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(fixture.CreateTransientEvent(
                    200,
                    OrpheusLoadPolicy.ExplicitTransient,
                    fixture.CreateClip("ExternallyLoadingExplicit")));
                fixture.Readiness.SetState(0, OrpheusClipLoadState.Loading);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                var baseline = Time.realtimeSinceStartupAsDouble;

                Assert.That(manager.GetLoadState(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusClipLoadState.Loading));
                manager.Tick(baseline + 3d, 0.016f);
                Assert.That(manager.TryGetDiagnostics(out var pollingOnly), Is.True);
                Assert.That(pollingOnly.Counters.LoadStalled, Is.Zero);

                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.AlreadyLoading));
                Assert.That(fixture.Readiness.RequestCalls, Is.Empty);
                manager.Tick(baseline + 3.1d, 0.016f);
                manager.Tick(baseline + 5.2d, 0.016f);
                Assert.That(manager.TryGetDiagnostics(out var observed), Is.True);
                Assert.That(observed.Counters.LoadStalled, Is.EqualTo(1));

                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.AlreadyLoading));
                manager.Tick(baseline + 8d, 0.016f);
                Assert.That(manager.TryGetDiagnostics(out var repeated), Is.True);
                Assert.That(repeated.Counters.LoadStalled, Is.EqualTo(1));
                Assert.That(fixture.Readiness.RequestCalls, Is.Empty);

                manager.Dispose();
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void BootstrapLoadingAcrossMultipleKeys_ReportsEachKeyOnce()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(
                    fixture.CreateTransientEvent(
                        100,
                        OrpheusLoadPolicy.BootstrapTransient,
                        fixture.CreateClip("BootstrapLoadingA")),
                    fixture.CreateTransientEvent(
                        200,
                        OrpheusLoadPolicy.BootstrapTransient,
                        fixture.CreateClip("BootstrapLoadingB")));
                fixture.Readiness.SetStates(
                    OrpheusClipLoadState.Loading,
                    OrpheusClipLoadState.Loading);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Readiness.RequestCalls, Is.EqualTo(new[] { 0, 1 }));
                Assert.That(fixture.Host.Bind(manager), Is.True);
                var baseline = Time.realtimeSinceStartupAsDouble;

                manager.Tick(baseline + 0.1d, 0.016f);
                manager.Tick(baseline + 2.2d, 0.016f);
                Assert.That(manager.TryGetDiagnostics(out var stalled), Is.True);
                Assert.That(stalled.Counters.LoadStalled, Is.EqualTo(2));

                manager.Tick(baseline + 5d, 0.016f);
                Assert.That(manager.TryGetDiagnostics(out var repeated), Is.True);
                Assert.That(repeated.Counters.LoadStalled, Is.EqualTo(2));
                Assert.That(fixture.Readiness.RequestCalls, Is.EqualTo(new[] { 0, 1 }));

                manager.Dispose();
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void FirstTickAfterDelayedBind_UsesIntentRealtimeForBootstrapAndPrepare()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(
                    fixture.CreateTransientEvent(
                        100,
                        OrpheusLoadPolicy.BootstrapTransient,
                        fixture.CreateClip("DelayedBootstrap")),
                    fixture.CreateTransientEvent(
                        200,
                        OrpheusLoadPolicy.ExplicitTransient,
                        fixture.CreateClip("DelayedExplicit")));
                fixture.Readiness.SetStates(
                    OrpheusClipLoadState.Loading,
                    OrpheusClipLoadState.Loading);
                var baseline = Time.realtimeSinceStartupAsDouble;

                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.AlreadyLoading));
                Assert.That(fixture.Host.Bind(manager), Is.True);

                manager.Tick(baseline + 3d, 0.016f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Counters.LoadStalled, Is.EqualTo(2));
                Assert.That(fixture.Readiness.GetState(0), Is.EqualTo(OrpheusClipLoadState.Loading));
                Assert.That(fixture.Readiness.GetState(1), Is.EqualTo(OrpheusClipLoadState.Loading));

                manager.Dispose();
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void FailedRequestAttempts_DoNotLeavePermanentTickObservations()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(fixture.CreateTransientEvent(
                    200,
                    OrpheusLoadPolicy.ExplicitTransient,
                    fixture.CreateClips("RejectedRequestA", "RejectedRequestB")));
                fixture.Readiness.SetStates(
                    OrpheusClipLoadState.Unloaded,
                    OrpheusClipLoadState.Unloaded);
                fixture.Readiness.SetRequestResult(0, false);
                fixture.Readiness.SetRequestResult(1, false);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                fixture.Readiness.ClearCalls();

                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.FailedToRequest));
                Assert.That(fixture.Readiness.RequestCalls, Is.EqualTo(new[] { 0, 1 }));
                var getCallCountAfterPrepare = fixture.Readiness.GetCalls.Count;
                var baseline = Time.realtimeSinceStartupAsDouble;

                manager.Tick(baseline + 0.1d, 0.016f);
                manager.Tick(baseline + 3d, 0.016f);

                Assert.That(fixture.Readiness.GetCalls.Count, Is.EqualTo(getCallCountAfterPrepare));
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Counters.LoadStalled, Is.Zero);

                manager.Dispose();
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void LateCreateFailure_DoesNotPollOrPreloadBootstrapClips()
        {
            using (var fixture = new LoadingFixture())
            {
                fixture.SetEvents(fixture.CreateTransientEvent(
                    100,
                    OrpheusLoadPolicy.BootstrapTransient,
                    fixture.CreateClip("LateFailureBootstrap")));
                fixture.Readiness.SetState(0, OrpheusClipLoadState.Unloaded);
                fixture.AudioSystem.Configuration =
                    new OrpheusAudioSystemConfiguration(1024, 48000, 0, 64);

                var result = fixture.Create(out var manager);

                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorCode,
                    Is.EqualTo(OrpheusAudioInitErrorCode.InvalidAudioConfiguration));
                Assert.That(manager, Is.Null);
                Assert.That(fixture.Readiness.GetCalls, Is.Empty);
                Assert.That(fixture.Readiness.RequestCalls, Is.Empty);
            }
        }

        [Test]
        public void PublicLoadingApis_MapKeysToExactFlattenedClipOffsets()
        {
            using (var fixture = new LoadingFixture())
            {
                var first = fixture.CreateClips("Key100_A", "Key100_B");
                var second = fixture.CreateClips("Key200_A", "Key200_B", "Key200_C");
                var third = fixture.CreateClip("Key300_A");
                fixture.SetEvents(
                    fixture.CreateTransientEvent(100, OrpheusLoadPolicy.ExplicitTransient, first),
                    fixture.CreateTransientEvent(200, OrpheusLoadPolicy.ExplicitTransient, second),
                    fixture.CreateTransientEvent(300, OrpheusLoadPolicy.ExplicitTransient, third));
                fixture.Readiness.BindExpectedClips(
                    first[0], first[1], second[0], second[1], second[2], third);
                fixture.Readiness.SetStates(
                    OrpheusClipLoadState.Loaded,
                    OrpheusClipLoadState.Loaded,
                    OrpheusClipLoadState.Loaded,
                    OrpheusClipLoadState.Loading,
                    OrpheusClipLoadState.Loaded,
                    OrpheusClipLoadState.Loaded);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                fixture.Readiness.ClearCalls();

                Assert.That(manager.GetLoadState(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusClipLoadState.Loading));
                Assert.That(fixture.Readiness.GetCalls, Is.EqualTo(new[] { 2, 3, 4 }));
                Assert.That(fixture.Readiness.GetClipNames,
                    Is.EqualTo(new[] { "Key200_A", "Key200_B", "Key200_C" }));
                Assert.That(fixture.Readiness.RequestClipNames, Is.Empty);

                fixture.Readiness.ClearCalls();
                fixture.Readiness.SetState(2, OrpheusClipLoadState.Unloaded);
                fixture.Readiness.SetState(3, OrpheusClipLoadState.Failed);
                fixture.Readiness.SetState(4, OrpheusClipLoadState.Loaded);
                Assert.That(manager.Prepare(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusAudioPrepareResult.LoadRequested));
                Assert.That(fixture.Readiness.GetCalls, Is.EqualTo(new[] { 2, 3, 4 }));
                Assert.That(fixture.Readiness.RequestCalls, Is.EqualTo(new[] { 2, 3 }));
                Assert.That(fixture.Readiness.GetClipNames,
                    Is.EqualTo(new[] { "Key200_A", "Key200_B", "Key200_C" }));
                Assert.That(fixture.Readiness.RequestClipNames,
                    Is.EqualTo(new[] { "Key200_A", "Key200_B" }));

                manager.Dispose();
            }
        }

        [Test]
        public void ProductionReadiness_UsesRetainedRuntimeSnapshotClipReferences()
        {
            using (var fixture = new LoadingFixture())
            {
                var firstClip = fixture.CreateClip("ProductionSnapshotFirst");
                var secondClip = fixture.CreateClip("ProductionSnapshotSecond");
                var replacementClip = fixture.CreateClip("ProductionSnapshotReplacement");
                var firstEvent = fixture.CreateTransientEvent(
                    100,
                    OrpheusLoadPolicy.ExplicitTransient,
                    firstClip);
                var secondEvent = fixture.CreateTransientEvent(
                    200,
                    OrpheusLoadPolicy.ExplicitTransient,
                    secondClip);
                fixture.SetEvents(
                    firstEvent,
                    secondEvent);
                Assert.That(fixture.Create(out var manager, null).Success, Is.True);
                var firstState = manager.GetLoadState(new OrpheusAudioKey(100));
                var secondState = manager.GetLoadState(new OrpheusAudioKey(200));
                Assert.That(firstState, Is.Not.EqualTo(OrpheusClipLoadState.Invalid));
                Assert.That(secondState, Is.Not.EqualTo(OrpheusClipLoadState.Invalid));

                fixture.ReplaceEventClips(secondEvent, replacementClip);
                UnityEngine.Object.DestroyImmediate(secondClip);

                Assert.That(manager.GetLoadState(new OrpheusAudioKey(100)), Is.EqualTo(firstState));
                Assert.That(manager.GetLoadState(new OrpheusAudioKey(200)),
                    Is.EqualTo(OrpheusClipLoadState.Invalid));
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
                Assert.That(diagnostics.Counters.UnexpectedException, Is.EqualTo(1));

                manager.Dispose();
            }
        }

        private static void AssertOnlyCounterIs(
            OrpheusAudioDiagnosticsCounters counters,
            string expectedName,
            ulong expectedValue)
        {
            var fields = typeof(OrpheusAudioDiagnosticsCounters)
                .GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (var index = 0; index < fields.Length; index++)
            {
                var field = fields[index];
                var expected = field.Name == expectedName ? expectedValue : 0ul;
                Assert.That((ulong)field.GetValue(counters), Is.EqualTo(expected), field.Name);
            }
        }

        private sealed class LoadingFixture : IDisposable
        {
            private static readonly OrpheusAudioUserGains DefaultGains =
                new OrpheusAudioUserGains(1f, 1f, 1f, 1f, 1f, 1f);

            private readonly GameObject _root;
            private readonly OrpheusAudioSettings _settings;
            private readonly FakeMixerPort _mixer;
            private readonly FakeAudioSystemPort _audioSystem;
            private readonly List<OrpheusAudioEvent> _events = new List<OrpheusAudioEvent>();
            private readonly List<AudioClip> _clips = new List<AudioClip>();

            internal LoadingFixture()
            {
                _root = new GameObject("OrpheusLoadingTestHost");
                Host = _root.AddComponent<OrpheusAudioRuntimeHost>();
                var bank = _root.AddComponent<OrpheusAudioSourceBank>();
                var sources = BuildSources(_root.transform);
                SetField(bank, "_oneShot3D", Slice(sources, 0, 12));
                SetField(bank, "_oneShot2D", Slice(sources, 12, 4));
                SetField(bank, "_bgm", Slice(sources, 16, 2));
                SetField(bank, "_profileAmbience", Slice(sources, 18, 2));
                SetField(bank, "_globalLoop", Slice(sources, 20, 4));
                SetField(Host, "_sourceBank", bank);
                _settings = ScriptableObject.CreateInstance<OrpheusAudioSettings>();
                var validationMixer = ConfigureValidationMixer(_settings);
                Catalog = ScriptableObject.CreateInstance<OrpheusAudioCatalog>();
                Readiness = new FakeClipReadiness(64);
                _mixer = new FakeMixerPort(validationMixer);
                _audioSystem = new FakeAudioSystemPort();
            }

            internal OrpheusAudioRuntimeHost Host { get; }
            internal OrpheusAudioCatalog Catalog { get; }
            internal FakeClipReadiness Readiness { get; }
            internal FakeAudioSystemPort AudioSystem => _audioSystem;

            internal AudioClip CreateClip(string name)
            {
                var clip = AudioClip.Create(name, 32, 1, 8000, false);
                _clips.Add(clip);
                return clip;
            }

            internal AudioClip[] CreateClips(params string[] names)
            {
                var clips = new AudioClip[names.Length];
                for (var index = 0; index < names.Length; index++)
                {
                    clips[index] = CreateClip(names[index]);
                }

                return clips;
            }

            internal OrpheusAudioEvent CreateTransientEvent(
                ushort key,
                OrpheusLoadPolicy loadPolicy,
                params AudioClip[] clips)
            {
                var audioEvent = ScriptableObject.CreateInstance<OrpheusAudioEvent>();
                SetField(audioEvent, "_key", key);
                SetField(audioEvent, "_playbackKind", OrpheusPlaybackKind.OneShot2D);
                SetField(audioEvent, "_category", OrpheusCategory.SfxUi);
                SetField(audioEvent, "_loadPolicy", loadPolicy);
                SetField(audioEvent, "_polyphonyCap", (byte)1);
                SetField(audioEvent, "_clips", clips);
                _events.Add(audioEvent);
                return audioEvent;
            }

            internal OrpheusAudioEvent CreatePersistentEvent(ushort key, AudioClip clip)
            {
                var audioEvent = ScriptableObject.CreateInstance<OrpheusAudioEvent>();
                SetField(audioEvent, "_key", key);
                SetField(audioEvent, "_playbackKind", OrpheusPlaybackKind.GlobalLoop2D);
                SetField(audioEvent, "_category", OrpheusCategory.SfxWorld);
                SetField(audioEvent, "_loadPolicy", OrpheusLoadPolicy.PersistentStream);
                SetField(audioEvent, "_polyphonyCap", (byte)1);
                SetField(audioEvent, "_clips", new[] { clip });
                _events.Add(audioEvent);
                return audioEvent;
            }

            internal void SetEvents(params OrpheusAudioEvent[] audioEvents)
            {
                SetField(Catalog, "_events", audioEvents);
            }

            internal void ReplaceEventClips(OrpheusAudioEvent audioEvent, params AudioClip[] clips)
            {
                SetField(audioEvent, "_clips", clips);
            }

            internal OrpheusAudioInitResult Create(out OrpheusAudioManager manager)
            {
                return Create(out manager, Readiness);
            }

            internal OrpheusAudioInitResult Create(
                out OrpheusAudioManager manager,
                IOrpheusAudioClipReadiness readiness)
            {
                return OrpheusAudioTestFactory.Create(
                    Host,
                    _settings,
                    Catalog,
                    DefaultGains,
                    1,
                    true,
                    readiness,
                    _mixer,
                    _audioSystem,
                    out manager);
            }

            public void Dispose()
            {
                if (_root != null)
                {
                    UnityEngine.Object.DestroyImmediate(_root);
                }

                UnityEngine.Object.DestroyImmediate(_settings);
                UnityEngine.Object.DestroyImmediate(Catalog);
                for (var index = 0; index < _events.Count; index++)
                {
                    if (_events[index] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(_events[index]);
                    }
                }

                for (var index = 0; index < _clips.Count; index++)
                {
                    if (_clips[index] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(_clips[index]);
                    }
                }
            }

            private static AudioSource[] BuildSources(Transform parent)
            {
                var sources = new AudioSource[24];
                for (var index = 0; index < sources.Length; index++)
                {
                    var child = new GameObject(GetSourceName(index));
                    child.transform.SetParent(parent, false);
                    sources[index] = child.AddComponent<AudioSource>();
                }

                return sources;
            }

            private static string GetSourceName(int index)
            {
                if (index < 12)
                {
                    return "OneShot3D_" + index.ToString("00");
                }

                if (index < 16)
                {
                    return "OneShot2D_" + (index - 12).ToString("00");
                }

                if (index < 18)
                {
                    return "Bgm_" + (index - 16).ToString("00");
                }

                if (index < 20)
                {
                    return "ProfileAmbience_" + (index - 18).ToString("00");
                }

                return "GlobalLoop_" + (index - 20).ToString("00");
            }

            private static AudioSource[] Slice(AudioSource[] sources, int offset, int count)
            {
                var result = new AudioSource[count];
                Array.Copy(sources, offset, result, 0, count);
                return result;
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

            private static AudioMixerGroup GetOnlyGroup(AudioMixer mixer, string name)
            {
                var groups = mixer.FindMatchingGroups(string.Empty);
                AudioMixerGroup match = null;
                var matchCount = 0;
                for (var index = 0; index < groups.Length; index++)
                {
                    if (groups[index].name == name)
                    {
                        match = groups[index];
                        matchCount++;
                    }
                }

                Assert.That(matchCount, Is.EqualTo(1), name);
                return match;
            }

            private static void SetField(object target, string name, object value)
            {
                target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(target, value);
            }
        }

        private sealed class FakeClipReadiness : IOrpheusAudioClipReadiness
        {
            private readonly OrpheusClipLoadState[] _states;
            private readonly bool[] _requestResults;
            private readonly int[] _requestCounts;
            private AudioClip[] _expectedClips;

            internal FakeClipReadiness(int capacity)
            {
                _states = new OrpheusClipLoadState[capacity];
                _requestResults = new bool[capacity];
                _requestCounts = new int[capacity];
                for (var index = 0; index < capacity; index++)
                {
                    _states[index] = OrpheusClipLoadState.Loaded;
                    _requestResults[index] = true;
                }
            }

            internal List<int> GetCalls { get; } = new List<int>();
            internal List<int> RequestCalls { get; } = new List<int>();
            internal List<string> GetClipNames { get; } = new List<string>();
            internal List<string> RequestClipNames { get; } = new List<string>();
            internal bool AutoTransitionSuccessfulRequestsToLoading { get; set; }

            public OrpheusClipLoadState GetLoadState(int flattenedClipIndex)
            {
                GetCalls.Add(flattenedClipIndex);
                RecordClipName(flattenedClipIndex, GetClipNames);
                return _states[flattenedClipIndex];
            }

            public bool RequestLoad(int flattenedClipIndex)
            {
                RequestCalls.Add(flattenedClipIndex);
                RecordClipName(flattenedClipIndex, RequestClipNames);
                _requestCounts[flattenedClipIndex]++;
                var result = _requestResults[flattenedClipIndex];
                if (result && AutoTransitionSuccessfulRequestsToLoading)
                {
                    _states[flattenedClipIndex] = OrpheusClipLoadState.Loading;
                }

                return result;
            }

            internal void SetState(int flattenedClipIndex, OrpheusClipLoadState state)
            {
                _states[flattenedClipIndex] = state;
            }

            internal OrpheusClipLoadState GetState(int flattenedClipIndex)
            {
                return _states[flattenedClipIndex];
            }

            internal void SetStates(params OrpheusClipLoadState[] states)
            {
                Array.Copy(states, _states, states.Length);
            }

            internal void SetRequestResult(int flattenedClipIndex, bool result)
            {
                _requestResults[flattenedClipIndex] = result;
            }

            internal int GetRequestCount(int flattenedClipIndex)
            {
                return _requestCounts[flattenedClipIndex];
            }

            internal void BindExpectedClips(params AudioClip[] clips)
            {
                _expectedClips = clips;
            }

            internal void ClearCalls()
            {
                GetCalls.Clear();
                RequestCalls.Clear();
                GetClipNames.Clear();
                RequestClipNames.Clear();
            }

            private void RecordClipName(int flattenedClipIndex, List<string> target)
            {
                if (_expectedClips != null && flattenedClipIndex < _expectedClips.Length)
                {
                    target.Add(_expectedClips[flattenedClipIndex].name);
                }
            }
        }

        private sealed class FakeMixerPort : IOrpheusAudioMixerPort
        {
            private readonly object _identity;

            internal FakeMixerPort(object identity)
            {
                _identity = identity;
            }

            public object LeaseIdentity => _identity;

            public bool SetFloat(string parameterName, float decibels)
            {
                return true;
            }

            public void TransitionTo(OrpheusEffectiveSnapshot snapshot, float transitionSeconds)
            {
            }
        }

        private sealed class FakeAudioSystemPort : IOrpheusAudioSystemPort
        {
            internal OrpheusAudioSystemConfiguration Configuration { get; set; } =
                new OrpheusAudioSystemConfiguration(1024, 48000, 32, 64);

            public OrpheusAudioSystemConfiguration GetConfiguration()
            {
                return Configuration;
            }

            public bool ResetCurrentConfiguration()
            {
                return true;
            }
        }
    }
}
