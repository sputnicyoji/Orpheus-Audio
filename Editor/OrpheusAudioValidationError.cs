namespace Orpheus.Audio.Editor
{
    internal enum OrpheusAudioValidationErrorCode : ushort
    {
        None = 0,
        InvalidProfileSchema = 1,
        MissingSettings = 2,
        MissingCatalog = 3,
        MissingKeyManifest = 4,
        InvalidSettingsSchema = 5,
        InvalidCatalogSchema = 6,
        InvalidManifestSchema = 7,
        InvalidManifestStorage = 8,
        InvalidManifestId = 9,
        InvalidManifestSymbol = 10,
        InvalidManifestStatus = 11,
        DuplicateManifestId = 12,
        DuplicateManifestSymbol = 13,
        MissingTypedKeyProjection = 14,
        StaleTypedKeyProjection = 15,
        NullEvent = 16,
        UnsortedCatalog = 17,
        DuplicateEventKey = 18,
        InvalidEventPolicy = 19,
        EventKeyNotActive = 20,
        MissingActiveEvent = 21,
        InvalidEventAssetName = 22,
        MissingClipReference = 23,
        DuplicateClipReference = 24,
        MissingAudioImporter = 25,
        InvalidAudioImporterPolicy = 26,
        MultipleKeyManifests = 27,
        InvalidBgmCrossfadeDuration = 28,
        InvalidProfileAmbienceCrossfadeDuration = 29,
        InvalidSnapshotTransitionDuration = 30,
        MissingMixer = 31,
        MissingMixerGroup = 32,
        MissingMixerSnapshot = 33,
        MismatchedMixerReference = 34,
        InvalidMixerUpdateMode = 35,
        UnsupportedMixerSerialization = 36,
        InvalidMixerGroupContract = 37,
        InvalidMixerExposedParameterContract = 38,
        InvalidMixerSnapshotContract = 39,
        InvalidMixerSnapshotOverride = 40,
        InvalidMixerAttenuation = 41,
        InaudibleStateUi = 42,
        MissingRuntimeHostPrefab = 43,
        InvalidRuntimeHostPrefab = 44,
        InvalidRuntimeHostCount = 45,
        MissingSourceBank = 46,
        InvalidSourceBankCount = 47,
        MismatchedSourceBankReference = 48,
        InvalidSourceBankRoleCount = 49,
        MissingSourceBankLeaf = 50,
        InvalidSourceBankLeafName = 51,
        DuplicateSourceBankLeaf = 52,
        InvalidSourceBankLeafAudioSourceCount = 53,
        UnexpectedSourceBankAudioSource = 54,
        SourceBankPlayOnAwake = 55,
        SourceBankPresetClip = 56,
        SourceBankStaleRoute = 57,
        NullListenerScene = 58,
        UnresolvedListenerScene = 59,
        InvalidListenerSceneType = 60,
        UnloadableListenerScene = 61,
        SourceBankLeafOutsideBank = 62,
        InvalidAuthoringProfile = 63,
        StaleGeneratedOwnership = 64,
        AuthoringEnrollmentIdentityMismatch = 65,
        LostAuthoringOwnership = 66,
        GeneratedCatalogMismatch = 67,
        StaleAuthoringInput = 68,
        AuthoringOutputFingerprintMismatch = 69
    }

    internal readonly struct OrpheusAudioValidationError
    {
        internal OrpheusAudioValidationError(
            OrpheusAudioValidationErrorCode code,
            string profilePath,
            string assetPath,
            int eventIndex = -1,
            int relatedIndex = -1,
            uint detail = 0)
        {
            Code = code;
            ProfilePath = profilePath ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            EventIndex = eventIndex;
            RelatedIndex = relatedIndex;
            Detail = detail;
        }

        internal OrpheusAudioValidationErrorCode Code { get; }
        internal string ProfilePath { get; }
        internal string AssetPath { get; }
        internal int EventIndex { get; }
        internal int RelatedIndex { get; }
        internal uint Detail { get; }

        public override string ToString()
        {
            return Code + " | " + ProfilePath + " | " + AssetPath +
                   " | event=" + EventIndex + " | related=" + RelatedIndex +
                   " | detail=" + Detail;
        }
    }
}
