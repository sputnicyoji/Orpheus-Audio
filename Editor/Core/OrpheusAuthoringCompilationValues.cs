using System;
using Orpheus.Audio.Core;

namespace Orpheus.Audio.Editor
{
    internal enum OrpheusAuthoringErrorCode : ushort
    {
        Invalid = 0,
        CompilerNotImplemented = 1,
        InvalidValidationProfile = 2,
        DisabledValidationProfile = 3,
        ManualProfileNotCompilable = 4,
        SchemaVersionMismatch = 5,
        MissingManifest = 6,
        DuplicateManifestId = 7,
        DuplicateManifestSymbol = 8,
        InvalidManifestStatus = 9,
        ManifestIdentityRemoved = 10,
        ManifestLifecycleRegression = 11,
        ManifestIdReuse = 12,
        InvalidSymbolRename = 13,
        NullRecipe = 14,
        InvalidModuleId = 15,
        DuplicateModuleId = 16,
        MissingRecipeSymbol = 17,
        InactiveRecipeSymbol = 18,
        DuplicateRecipeOwner = 19,
        ActiveManifestIdentityUnowned = 20,
        InvalidEventPolicy = 21,
        InvalidClipIdentity = 22,
        DuplicateClip = 23,
        InvalidGeneratedRoot = 24,
        OutputPathCollision = 25,
        GeneratedRootOverlap = 26,
        TypedKeyOwnershipConflict = 27,
        EnrollmentIdentityMismatch = 28,
        LostOwnershipState = 29,
        CatalogMismatch = 30,
        CatalogNotEmptyAtEnrollment = 31,
        OwnershipStateInvalid = 32,
        ImporterPolicyMismatch = 33,
        ActualOutputFingerprintMismatch = 34,
        TransactionRecoveryRequired = 35,
        ConcurrentCompilation = 36,
        RollbackFailed = 37,
        AuthoringProfileConflict = 38,
        ManualProfileWouldBecomeStale = 39,
        UnsupportedBuildTarget = 40,
        InvalidBatchArguments = 41
    }

    internal readonly struct OrpheusAuthoringCompilationError
    {
        internal OrpheusAuthoringCompilationError(
            OrpheusAuthoringErrorCode code,
            ushort key,
            string moduleId,
            string symbol,
            string assetPath,
            int clipIndex,
            string detail)
        {
            Code = code;
            Key = key;
            ModuleId = moduleId ?? string.Empty;
            Symbol = symbol ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            ClipIndex = clipIndex;
            Detail = detail ?? string.Empty;
        }

        internal OrpheusAuthoringErrorCode Code { get; }

        internal ushort Key { get; }

        internal string ModuleId { get; }

        internal string Symbol { get; }

        internal string AssetPath { get; }

        internal int ClipIndex { get; }

        internal string Detail { get; }
    }

    internal readonly struct OrpheusAuthoringManifestEntryValue
    {
        internal OrpheusAuthoringManifestEntryValue(
            ushort key,
            string symbol,
            OrpheusAudioKeyStatus status)
        {
            Key = key;
            Symbol = symbol ?? string.Empty;
            Status = status;
        }

        internal ushort Key { get; }

        internal string Symbol { get; }

        internal OrpheusAudioKeyStatus Status { get; }
    }

    internal sealed class OrpheusAuthoringManifestValue
    {
        private readonly OrpheusAuthoringManifestEntryValue[] _entries;

        internal OrpheusAuthoringManifestValue(
            int schemaVersion,
            OrpheusAuthoringManifestEntryValue[] entries)
        {
            SchemaVersion = schemaVersion;
            _entries = entries == null
                ? null
                : (OrpheusAuthoringManifestEntryValue[])entries.Clone();
        }

        internal int SchemaVersion { get; }

        internal bool HasEntryStorage => _entries != null;

        internal int EntryCount => _entries == null ? 0 : _entries.Length;

        internal OrpheusAuthoringManifestEntryValue GetEntry(int index)
        {
            return _entries[index];
        }
    }

    internal readonly struct OrpheusAuthoringClipValue
    {
        internal OrpheusAuthoringClipValue(
            string guid,
            long localFileId,
            string assetPath)
        {
            Guid = guid ?? string.Empty;
            LocalFileId = localFileId;
            AssetPath = assetPath ?? string.Empty;
        }

        internal string Guid { get; }

        internal long LocalFileId { get; }

        internal string AssetPath { get; }
    }

    internal sealed class OrpheusAuthoringEventRecipeValue
    {
        private readonly OrpheusAuthoringClipValue[] _clips;

        internal OrpheusAuthoringEventRecipeValue(
            string symbol,
            OrpheusPlaybackKind playbackKind,
            OrpheusCategory category,
            OrpheusLoadPolicy loadPolicy,
            OrpheusAuthoringClipValue[] clips,
            float volumeMinimum,
            float volumeMaximum,
            float pitchMinimum,
            float pitchMaximum,
            byte priority,
            byte polyphonyCap,
            float cooldownSeconds,
            float minimumDistance,
            float maximumDistance,
            OrpheusRolloffMode rolloffMode,
            string profileHint,
            string candidateContentBankId)
        {
            Symbol = symbol ?? string.Empty;
            PlaybackKind = playbackKind;
            Category = category;
            LoadPolicy = loadPolicy;
            _clips = clips == null ? null : (OrpheusAuthoringClipValue[])clips.Clone();
            VolumeMinimum = volumeMinimum;
            VolumeMaximum = volumeMaximum;
            PitchMinimum = pitchMinimum;
            PitchMaximum = pitchMaximum;
            Priority = priority;
            PolyphonyCap = polyphonyCap;
            CooldownSeconds = cooldownSeconds;
            MinimumDistance = minimumDistance;
            MaximumDistance = maximumDistance;
            RolloffMode = rolloffMode;
            ProfileHint = profileHint ?? string.Empty;
            CandidateContentBankId = candidateContentBankId ?? string.Empty;
        }

        internal string Symbol { get; }

        internal OrpheusPlaybackKind PlaybackKind { get; }

        internal OrpheusCategory Category { get; }

        internal OrpheusLoadPolicy LoadPolicy { get; }

        internal bool HasClipStorage => _clips != null;

        internal int ClipCount => _clips == null ? 0 : _clips.Length;

        internal float VolumeMinimum { get; }

        internal float VolumeMaximum { get; }

        internal float PitchMinimum { get; }

        internal float PitchMaximum { get; }

        internal byte Priority { get; }

        internal byte PolyphonyCap { get; }

        internal float CooldownSeconds { get; }

        internal float MinimumDistance { get; }

        internal float MaximumDistance { get; }

        internal OrpheusRolloffMode RolloffMode { get; }

        internal string ProfileHint { get; }

        internal string CandidateContentBankId { get; }

        internal OrpheusAuthoringClipValue GetClip(int index)
        {
            return _clips[index];
        }
    }

    internal sealed class OrpheusAuthoringModuleRecipeValue
    {
        private readonly OrpheusAuthoringEventRecipeValue[] _events;

        internal OrpheusAuthoringModuleRecipeValue(
            int schemaVersion,
            string moduleId,
            OrpheusAuthoringEventRecipeValue[] events)
        {
            SchemaVersion = schemaVersion;
            ModuleId = moduleId ?? string.Empty;
            _events = events == null
                ? null
                : (OrpheusAuthoringEventRecipeValue[])events.Clone();
        }

        internal int SchemaVersion { get; }

        internal string ModuleId { get; }

        internal bool HasEventStorage => _events != null;

        internal int EventCount => _events == null ? 0 : _events.Length;

        internal OrpheusAuthoringEventRecipeValue GetEvent(int index)
        {
            return _events[index];
        }
    }

    internal readonly struct OrpheusAuthoringOwnedEventValue
    {
        internal OrpheusAuthoringOwnedEventValue(
            ushort key,
            string moduleId,
            string symbol,
            string guid,
            string assetPath,
            string contentFingerprint)
        {
            Key = key;
            ModuleId = moduleId ?? string.Empty;
            Symbol = symbol ?? string.Empty;
            Guid = guid ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            ContentFingerprint = contentFingerprint ?? string.Empty;
        }

        internal ushort Key { get; }

        internal string ModuleId { get; }

        internal string Symbol { get; }

        internal string Guid { get; }

        internal string AssetPath { get; }

        internal string ContentFingerprint { get; }
    }

    internal sealed class OrpheusAuthoringCatalogValue
    {
        private readonly string[] _eventAssetPaths;

        internal OrpheusAuthoringCatalogValue(
            string guid,
            string assetPath,
            int eventCount,
            bool ownershipConflict,
            string[] eventAssetPaths = null)
        {
            Guid = guid ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            EventCount = eventCount;
            OwnershipConflict = ownershipConflict;
            _eventAssetPaths = eventAssetPaths == null
                ? null
                : (string[])eventAssetPaths.Clone();
        }

        internal string Guid { get; }

        internal string AssetPath { get; }

        internal int EventCount { get; }

        internal bool OwnershipConflict { get; }

        internal bool HasEventAssetPaths =>
            _eventAssetPaths != null &&
            _eventAssetPaths.Length == EventCount;

        internal string GetEventAssetPath(int index)
        {
            return _eventAssetPaths[index] ?? string.Empty;
        }
    }

    internal readonly struct OrpheusAuthoringExistingOutputValue
    {
        internal OrpheusAuthoringExistingOutputValue(
            string assetPath,
            bool compilerOwned,
            string guid = "",
            string mainType = "",
            bool exists = true)
        {
            AssetPath = assetPath ?? string.Empty;
            CompilerOwned = compilerOwned;
            Guid = guid ?? string.Empty;
            MainType = mainType ?? string.Empty;
            Exists = exists;
        }

        internal string AssetPath { get; }

        internal bool CompilerOwned { get; }

        internal string Guid { get; }

        internal string MainType { get; }

        internal bool Exists { get; }
    }

    internal sealed class OrpheusAuthoringCompilationInput
    {
        private readonly OrpheusAuthoringModuleRecipeValue[] _recipes;
        private readonly OrpheusAuthoringManifestEntryValue[] _acceptedManifest;
        private readonly OrpheusAuthoringOwnedEventValue[] _ownedEvents;
        private readonly OrpheusAuthoringExistingOutputValue[] _existingOutputs;
        private readonly string[] _otherGeneratedRoots;

        internal OrpheusAuthoringCompilationInput(
            int authoringSchemaVersion,
            OrpheusAuthoringManifestValue manifest,
            OrpheusAuthoringModuleRecipeValue[] recipes,
            string generatedRoot,
            OrpheusAuthoringManifestEntryValue[] acceptedManifest = null,
            OrpheusAuthoringOwnedEventValue[] ownedEvents = null,
            OrpheusAuthoringExistingOutputValue[] existingOutputs = null,
            string[] otherGeneratedRoots = null,
            bool typedKeyOwnershipConflict = false,
            bool deleteTrackedOrphans = false,
            OrpheusAuthoringCatalogValue catalog = null,
            bool writeEnrollmentIdentity = false,
            string validationProfileAssetPath = "",
            string typedKeyAssemblyText = "",
            string typedKeySourceText = "")
        {
            AuthoringSchemaVersion = authoringSchemaVersion;
            Manifest = manifest;
            _recipes = recipes == null
                ? null
                : (OrpheusAuthoringModuleRecipeValue[])recipes.Clone();
            GeneratedRoot = generatedRoot ?? string.Empty;
            _acceptedManifest = acceptedManifest == null
                ? null
                : (OrpheusAuthoringManifestEntryValue[])acceptedManifest.Clone();
            _ownedEvents = ownedEvents == null
                ? Array.Empty<OrpheusAuthoringOwnedEventValue>()
                : (OrpheusAuthoringOwnedEventValue[])ownedEvents.Clone();
            _existingOutputs = existingOutputs == null
                ? Array.Empty<OrpheusAuthoringExistingOutputValue>()
                : (OrpheusAuthoringExistingOutputValue[])existingOutputs.Clone();
            _otherGeneratedRoots = otherGeneratedRoots == null
                ? Array.Empty<string>()
                : (string[])otherGeneratedRoots.Clone();
            TypedKeyOwnershipConflict = typedKeyOwnershipConflict;
            DeleteTrackedOrphans = deleteTrackedOrphans;
            Catalog = catalog;
            WriteEnrollmentIdentity = writeEnrollmentIdentity;
            ValidationProfileAssetPath = validationProfileAssetPath ?? string.Empty;
            TypedKeyAssemblyText = typedKeyAssemblyText ?? string.Empty;
            TypedKeySourceText = typedKeySourceText ?? string.Empty;
        }

        internal int AuthoringSchemaVersion { get; }

        internal OrpheusAuthoringManifestValue Manifest { get; }

        internal bool HasRecipeStorage => _recipes != null;

        internal int RecipeCount => _recipes == null ? 0 : _recipes.Length;

        internal string GeneratedRoot { get; }

        internal bool HasAcceptedManifest => _acceptedManifest != null;

        internal int AcceptedManifestCount =>
            _acceptedManifest == null ? 0 : _acceptedManifest.Length;

        internal int OwnedEventCount => _ownedEvents.Length;

        internal int ExistingOutputCount => _existingOutputs.Length;

        internal int OtherGeneratedRootCount => _otherGeneratedRoots.Length;

        internal bool TypedKeyOwnershipConflict { get; }

        internal bool DeleteTrackedOrphans { get; }

        internal OrpheusAuthoringCatalogValue Catalog { get; }

        internal bool WriteEnrollmentIdentity { get; }

        internal string ValidationProfileAssetPath { get; }

        internal string TypedKeyAssemblyText { get; }

        internal string TypedKeySourceText { get; }

        internal OrpheusAuthoringModuleRecipeValue GetRecipe(int index)
        {
            return _recipes[index];
        }

        internal OrpheusAuthoringManifestEntryValue GetAcceptedManifestEntry(int index)
        {
            return _acceptedManifest[index];
        }

        internal OrpheusAuthoringOwnedEventValue GetOwnedEvent(int index)
        {
            return _ownedEvents[index];
        }

        internal OrpheusAuthoringExistingOutputValue GetExistingOutput(int index)
        {
            return _existingOutputs[index];
        }

        internal string GetOtherGeneratedRoot(int index)
        {
            return _otherGeneratedRoots[index] ?? string.Empty;
        }
    }

    internal enum OrpheusAuthoringWriteKind : byte
    {
        CreateDirectory = 1,
        CreateEvent = 2,
        UpdateEvent = 3,
        KeepEvent = 4,
        WriteCatalog = 5,
        WriteTypedKeys = 6,
        WriteEnrollmentIdentity = 7,
        WriteOwnershipIndex = 8,
        WriteClosureReport = 9,
        DeleteTrackedOrphan = 10,
        MoveRenamedEvent = 11
    }

    internal readonly struct OrpheusAuthoringWriteOperationValue
    {
        internal OrpheusAuthoringWriteOperationValue(
            OrpheusAuthoringWriteKind kind,
            ushort key,
            string moduleId,
            string symbol,
            string sourcePath,
            string assetPath,
            string contentFingerprint)
        {
            Kind = kind;
            Key = key;
            ModuleId = moduleId ?? string.Empty;
            Symbol = symbol ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            ContentFingerprint = contentFingerprint ?? string.Empty;
        }

        internal OrpheusAuthoringWriteKind Kind { get; }

        internal ushort Key { get; }

        internal string ModuleId { get; }

        internal string Symbol { get; }

        internal string SourcePath { get; }

        internal string AssetPath { get; }

        internal string ContentFingerprint { get; }
    }

    internal sealed class OrpheusAuthoringCompiledEventValue
    {
        private readonly OrpheusAuthoringClipValue[] _clips;

        internal OrpheusAuthoringCompiledEventValue(
            ushort key,
            string moduleId,
            OrpheusAuthoringEventRecipeValue recipe,
            string assetPath,
            string contentFingerprint)
        {
            Key = key;
            ModuleId = moduleId ?? string.Empty;
            Symbol = recipe.Symbol;
            PlaybackKind = recipe.PlaybackKind;
            Category = recipe.Category;
            LoadPolicy = recipe.LoadPolicy;
            _clips = new OrpheusAuthoringClipValue[recipe.ClipCount];
            for (var index = 0; index < _clips.Length; index++)
            {
                _clips[index] = recipe.GetClip(index);
            }

            VolumeMinimum = recipe.VolumeMinimum;
            VolumeMaximum = recipe.VolumeMaximum;
            PitchMinimum = recipe.PitchMinimum;
            PitchMaximum = recipe.PitchMaximum;
            Priority = recipe.Priority;
            PolyphonyCap = recipe.PolyphonyCap;
            CooldownSeconds = recipe.CooldownSeconds;
            MinimumDistance = recipe.MinimumDistance;
            MaximumDistance = recipe.MaximumDistance;
            RolloffMode = recipe.RolloffMode;
            ProfileHint = recipe.ProfileHint;
            CandidateContentBankId = recipe.CandidateContentBankId;
            AssetPath = assetPath ?? string.Empty;
            ContentFingerprint = contentFingerprint ?? string.Empty;
        }

        internal ushort Key { get; }

        internal string ModuleId { get; }

        internal string Symbol { get; }

        internal OrpheusPlaybackKind PlaybackKind { get; }

        internal OrpheusCategory Category { get; }

        internal OrpheusLoadPolicy LoadPolicy { get; }

        internal int ClipCount => _clips.Length;

        internal float VolumeMinimum { get; }

        internal float VolumeMaximum { get; }

        internal float PitchMinimum { get; }

        internal float PitchMaximum { get; }

        internal byte Priority { get; }

        internal byte PolyphonyCap { get; }

        internal float CooldownSeconds { get; }

        internal float MinimumDistance { get; }

        internal float MaximumDistance { get; }

        internal OrpheusRolloffMode RolloffMode { get; }

        internal string ProfileHint { get; }

        internal string CandidateContentBankId { get; }

        internal string AssetPath { get; }

        internal string ContentFingerprint { get; }

        internal OrpheusAuthoringClipValue GetClip(int index)
        {
            return _clips[index];
        }
    }

    internal readonly struct OrpheusAuthoringFutureDeliveryValue
    {
        internal OrpheusAuthoringFutureDeliveryValue(
            string moduleId,
            string profileHint,
            string candidateContentBankId,
            ushort key,
            string symbol)
        {
            ModuleId = moduleId ?? string.Empty;
            ProfileHint = profileHint ?? string.Empty;
            CandidateContentBankId = candidateContentBankId ?? string.Empty;
            Key = key;
            Symbol = symbol ?? string.Empty;
        }

        internal string ModuleId { get; }

        internal string ProfileHint { get; }

        internal string CandidateContentBankId { get; }

        internal ushort Key { get; }

        internal string Symbol { get; }
    }

    internal sealed class OrpheusAuthoringCompilationPlan
    {
        private static readonly OrpheusAuthoringCompilationPlan EmptyPlan =
            new OrpheusAuthoringCompilationPlan(
                Array.Empty<OrpheusAuthoringCompiledEventValue>(),
                Array.Empty<ushort>(),
                Array.Empty<OrpheusAuthoringFutureDeliveryValue>(),
                Array.Empty<OrpheusAuthoringWriteOperationValue>(),
                string.Empty,
                string.Empty,
                 string.Empty,
                 string.Empty,
                 string.Empty,
                 string.Empty,
                 Array.Empty<OrpheusAuthoringOwnedEventValue>());

        private readonly OrpheusAuthoringCompiledEventValue[] _events;
        private readonly ushort[] _catalogKeys;
        private readonly OrpheusAuthoringFutureDeliveryValue[] _futureDelivery;
        private readonly OrpheusAuthoringWriteOperationValue[] _writeOperations;
        private readonly OrpheusAuthoringOwnedEventValue[] _orphans;

        internal OrpheusAuthoringCompilationPlan(
            OrpheusAuthoringCompiledEventValue[] events,
            ushort[] catalogKeys,
            OrpheusAuthoringFutureDeliveryValue[] futureDelivery,
            OrpheusAuthoringWriteOperationValue[] writeOperations,
            string inputFingerprint,
            string outputFingerprint,
            string catalogGuid,
            string catalogAssetPath,
            string typedKeyAssemblyText,
            string typedKeySourceText,
            OrpheusAuthoringOwnedEventValue[] orphans = null)
        {
            _events = events == null
                ? Array.Empty<OrpheusAuthoringCompiledEventValue>()
                : (OrpheusAuthoringCompiledEventValue[])events.Clone();
            _catalogKeys = catalogKeys == null
                ? Array.Empty<ushort>()
                : (ushort[])catalogKeys.Clone();
            _futureDelivery = futureDelivery == null
                ? Array.Empty<OrpheusAuthoringFutureDeliveryValue>()
                : (OrpheusAuthoringFutureDeliveryValue[])futureDelivery.Clone();
            _writeOperations = writeOperations == null
                ? Array.Empty<OrpheusAuthoringWriteOperationValue>()
                : (OrpheusAuthoringWriteOperationValue[])writeOperations.Clone();
            _orphans = orphans == null
                ? Array.Empty<OrpheusAuthoringOwnedEventValue>()
                : (OrpheusAuthoringOwnedEventValue[])orphans.Clone();
            InputFingerprint = inputFingerprint ?? string.Empty;
            OutputFingerprint = outputFingerprint ?? string.Empty;
            CatalogGuid = catalogGuid ?? string.Empty;
            CatalogAssetPath = catalogAssetPath ?? string.Empty;
            TypedKeyAssemblyText = typedKeyAssemblyText ?? string.Empty;
            TypedKeySourceText = typedKeySourceText ?? string.Empty;
        }

        internal static OrpheusAuthoringCompilationPlan Empty => EmptyPlan;

        internal int EventCount => _events.Length;

        internal ushort[] CatalogKeys => (ushort[])_catalogKeys.Clone();

        internal int FutureDeliveryCount => _futureDelivery.Length;

        internal int WriteOperationCount => _writeOperations.Length;

        internal int OrphanCount => _orphans.Length;

        internal string InputFingerprint { get; }

        internal string OutputFingerprint { get; }

        internal string CatalogGuid { get; }

        internal string CatalogAssetPath { get; }

        internal string TypedKeyAssemblyText { get; }

        internal string TypedKeySourceText { get; }

        internal OrpheusAuthoringCompiledEventValue GetEvent(int index)
        {
            return _events[index];
        }

        internal OrpheusAuthoringFutureDeliveryValue GetFutureDelivery(int index)
        {
            return _futureDelivery[index];
        }

        internal OrpheusAuthoringWriteOperationValue GetWriteOperation(int index)
        {
            return _writeOperations[index];
        }

        internal OrpheusAuthoringOwnedEventValue GetOrphan(int index)
        {
            return _orphans[index];
        }
    }
}
