using System;
using System.Threading;
using NUnit.Framework;
using Orpheus.Audio.Core;
using Orpheus.Audio.Tests.Fixtures;
using UnityEngine;

namespace Orpheus.Audio.Tests
{
    public sealed partial class OrpheusAudioSessionTests
    {
        private const int Ticket20WrongThreadSurfaceCount = 36;

        [Test]
        public void Ticket20_MainThreadCoverageMatrix_ExercisesEveryCallableSurface()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var publicFactoryFixture = new SessionFixture())
                {
                    var settings = (OrpheusAudioSettings)GetField(
                        publicFactoryFixture,
                        "_settings");
                    var result = OrpheusAudioFactory.Create(
                        publicFactoryFixture.Host,
                        settings,
                        publicFactoryFixture.Catalog,
                        new OrpheusAudioUserGains(1f, 1f, 1f, 1f, 1f, 1f),
                        out var publicManager);

                    Assert.That(result.Success, Is.True, result.ErrorCode.ToString());
                    publicManager.Dispose();
                }

                using (var fixture = new SessionFixture())
                {
                    var listenerObject = new GameObject("Ticket20Listener");
                    var replacementObject = new GameObject("Ticket20ReplacementListener");
                    var bindingObject = new GameObject("Ticket20SceneBinding");
                    var listener = listenerObject.AddComponent<AudioListener>();
                    var replacement = replacementObject.AddComponent<AudioListener>();
                    var sceneBinding = bindingObject.AddComponent<OrpheusAudioFixtureSceneBinding>();
                    SetField(sceneBinding, "_listener", listener);

                    try
                    {
                        Assert.That(fixture.Create(out var manager).Success, Is.True);
                        Assert.That(fixture.Host.Bind(manager), Is.True);

                        Assert.That(sceneBinding.BindListener(manager), Is.True);
                        Assert.That(sceneBinding.ReplaceListener(manager, replacement), Is.True);
                        Assert.That(sceneBinding.RemoveListener(manager), Is.True);

                        Assert.That(manager.BindListener(listener), Is.True);
                        Assert.That(manager.ReplaceListener(listener, replacement), Is.True);
                        Assert.That(manager.RemoveListener(replacement), Is.True);
                        Assert.That(manager.BindListener(listener), Is.True);

                        manager.SetUserGain(OrpheusBus.Master, 0.75f);
                        Assert.That(manager.TryGetUserGain(OrpheusBus.Master, out _), Is.True);
                        var authority = manager.CaptureBootstrapAuthority();
                        Assert.That(manager.CompleteBootstrapHydration(authority), Is.True);
                        manager.CompleteHostReady();
                        Assert.That(manager.TryGetDiagnostics(out _), Is.True);

                        manager.Play(OrpheusAudioKey.Invalid);
                        manager.PlayAt(OrpheusAudioKey.Invalid, Vector3.zero);
                        manager.PlayLoop(OrpheusAudioKey.Invalid);
                        manager.StopLoop(OrpheusAudioKey.Invalid);
                        manager.Prepare(OrpheusAudioKey.Invalid);
                        manager.GetLoadState(OrpheusAudioKey.Invalid);
                        manager.ApplyProfile(new OrpheusAudioProfileIntent(
                            1,
                            OrpheusBaseState.Peace,
                            OrpheusAudioKey.Invalid,
                            OrpheusAudioKey.Invalid));
                        manager.SetOverlay(OrpheusOverlay.Menu, true);
                        manager.StopAll(OrpheusBus.SfxUi);

                        Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
                        Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);
                        OrpheusAudioBridge.Play(OrpheusAudioKey.Invalid);
                        OrpheusAudioBridge.PlayAt(OrpheusAudioKey.Invalid, Vector3.zero);
                        OrpheusAudioRawBridge.Play(0);
                        OrpheusAudioRawBridge.PlayAt(0, Vector3.zero);
                        Assert.That(OrpheusAudioBridge.Unbind(manager), Is.True);
                        Assert.That(OrpheusAudioRawBridge.Unbind(manager), Is.True);

                        manager.Tick(fixture.Host.ReadRealtime(), 1f / 60f);
                        manager.LateTick();
                        fixture.Host.HandleApplicationFocusChanged(false);
                        fixture.Host.HandleApplicationFocusChanged(true);
                        fixture.Host.HandleApplicationPauseChanged(true);
                        fixture.Host.HandleApplicationPauseChanged(false);
                        fixture.Host.RecordAudioConfigurationChanged(true);
                        fixture.Host.RecordAudioConfigurationChanged(true);
                        fixture.Host.ConsumePendingAudioConfigurationChanges();

                        manager.Dispose();
                        Assert.That(fixture.Host.Unbind(manager), Is.True);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(bindingObject);
                        UnityEngine.Object.DestroyImmediate(replacementObject);
                        UnityEngine.Object.DestroyImmediate(listenerObject);
                    }
                }
            });
        }

        [Test]
        public void Ticket20_WrongThreadCoverageMatrix_OnlyIncrementsOneCounterPerSurface()
        {
            RunWithCleanRuntimeStatics(() =>
            {
                using (var fixture = new SessionFixture())
                {
                    var listenerObject = new GameObject("Ticket20WrongThreadListener");
                    var replacementObject = new GameObject("Ticket20WrongThreadReplacement");
                    var bindingObject = new GameObject("Ticket20WrongThreadSceneBinding");
                    var listener = listenerObject.AddComponent<AudioListener>();
                    var replacement = replacementObject.AddComponent<AudioListener>();
                    var sceneBinding = bindingObject.AddComponent<OrpheusAudioFixtureSceneBinding>();
                    SetField(sceneBinding, "_listener", listener);

                    try
                    {
                        Assert.That(fixture.Create(out var manager).Success, Is.True);
                        Assert.That(fixture.Host.Bind(manager), Is.True);
                        Assert.That(OrpheusAudioBridge.Bind(manager), Is.True);
                        Assert.That(OrpheusAudioRawBridge.Bind(manager), Is.True);
                        var authority = manager.CaptureBootstrapAuthority();
                        var profile = new OrpheusAudioProfileIntent(
                            1,
                            OrpheusBaseState.Peace,
                            OrpheusAudioKey.Invalid,
                            OrpheusAudioKey.Invalid);
                        var tryGetUserGainResult = true;
                        var returnedGain = -1f;
                        var returnedAuthority = authority;
                        var completeHydrationResult = true;
                        var hostReadyResult = OrpheusCompleteHostReadyResult.Completed;
                        var bindListenerResult = true;
                        var replaceListenerResult = true;
                        var removeListenerResult = true;
                        var diagnosticsResult = true;
                        var prepareResult = OrpheusAudioPrepareResult.AlreadyLoaded;
                        var loadState = OrpheusClipLoadState.Loaded;
                        var hostBindResult = true;
                        var hostUnbindResult = true;
                        var sceneBindResult = true;
                        var sceneReplaceResult = true;
                        var sceneRemoveResult = true;
                        var bridgeBindResult = true;
                        var bridgeUnbindResult = true;
                        var rawBridgeBindResult = true;
                        var rawBridgeUnbindResult = true;
                        var calls = new[]
                        {
                            WrongThreadCall("Manager.SetUserGain", () =>
                                manager.SetUserGain(OrpheusBus.Master, 0.25f)),
                            WrongThreadCall("Manager.TryGetUserGain", () =>
                                tryGetUserGainResult = manager.TryGetUserGain(
                                    OrpheusBus.Master,
                                    out returnedGain)),
                            WrongThreadCall("Manager.CaptureBootstrapAuthority", () =>
                                returnedAuthority = manager.CaptureBootstrapAuthority()),
                            WrongThreadCall("Manager.CompleteBootstrapHydration", () =>
                                completeHydrationResult = manager.CompleteBootstrapHydration(authority)),
                            WrongThreadCall("Manager.CompleteHostReady", () =>
                                hostReadyResult = manager.CompleteHostReady()),
                            WrongThreadCall("Manager.BindListener", () =>
                                bindListenerResult = manager.BindListener(listener)),
                            WrongThreadCall("Manager.ReplaceListener", () =>
                                replaceListenerResult = manager.ReplaceListener(listener, replacement)),
                            WrongThreadCall("Manager.RemoveListener", () =>
                                removeListenerResult = manager.RemoveListener(listener)),
                            WrongThreadCall("Manager.TryGetDiagnostics", () =>
                                diagnosticsResult = manager.TryGetDiagnostics(out _)),
                            WrongThreadCall("Manager.Dispose", manager.Dispose),
                            WrongThreadCall("Manager.PlayLoop", () =>
                                manager.PlayLoop(OrpheusAudioKey.Invalid)),
                            WrongThreadCall("Manager.StopLoop", () =>
                                manager.StopLoop(OrpheusAudioKey.Invalid)),
                            WrongThreadCall("Manager.Prepare", () =>
                                prepareResult = manager.Prepare(OrpheusAudioKey.Invalid)),
                            WrongThreadCall("Manager.GetLoadState", () =>
                                loadState = manager.GetLoadState(OrpheusAudioKey.Invalid)),
                            WrongThreadCall("Manager.Play", () =>
                                manager.Play(OrpheusAudioKey.Invalid)),
                            WrongThreadCall("Manager.PlayAt", () =>
                                manager.PlayAt(OrpheusAudioKey.Invalid, Vector3.zero)),
                            WrongThreadCall("Manager.ApplyProfile", () => manager.ApplyProfile(profile)),
                            WrongThreadCall("Manager.SetOverlay", () =>
                                manager.SetOverlay(OrpheusOverlay.Menu, true)),
                            WrongThreadCall("Manager.StopAll", () =>
                                manager.StopAll(OrpheusBus.Master)),
                            WrongThreadCall("Host.Bind", () =>
                                hostBindResult = fixture.Host.Bind(manager)),
                            WrongThreadCall("Host.Unbind", () =>
                                hostUnbindResult = fixture.Host.Unbind(manager)),
                            WrongThreadCall("SceneBinding.BindListener", () =>
                                sceneBindResult = sceneBinding.BindListener(manager)),
                            WrongThreadCall("SceneBinding.ReplaceListener", () =>
                                sceneReplaceResult = sceneBinding.ReplaceListener(manager, replacement)),
                            WrongThreadCall("SceneBinding.RemoveListener", () =>
                                sceneRemoveResult = sceneBinding.RemoveListener(manager)),
                            WrongThreadCall("Bridge.Bind", () =>
                                bridgeBindResult = OrpheusAudioBridge.Bind(manager)),
                            WrongThreadCall("Bridge.Unbind", () =>
                                bridgeUnbindResult = OrpheusAudioBridge.Unbind(manager)),
                            WrongThreadCall("Bridge.Play", () =>
                                OrpheusAudioBridge.Play(OrpheusAudioKey.Invalid)),
                            WrongThreadCall("Bridge.PlayAt", () =>
                                OrpheusAudioBridge.PlayAt(OrpheusAudioKey.Invalid, Vector3.zero)),
                            WrongThreadCall("RawBridge.Bind", () =>
                                rawBridgeBindResult = OrpheusAudioRawBridge.Bind(manager)),
                            WrongThreadCall("RawBridge.Unbind", () =>
                                rawBridgeUnbindResult = OrpheusAudioRawBridge.Unbind(manager)),
                            WrongThreadCall("RawBridge.Play", () => OrpheusAudioRawBridge.Play(0)),
                            WrongThreadCall("RawBridge.PlayAt", () =>
                                OrpheusAudioRawBridge.PlayAt(0, Vector3.zero)),
                            WrongThreadCall("Manager.Tick", () => manager.Tick(1d, 1f / 60f)),
                            WrongThreadCall("Manager.LateTick", manager.LateTick),
                            WrongThreadCall("Host.Focus", () =>
                                fixture.Host.HandleApplicationFocusChanged(false)),
                            WrongThreadCall("Host.Pause", () =>
                                fixture.Host.HandleApplicationPauseChanged(true))
                        };

                        Assert.That(calls, Has.Length.EqualTo(Ticket20WrongThreadSurfaceCount));
                        for (var index = 0; index < calls.Length; index++)
                        {
                            AssertTicket20WrongThreadCall(fixture, manager, calls[index]);
                        }

                        Assert.That(tryGetUserGainResult, Is.False);
                        Assert.That(returnedGain, Is.Zero);
                        Assert.That(returnedAuthority, Is.EqualTo(default(OrpheusBootstrapAuthority)));
                        Assert.That(completeHydrationResult, Is.False);
                        Assert.That(hostReadyResult, Is.EqualTo(OrpheusCompleteHostReadyResult.Invalid));
                        Assert.That(bindListenerResult, Is.False);
                        Assert.That(replaceListenerResult, Is.False);
                        Assert.That(removeListenerResult, Is.False);
                        Assert.That(diagnosticsResult, Is.False);
                        Assert.That(prepareResult, Is.EqualTo(OrpheusAudioPrepareResult.Invalid));
                        Assert.That(loadState, Is.EqualTo(OrpheusClipLoadState.Invalid));
                        Assert.That(hostBindResult, Is.False);
                        Assert.That(hostUnbindResult, Is.False);
                        Assert.That(sceneBindResult, Is.False);
                        Assert.That(sceneReplaceResult, Is.False);
                        Assert.That(sceneRemoveResult, Is.False);
                        Assert.That(bridgeBindResult, Is.False);
                        Assert.That(bridgeUnbindResult, Is.False);
                        Assert.That(rawBridgeBindResult, Is.False);
                        Assert.That(rawBridgeUnbindResult, Is.False);

                        Assert.That(OrpheusAudioBridge.Unbind(manager), Is.True);
                        Assert.That(OrpheusAudioRawBridge.Unbind(manager), Is.True);
                        manager.Dispose();
                        Assert.That(fixture.Host.Unbind(manager), Is.True);
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(bindingObject);
                        UnityEngine.Object.DestroyImmediate(replacementObject);
                        UnityEngine.Object.DestroyImmediate(listenerObject);
                    }
                }
            });
        }

        [Test]
        public void Ticket20_WrongThreadPublicFactory_HasNoSessionCounter()
        {
            OrpheusAudioInitResult result = default;
            OrpheusAudioManager manager = null;
            var worker = new Thread(() =>
            {
                result = OrpheusAudioFactory.Create(
                    null,
                    null,
                    null,
                    default,
                    out manager);
            });
            worker.Start();
            worker.Join();

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidArguments));
            Assert.That(manager, Is.Null);
        }

        [Test]
        public void Ticket20_WrongThreadCounter_IsAtomicAcrossConcurrentPublicCalls()
        {
            const int WorkerCount = 8;
            const int CallsPerWorker = 1024;

            using (var fixture = new SessionFixture())
            {
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                var workers = new Thread[WorkerCount];
                for (var workerIndex = 0; workerIndex < workers.Length; workerIndex++)
                {
                    workers[workerIndex] = new Thread(() =>
                    {
                        for (var call = 0; call < CallsPerWorker; call++)
                        {
                            manager.TryGetDiagnostics(out _);
                        }
                    });
                    workers[workerIndex].Start();
                }

                for (var workerIndex = 0; workerIndex < workers.Length; workerIndex++)
                {
                    workers[workerIndex].Join();
                }

                Assert.That(manager.TryGetDiagnostics(out var after), Is.True);
                AssertDiagnosticsEqualExceptWrongThread(before, after);
                Assert.That(
                    after.Counters.WrongThreadRejected,
                    Is.EqualTo((ulong)(WorkerCount * CallsPerWorker)));
                manager.Dispose();
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void Ticket20_AudioConfigurationCallback_IsMainThreadDeferredAndCoalesced()
        {
            using (var fixture = new SessionFixture())
            {
                var manager = fixture.CreateReadyManager(out _);
                Assert.That(manager.TryGetDiagnostics(out var before), Is.True);
                Assert.That(
                    before.RecoveryPendingReasons,
                    Is.EqualTo(OrpheusRecoveryPendingReason.None));

                fixture.Host.RecordAudioConfigurationChanged(false);
                fixture.Host.RecordAudioConfigurationChanged(true);
                fixture.Host.RecordAudioConfigurationChanged(true);

                Assert.That(manager.TryGetDiagnostics(out var deferred), Is.True);
                Assert.That(
                    deferred.RecoveryPendingReasons,
                    Is.EqualTo(OrpheusRecoveryPendingReason.ExternalConfiguration));
                Assert.That(fixture.AudioSystem.ResetCallCount, Is.Zero);

                fixture.Host.ConsumePendingAudioConfigurationChanges();

                Assert.That(manager.TryGetDiagnostics(out var consumed), Is.True);
                Assert.That(
                    consumed.RecoveryPendingReasons,
                    Is.EqualTo(OrpheusRecoveryPendingReason.None));
                Assert.That(fixture.AudioSystem.ResetCallCount, Is.Zero);
                fixture.Host.ConsumePendingAudioConfigurationChanges();
                Assert.That(manager.TryGetDiagnostics(out var duplicateConsume), Is.True);
                Assert.That(
                    duplicateConsume.RecoveryPendingReasons,
                    Is.EqualTo(OrpheusRecoveryPendingReason.None));
                Assert.That(fixture.AudioSystem.ResetCallCount, Is.Zero);

                DisposeBound(fixture, manager);
            }
        }

        private static Ticket20WrongThreadCall WrongThreadCall(string name, Action invoke)
        {
            return new Ticket20WrongThreadCall(name, invoke);
        }

        private static void AssertTicket20WrongThreadCall(
            SessionFixture fixture,
            OrpheusAudioManager manager,
            Ticket20WrongThreadCall call)
        {
            Assert.That(manager.TryGetDiagnostics(out var before), Is.True, call.Name);
            var sourceStates = CaptureTicket20OwnedSources(fixture.Sources);
            var portState = new Ticket20PortState(fixture);
            Exception workerException = null;
            var worker = new Thread(() =>
            {
                try
                {
                    call.Invoke();
                }
                catch (Exception exception)
                {
                    workerException = exception;
                }
            });

            worker.Start();
            worker.Join();

            Assert.That(workerException, Is.Null, call.Name);
            Assert.That(manager.TryGetDiagnostics(out var after), Is.True, call.Name);
            AssertDiagnosticsEqualExceptCounter(
                before,
                after,
                nameof(OrpheusAudioDiagnosticsCounters.WrongThreadRejected),
                1);
            AssertTicket20OwnedSources(sourceStates, fixture.Sources, call.Name);
            portState.AssertMatches(fixture, call.Name);
        }

        private static Ticket20OwnedSourceState[] CaptureTicket20OwnedSources(
            AudioSource[] sources)
        {
            var states = new Ticket20OwnedSourceState[sources.Length];
            for (var index = 0; index < sources.Length; index++)
            {
                states[index] = new Ticket20OwnedSourceState(sources[index]);
            }

            return states;
        }

        private static void AssertTicket20OwnedSources(
            Ticket20OwnedSourceState[] expected,
            AudioSource[] actual,
            string message)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length), message);
            for (var index = 0; index < actual.Length; index++)
            {
                expected[index].AssertMatches(
                    actual[index],
                    message + ":owned-source:" + index);
            }
        }

        private readonly struct Ticket20WrongThreadCall
        {
            internal Ticket20WrongThreadCall(string name, Action invoke)
            {
                Name = name;
                Invoke = invoke;
            }

            internal string Name { get; }
            internal Action Invoke { get; }
        }

        private readonly struct Ticket20OwnedSourceState
        {
            private readonly AudioClip _clip;
            private readonly int _timeSamples;
            private readonly bool _isPlaying;
            private readonly bool _playOnAwake;
            private readonly bool _loop;
            private readonly bool _mute;
            private readonly float _volume;
            private readonly float _pitch;
            private readonly int _priority;
            private readonly float _spatialBlend;
            private readonly float _panStereo;
            private readonly float _spread;
            private readonly float _dopplerLevel;
            private readonly float _reverbZoneMix;
            private readonly float _minDistance;
            private readonly float _maxDistance;
            private readonly AudioRolloffMode _rolloffMode;
            private readonly bool _bypassEffects;
            private readonly bool _bypassListenerEffects;
            private readonly bool _bypassReverbZones;
            private readonly bool _ignoreListenerPause;
            private readonly bool _ignoreListenerVolume;
            private readonly bool _spatialize;
            private readonly bool _spatializePostEffects;
            private readonly AudioVelocityUpdateMode _velocityUpdateMode;
            private readonly UnityEngine.Audio.AudioMixerGroup _route;
            private readonly Vector3 _position;

            internal Ticket20OwnedSourceState(AudioSource source)
            {
                _clip = source.clip;
                _timeSamples = source.timeSamples;
                _isPlaying = source.isPlaying;
                _playOnAwake = source.playOnAwake;
                _loop = source.loop;
                _mute = source.mute;
                _volume = source.volume;
                _pitch = source.pitch;
                _priority = source.priority;
                _spatialBlend = source.spatialBlend;
                _panStereo = source.panStereo;
                _spread = source.spread;
                _dopplerLevel = source.dopplerLevel;
                _reverbZoneMix = source.reverbZoneMix;
                _minDistance = source.minDistance;
                _maxDistance = source.maxDistance;
                _rolloffMode = source.rolloffMode;
                _bypassEffects = source.bypassEffects;
                _bypassListenerEffects = source.bypassListenerEffects;
                _bypassReverbZones = source.bypassReverbZones;
                _ignoreListenerPause = source.ignoreListenerPause;
                _ignoreListenerVolume = source.ignoreListenerVolume;
                _spatialize = source.spatialize;
                _spatializePostEffects = source.spatializePostEffects;
                _velocityUpdateMode = source.velocityUpdateMode;
                _route = source.outputAudioMixerGroup;
                _position = source.transform.position;
            }

            internal void AssertMatches(AudioSource source, string message)
            {
                Assert.That(source.clip, Is.SameAs(_clip), message + ":clip");
                Assert.That(source.timeSamples, Is.EqualTo(_timeSamples), message + ":time");
                Assert.That(source.isPlaying, Is.EqualTo(_isPlaying), message + ":playing");
                Assert.That(source.playOnAwake, Is.EqualTo(_playOnAwake), message + ":awake");
                Assert.That(source.loop, Is.EqualTo(_loop), message + ":loop");
                Assert.That(source.mute, Is.EqualTo(_mute), message + ":mute");
                Assert.That(source.volume, Is.EqualTo(_volume), message + ":volume");
                Assert.That(source.pitch, Is.EqualTo(_pitch), message + ":pitch");
                Assert.That(source.priority, Is.EqualTo(_priority), message + ":priority");
                Assert.That(source.spatialBlend, Is.EqualTo(_spatialBlend), message + ":blend");
                Assert.That(source.panStereo, Is.EqualTo(_panStereo), message + ":pan");
                Assert.That(source.spread, Is.EqualTo(_spread), message + ":spread");
                Assert.That(source.dopplerLevel, Is.EqualTo(_dopplerLevel), message + ":doppler");
                Assert.That(
                    source.reverbZoneMix,
                    Is.EqualTo(_reverbZoneMix),
                    message + ":reverb");
                Assert.That(source.minDistance, Is.EqualTo(_minDistance), message + ":min-distance");
                Assert.That(source.maxDistance, Is.EqualTo(_maxDistance), message + ":max-distance");
                Assert.That(source.rolloffMode, Is.EqualTo(_rolloffMode), message + ":rolloff");
                Assert.That(source.bypassEffects, Is.EqualTo(_bypassEffects), message + ":effects");
                Assert.That(
                    source.bypassListenerEffects,
                    Is.EqualTo(_bypassListenerEffects),
                    message + ":listener-effects");
                Assert.That(
                    source.bypassReverbZones,
                    Is.EqualTo(_bypassReverbZones),
                    message + ":reverb-zones");
                Assert.That(
                    source.ignoreListenerPause,
                    Is.EqualTo(_ignoreListenerPause),
                    message + ":listener-pause");
                Assert.That(
                    source.ignoreListenerVolume,
                    Is.EqualTo(_ignoreListenerVolume),
                    message + ":listener-volume");
                Assert.That(source.spatialize, Is.EqualTo(_spatialize), message + ":spatialize");
                Assert.That(
                    source.spatializePostEffects,
                    Is.EqualTo(_spatializePostEffects),
                    message + ":spatialize-post");
                Assert.That(
                    source.velocityUpdateMode,
                    Is.EqualTo(_velocityUpdateMode),
                    message + ":velocity-mode");
                Assert.That(source.outputAudioMixerGroup, Is.SameAs(_route), message + ":route");
                Assert.That(source.transform.position, Is.EqualTo(_position), message + ":position");
            }
        }

        private readonly struct Ticket20PortState
        {
            private readonly int _mixerParameterCount;
            private readonly int _mixerDecibelCount;
            private readonly int _mixerSnapshotCount;
            private readonly int _mixerTransitionCount;
            private readonly int _mixerOperationCount;
            private readonly int _readinessGetCount;
            private readonly int _readinessRequestCount;
            private readonly int _audioConfigurationGetCount;
            private readonly int _audioResetCount;

            internal Ticket20PortState(SessionFixture fixture)
            {
                _mixerParameterCount = fixture.Mixer.ParameterNames.Count;
                _mixerDecibelCount = fixture.Mixer.Decibels.Count;
                _mixerSnapshotCount = fixture.Mixer.Snapshots.Count;
                _mixerTransitionCount = fixture.Mixer.TransitionSeconds.Count;
                _mixerOperationCount = fixture.Mixer.OperationOrder.Count;
                _readinessGetCount = fixture.Readiness.GetCallCount;
                _readinessRequestCount = fixture.Readiness.RequestCallCount;
                _audioConfigurationGetCount = fixture.AudioSystem.GetConfigurationCallCount;
                _audioResetCount = fixture.AudioSystem.ResetCallCount;
            }

            internal void AssertMatches(SessionFixture fixture, string message)
            {
                Assert.That(fixture.Mixer.ParameterNames, Has.Count.EqualTo(_mixerParameterCount),
                    message + ":mixer-parameters");
                Assert.That(fixture.Mixer.Decibels, Has.Count.EqualTo(_mixerDecibelCount),
                    message + ":mixer-decibels");
                Assert.That(fixture.Mixer.Snapshots, Has.Count.EqualTo(_mixerSnapshotCount),
                    message + ":mixer-snapshots");
                Assert.That(
                    fixture.Mixer.TransitionSeconds,
                    Has.Count.EqualTo(_mixerTransitionCount),
                    message + ":mixer-transitions");
                Assert.That(
                    fixture.Mixer.OperationOrder,
                    Has.Count.EqualTo(_mixerOperationCount),
                    message + ":mixer-operation-order");
                Assert.That(fixture.Readiness.GetCallCount, Is.EqualTo(_readinessGetCount),
                    message + ":readiness-get");
                Assert.That(
                    fixture.Readiness.RequestCallCount,
                    Is.EqualTo(_readinessRequestCount),
                    message + ":readiness-request");
                Assert.That(
                    fixture.AudioSystem.GetConfigurationCallCount,
                    Is.EqualTo(_audioConfigurationGetCount),
                    message + ":audio-configuration-get");
                Assert.That(fixture.AudioSystem.ResetCallCount, Is.EqualTo(_audioResetCount),
                    message + ":audio-reset");
            }
        }
    }
}
