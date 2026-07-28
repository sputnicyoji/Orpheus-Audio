using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioSubsystemRegistrationTests
    {
        private static OrpheusAudioManager s_firstSessionManager;

        private bool _settingsCaptured;
        private bool _previousOptionsEnabled;
        private EnterPlayModeOptions _previousOptions;

        [UnityTest]
        public IEnumerator EnteringSecondDomainReloadDisabledPlaySession_ClearsBridgesAndCombinedLeaseLedger()
        {
            CaptureAndDisableDomainReload();

            yield return new EnterPlayMode(false);

            var firstManager = CreateRunningManagerWithoutSessionResources();
            s_firstSessionManager = firstManager;
            var root = new GameObject("OrpheusSubsystemRegistrationTest");
            var host = root.AddComponent<OrpheusAudioRuntimeHost>();
            var bank = root.AddComponent<OrpheusAudioSourceBank>();
            var sources = new[] { root.AddComponent<AudioSource>() };

            // Direct acquisition leaves no Host reservation, so scene teardown cannot release this stranded lease.
            Assert.That(
                OrpheusAudioLeaseRegistry.TryAcquire(
                    host,
                    bank,
                    sources,
                    new object(),
                    firstManager,
                    out var conflict),
                Is.True);
            Assert.That(conflict, Is.EqualTo(OrpheusAudioLeaseConflict.None));
            Assert.That(OrpheusAudioLeaseRegistry.IsSourceBankHeldBy(firstManager), Is.True);
            Assert.That(OrpheusAudioLeaseRegistry.IsMixerHeldBy(firstManager), Is.True);
            Assert.That(OrpheusAudioBridge.Bind(firstManager), Is.True);
            Assert.That(OrpheusAudioRawBridge.Bind(firstManager), Is.True);

            yield return new ExitPlayMode();

            Assert.That(s_firstSessionManager, Is.SameAs(firstManager));
            Assert.That(OrpheusAudioLeaseRegistry.IsSourceBankHeldBy(firstManager), Is.True);
            Assert.That(OrpheusAudioLeaseRegistry.IsMixerHeldBy(firstManager), Is.True);

            var secondManager = CreateRunningManagerWithoutSessionResources();
            Assert.That(OrpheusAudioBridge.Bind(secondManager), Is.False);
            Assert.That(OrpheusAudioRawBridge.Bind(secondManager), Is.False);

            yield return new EnterPlayMode(false);

            Assert.That(s_firstSessionManager, Is.SameAs(firstManager));
            Assert.That(OrpheusAudioLeaseRegistry.IsSourceBankHeldBy(firstManager), Is.False);
            Assert.That(OrpheusAudioLeaseRegistry.IsMixerHeldBy(firstManager), Is.False);
            Assert.That(OrpheusAudioBridge.Bind(secondManager), Is.True);
            Assert.That(OrpheusAudioRawBridge.Bind(secondManager), Is.True);
            Assert.That(OrpheusAudioBridge.Unbind(secondManager), Is.True);
            Assert.That(OrpheusAudioRawBridge.Unbind(secondManager), Is.True);

            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator RestoreEditorSettings()
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    yield return new ExitPlayMode();
                }
            }
            finally
            {
                OrpheusAudioRuntimeStatics.Reset();
                s_firstSessionManager = null;

                if (_settingsCaptured)
                {
                    EditorSettings.enterPlayModeOptionsEnabled = _previousOptionsEnabled;
                    EditorSettings.enterPlayModeOptions = _previousOptions;
                    _settingsCaptured = false;
                }
            }
        }

        private void CaptureAndDisableDomainReload()
        {
            _previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _previousOptions = EditorSettings.enterPlayModeOptions;
            _settingsCaptured = true;

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }

        private static OrpheusAudioManager CreateRunningManagerWithoutSessionResources()
        {
            // Reset must forget stale identities without touching prior-session Unity objects.
            // A lifecycle-only shell isolates that contract from normal teardown cleanup.
            var manager = (OrpheusAudioManager)FormatterServices.GetUninitializedObject(
                typeof(OrpheusAudioManager));
            var lifecycle = typeof(OrpheusAudioManager).GetField(
                "_lifecycle",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(lifecycle, Is.Not.Null);
            lifecycle.SetValue(manager, OrpheusAudioLifecycle.Running);
            return manager;
        }
    }
}
