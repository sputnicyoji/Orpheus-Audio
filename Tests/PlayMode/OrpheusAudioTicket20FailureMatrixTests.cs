using System;
using System.Reflection;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Orpheus.Audio.Tests
{
    public sealed partial class OrpheusAudioSessionTests
    {
        [Test]
        public void Ticket20_FatalMatrix_ExecutesEveryReasonThroughSharedTerminalOracle()
        {
            var scenarios = new[]
            {
                FatalScenario(
                    OrpheusAudioDisableReason.MixerSetFloatFailed,
                    RunTicket20MixerSetFloatFailed),
                FatalScenario(
                    OrpheusAudioDisableReason.ListenerPositionNonFinite,
                    RunTicket20ListenerPositionNonFinite),
                FatalScenario(
                    OrpheusAudioDisableReason.OwnedSourceDestroyed,
                    RunTicket20OwnedSourceDestroyed),
                FatalScenario(
                    OrpheusAudioDisableReason.OwnedSourceReferenceChanged,
                    RunTicket20OwnedSourceReferenceChanged),
                FatalScenario(
                    OrpheusAudioDisableReason.SourceBankDuplicateReference,
                    RunTicket20SourceBankDuplicateReference),
                FatalScenario(
                    OrpheusAudioDisableReason.SourceBankLeaseLost,
                    RunTicket20SourceBankLeaseLost),
                FatalScenario(
                    OrpheusAudioDisableReason.MixerLeaseLost,
                    RunTicket20MixerLeaseLost),
                FatalScenario(
                    OrpheusAudioDisableReason.MixerReferenceInvalid,
                    RunTicket20MixerReferenceInvalid),
                FatalScenario(
                    OrpheusAudioDisableReason.SnapshotReferenceInvalid,
                    RunTicket20SnapshotReferenceInvalid),
                FatalScenario(
                    OrpheusAudioDisableReason.MixerGroupReferenceInvalid,
                    RunTicket20MixerGroupReferenceInvalid),
                FatalScenario(
                    OrpheusAudioDisableReason.RuntimeTimeInvalid,
                    RunTicket20RuntimeTimeInvalid),
                FatalScenario(
                    OrpheusAudioDisableReason.RealtimeMovedBackwards,
                    RunTicket20RealtimeMovedBackwards),
                FatalScenario(
                    OrpheusAudioDisableReason.UnexpectedRuntimeException,
                    RunTicket20UnexpectedRuntimeException),
                FatalScenario(
                    OrpheusAudioDisableReason.RuntimeHostDestroyed,
                    RunTicket20RuntimeHostDestroyed)
            };

            var covered = new bool[byte.MaxValue + 1];
            for (var index = 0; index < scenarios.Length; index++)
            {
                var scenario = scenarios[index];
                Assert.That(scenario.Reason, Is.Not.EqualTo(OrpheusAudioDisableReason.None));
                Assert.That(covered[(byte)scenario.Reason], Is.False, scenario.Reason.ToString());
                covered[(byte)scenario.Reason] = true;
                RunWithCleanRuntimeStatics(() => scenario.Execute(scenario.Reason));
            }

            var reasons = (OrpheusAudioDisableReason[])Enum.GetValues(
                typeof(OrpheusAudioDisableReason));
            Assert.That(reasons, Has.Length.EqualTo(scenarios.Length + 1));
            for (var index = 0; index < reasons.Length; index++)
            {
                if (reasons[index] != OrpheusAudioDisableReason.None)
                {
                    Assert.That(covered[(byte)reasons[index]], Is.True, reasons[index].ToString());
                }
            }
        }

        [Test]
        public void Ticket20_Nonfatal_InvalidRequest_ChangesOnlyItsFixedCounter()
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                manager.Play(OrpheusAudioKey.Invalid);

                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                AssertTicket20Running(after);
                AssertDiagnosticsEqualExceptCounter(
                    before,
                    after,
                    nameof(OrpheusAudioDiagnosticsCounters.InvalidKeyRejected),
                    1);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Ticket20_Nonfatal_LoadingAndFailed_ChangeOnlyLoadCounters()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(fixture.CreatePlaybackEvent(720));
                var manager = fixture.CreateReadyManager(out _);
                fixture.Readiness.ResetCalls();

                fixture.Readiness.State = OrpheusClipLoadState.Loading;
                manager.Play(new OrpheusAudioKey(720));
                fixture.Readiness.State = OrpheusClipLoadState.Failed;
                manager.Play(new OrpheusAudioKey(720));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                AssertTicket20Running(diagnostics);
                AssertTicket20OnlyCounters(
                    diagnostics.Counters,
                    nameof(OrpheusAudioDiagnosticsCounters.LoadNotReadyRejected),
                    1,
                    nameof(OrpheusAudioDiagnosticsCounters.LoadFailed),
                    1);
                Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
                Assert.That(fixture.Readiness.RequestCallCount, Is.Zero);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Ticket20_Nonfatal_PoolPressure_ChangesOnlyCapacityCounter()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetEvents(
                    fixture.CreatePlaybackEvent(721),
                    fixture.CreatePlaybackEvent(722),
                    fixture.CreatePlaybackEvent(723),
                    fixture.CreatePlaybackEvent(724),
                    fixture.CreatePlaybackEvent(725));
                var manager = fixture.CreateReadyManager(out _);

                for (ushort key = 721; key <= 725; key++)
                {
                    manager.Play(new OrpheusAudioKey(key));
                }

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                AssertTicket20Running(diagnostics);
                Assert.That(diagnostics.Transient2DActiveCount, Is.EqualTo(4));
                AssertTicket20OnlyCounters(
                    diagnostics.Counters,
                    nameof(OrpheusAudioDiagnosticsCounters.PoolCapacityRejected),
                    1,
                    null,
                    0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Ticket20_Nonfatal_DegradedVoiceBudget_RemainsRunningWithoutCounters()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.AudioSystem.Configuration =
                    new OrpheusAudioSystemConfiguration(1024, 48000, 16, 32);

                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                AssertTicket20Running(diagnostics);
                Assert.That(diagnostics.IsVoiceBudgetDegraded, Is.True);
                AssertTicket20OnlyCounters(diagnostics.Counters, null, 0, null, 0);
                manager.Dispose();
            }
        }

        [Test]
        public void Ticket20_Nonfatal_EarlyHostReady_ReturnsFixedResultWithoutCounters()
        {
            using (var fixture = new SessionFixture())
            {
                var listener = CreateListener(fixture, "Ticket20EarlyHostReady");
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.BindListener(listener), Is.True);

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.RuntimeHostNotStarted));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                AssertTicket20Running(diagnostics);
                AssertTicket20OnlyCounters(diagnostics.Counters, null, 0, null, 0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Ticket20_Nonfatal_ListenerAbsence_ReturnsFixedResultWithoutCounters()
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateManagerBeforeHostReady(out var listener);
                Assert.That(manager.RemoveListener(listener), Is.True);

                Assert.That(
                    manager.CompleteHostReady(),
                    Is.EqualTo(OrpheusCompleteHostReadyResult.ListenerNotBound));

                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                AssertTicket20Running(diagnostics);
                AssertTicket20OnlyCounters(diagnostics.Counters, null, 0, null, 0);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Ticket20_Nonfatal_AndroidFalseReset_ChangesOnlyRecoveryCounter()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetAndroidManualResetEnabled(true);
                fixture.AudioSystem.ResetResult = false;
                var manager = fixture.CreateReadyManager(out _);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);

                manager.HandleRuntimeHostSuspensionInput(
                    fixture.Host,
                    OrpheusSuspensionReason.FocusLost,
                    true,
                    true);
                manager.HandleRuntimeHostSuspensionInput(
                    fixture.Host,
                    OrpheusSuspensionReason.FocusLost,
                    false,
                    true);

                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                AssertTicket20Running(after);
                Assert.That(fixture.AudioSystem.ResetCallCount, Is.EqualTo(1));
                AssertDiagnosticsEqualExceptCounter(
                    before,
                    after,
                    nameof(OrpheusAudioDiagnosticsCounters.RecoveryFailed),
                    1);
                DisposeBound(fixture, manager);
            }
        }

        [Test]
        public void Ticket20_NonfatalMatrix_MapsEveryRequiredScenarioToExecutableEvidence()
        {
            var evidenceMethods = new[]
            {
                nameof(Ticket20_Nonfatal_InvalidRequest_ChangesOnlyItsFixedCounter),
                nameof(Ticket20_Nonfatal_LoadingAndFailed_ChangeOnlyLoadCounters),
                nameof(Ticket20_Nonfatal_PoolPressure_ChangesOnlyCapacityCounter),
                nameof(Ticket20_Nonfatal_DegradedVoiceBudget_RemainsRunningWithoutCounters),
                nameof(Ticket20_Nonfatal_EarlyHostReady_ReturnsFixedResultWithoutCounters),
                nameof(Ticket20_Nonfatal_ListenerAbsence_ReturnsFixedResultWithoutCounters),
                nameof(Ticket20_Nonfatal_AndroidFalseReset_ChangesOnlyRecoveryCounter)
            };

            Assert.That(evidenceMethods, Has.Length.EqualTo(7));
            for (var index = 0; index < evidenceMethods.Length; index++)
            {
                AssertTicket20EvidenceMethod(evidenceMethods[index], evidenceMethods[index]);
            }
        }

        private static Ticket20FatalScenario FatalScenario(
            OrpheusAudioDisableReason reason,
            Action<OrpheusAudioDisableReason> execute)
        {
            return new Ticket20FatalScenario(reason, execute);
        }

        private static void RunTicket20MixerSetFloatFailed(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                BindTicket20Bridges(manager);
                fixture.ResetMixerCalls();
                fixture.Mixer.FailOnSetFloatCall = 1;

                manager.SetUserGain(OrpheusBus.Master, 0.5f);

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    nameof(OrpheusAudioDiagnosticsCounters.SetFloatFailed),
                    1,
                    () => manager.SetUserGain(OrpheusBus.Master, 0.25f));
            }
        }

        private static void RunTicket20ListenerPositionNonFinite(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out var listener);
                BindTicket20Bridges(manager);
                SetNonFinitePosition(fixture, listener);

                manager.LateTick();

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    null,
                    0,
                    manager.LateTick);
            }
        }

        private static void RunTicket20OwnedSourceDestroyed(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                BindTicket20Bridges(manager);
                UnityEngine.Object.DestroyImmediate(fixture.Sources[0].gameObject);
                var realtime = Time.realtimeSinceStartupAsDouble + 1d;

                manager.Tick(realtime, 0f);

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    null,
                    0,
                    () => manager.Tick(realtime + 1d, 0f),
                    destroyedSourceIndex: 0);
            }
        }

        private static void RunTicket20OwnedSourceReferenceChanged(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                BindTicket20Bridges(manager);
                fixture.ReplaceFirst3DSource();
                var realtime = Time.realtimeSinceStartupAsDouble + 1d;

                manager.Tick(realtime, 0f);

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    null,
                    0,
                    () => manager.Tick(realtime + 1d, 0f));
            }
        }

        private static void RunTicket20SourceBankDuplicateReference(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                BindTicket20Bridges(manager);
                fixture.SetDuplicateSource();
                var realtime = Time.realtimeSinceStartupAsDouble + 1d;

                manager.Tick(realtime, 0f);

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    null,
                    0,
                    () => manager.Tick(realtime + 1d, 0f));
            }
        }

        private static void RunTicket20SourceBankLeaseLost(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var dirtyClip = AudioClip.Create(
                    "Ticket20SourceBankLeaseLost",
                    4800,
                    1,
                    48000,
                    false);
                try
                {
                    var manager = fixture.CreateReadyManager(out _);
                    BindTicket20Bridges(manager);
                    fixture.DirtyAllSources(dirtyClip);
                    var releasedSources = CaptureTicket20OwnedSources(fixture.Sources);
                    var realtime = Time.realtimeSinceStartupAsDouble + 1d;
                    // Supported lifecycle never creates a partial lease state. Exercise the
                    // production ownership transition, then drive failure through public Tick.
                    OrpheusAudioLeaseRegistry.ReleaseSourceBank(manager);

                    manager.Tick(realtime, 0f);

                    AssertTicket20FatalTerminal(
                        fixture,
                        manager,
                        expectedReason,
                        null,
                        0,
                        () => manager.Tick(realtime + 1d, 0f),
                        releasedSources);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(dirtyClip);
                }
            }
        }

        private static void RunTicket20MixerLeaseLost(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var dirtyClip = AudioClip.Create(
                    "Ticket20MixerLeaseLost",
                    4800,
                    1,
                    48000,
                    false);
                try
                {
                    var manager = fixture.CreateReadyManager(out _);
                    BindTicket20Bridges(manager);
                    fixture.DirtyAllSources(dirtyClip);
                    var realtime = Time.realtimeSinceStartupAsDouble + 1d;
                    // Source ownership remains held. Losing only Mixer ownership must still
                    // normalize every Source through the common fail-closed transaction.
                    OrpheusAudioLeaseRegistry.ReleaseMixerAndHost(manager);

                    manager.Tick(realtime, 0f);

                    AssertTicket20FatalTerminal(
                        fixture,
                        manager,
                        expectedReason,
                        null,
                        0,
                        () => manager.Tick(realtime + 1d, 0f));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(dirtyClip);
                }
            }
        }

        private static void RunTicket20MixerReferenceInvalid(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                BindTicket20Bridges(manager);
                fixture.Mixer.SetLeaseIdentity(new object());
                var realtime = Time.realtimeSinceStartupAsDouble + 1d;

                manager.Tick(realtime, 0f);

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    null,
                    0,
                    () => manager.Tick(realtime + 1d, 0f));
            }
        }

        private static void RunTicket20SnapshotReferenceInvalid(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                BindTicket20Bridges(manager);
                fixture.Mixer.FailLeaseIdentity(expectedReason);
                var realtime = Time.realtimeSinceStartupAsDouble + 1d;

                manager.Tick(realtime, 0f);

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    null,
                    0,
                    () => manager.Tick(realtime + 1d, 0f));
            }
        }

        private static void RunTicket20MixerGroupReferenceInvalid(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                fixture.ClearStateGroup(OrpheusCategory.SfxUi);
                var manager = fixture.CreateManagerBeforeHostReady(out _);
                BindTicket20Bridges(manager);

                manager.CompleteHostReady();

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    null,
                    0,
                    () => manager.CompleteHostReady());
            }
        }

        private static void RunTicket20RuntimeTimeInvalid(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                BindTicket20Bridges(manager);

                manager.Tick(double.NaN, 1f / 60f);

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    null,
                    0,
                    () => manager.Tick(double.NaN, 1f / 60f));
            }
        }

        private static void RunTicket20RealtimeMovedBackwards(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                BindTicket20Bridges(manager);
                var forward = Time.realtimeSinceStartupAsDouble + 2d;
                manager.Tick(forward, 0f);

                manager.Tick(forward - 1d, 0f);

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    null,
                    0,
                    () => manager.Tick(forward - 1d, 0f));
            }
        }

        private static void RunTicket20UnexpectedRuntimeException(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                BindTicket20Bridges(manager);
                fixture.ResetMixerCalls();
                fixture.Mixer.ThrowOnSetFloatCall = 1;

                manager.SetUserGain(OrpheusBus.Master, 0.5f);

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    nameof(OrpheusAudioDiagnosticsCounters.UnexpectedException),
                    1,
                    () => manager.SetUserGain(OrpheusBus.Master, 0.25f));
            }
        }

        private static void RunTicket20RuntimeHostDestroyed(
            OrpheusAudioDisableReason expectedReason)
        {
            using (var fixture = new SessionFixture(new object(), true))
            {
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                BindTicket20Bridges(manager);

                fixture.DestroyHostOnly();

                AssertTicket20FatalTerminal(
                    fixture,
                    manager,
                    expectedReason,
                    null,
                    0,
                    () => manager.SetUserGain(OrpheusBus.Master, 1f),
                    runtimeHostDestroyed: true);
            }
        }

        private static void AssertTicket20EvidenceMethod(string methodName, string message)
        {
            var method = typeof(OrpheusAudioSessionTests).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, message);
            var attributes = method.GetCustomAttributes(false);
            var executable = false;
            for (var index = 0; index < attributes.Length; index++)
            {
                if (attributes[index] is TestAttribute ||
                    attributes[index] is TestCaseAttribute ||
                    attributes[index] is UnityTestAttribute)
                {
                    executable = true;
                    break;
                }
            }

            Assert.That(executable, Is.True, message);
        }

        private static void BindTicket20Bridges(OrpheusAudioManager manager)
        {
            Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
            Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);
        }

        private static void AssertTicket20FatalTerminal(
            SessionFixture fixture,
            OrpheusAudioManager manager,
            OrpheusAudioDisableReason reason,
            string expectedCounterName,
            ulong expectedCounterValue,
            Action repeatStimulus,
            Ticket20OwnedSourceState[] releasedSources = null,
            int destroyedSourceIndex = -1,
            bool runtimeHostDestroyed = false)
        {
            Assert.That(manager.TryGetDiagnostics(out var disabled), Is.True);
            Assert.That(disabled.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
            Assert.That(disabled.DisableReason, Is.EqualTo(reason));
            Assert.That(disabled.TransportState, Is.EqualTo(OrpheusTransportState.Invalid));
            Assert.That(disabled.Readiness, Is.EqualTo(OrpheusReadiness.None));
            Assert.That(disabled.Transient3DActiveCount, Is.Zero);
            Assert.That(disabled.Transient2DActiveCount, Is.Zero);
            Assert.That(disabled.FadingCount, Is.Zero);
            Assert.That(disabled.PendingCount, Is.Zero);
            Assert.That(disabled.BgmActiveCount, Is.Zero);
            Assert.That(disabled.ProfileAmbienceActiveCount, Is.Zero);
            Assert.That(disabled.GlobalLoopActiveCount, Is.Zero);
            AssertTicket20OnlyCounters(
                disabled.Counters,
                expectedCounterName,
                expectedCounterValue,
                null,
                0);
            Assert.That(OrpheusAudioBridge.Unbind(manager), Is.False);
            Assert.That(OrpheusAudioRawBridge.Unbind(manager), Is.False);
            AssertTicket20TerminalSources(
                fixture,
                releasedSources,
                destroyedSourceIndex,
                reason.ToString());

            repeatStimulus();
            Assert.That(manager.TryGetDiagnostics(out var repeated), Is.True);
            Assert.That(repeated.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
            Assert.That(repeated.DisableReason, Is.EqualTo(reason));
            AssertTicket20TerminalRetained(disabled, repeated, false);
            AssertCountersEqual(disabled.Counters, repeated.Counters);
            AssertTicket20TerminalSources(
                fixture,
                releasedSources,
                destroyedSourceIndex,
                reason + ":repeat");

            manager.Dispose();
            Assert.That(manager.TryGetDiagnostics(out var disposed), Is.True);
            Assert.That(disposed.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disposed));
            Assert.That(disposed.DisableReason, Is.EqualTo(reason));
            Assert.That(disposed.TransportState, Is.EqualTo(OrpheusTransportState.Disposed));
            AssertTicket20TerminalRetained(disabled, disposed, true);
            AssertCountersEqual(disabled.Counters, disposed.Counters);
            AssertTicket20TerminalSources(
                fixture,
                releasedSources,
                destroyedSourceIndex,
                reason + ":dispose");
            if (!runtimeHostDestroyed)
            {
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }

            AssertTicket20DisposedManagerCannotUnbindBridgeReplacement(manager);
        }

        private static void AssertTicket20TerminalSources(
            SessionFixture fixture,
            Ticket20OwnedSourceState[] releasedSources,
            int destroyedSourceIndex,
            string message)
        {
            if (releasedSources != null)
            {
                AssertTicket20OwnedSources(releasedSources, fixture.Sources, message);
                return;
            }

            for (var sourceIndex = 0; sourceIndex < fixture.Sources.Length; sourceIndex++)
            {
                if (sourceIndex != destroyedSourceIndex)
                {
                    AssertNormalized(fixture.Sources[sourceIndex]);
                }
            }
        }

        private static void AssertTicket20TerminalRetained(
            OrpheusAudioDiagnostics expected,
            OrpheusAudioDiagnostics actual,
            bool allowDisposedState)
        {
            var fields = typeof(OrpheusAudioDiagnostics).GetFields(
                BindingFlags.Instance | BindingFlags.Public);
            for (var index = 0; index < fields.Length; index++)
            {
                var field = fields[index];
                if (field.Name == nameof(OrpheusAudioDiagnostics.Counters) ||
                    allowDisposedState &&
                    (field.Name == nameof(OrpheusAudioDiagnostics.Lifecycle) ||
                     field.Name == nameof(OrpheusAudioDiagnostics.TransportState)))
                {
                    continue;
                }

                Assert.That(
                    field.GetValue(actual),
                    Is.EqualTo(field.GetValue(expected)),
                    field.Name);
            }
        }

        private static void AssertTicket20DisposedManagerCannotUnbindBridgeReplacement(
            OrpheusAudioManager disposedManager)
        {
            using (var replacement = new SessionFixture())
            {
                var replacementManager = replacement.CreateReadyManager(out _);
                Assert.That(OrpheusAudioBridge.Bind(replacementManager), Is.True);
                Assert.That(OrpheusAudioRawBridge.Bind(replacementManager), Is.True);

                disposedManager.Dispose();

                Assert.That(OrpheusAudioBridge.Unbind(disposedManager), Is.False);
                Assert.That(OrpheusAudioRawBridge.Unbind(disposedManager), Is.False);
                Assert.That(OrpheusAudioBridge.Unbind(replacementManager), Is.True);
                Assert.That(OrpheusAudioRawBridge.Unbind(replacementManager), Is.True);
                replacementManager.Dispose();
                Assert.That(replacement.Host.Unbind(replacementManager), Is.True);
            }
        }

        private static void AssertTicket20Running(OrpheusAudioDiagnostics diagnostics)
        {
            Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
            Assert.That(diagnostics.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.None));
        }

        private static void AssertTicket20OnlyCounters(
            OrpheusAudioDiagnosticsCounters counters,
            string firstName,
            ulong firstValue,
            string secondName,
            ulong secondValue)
        {
            var fields = typeof(OrpheusAudioDiagnosticsCounters).GetFields(
                BindingFlags.Instance | BindingFlags.Public);
            for (var index = 0; index < fields.Length; index++)
            {
                var field = fields[index];
                var expected = field.Name == firstName
                    ? firstValue
                    : field.Name == secondName
                        ? secondValue
                        : 0UL;
                Assert.That(
                    (ulong)field.GetValue(counters),
                    Is.EqualTo(expected),
                    field.Name);
            }
        }

        private readonly struct Ticket20FatalScenario
        {
            internal Ticket20FatalScenario(
                OrpheusAudioDisableReason reason,
                Action<OrpheusAudioDisableReason> execute)
            {
                Reason = reason;
                Execute = execute;
            }

            internal OrpheusAudioDisableReason Reason { get; }
            internal Action<OrpheusAudioDisableReason> Execute { get; }
        }
    }
}
