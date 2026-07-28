using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.ContractConsumer
{
    internal static class OrpheusAudioConsumerContract
    {
        internal static OrpheusAudioInitResult Create(
            OrpheusAudioRuntimeHost runtimeHost,
            OrpheusAudioSettings settings,
            OrpheusAudioCatalog catalog,
            OrpheusAudioUserGains gains,
            out OrpheusAudioManager manager)
        {
            return OrpheusAudioFactory.Create(
                runtimeHost,
                settings,
                catalog,
                gains,
                out manager);
        }

        internal static void ExerciseHostIntegration(
            OrpheusAudioManager manager,
            OrpheusAudioRuntimeHost runtimeHost,
            AudioListener currentListener,
            AudioListener replacementListener,
            OrpheusAudioKey key,
            OrpheusAudioProfileIntent profile)
        {
            bool runtimeHostBound = runtimeHost.Bind(manager);
            manager.SetUserGain(OrpheusBus.Master, 1f);
            bool gainRead = manager.TryGetUserGain(
                OrpheusBus.Master,
                out float linearGain);
            OrpheusBootstrapAuthority authority = manager.CaptureBootstrapAuthority();
            bool bootstrapHydrated = manager.CompleteBootstrapHydration(authority);
            bool listenerBound = manager.BindListener(currentListener);
            bool listenerReplaced =
                manager.ReplaceListener(currentListener, replacementListener);
            OrpheusCompleteHostReadyResult hostReadyResult = manager.CompleteHostReady();
            manager.ApplyProfile(profile);
            manager.SetOverlay(OrpheusOverlay.Menu, true);
            OrpheusAudioPrepareResult prepareResult = manager.Prepare(key);
            OrpheusClipLoadState loadState = manager.GetLoadState(key);
            manager.Play(key);
            manager.PlayAt(key, Vector3.zero);
            manager.PlayLoop(key);
            manager.StopLoop(key);
            manager.StopAll(OrpheusBus.SfxWorld);
            bool diagnosticsRead =
                manager.TryGetDiagnostics(out OrpheusAudioDiagnostics diagnostics);
            bool listenerRemoved = manager.RemoveListener(replacementListener);
            bool runtimeHostUnbound = runtimeHost.Unbind(manager);
            manager.Dispose();

            AcceptRuntimeResults(
                runtimeHostBound,
                gainRead,
                linearGain,
                bootstrapHydrated,
                listenerBound,
                listenerReplaced,
                hostReadyResult,
                prepareResult,
                loadState,
                diagnosticsRead,
                diagnostics,
                listenerRemoved,
                runtimeHostUnbound);
        }

        internal static void ExerciseGameplayBridges(
            OrpheusAudioManager manager,
            OrpheusAudioKey key,
            int rawKey,
            Vector3 position)
        {
            bool typedBound = OrpheusAudioBridge.Bind(manager);
            OrpheusAudioBridge.Play(key);
            OrpheusAudioBridge.PlayAt(key, position);
            bool typedUnbound = OrpheusAudioBridge.Unbind(manager);

            bool rawBound = OrpheusAudioRawBridge.Bind(manager);
            OrpheusAudioRawBridge.Play(rawKey);
            OrpheusAudioRawBridge.PlayAt(rawKey, position);
            bool rawUnbound = OrpheusAudioRawBridge.Unbind(manager);

            AcceptBridgeResults(typedBound, typedUnbound, rawBound, rawUnbound);
        }

        internal static void AcceptCoreContracts(
            OrpheusAudioKey key,
            OrpheusBus bus,
            OrpheusBaseState baseState,
            OrpheusOverlay overlay,
            OrpheusClipLoadState clipLoadState,
            OrpheusAudioLifecycle lifecycle,
            OrpheusTransportState transportState,
            OrpheusSuspensionReason suspensionReasons,
            OrpheusReadiness readiness,
            OrpheusEffectiveSnapshot effectiveSnapshot,
            OrpheusRecoveryPendingReason recoveryPendingReasons,
            OrpheusAudioInitErrorCode initErrorCode,
            OrpheusCompleteHostReadyResult hostReadyResult,
            OrpheusAudioPrepareResult prepareResult,
            OrpheusAudioDisableReason disableReason,
            OrpheusAudioInitResult initResult,
            OrpheusAudioUserGains gains,
            OrpheusAudioProfileIntent profileIntent,
            OrpheusAudioDiagnosticsCounters counters,
            OrpheusAudioDiagnostics diagnostics,
            OrpheusBootstrapAuthority bootstrapAuthority)
        {
            _ = key;
            _ = bus;
            _ = baseState;
            _ = overlay;
            _ = clipLoadState;
            _ = lifecycle;
            _ = transportState;
            _ = suspensionReasons;
            _ = readiness;
            _ = effectiveSnapshot;
            _ = recoveryPendingReasons;
            _ = initErrorCode;
            _ = hostReadyResult;
            _ = prepareResult;
            _ = disableReason;
            _ = initResult;
            _ = gains;
            _ = profileIntent;
            _ = counters;
            _ = diagnostics;
            _ = bootstrapAuthority;
        }

        internal static void AcceptAssets(
            OrpheusAudioEvent audioEvent,
            OrpheusAudioCatalog catalog,
            OrpheusAudioSettings settings,
            OrpheusAudioRuntimeHost runtimeHost,
            OrpheusAudioSourceBank sourceBank)
        {
        }

        private static void AcceptRuntimeResults(
            bool runtimeHostBound,
            bool gainRead,
            float linearGain,
            bool bootstrapHydrated,
            bool listenerBound,
            bool listenerReplaced,
            OrpheusCompleteHostReadyResult hostReadyResult,
            OrpheusAudioPrepareResult prepareResult,
            OrpheusClipLoadState loadState,
            bool diagnosticsRead,
            OrpheusAudioDiagnostics diagnostics,
            bool listenerRemoved,
            bool runtimeHostUnbound)
        {
            _ = runtimeHostBound;
            _ = gainRead;
            _ = linearGain;
            _ = bootstrapHydrated;
            _ = listenerBound;
            _ = listenerReplaced;
            _ = hostReadyResult;
            _ = prepareResult;
            _ = loadState;
            _ = diagnosticsRead;
            _ = diagnostics;
            _ = listenerRemoved;
            _ = runtimeHostUnbound;
        }

        private static void AcceptBridgeResults(
            bool typedBound,
            bool typedUnbound,
            bool rawBound,
            bool rawUnbound)
        {
            _ = typedBound;
            _ = typedUnbound;
            _ = rawBound;
            _ = rawUnbound;
        }
    }
}
