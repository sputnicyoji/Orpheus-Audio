using System;
using System.Collections.Generic;
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
        public void CreateAndDispose_NormalizesAndReleasesUnboundSession()
        {
            using (var fixture = new SessionFixture())
            {
                var dirtyClip = AudioClip.Create("DirtySource", 4800, 1, 48000, false);
                fixture.DirtyAllSources(dirtyClip);

                var result = fixture.Create(out var manager);

                Assert.That(result.Success, Is.True);
                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.None));
                Assert.That(manager, Is.Not.Null);
                for (var sourceIndex = 0; sourceIndex < fixture.Sources.Length; sourceIndex++)
                {
                    AssertNormalized(fixture.Sources[sourceIndex]);
                }
                Assert.That(fixture.AudioSystem.GetConfigurationCallCount, Is.EqualTo(1));
                Assert.That(manager.TryGetDiagnostics(out var running), Is.True);
                Assert.That(running.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
                Assert.That(running.TransportState, Is.EqualTo(OrpheusTransportState.Suspended));
                Assert.That(running.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.ListenerMissing));
                Assert.That(running.ConfiguredRealVoiceLimit, Is.EqualTo(32));
                Assert.That(running.ConfiguredVirtualVoiceLimit, Is.EqualTo(64));
                AssertInitialOrTerminalSentinels(running, OrpheusAudioLifecycle.Running);

                fixture.DirtyAllSources(dirtyClip);
                manager.Dispose();

                Assert.That(manager.TryGetDiagnostics(out var disposed), Is.True);
                Assert.That(disposed.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disposed));
                Assert.That(disposed.TransportState, Is.EqualTo(OrpheusTransportState.Disposed));
                Assert.That(disposed.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.None));
                AssertInitialOrTerminalSentinels(disposed, OrpheusAudioLifecycle.Disposed);
                for (var sourceIndex = 0; sourceIndex < fixture.Sources.Length; sourceIndex++)
                {
                    AssertNormalized(fixture.Sources[sourceIndex]);
                }

                var secondResult = fixture.Create(out var secondManager);
                Assert.That(secondResult.Success, Is.True);
                secondManager.Dispose();
                UnityEngine.Object.DestroyImmediate(dirtyClip);
            }
        }

        [Test]
        public void TwoManagers_WithDistinctResourcesRemainRunningTogether()
        {
            using (var first = new SessionFixture(new object()))
            using (var second = new SessionFixture(new object()))
            {
                Assert.That(first.Create(out var firstManager).Success, Is.True);
                Assert.That(second.Create(out var secondManager).Success, Is.True);
                Assert.That(firstManager.TryGetDiagnostics(out var firstDiagnostics), Is.True);
                Assert.That(secondManager.TryGetDiagnostics(out var secondDiagnostics), Is.True);
                Assert.That(firstDiagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
                Assert.That(secondDiagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
                firstManager.Dispose();
                secondManager.Dispose();
            }
        }

        [Test]
        public void BoundSession_HoldsLeasesUntilIdentitySafeUnbind()
        {
            using (var fixture = new SessionFixture())
            {
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);

                manager.Dispose();
                Assert.That(fixture.Create(out var blocked).Success, Is.False);
                Assert.That(blocked, Is.Null);

                Assert.That(fixture.Host.Unbind(manager), Is.True);
                Assert.That(fixture.Host.Unbind(manager), Is.True);
                Assert.That(fixture.Create(out var replacement).Success, Is.True);
                replacement.Dispose();
            }
        }

        [Test]
        public void BoundUnavailableManager_RemainsIdempotentlyBoundUntilUnbind()
        {
            using (var fixture = new SessionFixture())
            {
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                manager.FailClosed(OrpheusAudioDisableReason.MixerLeaseLost);
                Assert.That(fixture.Host.Bind(manager), Is.True);
                manager.Dispose();
                Assert.That(fixture.Host.Bind(manager), Is.True);
                Assert.That(fixture.Host.Unbind(manager), Is.True);
            }
        }

        [Test]
        public void ReservedRuntimeHost_PrecedesInvalidGainFailure()
        {
            using (var fixture = new SessionFixture())
            {
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                var invalidGains = new OrpheusAudioUserGains(float.NaN, 1f, 1f, 1f, 1f, 1f);
                var result = fixture.CreateWithGains(invalidGains, out var rejectedManager);
                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidArguments));
                Assert.That(rejectedManager, Is.Null);
                manager.Dispose();
            }
        }

        [Test]
        public void SharedMixer_IsRejectedWithoutStrandingOtherResources()
        {
            var mixerIdentity = new object();
            using (var first = new SessionFixture(mixerIdentity))
            using (var second = new SessionFixture(mixerIdentity))
            {
                Assert.That(first.Create(out var firstManager).Success, Is.True);
                second.AudioSystem.Configuration = new OrpheusAudioSystemConfiguration(0, 0, 0, 0);

                var blocked = second.Create(out var blockedManager);
                Assert.That(blocked.Success, Is.False);
                Assert.That(blocked.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidMixerContract));
                Assert.That(blockedManager, Is.Null);
                Assert.That(second.AudioSystem.GetConfigurationCallCount, Is.Zero);

                firstManager.Dispose();
                second.AudioSystem.Configuration = new OrpheusAudioSystemConfiguration(1024, 48000, 32, 64);
                Assert.That(second.Create(out var secondManager).Success, Is.True);
                secondManager.Dispose();
            }
        }

        [Test]
        public void SharedSourceBank_IsRejectedWithoutAcquiringDistinctMixer()
        {
            using (var first = new SessionFixture(new object()))
            using (var second = new SessionFixture(new object()))
            {
                second.SetSourceBank(first.Host.SourceBank);
                Assert.That(first.Create(out var firstManager).Success, Is.True);
                second.AudioSystem.Configuration = new OrpheusAudioSystemConfiguration(0, 0, 0, 0);

                var blocked = second.Create(out var blockedManager);
                Assert.That(blocked.Success, Is.False);
                Assert.That(blocked.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidSourceBank));
                Assert.That(blockedManager, Is.Null);
                Assert.That(second.AudioSystem.GetConfigurationCallCount, Is.Zero);

                firstManager.Dispose();
                second.AudioSystem.Configuration = new OrpheusAudioSystemConfiguration(1024, 48000, 32, 64);
                Assert.That(second.Create(out var secondManager).Success, Is.True);
                secondManager.Dispose();
            }
        }

        [Test]
        public void DistinctBanksWithOneOverlappingPhysicalSource_AreRejected()
        {
            using (var first = new SessionFixture(new object()))
            using (var second = new SessionFixture(new object()))
            {
                second.OverlapFirst3DSource(first.Sources[0]);
                Assert.That(first.Create(out var firstManager).Success, Is.True);
                second.AudioSystem.Configuration = new OrpheusAudioSystemConfiguration(0, 0, 0, 0);

                var blocked = second.Create(out var blockedManager);
                Assert.That(blocked.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidSourceBank));
                Assert.That(blockedManager, Is.Null);
                Assert.That(second.AudioSystem.GetConfigurationCallCount, Is.Zero);

                firstManager.Dispose();
                second.AudioSystem.Configuration = new OrpheusAudioSystemConfiguration(1024, 48000, 32, 64);
                Assert.That(second.Create(out var secondManager).Success, Is.True);
                secondManager.Dispose();
            }
        }

        [Test]
        public void DuplicateSource_FailsWithoutManager()
        {
            using (var duplicate = new SessionFixture())
            {
                duplicate.SetDuplicateSource();
                var result = duplicate.Create(out var manager);
                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidSourceBank));
                Assert.That(manager, Is.Null);
            }

        }

        [TestCase(0, 48000, 32, 64)]
        [TestCase(1024, 0, 32, 64)]
        [TestCase(1024, 48000, 0, 64)]
        [TestCase(1024, 48000, 32, 31)]
        public void InvalidAudioConfiguration_FailsAfterExactlyOneRead(
            int dspBufferSize,
            int sampleRate,
            int realVoices,
            int virtualVoices)
        {
            using (var fixture = new SessionFixture())
            {
                fixture.AudioSystem.Configuration =
                    new OrpheusAudioSystemConfiguration(dspBufferSize, sampleRate, realVoices, virtualVoices);
                var result = fixture.Create(out var manager);
                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidAudioConfiguration));
                Assert.That(manager, Is.Null);
                Assert.That(fixture.AudioSystem.GetConfigurationCallCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void FirstErrorOrder_SourceBankPrecedesNullCatalog()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetDuplicateSource();
                var result = fixture.CreateWithCatalog(null, out var manager);
                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidSourceBank));
                Assert.That(manager, Is.Null);
            }
        }

        [Test]
        public void InvalidSettingsSchemaPrecedesCatalogAndAudioConfiguration()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.SetSettingsSchemaVersion(0);
                fixture.SetCatalogSchemaVersion(0);
                fixture.AudioSystem.Configuration = new OrpheusAudioSystemConfiguration(0, 0, 0, 0);

                var result = fixture.Create(out var manager);

                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidSettings));
                Assert.That(manager, Is.Null);
                Assert.That(fixture.AudioSystem.GetConfigurationCallCount, Is.Zero);
            }
        }

        [Test]
        public void InvalidCatalogSchemaPrecedesDuplicateAndInvalidEvent()
        {
            using (var fixture = new SessionFixture())
            {
                var firstEvent = CreateEvent(200, null);
                var secondEvent = CreateEvent(200, null);
                fixture.SetEvents(firstEvent, secondEvent);
                fixture.SetCatalogSchemaVersion(0);

                var result = fixture.Create(out var manager);

                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidCatalog));
                Assert.That(manager, Is.Null);
                UnityEngine.Object.DestroyImmediate(firstEvent);
                UnityEngine.Object.DestroyImmediate(secondEvent);
            }
        }

        [Test]
        public void InvalidEventPolicyMapsToInvalidEventBeforeMissingClip()
        {
            using (var fixture = new SessionFixture())
            {
                var audioEvent = CreateEvent(200, null);
                SetField(audioEvent, "_category", OrpheusCategory.Music);
                SetField(audioEvent, "_playbackKind", OrpheusPlaybackKind.OneShot3D);
                fixture.SetEvents(audioEvent);

                var result = fixture.Create(out var manager);

                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidEvent));
                Assert.That(result.RelatedKey, Is.EqualTo(new OrpheusAudioKey(200)));
                Assert.That(result.RelatedIndex, Is.Zero);
                Assert.That(manager, Is.Null);
                UnityEngine.Object.DestroyImmediate(audioEvent);
            }
        }

        [Test]
        public void RepeatedNullClipsRemainInvalidClipReferenceNotDuplicateClip()
        {
            using (var fixture = new SessionFixture())
            {
                var audioEvent = CreateEventWithClips(200, null, null);
                fixture.SetEvents(audioEvent);

                var result = fixture.Create(out var manager);

                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidClipReference));
                Assert.That(result.RelatedKey, Is.EqualTo(new OrpheusAudioKey(200)));
                Assert.That(result.RelatedIndex, Is.Zero);
                Assert.That(manager, Is.Null);
                UnityEngine.Object.DestroyImmediate(audioEvent);
            }
        }

        [Test]
        public void CatalogOrderComparesValidKeysAcrossNullEntries()
        {
            using (var fixture = new SessionFixture())
            {
                var clip = AudioClip.Create("SeparatedOrder", 32, 1, 8000, false);
                var firstEvent = CreateEvent(200, clip);
                var lastEvent = CreateEvent(100, clip);
                fixture.SetEvents(firstEvent, null, lastEvent);

                var result = fixture.Create(out var manager);

                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidCatalog));
                Assert.That(result.RelatedKey, Is.EqualTo(new OrpheusAudioKey(100)));
                Assert.That(result.RelatedIndex, Is.EqualTo(2));
                Assert.That(manager, Is.Null);
                UnityEngine.Object.DestroyImmediate(firstEvent);
                UnityEngine.Object.DestroyImmediate(lastEvent);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void DuplicateKeyPrecedesInvalidClipAndReportsSecondAuthoredIndex()
        {
            using (var fixture = new SessionFixture())
            {
                var clip = AudioClip.Create("DuplicateFixture", 32, 1, 8000, false);
                var firstEvent = CreateEvent(200, clip);
                var secondEvent = CreateEvent(200, null);
                fixture.SetEvents(firstEvent, secondEvent);

                var result = fixture.Create(out var manager);

                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.DuplicateKey));
                Assert.That(result.RelatedKey, Is.EqualTo(new OrpheusAudioKey(200)));
                Assert.That(result.RelatedIndex, Is.EqualTo(1));
                Assert.That(manager, Is.Null);
                UnityEngine.Object.DestroyImmediate(firstEvent);
                UnityEngine.Object.DestroyImmediate(secondEvent);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void InvalidCatalogOrderPrecedesLaterDuplicateKey()
        {
            using (var fixture = new SessionFixture())
            {
                var clip = AudioClip.Create("CatalogOrderFixture", 32, 1, 8000, false);
                var firstEvent = CreateEvent(200, clip);
                var secondEvent = CreateEvent(100, clip);
                var thirdEvent = CreateEvent(100, clip);
                fixture.SetEvents(firstEvent, secondEvent, thirdEvent);

                var result = fixture.Create(out var manager);

                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidCatalog));
                Assert.That(result.RelatedKey, Is.EqualTo(new OrpheusAudioKey(100)));
                Assert.That(result.RelatedIndex, Is.EqualTo(1));
                Assert.That(manager, Is.Null);
                UnityEngine.Object.DestroyImmediate(firstEvent);
                UnityEngine.Object.DestroyImmediate(secondEvent);
                UnityEngine.Object.DestroyImmediate(thirdEvent);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void InvalidEventPrecedesEarlierInvalidClipReference()
        {
            using (var fixture = new SessionFixture())
            {
                var clip = AudioClip.Create("EventOrderFixture", 32, 1, 8000, false);
                var nullClipEvent = CreateEvent(200, null);
                var duplicateClipEvent = CreateEventWithClips(201, clip, clip);
                fixture.SetEvents(nullClipEvent, duplicateClipEvent);

                var result = fixture.Create(out var manager);

                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidEvent));
                Assert.That(result.RelatedKey, Is.EqualTo(new OrpheusAudioKey(201)));
                Assert.That(result.RelatedIndex, Is.EqualTo(1));
                Assert.That(manager, Is.Null);
                UnityEngine.Object.DestroyImmediate(nullClipEvent);
                UnityEngine.Object.DestroyImmediate(duplicateClipEvent);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void RuntimeCatalogSnapshot_CopiesFlattenedClipAlignmentAndRetainsCatalog()
        {
            using (var fixture = new SessionFixture())
            {
                var authoredClip = AudioClip.Create("Authored", 32, 1, 8000, false);
                var replacementClip = AudioClip.Create("Replacement", 32, 1, 8000, false);
                var audioEvent = CreateEvent(201, authoredClip);
                fixture.SetEvents(audioEvent);
                Assert.That(fixture.Create(out var manager).Success, Is.True);

                var snapshot = (OrpheusRuntimeCatalogSnapshot)GetField(manager, "_catalogSnapshot");
                Assert.That(snapshot.Catalog, Is.SameAs(fixture.Catalog));
                Assert.That(snapshot.FlattenedClipCount, Is.EqualTo(1));
                Assert.That(snapshot.TryGetEntry(new OrpheusAudioKey(201), out var entry), Is.True);
                Assert.That(entry.ClipRange.Offset, Is.Zero);
                Assert.That(entry.ClipRange.Count, Is.EqualTo(1));
                Assert.That(snapshot.GetClip(0), Is.SameAs(authoredClip));

                SetField(audioEvent, "_clips", new[] { replacementClip });
                Assert.That(snapshot.GetClip(0), Is.SameAs(authoredClip));

                manager.Dispose();
                UnityEngine.Object.DestroyImmediate(audioEvent);
                UnityEngine.Object.DestroyImmediate(authoredClip);
                UnityEngine.Object.DestroyImmediate(replacementClip);
            }
        }

        [Test]
        public void DegradedVoiceBudget_IsCapturedWithoutFailingCreation()
        {
            using (var fixture = new SessionFixture())
            {
                fixture.AudioSystem.Configuration =
                    new OrpheusAudioSystemConfiguration(1024, 48000, 16, 32);

                Assert.That(fixture.Create(out var manager).Success, Is.True);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.ConfiguredRealVoiceLimit, Is.EqualTo(16));
                Assert.That(diagnostics.ConfiguredVirtualVoiceLimit, Is.EqualTo(32));
                Assert.That(diagnostics.IsVoiceBudgetDegraded, Is.True);
                manager.Dispose();
            }
        }

        [Test]
        public void GenericFailClose_MarksDisabledBeforeCleanupAndRetainsReasonThroughDispose()
        {
            using (var fixture = new SessionFixture())
            {
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                fixture.Sources[23].loop = true;
                fixture.Sources[23].volume = 1f;

                manager.FailClosed(OrpheusAudioDisableReason.MixerLeaseLost);

                Assert.That(manager.TryGetDiagnostics(out var disabled), Is.True);
                Assert.That(disabled.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(disabled.TransportState, Is.EqualTo(OrpheusTransportState.Invalid));
                Assert.That(disabled.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.MixerLeaseLost));
                AssertInitialOrTerminalSentinels(disabled, OrpheusAudioLifecycle.Disabled);
                AssertNormalized(fixture.Sources[23]);

                fixture.Sources[23].loop = true;
                fixture.Sources[23].volume = 0.37f;
                manager.Dispose();
                Assert.That(manager.TryGetDiagnostics(out var disposed), Is.True);
                Assert.That(disposed.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disposed));
                Assert.That(disposed.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.MixerLeaseLost));
                Assert.That(disposed.Counters.UnexpectedException,
                    Is.EqualTo(disabled.Counters.UnexpectedException));
                AssertNormalized(fixture.Sources[23]);
                AssertInitialOrTerminalSentinels(disposed, OrpheusAudioLifecycle.Disposed);
            }
        }

        [Test]
        public void UnknownDisableReason_IsClassifiedAsUnexpectedRuntimeException()
        {
            using (var fixture = new SessionFixture())
            {
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                manager.FailClosed((OrpheusAudioDisableReason)255);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.UnexpectedRuntimeException));
                Assert.That(diagnostics.Counters.UnexpectedException, Is.EqualTo(1));
                manager.Dispose();
            }
        }

        [Test]
        public void RuntimeHostDestruction_ReleasesBothLeasesAndLaterDisposeTouchesNoSource()
        {
            var mixerIdentity = new object();
            using (var first = new SessionFixture(mixerIdentity, true))
            using (var replacement = new SessionFixture(mixerIdentity))
            {
                replacement.SetSourceBank(first.Bank);
                Assert.That(first.Create(out var oldManager).Success, Is.True);
                Assert.That(first.Host.Bind(oldManager), Is.True);
                Assert.That(OrpheusAudioBridge.Bind(oldManager), Is.True);
                Assert.That(OrpheusAudioRawBridge.Bind(oldManager), Is.True);
                var dirtyClip = AudioClip.Create("HostDestroyedDirty", 4800, 1, 48000, false);
                first.DirtyAllSources(dirtyClip);
                first.DestroyHostOnly();
                Assert.That(oldManager.TryGetDiagnostics(out var disabled), Is.True);
                Assert.That(disabled.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disabled));
                Assert.That(disabled.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.RuntimeHostDestroyed));
                for (var sourceIndex = 0; sourceIndex < first.Sources.Length; sourceIndex++)
                {
                    AssertNormalized(first.Sources[sourceIndex]);
                }
                Assert.That(OrpheusAudioBridge.Unbind(oldManager), Is.False);
                Assert.That(OrpheusAudioRawBridge.Unbind(oldManager), Is.False);

                Assert.That(replacement.Create(out var replacementManager).Success, Is.True);
                first.Sources[0].volume = 0.37f;
                Assert.That(() => oldManager.Dispose(), Throws.Nothing);
                Assert.That(oldManager.TryGetDiagnostics(out var disposed), Is.True);
                Assert.That(disposed.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Disposed));
                Assert.That(disposed.DisableReason, Is.EqualTo(OrpheusAudioDisableReason.RuntimeHostDestroyed));
                Assert.That(first.Sources[0].volume, Is.EqualTo(0.37f));
                replacementManager.Dispose();
                UnityEngine.Object.DestroyImmediate(dirtyClip);
            }
        }

        [Test]
        public void EstablishedManager_WrongThreadCallsOnlyIncrementCounter()
        {
            using (var fixture = new SessionFixture())
            {
                Assert.That(fixture.Create(out var manager).Success, Is.True);
                var diagnosticsResult = true;
                var thread = new Thread(() =>
                {
                    diagnosticsResult = manager.TryGetDiagnostics(out _);
                    manager.Dispose();
                });
                thread.Start();
                thread.Join();

                Assert.That(diagnosticsResult, Is.False);
                Assert.That(manager.TryGetDiagnostics(out var diagnostics), Is.True);
                Assert.That(diagnostics.Lifecycle, Is.EqualTo(OrpheusAudioLifecycle.Running));
                Assert.That(diagnostics.Counters.WrongThreadRejected, Is.EqualTo(2));
                manager.Dispose();
            }
        }

        [Test]
        public void WrongThreadPublicFactory_ReturnsInvalidArgumentsBeforeUnityAccess()
        {
            OrpheusAudioInitResult result = default;
            OrpheusAudioManager manager = null;
            var thread = new Thread(() =>
            {
                result = OrpheusAudioFactory.Create(null, null, null, default, out manager);
            });
            thread.Start();
            thread.Join();

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidArguments));
            Assert.That(manager, Is.Null);
        }

        [Test]
        public void WorkerSynchronizationContext_CannotClaimUnityMainThread()
        {
            using (var fixture = new SessionFixture())
            {
                var mainThreadField = typeof(OrpheusMainThread)
                    .GetField("_mainThreadId", BindingFlags.Static | BindingFlags.NonPublic);
                mainThreadField.SetValue(null, 0);
                OrpheusAudioInitResult result = default;
                OrpheusAudioManager manager = null;
                var thread = new Thread(() =>
                {
                    SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                    result = fixture.Create(out manager);
                });

                try
                {
                    thread.Start();
                    thread.Join();
                }
                finally
                {
                    mainThreadField.SetValue(null, Thread.CurrentThread.ManagedThreadId);
                }

                Assert.That(result.ErrorCode, Is.EqualTo(OrpheusAudioInitErrorCode.InvalidArguments));
                Assert.That(manager, Is.Null);
            }
        }

        private static void AssertNormalized(AudioSource source)
        {
            Assert.That(source.clip, Is.Null);
            Assert.That(source.time, Is.EqualTo(0f));
            Assert.That(source.playOnAwake, Is.False);
            Assert.That(source.loop, Is.False);
            Assert.That(source.mute, Is.False);
            Assert.That(source.volume, Is.EqualTo(0f));
            Assert.That(source.pitch, Is.EqualTo(1f));
            Assert.That(source.priority, Is.EqualTo(128));
            Assert.That(source.outputAudioMixerGroup, Is.Null);
            Assert.That(source.spatialBlend, Is.EqualTo(0f));
            Assert.That(source.panStereo, Is.EqualTo(0f));
            Assert.That(source.spread, Is.EqualTo(0f));
            Assert.That(source.dopplerLevel, Is.EqualTo(0f));
            Assert.That(source.reverbZoneMix, Is.EqualTo(1f));
            Assert.That(source.minDistance, Is.EqualTo(1f));
            Assert.That(source.maxDistance, Is.EqualTo(500f));
            Assert.That(source.rolloffMode, Is.EqualTo(AudioRolloffMode.Logarithmic));
            Assert.That(source.bypassEffects, Is.False);
            Assert.That(source.bypassListenerEffects, Is.False);
            Assert.That(source.bypassReverbZones, Is.False);
            Assert.That(source.ignoreListenerPause, Is.False);
            Assert.That(source.ignoreListenerVolume, Is.False);
            Assert.That(source.spatialize, Is.False);
            Assert.That(source.spatializePostEffects, Is.False);
            Assert.That(source.velocityUpdateMode, Is.EqualTo(AudioVelocityUpdateMode.Fixed));
        }

        private static void AssertInitialOrTerminalSentinels(
            OrpheusAudioDiagnostics diagnostics,
            OrpheusAudioLifecycle expectedLifecycle)
        {
            Assert.That(diagnostics.SchemaVersion, Is.EqualTo(1));
            Assert.That(diagnostics.Lifecycle, Is.EqualTo(expectedLifecycle));
            Assert.That(diagnostics.ProfileId, Is.Zero);
            Assert.That(diagnostics.BaseState, Is.EqualTo(OrpheusBaseState.Peace));
            Assert.That(diagnostics.Overlay, Is.EqualTo(OrpheusOverlay.None));
            Assert.That(diagnostics.EffectiveSnapshot, Is.EqualTo(OrpheusEffectiveSnapshot.Peace));
            Assert.That(diagnostics.DesiredBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.CurrentBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.TargetBgmKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.DesiredProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.CurrentProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.TargetProfileAmbienceKey, Is.EqualTo(OrpheusAudioKey.Invalid));
            Assert.That(diagnostics.Readiness, Is.EqualTo(OrpheusReadiness.None));
            Assert.That(diagnostics.RecoveryPendingReasons, Is.EqualTo(OrpheusRecoveryPendingReason.None));
            if (expectedLifecycle != OrpheusAudioLifecycle.Running)
            {
                Assert.That(diagnostics.SuspensionReasons, Is.EqualTo(OrpheusSuspensionReason.None));
            }

            Assert.That(diagnostics.Transient3DActiveCount, Is.Zero);
            Assert.That(diagnostics.Transient2DActiveCount, Is.Zero);
            Assert.That(diagnostics.FadingCount, Is.Zero);
            Assert.That(diagnostics.PendingCount, Is.Zero);
            Assert.That(diagnostics.BgmActiveCount, Is.Zero);
            Assert.That(diagnostics.ProfileAmbienceActiveCount, Is.Zero);
            Assert.That(diagnostics.GlobalLoopActiveCount, Is.Zero);
            Assert.That(diagnostics.Reserved0, Is.Zero);
            Assert.That(diagnostics.Reserved1, Is.Zero);
            Assert.That(diagnostics.Reserved2, Is.Zero);
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        private static object GetField(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
        }

        private static OrpheusAudioEvent CreateEvent(ushort key, AudioClip clip)
        {
            return CreateEventWithClips(key, clip);
        }

        private static OrpheusAudioEvent CreateEventWithClips(ushort key, params AudioClip[] clips)
        {
            var audioEvent = ScriptableObject.CreateInstance<OrpheusAudioEvent>();
            SetField(audioEvent, "_key", key);
            SetField(audioEvent, "_playbackKind", OrpheusPlaybackKind.OneShot2D);
            SetField(audioEvent, "_category", OrpheusCategory.SfxUi);
            SetField(audioEvent, "_loadPolicy", OrpheusLoadPolicy.BootstrapTransient);
            SetField(audioEvent, "_polyphonyCap", (byte)1);
            SetField(audioEvent, "_clips", clips);
            return audioEvent;
        }

        private sealed partial class SessionFixture : IDisposable
        {
            private static readonly OrpheusAudioUserGains DefaultGains =
                new OrpheusAudioUserGains(1f, 1f, 1f, 1f, 1f, 1f);

            private readonly GameObject _root;
            private readonly GameObject _sourceRoot;
            private readonly OrpheusAudioSettings _settings;
            private readonly OrpheusAudioCatalog _catalog;
            private readonly FakeClipReadiness _clipReadiness;
            private readonly FakeMixerPort _mixer;

            internal SessionFixture(object mixerIdentity = null, bool externalSourceBank = false)
            {
                _root = new GameObject("OrpheusTestHost");
                Host = _root.AddComponent<OrpheusAudioRuntimeHost>();
                _sourceRoot = externalSourceBank ? new GameObject("OrpheusExternalSourceBank") : _root;
                Bank = _sourceRoot.AddComponent<OrpheusAudioSourceBank>();
                Sources = BuildSources(_sourceRoot.transform);
                SetBankSources(Bank, Sources);
                SetField(Host, "_sourceBank", Bank);
                _settings = ScriptableObject.CreateInstance<OrpheusAudioSettings>();
                var validationMixer = ConfigureValidationMixer(_settings);
                _catalog = ScriptableObject.CreateInstance<OrpheusAudioCatalog>();
                _clipReadiness = new FakeClipReadiness();
                _mixer = new FakeMixerPort(mixerIdentity ?? validationMixer);
                AudioSystem = new FakeAudioSystemPort
                {
                    Configuration = new OrpheusAudioSystemConfiguration(1024, 48000, 32, 64)
                };
            }

            internal OrpheusAudioRuntimeHost Host { get; }
            internal OrpheusAudioSourceBank Bank { get; }
            internal AudioSource[] Sources { get; }
            internal FakeAudioSystemPort AudioSystem { get; }
            internal OrpheusAudioCatalog Catalog => _catalog;

            internal void SetSettingsSchemaVersion(int schemaVersion)
            {
                SetField(_settings, "_schemaVersion", schemaVersion);
            }

            internal void SetCatalogSchemaVersion(int schemaVersion)
            {
                SetField(_catalog, "_schemaVersion", schemaVersion);
            }

            internal OrpheusAudioInitResult Create(out OrpheusAudioManager manager)
            {
                return CreateWithCatalog(_catalog, out manager);
            }

            internal OrpheusAudioInitResult CreateWithGains(
                OrpheusAudioUserGains gains,
                out OrpheusAudioManager manager)
            {
                return OrpheusAudioTestFactory.Create(
                    Host,
                    _settings,
                    _catalog,
                    gains,
                    1,
                    true,
                    _clipReadiness,
                    _mixer,
                    AudioSystem,
                    out manager);
            }

            internal OrpheusAudioInitResult CreateWithCatalog(
                OrpheusAudioCatalog catalog,
                out OrpheusAudioManager manager)
            {
                return OrpheusAudioTestFactory.Create(
                    Host,
                    _settings,
                    catalog,
                    DefaultGains,
                    1,
                    true,
                    _clipReadiness,
                    _mixer,
                    AudioSystem,
                    out manager);
            }

            internal void SetDuplicateSource()
            {
                var bank = Host.SourceBank;
                var duplicated = Slice(Sources, 0, 12);
                duplicated[1] = duplicated[0];
                SetField(bank, "_oneShot3D", duplicated);
            }

            internal void SetSourceBank(OrpheusAudioSourceBank sourceBank)
            {
                SetField(Host, "_sourceBank", sourceBank);
            }

            internal void OverlapFirst3DSource(AudioSource source)
            {
                var oneShot3D = Slice(Sources, 0, 12);
                oneShot3D[0] = source;
                SetField(Bank, "_oneShot3D", oneShot3D);
            }

            internal void SetEvents(params OrpheusAudioEvent[] events)
            {
                SetField(_catalog, "_events", events);
            }

            internal void DirtyAllSources(AudioClip clip)
            {
                for (var index = 0; index < Sources.Length; index++)
                {
                    var source = Sources[index];
                    source.clip = clip;
                    source.time = 0.05f;
                    source.playOnAwake = true;
                    source.loop = true;
                    source.mute = true;
                    source.volume = 0.75f;
                    source.pitch = 0.5f;
                    source.priority = 1;
                    source.spatialBlend = 1f;
                    source.panStereo = 0.5f;
                    source.spread = 120f;
                    source.dopplerLevel = 1f;
                    source.reverbZoneMix = 0f;
                    source.minDistance = 2f;
                    source.maxDistance = 100f;
                    source.rolloffMode = AudioRolloffMode.Linear;
                    source.bypassEffects = true;
                    source.bypassListenerEffects = true;
                    source.bypassReverbZones = true;
                    source.ignoreListenerPause = true;
                    source.ignoreListenerVolume = true;
                    source.spatialize = true;
                    source.spatializePostEffects = true;
                    source.velocityUpdateMode = AudioVelocityUpdateMode.Dynamic;
                }
            }

            internal void DestroyHostOnly()
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }

            public void Dispose()
            {
                DestroyPlaybackTestObjects();
                DestroyActivationTestObjects();
                if (_root != null)
                {
                    UnityEngine.Object.DestroyImmediate(_root);
                }
                if (!ReferenceEquals(_sourceRoot, _root) && _sourceRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(_sourceRoot);
                }
                UnityEngine.Object.DestroyImmediate(_settings);
                UnityEngine.Object.DestroyImmediate(_catalog);
            }

            private static void SetBankSources(OrpheusAudioSourceBank bank, AudioSource[] sources)
            {
                SetField(bank, "_oneShot3D", Slice(sources, 0, 12));
                SetField(bank, "_oneShot2D", Slice(sources, 12, 4));
                SetField(bank, "_bgm", Slice(sources, 16, 2));
                SetField(bank, "_profileAmbience", Slice(sources, 18, 2));
                SetField(bank, "_globalLoop", Slice(sources, 20, 4));
            }

            private static AudioSource[] BuildSources(Transform parent)
            {
                var sources = new AudioSource[24];
                for (var index = 0; index < sources.Length; index++)
                {
                    var child = new GameObject(GetName(index));
                    child.transform.SetParent(parent, false);
                    sources[index] = child.AddComponent<AudioSource>();
                }

                return sources;
            }

            private static string GetName(int index)
            {
                if (index < 12) return "OneShot3D_" + index.ToString("00");
                if (index < 16) return "OneShot2D_" + (index - 12).ToString("00");
                if (index < 18) return "Bgm_" + (index - 16).ToString("00");
                if (index < 20) return "ProfileAmbience_" + (index - 18).ToString("00");
                return "GlobalLoop_" + (index - 20).ToString("00");
            }

            private static AudioSource[] Slice(AudioSource[] sources, int offset, int count)
            {
                var result = new AudioSource[count];
                Array.Copy(sources, offset, result, 0, count);
                return result;
            }

            private static void SetField(object target, string name, object value)
            {
                target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(target, value);
            }
        }

        private sealed class FakeClipReadiness : IOrpheusAudioClipReadiness
        {
            private readonly Dictionary<int, OrpheusClipLoadState> _states =
                new Dictionary<int, OrpheusClipLoadState>();
            private readonly Dictionary<int, int> _requestCounts = new Dictionary<int, int>();

            internal OrpheusClipLoadState State { get; set; } = OrpheusClipLoadState.Loaded;
            internal int GetCallCount { get; private set; }
            internal int RequestCallCount { get; private set; }
            internal bool ThrowOnGet { get; set; }

            internal void ResetCalls()
            {
                GetCallCount = 0;
                RequestCallCount = 0;
                _requestCounts.Clear();
            }

            internal void SetState(int flattenedClipIndex, OrpheusClipLoadState state)
            {
                _states[flattenedClipIndex] = state;
            }

            internal int GetRequestCount(int flattenedClipIndex)
            {
                return _requestCounts.TryGetValue(flattenedClipIndex, out var count)
                    ? count
                    : 0;
            }

            public OrpheusClipLoadState GetLoadState(int flattenedClipIndex)
            {
                GetCallCount++;
                if (ThrowOnGet)
                {
                    throw new InvalidOperationException("Injected readiness failure.");
                }

                return _states.TryGetValue(flattenedClipIndex, out var state)
                    ? state
                    : State;
            }

            public bool RequestLoad(int flattenedClipIndex)
            {
                RequestCallCount++;
                _requestCounts.TryGetValue(flattenedClipIndex, out var count);
                _requestCounts[flattenedClipIndex] = count + 1;
                return true;
            }
        }

        private sealed class FakeMixerPort : IOrpheusAudioMixerPort
        {
            private object _leaseIdentity;
            private OrpheusAudioDisableReason _leaseIdentityFailure;
            private readonly List<string> _parameterNames = new List<string>(64);
            private readonly List<float> _decibels = new List<float>(64);
            private readonly List<OrpheusEffectiveSnapshot> _snapshots =
                new List<OrpheusEffectiveSnapshot>(16);
            private readonly List<float> _transitionSeconds = new List<float>(16);
            private readonly List<string> _operationOrder = new List<string>(96);

            internal FakeMixerPort(object identity)
            {
                _leaseIdentity = identity;
            }

            public object LeaseIdentity
            {
                get
                {
                    if (_leaseIdentityFailure != OrpheusAudioDisableReason.None)
                    {
                        throw new OrpheusAudioCarrierException(_leaseIdentityFailure);
                    }

                    return _leaseIdentity;
                }
            }
            internal IReadOnlyList<string> ParameterNames => _parameterNames;
            internal IReadOnlyList<float> Decibels => _decibels;
            internal IReadOnlyList<OrpheusEffectiveSnapshot> Snapshots => _snapshots;
            internal IReadOnlyList<float> TransitionSeconds => _transitionSeconds;
            internal IReadOnlyList<string> OperationOrder => _operationOrder;
            internal int FailOnSetFloatCall { get; set; } = -1;
            internal int ThrowOnSetFloatCall { get; set; } = -1;
            internal bool ThrowOnTransition { get; set; }
            internal OrpheusAudioDisableReason TransitionFailureReason { get; set; }
            internal bool SuppressOperationOrder { get; set; }
            internal Action OnBeforeMixerOperation { get; set; }

            internal void ResetCalls()
            {
                _parameterNames.Clear();
                _decibels.Clear();
                _snapshots.Clear();
                _transitionSeconds.Clear();
                _operationOrder.Clear();
            }

            internal void SetLeaseIdentity(object identity)
            {
                _leaseIdentity = identity;
            }

            internal void FailLeaseIdentity(OrpheusAudioDisableReason reason)
            {
                _leaseIdentityFailure = reason;
            }

            public bool SetFloat(string parameterName, float decibels)
            {
                OnBeforeMixerOperation?.Invoke();
                _parameterNames.Add(parameterName);
                _decibels.Add(decibels);
                if (!SuppressOperationOrder)
                {
                    _operationOrder.Add("SetFloat:" + parameterName);
                }
                var call = _parameterNames.Count;
                if (call == ThrowOnSetFloatCall)
                {
                    throw new InvalidOperationException("Injected SetFloat failure.");
                }

                return call != FailOnSetFloatCall;
            }

            public void TransitionTo(OrpheusEffectiveSnapshot snapshot, float transitionSeconds)
            {
                OnBeforeMixerOperation?.Invoke();
                _snapshots.Add(snapshot);
                _transitionSeconds.Add(transitionSeconds);
                if (!SuppressOperationOrder)
                {
                    _operationOrder.Add("TransitionTo:" + snapshot);
                }
                if (TransitionFailureReason != OrpheusAudioDisableReason.None)
                {
                    throw new OrpheusAudioCarrierException(TransitionFailureReason);
                }

                if (ThrowOnTransition)
                {
                    throw new InvalidOperationException("Injected snapshot failure.");
                }
            }
        }

        internal sealed class FakeAudioSystemPort : IOrpheusAudioSystemPort
        {
            internal OrpheusAudioSystemConfiguration Configuration { get; set; }
            internal int GetConfigurationCallCount { get; private set; }
            internal int ResetCallCount { get; private set; }
            internal bool ResetResult { get; set; } = true;
            internal Action OnReset { get; set; }

            public OrpheusAudioSystemConfiguration GetConfiguration()
            {
                GetConfigurationCallCount++;
                return Configuration;
            }

            public bool ResetCurrentConfiguration()
            {
                ResetCallCount++;
                OnReset?.Invoke();
                return ResetResult;
            }
        }
    }
}
