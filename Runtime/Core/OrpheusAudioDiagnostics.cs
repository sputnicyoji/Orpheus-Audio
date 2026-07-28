using System.Runtime.InteropServices;

namespace Orpheus.Audio.Core
{
    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 176)]
    public readonly struct OrpheusAudioDiagnosticsCounters
    {
        public readonly ulong PoolCapacityRejected;
        public readonly ulong Stolen;
        public readonly ulong CooldownRejected;
        public readonly ulong PolyphonyRejected;
        public readonly ulong DistanceRejected;
        public readonly ulong PreReadyRejected;
        public readonly ulong SuspendedRejected;
        public readonly ulong UnavailableRejected;
        public readonly ulong WrongThreadRejected;
        public readonly ulong LoadNotReadyRejected;
        public readonly ulong LoadFailed;
        public readonly ulong LoadStalled;
        public readonly ulong InvalidKeyRejected;
        public readonly ulong InvalidRawKeyRejected;
        public readonly ulong InvalidPositionRejected;
        public readonly ulong InvalidValueRejected;
        public readonly ulong PlaybackKindRejected;
        public readonly ulong LoopRegistryFull;
        public readonly ulong SetFloatFailed;
        public readonly ulong RecoveryFailed;
        public readonly ulong StaleBootstrapTokenRejected;
        public readonly ulong UnexpectedException;

        internal OrpheusAudioDiagnosticsCounters(
            ulong poolCapacityRejected,
            ulong stolen,
            ulong cooldownRejected,
            ulong polyphonyRejected,
            ulong distanceRejected,
            ulong preReadyRejected,
            ulong suspendedRejected,
            ulong unavailableRejected,
            ulong wrongThreadRejected,
            ulong loadNotReadyRejected,
            ulong loadFailed,
            ulong loadStalled,
            ulong invalidKeyRejected,
            ulong invalidRawKeyRejected,
            ulong invalidPositionRejected,
            ulong invalidValueRejected,
            ulong playbackKindRejected,
            ulong loopRegistryFull,
            ulong setFloatFailed,
            ulong recoveryFailed,
            ulong staleBootstrapTokenRejected,
            ulong unexpectedException)
        {
            PoolCapacityRejected = poolCapacityRejected;
            Stolen = stolen;
            CooldownRejected = cooldownRejected;
            PolyphonyRejected = polyphonyRejected;
            DistanceRejected = distanceRejected;
            PreReadyRejected = preReadyRejected;
            SuspendedRejected = suspendedRejected;
            UnavailableRejected = unavailableRejected;
            WrongThreadRejected = wrongThreadRejected;
            LoadNotReadyRejected = loadNotReadyRejected;
            LoadFailed = loadFailed;
            LoadStalled = loadStalled;
            InvalidKeyRejected = invalidKeyRejected;
            InvalidRawKeyRejected = invalidRawKeyRejected;
            InvalidPositionRejected = invalidPositionRejected;
            InvalidValueRejected = invalidValueRejected;
            PlaybackKindRejected = playbackKindRejected;
            LoopRegistryFull = loopRegistryFull;
            SetFloatFailed = setFloatFailed;
            RecoveryFailed = recoveryFailed;
            StaleBootstrapTokenRejected = staleBootstrapTokenRejected;
            UnexpectedException = unexpectedException;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = 224)]
    public readonly struct OrpheusAudioDiagnostics
    {
        public readonly uint SchemaVersion;
        public readonly uint ProfileId;
        public readonly int ConfiguredRealVoiceLimit;
        public readonly int ConfiguredVirtualVoiceLimit;
        public readonly OrpheusAudioKey DesiredBgmKey;
        public readonly OrpheusAudioKey CurrentBgmKey;
        public readonly OrpheusAudioKey TargetBgmKey;
        public readonly OrpheusAudioKey DesiredProfileAmbienceKey;
        public readonly OrpheusAudioKey CurrentProfileAmbienceKey;
        public readonly OrpheusAudioKey TargetProfileAmbienceKey;
        public readonly OrpheusAudioLifecycle Lifecycle;
        public readonly OrpheusAudioDisableReason DisableReason;
        public readonly OrpheusTransportState TransportState;
        public readonly OrpheusSuspensionReason SuspensionReasons;
        public readonly OrpheusReadiness Readiness;
        public readonly OrpheusOverlay Overlay;
        public readonly OrpheusEffectiveSnapshot EffectiveSnapshot;
        public readonly OrpheusRecoveryPendingReason RecoveryPendingReasons;
        public readonly OrpheusBaseState BaseState;
        public readonly byte VoiceBudgetDegraded;
        public readonly byte Transient3DActiveCount;
        public readonly byte Transient2DActiveCount;
        public readonly byte FadingCount;
        public readonly byte PendingCount;
        public readonly byte BgmActiveCount;
        public readonly byte ProfileAmbienceActiveCount;
        public readonly byte GlobalLoopActiveCount;
        public readonly byte Reserved0;
        public readonly byte Reserved1;
        public readonly byte Reserved2;
        public readonly OrpheusAudioDiagnosticsCounters Counters;

        internal OrpheusAudioDiagnostics(
            uint profileId,
            OrpheusAudioKey desiredBgmKey,
            OrpheusAudioKey currentBgmKey,
            OrpheusAudioKey targetBgmKey,
            OrpheusAudioKey desiredProfileAmbienceKey,
            OrpheusAudioKey currentProfileAmbienceKey,
            OrpheusAudioKey targetProfileAmbienceKey,
            OrpheusOverlay overlay,
            OrpheusEffectiveSnapshot effectiveSnapshot,
            OrpheusRecoveryPendingReason recoveryPendingReasons,
            OrpheusBaseState baseState,
            OrpheusAudioLifecycle lifecycle,
            OrpheusAudioDisableReason disableReason,
            OrpheusTransportState transportState,
            OrpheusSuspensionReason suspensionReasons,
            OrpheusReadiness readiness,
            int configuredRealVoiceLimit,
            int configuredVirtualVoiceLimit,
            bool voiceBudgetDegraded,
            byte transient3DActiveCount,
            byte transient2DActiveCount,
            byte fadingCount,
            byte pendingCount,
            byte bgmActiveCount,
            byte profileAmbienceActiveCount,
            byte globalLoopActiveCount,
            OrpheusAudioDiagnosticsCounters counters)
        {
            SchemaVersion = 1;
            ProfileId = profileId;
            ConfiguredRealVoiceLimit = configuredRealVoiceLimit;
            ConfiguredVirtualVoiceLimit = configuredVirtualVoiceLimit;
            DesiredBgmKey = desiredBgmKey;
            CurrentBgmKey = currentBgmKey;
            TargetBgmKey = targetBgmKey;
            DesiredProfileAmbienceKey = desiredProfileAmbienceKey;
            CurrentProfileAmbienceKey = currentProfileAmbienceKey;
            TargetProfileAmbienceKey = targetProfileAmbienceKey;
            Lifecycle = lifecycle;
            DisableReason = disableReason;
            TransportState = transportState;
            SuspensionReasons = suspensionReasons;
            Readiness = readiness;
            Overlay = overlay;
            EffectiveSnapshot = effectiveSnapshot;
            RecoveryPendingReasons = recoveryPendingReasons;
            BaseState = baseState;
            VoiceBudgetDegraded = voiceBudgetDegraded ? (byte)1 : (byte)0;
            Transient3DActiveCount = transient3DActiveCount;
            Transient2DActiveCount = transient2DActiveCount;
            FadingCount = fadingCount;
            PendingCount = pendingCount;
            BgmActiveCount = bgmActiveCount;
            ProfileAmbienceActiveCount = profileAmbienceActiveCount;
            GlobalLoopActiveCount = globalLoopActiveCount;
            Reserved0 = 0;
            Reserved1 = 0;
            Reserved2 = 0;
            Counters = counters;
        }

        public bool IsAvailable => Lifecycle == OrpheusAudioLifecycle.Running;
        public bool IsVoiceBudgetDegraded => VoiceBudgetDegraded != 0;
        public bool HostReady => (Readiness & OrpheusReadiness.HostReady) != 0;
        public bool BootstrapHydrated => (Readiness & OrpheusReadiness.BootstrapHydrated) != 0;
        public bool ActivationReady => (Readiness & OrpheusReadiness.ActivationReady) != 0;
        public bool PlaybackReady => (Readiness & OrpheusReadiness.PlaybackReady) != 0;
        public bool MenuOverlayEnabled => (Overlay & OrpheusOverlay.Menu) != 0;
        public bool PauseOverlayEnabled => (Overlay & OrpheusOverlay.Pause) != 0;
        public bool ConfigurationRecoveryPending => RecoveryPendingReasons != OrpheusRecoveryPendingReason.None;
    }
}
