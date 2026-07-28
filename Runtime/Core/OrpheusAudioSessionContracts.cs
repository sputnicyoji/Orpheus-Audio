using System;

namespace Orpheus.Audio.Core
{
    public enum OrpheusAudioLifecycle : byte
    {
        Invalid = 0,
        Running = 1,
        Disabled = 2,
        Disposed = 3
    }

    public enum OrpheusTransportState : byte
    {
        Invalid = 0,
        Active = 1,
        Suspended = 2,
        Recovering = 3,
        Disposed = 4
    }

    [Flags]
    public enum OrpheusSuspensionReason : byte
    {
        None = 0,
        FocusLost = 1,
        ApplicationPaused = 2,
        ListenerMissing = 4
    }

    [Flags]
    public enum OrpheusReadiness : byte
    {
        None = 0,
        HostReady = 1,
        BootstrapHydrated = 2,
        ActivationReady = 4,
        PlaybackReady = 8
    }

    public enum OrpheusEffectiveSnapshot : byte
    {
        Invalid = 0,
        Peace = 1,
        Combat = 2,
        Menu = 3,
        Pause = 4
    }

    [Flags]
    public enum OrpheusRecoveryPendingReason : byte
    {
        None = 0,
        ExternalConfiguration = 1,
        SelfResetNotification = 2
    }

    public enum OrpheusAudioInitErrorCode : byte
    {
        None = 0,
        InvalidArguments = 1,
        InvalidInitialGain = 2,
        InvalidSettings = 3,
        InvalidMixerContract = 4,
        InvalidSourceBank = 5,
        InvalidCatalog = 6,
        DuplicateKey = 7,
        InvalidEvent = 8,
        InvalidClipReference = 9,
        InvalidAudioConfiguration = 10
    }

    public enum OrpheusCompleteHostReadyResult : byte
    {
        Invalid = 0,
        Completed = 1,
        AlreadyCompleted = 2,
        RuntimeHostNotStarted = 3,
        RuntimeHostNotBound = 4,
        ListenerNotBound = 5,
        InvalidLifecycle = 6,
        DisabledByValidationFailure = 7
    }

    public enum OrpheusAudioPrepareResult : byte
    {
        Invalid = 0,
        LoadRequested = 1,
        AlreadyLoading = 2,
        AlreadyLoaded = 3,
        RejectedInvalidKey = 4,
        RejectedPolicy = 5,
        FailedToRequest = 6
    }

    public enum OrpheusAudioDisableReason : byte
    {
        None = 0,
        MixerSetFloatFailed = 1,
        ListenerPositionNonFinite = 2,
        OwnedSourceDestroyed = 3,
        OwnedSourceReferenceChanged = 4,
        SourceBankDuplicateReference = 5,
        SourceBankLeaseLost = 6,
        MixerLeaseLost = 7,
        MixerReferenceInvalid = 8,
        SnapshotReferenceInvalid = 9,
        MixerGroupReferenceInvalid = 10,
        RuntimeTimeInvalid = 11,
        RealtimeMovedBackwards = 12,
        UnexpectedRuntimeException = 13,
        RuntimeHostDestroyed = 14
    }

    public readonly struct OrpheusAudioInitResult
    {
        internal OrpheusAudioInitResult(
            bool success,
            OrpheusAudioInitErrorCode errorCode,
            OrpheusAudioKey relatedKey,
            int relatedIndex)
        {
            Success = success;
            ErrorCode = errorCode;
            RelatedKey = relatedKey;
            RelatedIndex = relatedIndex;
        }

        public bool Success { get; }
        public OrpheusAudioInitErrorCode ErrorCode { get; }
        public OrpheusAudioKey RelatedKey { get; }
        public int RelatedIndex { get; }

        internal static OrpheusAudioInitResult Succeeded =>
            new OrpheusAudioInitResult(true, OrpheusAudioInitErrorCode.None, OrpheusAudioKey.Invalid, -1);

        internal static OrpheusAudioInitResult Failed(
            OrpheusAudioInitErrorCode errorCode,
            OrpheusAudioKey relatedKey = default,
            int relatedIndex = -1)
        {
            return new OrpheusAudioInitResult(false, errorCode, relatedKey, relatedIndex);
        }
    }

    public readonly struct OrpheusAudioUserGains
    {
        public OrpheusAudioUserGains(
            float master,
            float music,
            float sfxCombat,
            float sfxWorld,
            float sfxUi,
            float ambience)
        {
            Master = master;
            Music = music;
            SfxCombat = sfxCombat;
            SfxWorld = sfxWorld;
            SfxUi = sfxUi;
            Ambience = ambience;
        }

        public float Master { get; }
        public float Music { get; }
        public float SfxCombat { get; }
        public float SfxWorld { get; }
        public float SfxUi { get; }
        public float Ambience { get; }
    }

    public readonly struct OrpheusAudioProfileIntent
    {
        public OrpheusAudioProfileIntent(
            uint profileId,
            OrpheusBaseState baseState,
            OrpheusAudioKey bgmKey,
            OrpheusAudioKey profileAmbienceKey)
        {
            ProfileId = profileId;
            BaseState = baseState;
            BgmKey = bgmKey;
            ProfileAmbienceKey = profileAmbienceKey;
        }

        public uint ProfileId { get; }
        public OrpheusBaseState BaseState { get; }
        public OrpheusAudioKey BgmKey { get; }
        public OrpheusAudioKey ProfileAmbienceKey { get; }
    }
}
