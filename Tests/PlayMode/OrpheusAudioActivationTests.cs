using System;
using System.Collections;
using System.Collections.Generic;
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
        [UnityTest]
        public IEnumerator HostReady_ReportsUnboundAndMissingListenerWithoutDisabling()
        {
            using (var fixture = new SessionFixture())
            {
                var listener = CreateListener(fixture, "RecoverableHostReadyListener");
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                yield return null;

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.RuntimeHostNotBound));
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.ListenerNotBound));
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);
                Assert.That(manager.TryGetDiagnostics(out var recoverable), Is.True);
                Assert.That(recoverable.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));

                Assert.That(manager.BindListener(listener), Is.True);
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                DisposeBound(fixture, manager);
                UnityEngine.Object.DestroyImmediate(listener.gameObject);
            }
        }

        [Test]
        public void RuntimeTimeValidation_UsesExactFatalReasons()
        {
            using (var invalid = new SessionFixture())
            {
                Assert.That(invalid.Create(out var manager).Success, Is.True);
                Assert.That(invalid.Host.Bind(manager), Is.True);

                manager.Tick(double.NaN, 0.016f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(diagnostics.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.RuntimeTimeInvalid));
                DisposeBound(invalid, manager);
            }

            using (var backwards = new SessionFixture())
            {
                Assert.That(backwards.Create(out var manager).Success, Is.True);
                Assert.That(backwards.Host.Bind(manager), Is.True);
                var forwardRealtime = Time.realtimeSinceStartupAsDouble + 1d;
                manager.Tick(forwardRealtime, 0.016f);
                Assert.That(manager.TryGetDiagnostics(out var running), Is.True);
                Assert.That(running.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));

                manager.Tick(forwardRealtime - 0.5d, 0.016f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.RealtimeMovedBackwards));
                DisposeBound(backwards, manager);
            }
        }

        [Test]
        public void RuntimeHostBind_CachesCurrentRealtimeBeforeFirstTick()
        {
            using (var fixture = new SessionFixture())
            {
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                var beforeBind = Time.realtimeSinceStartupAsDouble;

                Assert.That(fixture.Host.Bind(manager), Is.True);

                var afterBind = Time.realtimeSinceStartupAsDouble;
                var cached = (double)GetField(manager, "_lastRealtime");
                Assert.That(cached, Is.GreaterThanOrEqualTo(beforeBind));
                Assert.That(cached, Is.LessThanOrEqualTo(afterBind));

                manager.Tick(afterBind, 0.016f);

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
                DisposeBound(fixture, manager);
            }
        }

        [UnityTest]
        public IEnumerator ListenerCache_IsCapturedAtHostReadyAndRefreshedAtLateTick()
        {
            using (var fixture = new SessionFixture())
            {
                var listener = CreateListener(fixture, "FiniteCacheListener");
                listener.transform.position = new Vector3(1f, 2f, 3f);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                yield return null;

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                Assert.That(
                    (Vector3)GetField(manager, "_cachedListenerPosition"),
                    Is.EqualTo(new Vector3(1f, 2f, 3f)));

                listener.transform.position = new Vector3(4f, 5f, 6f);
                manager.LateTick();

                Assert.That(
                    (Vector3)GetField(manager, "_cachedListenerPosition"),
                    Is.EqualTo(new Vector3(4f, 5f, 6f)));
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void InactiveBoundNonFiniteListener_FailClosesAtHostReady()
        {
            using (var fixture = new SessionFixture())
            {
                var listener = CreateListener(fixture, "InactiveNonFiniteListener");
                listener.gameObject.SetActive(false);
                SetNonFinitePosition(fixture, listener);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                SetField(fixture.Host, "_hasStarted", true);

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.DisabledByValidationFailure));
                AssertDisabledByNonFiniteListener(manager);
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);
                DisposeBound(fixture, manager);
            }
        }

        [UnityTest]
        public IEnumerator HostReady_DetectsSourceBankCorruptionBeforeMixerWork()
        {
            using (var fixture = new SessionFixture())
            {
                var listener = CreateListener(fixture, "CorruptSourceBankListener");
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                fixture.SetDuplicateSource();
                yield return null;

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.SourceBankDuplicateReference));
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.InvalidLifecycle));
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);
                DisposeBound(fixture, manager);
                UnityEngine.Object.DestroyImmediate(listener.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator HostReady_DetectsMixerIdentityAndSnapshotCarrierFailureBeforeGainWrites()
        {
            using (var identityFixture = new SessionFixture())
            {
                var listener = CreateListener(identityFixture, "MixerIdentityListener");
                Assert.That(identityFixture.Create(out var manager).Success, Is.True);
                Assert.That(identityFixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                identityFixture.Mixer.SetLeaseIdentity(new object());
                yield return null;

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(diagnostics.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.MixerReferenceInvalid));
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.InvalidLifecycle));
                Assert.That(identityFixture.Mixer.OperationOrder, Is.Empty);
                DisposeBound(identityFixture, manager);
            }

            using (var snapshotFixture = new SessionFixture())
            {
                var listener = CreateListener(snapshotFixture, "SnapshotReferenceListener");
                Assert.That(snapshotFixture.Create(out var manager).Success, Is.True);
                Assert.That(snapshotFixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                snapshotFixture.Mixer.FailLeaseIdentity(OrpheusAudioDisableReason.SnapshotReferenceInvalid);
                yield return null;

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(diagnostics.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.SnapshotReferenceInvalid));
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.InvalidLifecycle));
                Assert.That(snapshotFixture.Mixer.OperationOrder, Is.Empty);
                DisposeBound(snapshotFixture, manager);
            }
        }

        [Test]
        public void HostReady_MapsOwnedSourceAndLeaseFailuresToExactReasons()
        {
            using (var destroyedFixture = new SessionFixture())
            {
                var manager = PrepareCarrierFailureFixture(destroyedFixture, "DestroyedSourceListener");
                UnityEngine.Object.DestroyImmediate(destroyedFixture.Sources[0].gameObject);

                AssertHostReadyCarrierFailure(
                    destroyedFixture,
                    manager,
                    OrpheusAudioDisableReason.OwnedSourceDestroyed);
            }

            using (var changedFixture = new SessionFixture())
            {
                var manager = PrepareCarrierFailureFixture(changedFixture, "ChangedSourceListener");
                changedFixture.ReplaceFirst3DSource();

                AssertHostReadyCarrierFailure(
                    changedFixture,
                    manager,
                    OrpheusAudioDisableReason.OwnedSourceReferenceChanged);
            }

            using (var sourceLeaseFixture = new SessionFixture())
            {
                var manager = PrepareCarrierFailureFixture(sourceLeaseFixture, "SourceLeaseListener");
                OrpheusAudioLeaseRegistry.ReleaseSourceBank(manager);

                AssertHostReadyCarrierFailure(
                    sourceLeaseFixture,
                    manager,
                    OrpheusAudioDisableReason.SourceBankLeaseLost);
            }

            using (var mixerLeaseFixture = new SessionFixture())
            {
                var manager = PrepareCarrierFailureFixture(mixerLeaseFixture, "MixerLeaseListener");
                OrpheusAudioLeaseRegistry.ReleaseMixerAndHost(manager);

                AssertHostReadyCarrierFailure(
                    mixerLeaseFixture,
                    manager,
                    OrpheusAudioDisableReason.MixerLeaseLost);
            }
        }

        [UnityTest]
        public IEnumerator RuntimeHostStartGate_IsRecoverableAndPerformsNoMixerWorkEarly()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetHostActive(false);
                var listener = CreateListener(fixture, "StartGateListener");
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.RuntimeHostNotStarted));
                Assert.That(fixture.Mixer.ParameterNames, Is.Empty);
                Assert.That(fixture.Mixer.Snapshots, Is.Empty);
                Assert.That(manager.TryGetDiagnostics(out var early), Is.True);
                Assert.That(early.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
                Assert.That(early.HostReady, Is.False);

                fixture.SetHostActive(true);
                yield return null;

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                Assert.That(fixture.Mixer.ParameterNames.Count, Is.EqualTo(6));
                Assert.That(manager.TryGetDiagnostics(out var ready), Is.True);
                Assert.That(ready.HostReady, Is.True);

                DisposeBound(fixture, manager);
                UnityEngine.Object.DestroyImmediate(listener.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator BootstrapThenHostReady_ActivatesExactlyOnce()
        {
            using (var fixture = new SessionFixture())
            {
                var listener = CreateListener(fixture, "BootstrapFirstListener");
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                var authority = manager.CaptureBootstrapAuthority();

                Assert.That(manager.CompleteBootstrapHydration(authority), Is.True);
                Assert.That(manager.TryGetDiagnostics(out var bootstrapOnly), Is.True);
                Assert.That(bootstrapOnly.Readiness, Is.EqualTo(OrpheusReadiness.BootstrapHydrated));
                Assert.That(fixture.Mixer.OperationOrder, Is.Empty);

                yield return null;

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                AssertActivatedNoProfile(manager);
                AssertInitialMixerActivation(fixture.Mixer);

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.AlreadyCompleted));
                Assert.That(manager.CompleteBootstrapHydration(authority), Is.True);
                Assert.That(fixture.Mixer.OperationOrder.Count, Is.EqualTo(7));

                DisposeBound(fixture, manager);
                UnityEngine.Object.DestroyImmediate(listener.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator HostReadyThenBootstrap_ActivatesExactlyOnce()
        {
            using (var fixture = new SessionFixture())
            {
                var listener = CreateListener(fixture, "HostFirstListener");
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                yield return null;

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                Assert.That(fixture.Mixer.ParameterNames.Count, Is.EqualTo(6));
                Assert.That(fixture.Mixer.Snapshots, Is.Empty);
                Assert.That(manager.TryGetDiagnostics(out var hostOnly), Is.True);
                Assert.That(hostOnly.Readiness, Is.EqualTo(OrpheusReadiness.HostReady));

                var authority = manager.CaptureBootstrapAuthority();
                Assert.That(manager.CompleteBootstrapHydration(authority), Is.True);
                AssertActivatedNoProfile(manager);
                AssertInitialMixerActivation(fixture.Mixer);

                Assert.That(manager.CompleteBootstrapHydration(authority), Is.True);
                Assert.That(fixture.Mixer.OperationOrder.Count, Is.EqualTo(7));

                DisposeBound(fixture, manager);
                UnityEngine.Object.DestroyImmediate(listener.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator ActivationReady_LosesPlaybackOnListenerRemovalAndRestoresOnBindAndReplacement()
        {
            using (var fixture = new SessionFixture())
            {
                var first = CreateListener(fixture, "PlaybackFirstListener");
                var rebound = CreateListener(fixture, "PlaybackReboundListener", enabled: false);
                var replacement = CreateListener(fixture, "PlaybackReplacementListener", enabled: false);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(first), Is.True);
                yield return null;
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                Assert.That(
                    manager.CompleteBootstrapHydration(manager.CaptureBootstrapAuthority()),
                    Is.True);
                AssertActivatedNoProfile(manager);

                Assert.That(manager.RemoveListener(first), Is.True);
                Assert.That(manager.TryGetDiagnostics(out var removed), Is.True);
                Assert.That(removed.ActivationReady, Is.True);
                Assert.That(removed.PlaybackReady, Is.False);
                Assert.That(removed.TransportState, Is.EqualTo(OrpheusTransportState.Suspended));
                Assert.That(removed.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.ListenerMissing));

                first.enabled = false;
                rebound.transform.position = new Vector3(7f, 8f, 9f);
                rebound.enabled = true;
                Assert.That(manager.BindListener(rebound), Is.True);
                Assert.That(manager.TryGetDiagnostics(out var reboundReady), Is.True);
                Assert.That(reboundReady.PlaybackReady, Is.True);
                Assert.That(
                    (Vector3)GetField(manager, "_cachedListenerPosition"),
                    Is.EqualTo(new Vector3(7f, 8f, 9f)));

                rebound.enabled = false;
                replacement.enabled = true;
                Assert.That(manager.ReplaceListener(rebound, replacement), Is.True);
                Assert.That(manager.TryGetDiagnostics(out var replacementReady), Is.True);
                Assert.That(replacementReady.PlaybackReady, Is.True);
                Assert.That(replacementReady.TransportState, Is.EqualTo(OrpheusTransportState.Active));

                var nonFinite = CreateListener(fixture, "PlaybackNonFiniteRebind");
                SetNonFinitePosition(fixture, nonFinite);
                Assert.That(manager.RemoveListener(replacement), Is.True);
                Assert.That(manager.BindListener(nonFinite), Is.False);
                AssertDisabledByNonFiniteListener(manager);

                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void BootstrapAuthority_IsCurrentIdempotentForeignSafeAndTerminal()
        {
            using (var first = new SessionFixture(new object()))
            using (var second = new SessionFixture(new object()))
            {
                Assert.That(first.Create(out var firstManager).Success, Is.True);
                Assert.That(second.Create(out var secondManager).Success, Is.True);
                var firstAuthority = firstManager.CaptureBootstrapAuthority();
                var secondAuthority = secondManager.CaptureBootstrapAuthority();

                Assert.That(firstManager.CompleteBootstrapHydration(firstAuthority), Is.True);
                Assert.That(firstManager.CompleteBootstrapHydration(firstAuthority), Is.True);
                Assert.That(firstManager.TryGetDiagnostics(out var repeated), Is.True);
                Assert.That(repeated.Counters.StaleBootstrapTokenRejected, Is.Zero);

                Assert.That(secondManager.CompleteBootstrapHydration(firstAuthority), Is.False);
                Assert.That(secondManager.TryGetDiagnostics(out var foreign), Is.True);
                Assert.That(foreign.Counters.StaleBootstrapTokenRejected, Is.EqualTo(1));
                Assert.That(foreign.BootstrapHydrated, Is.False);
                Assert.That(secondManager.CompleteBootstrapHydration(default), Is.False);
                Assert.That(secondManager.TryGetDiagnostics(out var afterDefault), Is.True);
                Assert.That(afterDefault.Counters.StaleBootstrapTokenRejected, Is.EqualTo(1));

                firstManager.Dispose();
                Assert.That(firstManager.CaptureBootstrapAuthority(), Is.EqualTo(default(OrpheusBootstrapAuthority)));
                Assert.That(firstManager.CompleteBootstrapHydration(firstAuthority), Is.False);
                Assert.That(secondManager.CompleteBootstrapHydration(secondAuthority), Is.True);
                secondManager.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator UserGainLifecycle_CachesValidatesAndAppliesExactMixerContract()
        {
            var initial = new OrpheusAudioUserGains(1f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f);
            using (var fixture = new SessionFixture())
            {
                var listener = CreateListener(fixture, "GainListener");
                Assert.That(fixture.CreateWithGains(initial, out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);

                manager.SetUserGain(OrpheusBus.Music, 0.25f);
                Assert.That(manager.TryGetUserGain(OrpheusBus.Music, out var cached), Is.True);
                Assert.That(cached, Is.EqualTo(0.25f));
                Assert.That(fixture.Mixer.ParameterNames, Is.Empty);

                manager.SetUserGain(OrpheusBus.Music, float.NaN);
                manager.SetUserGain(OrpheusBus.Music, -0.01f);
                manager.SetUserGain(OrpheusBus.Music, 1.01f);
                manager.SetUserGain(OrpheusBus.Music, float.PositiveInfinity);
                Assert.That(manager.TryGetUserGain(OrpheusBus.Music, out var unchanged), Is.True);
                Assert.That(unchanged, Is.EqualTo(0.25f));
                Assert.That(manager.TryGetUserGain(OrpheusBus.Invalid, out var invalidBus), Is.False);
                Assert.That(invalidBus, Is.Zero);

                yield return null;
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                CollectionAssert.AreEqual(
                    new[]
                    {
                        "MasterVolume", "MusicVolume", "SfxCombatVolume",
                        "SfxWorldVolume", "SfxUiVolume", "AmbienceVolume"
                    },
                    fixture.Mixer.ParameterNames);
                Assert.That(fixture.Mixer.Decibels[0], Is.EqualTo(0f).Within(0.0001f));
                Assert.That(fixture.Mixer.Decibels[1], Is.EqualTo(-12.0412f).Within(0.0001f));
                Assert.That(fixture.Mixer.Decibels[2], Is.EqualTo(-1.9382f).Within(0.0001f));
                Assert.That(fixture.Mixer.Decibels[3], Is.EqualTo(-3.0980f).Within(0.0001f));
                Assert.That(fixture.Mixer.Decibels[4], Is.EqualTo(-4.4370f).Within(0.0001f));
                Assert.That(fixture.Mixer.Decibels[5], Is.EqualTo(-6.0206f).Within(0.0001f));

                manager.SetUserGain(OrpheusBus.SfxUi, 0f);
                Assert.That(fixture.Mixer.ParameterNames.Count, Is.EqualTo(7));
                Assert.That(fixture.Mixer.ParameterNames[6], Is.EqualTo("SfxUiVolume"));
                Assert.That(fixture.Mixer.Decibels[6], Is.EqualTo(-80f));
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Counters.InvalidValueRejected, Is.EqualTo(4));

                DisposeBound(fixture, manager);
                UnityEngine.Object.DestroyImmediate(listener.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator MixerSetFloatFalse_FailClosesDuringHostReadyAndRunningChange()
        {
            using (var hostReadyFixture = new SessionFixture())
            {
                var listener = CreateListener(hostReadyFixture, "HostReadyFailureListener");
                Assert.That(hostReadyFixture.Create(out var manager).Success, Is.True);
                Assert.That(hostReadyFixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                hostReadyFixture.Mixer.FailOnSetFloatCall = 4;
                yield return null;

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.DisabledByValidationFailure));
                AssertDisabledByMixerFailure(manager, 1);
                Assert.That(hostReadyFixture.Mixer.ParameterNames.Count, Is.EqualTo(4));
                Assert.That(hostReadyFixture.Mixer.Snapshots, Is.Empty);
                DisposeBound(hostReadyFixture, manager);
                UnityEngine.Object.DestroyImmediate(listener.gameObject);
            }

            using (var runningFixture = new SessionFixture())
            {
                var listener = CreateListener(runningFixture, "RunningGainFailureListener");
                Assert.That(runningFixture.Create(out var manager).Success, Is.True);
                Assert.That(runningFixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                yield return null;
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                runningFixture.Mixer.FailOnSetFloatCall = 7;

                manager.SetUserGain(OrpheusBus.Master, 0.5f);

                AssertDisabledByMixerFailure(manager, 1);
                Assert.That(runningFixture.Mixer.ParameterNames.Count, Is.EqualTo(7));
                DisposeBound(runningFixture, manager);
                UnityEngine.Object.DestroyImmediate(listener.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator MixerSetFloatException_MapsToUnexpectedExactlyOnceAtHostReadyAndRunningChange()
        {
            using (var hostReadyFixture = new SessionFixture())
            {
                var listener = CreateListener(hostReadyFixture, "HostReadyExceptionListener");
                Assert.That(hostReadyFixture.Create(out var manager).Success, Is.True);
                Assert.That(hostReadyFixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                hostReadyFixture.Mixer.ThrowOnSetFloatCall = 4;
                yield return null;

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.DisabledByValidationFailure));
                AssertDisabledByUnexpectedMixerException(manager);
                Assert.That(hostReadyFixture.Mixer.ParameterNames.Count, Is.EqualTo(4));
                DisposeBound(hostReadyFixture, manager);
            }

            using (var runningFixture = new SessionFixture())
            {
                var listener = CreateListener(runningFixture, "RunningExceptionListener");
                Assert.That(runningFixture.Create(out var manager).Success, Is.True);
                Assert.That(runningFixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                yield return null;
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                runningFixture.Mixer.ThrowOnSetFloatCall = 7;

                manager.SetUserGain(OrpheusBus.Master, 0.5f);

                AssertDisabledByUnexpectedMixerException(manager);
                Assert.That(runningFixture.Mixer.ParameterNames.Count, Is.EqualTo(7));
                DisposeBound(runningFixture, manager);
            }
        }

        [UnityTest]
        public IEnumerator SnapshotException_UsesGenericFailCloseBeforeActivationPublishes()
        {
            using (var fixture = new SessionFixture())
            {
                var listener = CreateListener(fixture, "SnapshotFailureListener");
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                yield return null;
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                fixture.Mixer.ThrowOnTransition = true;

                manager.CompleteBootstrapHydration(manager.CaptureBootstrapAuthority());
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(
                    diagnostics.DisableReason,
                    Is.EqualTo(OrpheusAudioDisableReason.UnexpectedRuntimeException));
                Assert.That(diagnostics.Counters.UnexpectedException, Is.EqualTo(1));
                Assert.That(diagnostics.ActivationReady, Is.False);
                AssertOwnedSourcesSilent(fixture.Sources);

                DisposeBound(fixture, manager);
                UnityEngine.Object.DestroyImmediate(listener.gameObject);
            }
        }

        [Test]
        public void ListenerHandoff_IsIdentitySafeAndNeverMutatesEnabledState()
        {
            using (var fixture = new SessionFixture())
            {
                var oldListener = CreateListener(fixture, "OldListener");
                var replacement = CreateListener(fixture, "ReplacementListener", enabled: false);
                var disabledReplacement = CreateListener(fixture, "DisabledReplacement", enabled: false);
                var inactiveReplacement = CreateListener(fixture, "InactiveReplacement");
                inactiveReplacement.gameObject.SetActive(false);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(manager.BindListener(oldListener), Is.True);
                Assert.That(manager.BindListener(oldListener), Is.True);

                oldListener.enabled = false;
                replacement.enabled = true;
                Assert.That(manager.ReplaceListener(oldListener, replacement), Is.True);
                Assert.That(oldListener.enabled, Is.False);
                Assert.That(replacement.enabled, Is.True);
                Assert.That(manager.ReplaceListener(oldListener, disabledReplacement), Is.False);
                Assert.That(manager.ReplaceListener(replacement, disabledReplacement), Is.False);
                Assert.That(manager.ReplaceListener(replacement, inactiveReplacement), Is.False);
                Assert.That(manager.RemoveListener(oldListener), Is.False);
                Assert.That(manager.RemoveListener(replacement), Is.True);
                Assert.That(replacement.enabled, Is.True);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(
                    diagnostics.SuspensionReasons & OrpheusSuspensionReason.ListenerMissing,
                    Is.EqualTo(OrpheusSuspensionReason.ListenerMissing));

                manager.Dispose();
                UnityEngine.Object.DestroyImmediate(oldListener.gameObject);
                UnityEngine.Object.DestroyImmediate(replacement.gameObject);
                UnityEngine.Object.DestroyImmediate(disabledReplacement.gameObject);
                UnityEngine.Object.DestroyImmediate(inactiveReplacement.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator NonFiniteListener_FailClosesAtHostReadyReplacementAndLateTick()
        {
            using (var hostReadyFixture = new SessionFixture())
            {
                var listener = CreateListener(hostReadyFixture, "NonFiniteHostReadyListener");
                Assert.That(hostReadyFixture.Create(out var manager).Success, Is.True);
                Assert.That(hostReadyFixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                yield return null;
                SetNonFinitePosition(hostReadyFixture, listener);

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.DisabledByValidationFailure));
                AssertDisabledByNonFiniteListener(manager);
                Assert.That(hostReadyFixture.Mixer.ParameterNames, Is.Empty);
                DisposeBound(hostReadyFixture, manager);
                DestroyListenerHierarchy(listener);
            }

            using (var replacementFixture = new SessionFixture())
            {
                var oldListener = CreateListener(replacementFixture, "FiniteOldListener");
                var replacement = CreateListener(replacementFixture, "NonFiniteReplacementListener");
                Assert.That(replacementFixture.Create(out var manager).Success, Is.True);
                Assert.That(manager.BindListener(oldListener), Is.True);
                SetNonFinitePosition(replacementFixture, replacement);

                Assert.That(manager.ReplaceListener(oldListener, replacement), Is.False);
                AssertDisabledByNonFiniteListener(manager);
                manager.Dispose();
                UnityEngine.Object.DestroyImmediate(oldListener.gameObject);
                DestroyListenerHierarchy(replacement);
            }

            using (var lateTickFixture = new SessionFixture())
            {
                var listener = CreateListener(lateTickFixture, "NonFiniteLateTickListener");
                Assert.That(lateTickFixture.Create(out var manager).Success, Is.True);
                Assert.That(lateTickFixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                yield return null;
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                SetNonFinitePosition(lateTickFixture, listener);

                yield return null;

                AssertDisabledByNonFiniteListener(manager);
                AssertOwnedSourcesSilent(lateTickFixture.Sources);
                DisposeBound(lateTickFixture, manager);
                DestroyListenerHierarchy(listener);
            }
        }

        [UnityTest]
        public IEnumerator Issue03WrongThreadMatrix_OnlyIncrementsOneCounterPerCall()
        {
            using (var fixture = new SessionFixture())
            {
                var listener = CreateListener(fixture, "WrongThreadListener");
                var replacement = CreateListener(fixture, "WrongThreadReplacement", enabled: false);
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                yield return null;
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                var authority = manager.CaptureBootstrapAuthority();
                Assert.That(manager.CompleteBootstrapHydration(authority), Is.True);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                var mixerCalls = fixture.Mixer.OperationOrder.Count;
                var realtimeBefore = (double)GetField(manager, "_lastRealtime");
                var listenerPositionBefore = (Vector3)GetField(manager, "_cachedListenerPosition");
                var bootstrapGenerationBefore = (uint)GetField(manager, "_bootstrapGeneration");
                var listenerBefore = (AudioListener)GetField(manager, "_listener");

                var bind = true;
                var bindListener = true;
                var replace = true;
                var remove = true;
                var tryGain = true;
                var captured = authority;
                var bootstrap = true;
                var hostReady = OrpheusCompleteHostReadyResult.Completed;
                var worker = new Thread(() =>
                {
                    bind = fixture.Host.Bind(manager);
                    bindListener = manager.BindListener(listener);
                    replace = manager.ReplaceListener(listener, replacement);
                    remove = manager.RemoveListener(listener);
                    manager.SetUserGain(OrpheusBus.Master, 0.25f);
                    tryGain = manager.TryGetUserGain(OrpheusBus.Master, out _);
                    captured = manager.CaptureBootstrapAuthority();
                    bootstrap = manager.CompleteBootstrapHydration(authority);
                    hostReady = manager.CompleteHostReady();
                    manager.Tick(1d, 0.016f);
                    manager.LateTick();
                });
                worker.Start();
                worker.Join();

                Assert.That(bind, Is.False);
                Assert.That(bindListener, Is.False);
                Assert.That(replace, Is.False);
                Assert.That(remove, Is.False);
                Assert.That(tryGain, Is.False);
                Assert.That(captured, Is.EqualTo(default(OrpheusBootstrapAuthority)));
                Assert.That(bootstrap, Is.False);
                Assert.That(hostReady, Is.EqualTo(OrpheusCompleteHostReadyResult.Invalid));
                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                Assert.That(after.Counters.WrongThreadRejected, Is.EqualTo(before.Counters.WrongThreadRejected + 11));
                AssertDiagnosticsEqualExceptWrongThread(before, after);
                Assert.That(fixture.Mixer.OperationOrder.Count, Is.EqualTo(mixerCalls));
                Assert.That(manager.TryGetUserGain(OrpheusBus.Master, out var master), Is.True);
                Assert.That(master, Is.EqualTo(1f));
                Assert.That((double)GetField(manager, "_lastRealtime"), Is.EqualTo(realtimeBefore));
                Assert.That(
                    (Vector3)GetField(manager, "_cachedListenerPosition"),
                    Is.EqualTo(listenerPositionBefore));
                Assert.That(
                    (uint)GetField(manager, "_bootstrapGeneration"),
                    Is.EqualTo(bootstrapGenerationBefore));
                Assert.That((AudioListener)GetField(manager, "_listener"), Is.SameAs(listenerBefore));

                yield return null;
                Assert.That(fixture.Mixer.OperationOrder.Count, Is.EqualTo(mixerCalls));
                DisposeBound(fixture, manager);
                UnityEngine.Object.DestroyImmediate(listener.gameObject);
            }
        }

        private static void AssertDiagnosticsEqualExceptWrongThread(
            OrpheusAudioDiagnostics before,
            OrpheusAudioDiagnostics after)
        {
            var diagnosticsFields = typeof(OrpheusAudioDiagnostics)
                .GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (var index = 0; index < diagnosticsFields.Length; index++)
            {
                var field = diagnosticsFields[index];
                if (field.Name == nameof(OrpheusAudioDiagnostics.Counters))
                {
                    continue;
                }

                Assert.That(field.GetValue(after), Is.EqualTo(field.GetValue(before)), field.Name);
            }

            var counterFields = typeof(OrpheusAudioDiagnosticsCounters)
                .GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (var index = 0; index < counterFields.Length; index++)
            {
                var field = counterFields[index];
                if (field.Name == nameof(OrpheusAudioDiagnosticsCounters.WrongThreadRejected))
                {
                    continue;
                }

                Assert.That(
                    field.GetValue(after.Counters),
                    Is.EqualTo(field.GetValue(before.Counters)),
                    field.Name);
            }
        }

        [UnityTest]
        public IEnumerator ActivationAndFatalFailure_HoldLeasesUntilDisposedAndUnbound()
        {
            using (var owner = new SessionFixture())
            using (var sourceContender = new SessionFixture(new object()))
            using (var mixerContender = new SessionFixture(owner.Mixer.LeaseIdentity))
            {
                sourceContender.SetSourceBank(owner.Bank);
                var listener = CreateListener(owner, "LeaseOwnerListener");
                Assert.That(owner.Create(out var manager).Success, Is.True);
                Assert.That(owner.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);
                yield return null;
                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.Completed));
                Assert.That(
                    manager.CompleteBootstrapHydration(manager.CaptureBootstrapAuthority()),
                    Is.True);

                AssertCreateRejected(
                    sourceContender,
                    OrpheusAudioInitErrorCode.InvalidSourceBank);
                AssertCreateRejected(
                    mixerContender,
                    OrpheusAudioInitErrorCode.InvalidMixerContract);
                owner.Mixer.FailOnSetFloatCall = 7;
                manager.SetUserGain(OrpheusBus.Master, 0.5f);
                AssertDisabledByMixerFailure(manager, 1);
                Assert.That(owner.Host.Bind(manager), Is.True);
                AssertCreateRejected(
                    sourceContender,
                    OrpheusAudioInitErrorCode.InvalidSourceBank);
                AssertCreateRejected(
                    mixerContender,
                    OrpheusAudioInitErrorCode.InvalidMixerContract);
                manager.Dispose();
                Assert.That(owner.Host.Bind(manager), Is.True);
                AssertCreateRejected(
                    sourceContender,
                    OrpheusAudioInitErrorCode.InvalidSourceBank);
                AssertCreateRejected(
                    mixerContender,
                    OrpheusAudioInitErrorCode.InvalidMixerContract);

                Assert.That(owner.Host.Unbind(manager), Is.True);
                Assert.That(sourceContender.Create(out var sourceReplacement).Success, Is.True);
                Assert.That(mixerContender.Create(out var mixerReplacement).Success, Is.True);
                sourceReplacement.Dispose();
                mixerReplacement.Dispose();
                UnityEngine.Object.DestroyImmediate(listener.gameObject);
            }
        }

        private static void AssertCreateRejected(
            SessionFixture fixture,
            OrpheusAudioInitErrorCode expectedError)
        {
            var result = fixture.Create(out var manager);
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedError));
            Assert.That(manager, Is.Null);
        }

        private static OrpheusAudioManager PrepareCarrierFailureFixture(
            SessionFixture fixture,
            string listenerName)
        {
            var listener = CreateListener(fixture, listenerName);
            Assert.That(fixture.Create(out var manager).Success, Is.True);
            Assert.That(fixture.Host.Bind(manager), Is.True);
            Assert.That(manager.BindListener(listener), Is.True);
            SetField(fixture.Host, "_hasStarted", true);
            return manager;
        }

        private static void AssertHostReadyCarrierFailure(
            SessionFixture fixture,
            OrpheusAudioManager manager,
            OrpheusAudioDisableReason expectedReason)
        {
            Assert.That(
                manager.CompleteHostReady(),
                Is.EqualTo(OrpheusCompleteHostReadyResult.DisabledByValidationFailure));
            Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
            Assert.That(diagnostics.DisableReason, Is.EqualTo(expectedReason));
            Assert.That(fixture.Mixer.OperationOrder, Is.Empty);
            DisposeBound(fixture, manager);
        }

        private static AudioListener CreateListener(
            SessionFixture fixture,
            string name,
            bool enabled = true)
        {
            var gameObject = new GameObject(name);
            fixture.TrackActivationTestObject(gameObject);
            var listener = gameObject.AddComponent<AudioListener>();
            listener.enabled = enabled;
            return listener;
        }

        private static void SetNonFinitePosition(SessionFixture fixture, AudioListener listener)
        {
            var ignored = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var parent = new GameObject(listener.name + "_OverflowParent");
                fixture.TrackActivationTestObject(parent);
                parent.transform.localScale = new Vector3(float.MaxValue, 1f, 1f);
                listener.transform.SetParent(parent.transform, false);
                listener.transform.localPosition = new Vector3(float.MaxValue, 0f, 0f);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = ignored;
            }

            Assert.That(
                float.IsNaN(listener.transform.position.x) || float.IsInfinity(listener.transform.position.x),
                Is.True,
                "Finite transform overflow did not produce a non-finite world position.");
        }

        private static void DestroyListenerHierarchy(AudioListener listener)
        {
            if (listener != null)
            {
                UnityEngine.Object.DestroyImmediate(listener.transform.root.gameObject);
            }
        }

        private static void DisposeBound(SessionFixture fixture, OrpheusAudioManager manager)
        {
            manager.Dispose();
            Assert.That(fixture.Host.Unbind(manager), Is.True);
        }

        private static void AssertActivatedNoProfile(OrpheusAudioManager manager)
        {
            Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
            Assert.That(
                diagnostics.Readiness,
                Is.EqualTo(
                    OrpheusReadiness.HostReady |
                    OrpheusReadiness.BootstrapHydrated |
                    OrpheusReadiness.ActivationReady |
                    OrpheusReadiness.PlaybackReady));
            Assert.That(diagnostics.TransportState, Is.EqualTo(OrpheusTransportState.Active));
            Assert.That(diagnostics.ProfileId, Is.Zero);
            Assert.That(diagnostics.BaseState, Is.EqualTo(OrpheusBaseState.Peace));
            Assert.That(diagnostics.Overlay, Is.EqualTo(OrpheusOverlay.None));
            Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Peace));
            Assert.That(diagnostics.DesiredBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.DesiredProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
        }

        private static void AssertInitialMixerActivation(FakeMixerPort mixer)
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "SetFloat:MasterVolume", "SetFloat:MusicVolume", "SetFloat:SfxCombatVolume",
                    "SetFloat:SfxWorldVolume", "SetFloat:SfxUiVolume", "SetFloat:AmbienceVolume",
                    "TransitionTo:Peace"
                },
                mixer.OperationOrder);
            Assert.That(mixer.Snapshots, Is.EqualTo(new[] { OrpheusEffectiveSnapshot.Peace }));
            Assert.That(mixer.TransitionSeconds, Is.EqualTo(new[] { 0f }));
        }

        private static void AssertDisabledByMixerFailure(OrpheusAudioManager manager, ulong expectedCount)
        {
            Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
            Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
            Assert.That(diagnostics.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.MixerSetFloatFailed));
            Assert.That(diagnostics.Counters.SetFloatFailed, Is.EqualTo(expectedCount));
            Assert.That(diagnostics.ActivationReady, Is.False);
        }

        private static void AssertDisabledByNonFiniteListener(OrpheusAudioManager manager)
        {
            Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
            Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
            Assert.That(
                diagnostics.DisableReason,
                Is.EqualTo(OrpheusAudioDisableReason.ListenerPositionNonFinite));
        }

        private static void AssertDisabledByUnexpectedMixerException(OrpheusAudioManager manager)
        {
            Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
            Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
            Assert.That(
                diagnostics.DisableReason,
                Is.EqualTo(OrpheusAudioDisableReason.UnexpectedRuntimeException));
            Assert.That(diagnostics.Counters.UnexpectedException, Is.EqualTo(1));
            Assert.That(diagnostics.Counters.SetFloatFailed, Is.Zero);
        }

        private static void AssertOwnedSourcesSilent(AudioSource[] sources)
        {
            for (var index = 0; index < sources.Length; index++)
            {
                Assert.That(sources[index].isPlaying, Is.False, index.ToString());
                Assert.That(sources[index].clip, Is.Null, index.ToString());
            }
        }

        private sealed partial class SessionFixture
        {
            private readonly List<GameObject> _activationTestObjects = new List<GameObject>();

            internal FakeMixerPort Mixer => _mixer;

            internal void SetHostActive(bool active)
            {
                _root.SetActive(active);
            }

            internal AudioSource ReplaceFirst3DSource()
            {
                var replacementObject = new GameObject("ReplacementOneShot3D_00");
                replacementObject.transform.SetParent(_root.transform, false);
                TrackActivationTestObject(replacementObject);
                var replacement = replacementObject.AddComponent<AudioSource>();
                var oneShot3D = Slice(Sources, 0, 12);
                oneShot3D[0] = replacement;
                SetField(Bank, "_oneShot3D", oneShot3D);
                return replacement;
            }

            internal void TrackActivationTestObject(GameObject gameObject)
            {
                _activationTestObjects.Add(gameObject);
            }

            private void DestroyActivationTestObjects()
            {
                for (var index = _activationTestObjects.Count - 1; index >= 0; index--)
                {
                    var gameObject = _activationTestObjects[index];
                    if (gameObject != null)
                    {
                        UnityEngine.Object.DestroyImmediate(gameObject);
                    }
                }

                _activationTestObjects.Clear();
            }
        }
    }
}
