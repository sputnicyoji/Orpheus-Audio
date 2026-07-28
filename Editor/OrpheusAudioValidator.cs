using System;
using System.Collections.Generic;
using System.IO;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor
{
    internal static class OrpheusAudioValidator
    {
        internal static List<OrpheusAudioValidationError> ValidateProfile(
            OrpheusAudioValidationProfile profile,
            BuildTargetGroup targetGroup)
        {
            return ValidateProfile(
                profile, targetGroup, OrpheusAudioTypedKeyProjection.ProductionPaths);
        }

        internal static List<OrpheusAudioValidationError> ValidateProfile(
            OrpheusAudioValidationProfile profile,
            BuildTargetGroup targetGroup,
            OrpheusAudioTypedKeyProjectionPaths projectionPaths)
        {
            var errors = new List<OrpheusAudioValidationError>();
            if (profile == null)
            {
                errors.Add(new OrpheusAudioValidationError(
                    OrpheusAudioValidationErrorCode.InvalidProfileSchema,
                    string.Empty,
                    string.Empty));
                return errors;
            }

            if (!profile.Enabled)
            {
                return errors;
            }

            var profilePath = AssetDatabase.GetAssetPath(profile);
            if (profile.SchemaVersion != OrpheusAudioAuthoringSchema.Current)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidProfileSchema, profilePath, profilePath);
            }

            var settings = profile.Settings;
            var catalog = profile.Catalog;
            var manifest = profile.KeyManifest;
            if (settings == null)
            {
                Add(errors, OrpheusAudioValidationErrorCode.MissingSettings, profilePath, profilePath);
            }

            if (catalog == null)
            {
                Add(errors, OrpheusAudioValidationErrorCode.MissingCatalog, profilePath, profilePath);
            }

            if (manifest == null)
            {
                Add(errors, OrpheusAudioValidationErrorCode.MissingKeyManifest, profilePath, profilePath);
            }

            if (settings != null && settings.SchemaVersion != OrpheusAudioAuthoringSchema.Current)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidSettingsSchema,
                    profilePath, AssetDatabase.GetAssetPath(settings));
            }

            var manifestValid = ValidateManifest(manifest, profilePath, errors);
            if (manifestValid)
            {
                ValidateProjection(manifest, profilePath, projectionPaths, errors);
            }

            if (catalog != null && catalog.SchemaVersion != OrpheusAudioAuthoringSchema.Current)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidCatalogSchema,
                    profilePath, AssetDatabase.GetAssetPath(catalog));
            }

            if (catalog != null && manifest != null)
            {
                ValidateCatalog(catalog, manifest, profilePath, targetGroup, errors);
            }

            OrpheusAudioIntegrationValidation.ValidateSettingsAndMixer(
                settings, profilePath, errors);
            OrpheusAudioIntegrationValidation.ValidateRuntimeHostAndSourceBank(
                profile.RuntimeHostPrefab, profilePath, errors);
            OrpheusAudioIntegrationValidation.ValidateListenerScenes(
                profile, profilePath, errors);

            return errors;
        }

        internal static OrpheusAudioImportPolicyMismatch EvaluateImporterPolicy(
            AudioImporter importer,
            OrpheusPlaybackKind playbackKind,
            OrpheusLoadPolicy loadPolicy,
            BuildTargetGroup targetGroup)
        {
            if (importer == null)
            {
                return OrpheusAudioImportPolicyMismatch.LoadPolicy |
                       OrpheusAudioImportPolicyMismatch.LoadType |
                       OrpheusAudioImportPolicyMismatch.PreloadAudioData |
                       OrpheusAudioImportPolicyMismatch.LoadInBackground |
                       OrpheusAudioImportPolicyMismatch.ForceToMono;
            }

            try
            {
                var sampleSettings = importer.GetOverrideSampleSettings(targetGroup);
                var values = new OrpheusAudioImportPolicyValues(
                    playbackKind,
                    loadPolicy,
                    MapLoadType(sampleSettings.loadType),
                    sampleSettings.preloadAudioData,
                    importer.loadInBackground,
                    importer.forceToMono);
                return OrpheusAudioImportPolicyEvaluator.Evaluate(values);
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                return OrpheusAudioImportPolicyMismatch.LoadPolicy |
                       OrpheusAudioImportPolicyMismatch.LoadType |
                       OrpheusAudioImportPolicyMismatch.PreloadAudioData |
                       OrpheusAudioImportPolicyMismatch.LoadInBackground |
                       OrpheusAudioImportPolicyMismatch.ForceToMono;
            }
        }

        private static bool ValidateManifest(
            OrpheusAudioKeyManifest manifest,
            string profilePath,
            List<OrpheusAudioValidationError> errors)
        {
            if (manifest == null)
            {
                return false;
            }

            var assetPath = AssetDatabase.GetAssetPath(manifest);
            var valid = true;
            if (manifest.SchemaVersion != OrpheusAudioAuthoringSchema.Current)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidManifestSchema,
                    profilePath, assetPath);
                valid = false;
            }

            if (!manifest.HasEntryStorage)
            {
                Add(errors, OrpheusAudioValidationErrorCode.InvalidManifestStorage,
                    profilePath, assetPath);
                return false;
            }

            var seenIds = new Dictionary<ushort, int>();
            var seenSymbols = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var entryIndex = 0; entryIndex < manifest.EntryCount; entryIndex++)
            {
                var entry = manifest.GetEntry(entryIndex);
                var mismatch = OrpheusAudioManifestPolicy.Evaluate(entry);
                if ((mismatch & OrpheusAudioManifestEntryMismatch.Id) != 0)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidManifestId,
                        profilePath, assetPath, -1, entryIndex);
                    valid = false;
                }

                if ((mismatch & OrpheusAudioManifestEntryMismatch.Symbol) != 0)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidManifestSymbol,
                        profilePath, assetPath, -1, entryIndex);
                    valid = false;
                }

                if ((mismatch & OrpheusAudioManifestEntryMismatch.Status) != 0)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidManifestStatus,
                        profilePath, assetPath, -1, entryIndex);
                    valid = false;
                }

                if (seenIds.ContainsKey(entry.Id))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.DuplicateManifestId,
                        profilePath, assetPath, -1, entryIndex);
                    valid = false;
                }
                else
                {
                    seenIds.Add(entry.Id, entryIndex);
                }

                if (entry.Symbol != null && seenSymbols.ContainsKey(entry.Symbol))
                {
                    Add(errors, OrpheusAudioValidationErrorCode.DuplicateManifestSymbol,
                        profilePath, assetPath, -1, entryIndex);
                    valid = false;
                }
                else if (entry.Symbol != null)
                {
                    seenSymbols.Add(entry.Symbol, entryIndex);
                }
            }

            return valid;
        }

        private static void ValidateProjection(
            OrpheusAudioKeyManifest manifest,
            string profilePath,
            OrpheusAudioTypedKeyProjectionPaths paths,
            List<OrpheusAudioValidationError> errors)
        {
            if (!File.Exists(paths.AssemblyPath) || !File.Exists(paths.SourcePath))
            {
                Add(errors, OrpheusAudioValidationErrorCode.MissingTypedKeyProjection,
                    profilePath, paths.SourcePath);
                return;
            }

            if (!OrpheusAudioTypedKeyProjection.IsCurrent(manifest, paths))
            {
                Add(errors, OrpheusAudioValidationErrorCode.StaleTypedKeyProjection,
                    profilePath, paths.SourcePath);
            }
        }

        private static void ValidateCatalog(
            OrpheusAudioCatalog catalog,
            OrpheusAudioKeyManifest manifest,
            string profilePath,
            BuildTargetGroup targetGroup,
            List<OrpheusAudioValidationError> errors)
        {
            var catalogPath = AssetDatabase.GetAssetPath(catalog);
            if (!catalog.HasEventList)
            {
                Add(errors, OrpheusAudioValidationErrorCode.NullEvent,
                    profilePath, catalogPath);
            }

            var manifestById = new Dictionary<ushort, OrpheusAudioKeyManifestEntryValue>();
            var activeCounts = new Dictionary<ushort, int>();
            for (var entryIndex = 0; entryIndex < manifest.EntryCount; entryIndex++)
            {
                var entry = manifest.GetEntry(entryIndex);
                if (!manifestById.ContainsKey(entry.Id))
                {
                    manifestById.Add(entry.Id, entry);
                    if (entry.Status == OrpheusAudioKeyStatus.Active)
                    {
                        activeCounts.Add(entry.Id, 0);
                    }
                }
            }

            var eventIndicesById = new Dictionary<ushort, int>();
            var hasPreviousKey = false;
            ushort previousKey = 0;
            for (var eventIndex = 0; eventIndex < catalog.EventCount; eventIndex++)
            {
                var audioEvent = catalog.GetEvent(eventIndex);
                if (audioEvent == null)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.NullEvent,
                        profilePath, catalogPath, eventIndex);
                    continue;
                }

                var eventPath = AssetDatabase.GetAssetPath(audioEvent);
                var rawKey = audioEvent.Key.Value;
                if (rawKey != 0 && hasPreviousKey && previousKey > rawKey)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.UnsortedCatalog,
                        profilePath, eventPath, eventIndex);
                }

                if (rawKey != 0)
                {
                    previousKey = rawKey;
                    hasPreviousKey = true;
                    if (eventIndicesById.ContainsKey(rawKey))
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.DuplicateEventKey,
                            profilePath, eventPath, eventIndex);
                    }
                    else
                    {
                        eventIndicesById.Add(rawKey, eventIndex);
                    }
                }

                var policyMismatch = OrpheusAudioEventPolicy.Evaluate(audioEvent.CapturePolicyValues());
                AddEventPolicyErrors(errors, policyMismatch, profilePath, eventPath, eventIndex);

                if (!manifestById.TryGetValue(rawKey, out var manifestEntry) ||
                    manifestEntry.Status != OrpheusAudioKeyStatus.Active)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.EventKeyNotActive,
                        profilePath, eventPath, eventIndex);
                }
                else
                {
                    activeCounts[rawKey] = activeCounts[rawKey] + 1;
                    var expectedName = "AE_" + manifestEntry.Symbol + ".asset";
                    if (!string.Equals(Path.GetFileName(eventPath), expectedName,
                            StringComparison.Ordinal))
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.InvalidEventAssetName,
                            profilePath, eventPath, eventIndex);
                    }
                }

                ValidateClips(audioEvent, profilePath, eventPath, eventIndex, targetGroup, errors);
            }

            for (var entryIndex = 0; entryIndex < manifest.EntryCount; entryIndex++)
            {
                var entry = manifest.GetEntry(entryIndex);
                if (entry.Status == OrpheusAudioKeyStatus.Active &&
                    activeCounts.TryGetValue(entry.Id, out var count) && count == 0)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.MissingActiveEvent,
                        profilePath, catalogPath, -1, entryIndex);
                }
            }
        }

        private static void ValidateClips(
            OrpheusAudioEvent audioEvent,
            string profilePath,
            string eventPath,
            int eventIndex,
            BuildTargetGroup targetGroup,
            List<OrpheusAudioValidationError> errors)
        {
            for (var clipIndex = 0; clipIndex < audioEvent.ClipCount; clipIndex++)
            {
                var clip = audioEvent.GetClip(clipIndex);
                if (clip == null)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.MissingClipReference,
                        profilePath, eventPath, eventIndex, clipIndex);
                    continue;
                }

                for (var previousIndex = 0; previousIndex < clipIndex; previousIndex++)
                {
                    if (ReferenceEquals(audioEvent.GetClip(previousIndex), clip))
                    {
                        Add(errors, OrpheusAudioValidationErrorCode.DuplicateClipReference,
                            profilePath, eventPath, eventIndex, clipIndex);
                        break;
                    }
                }

                ValidateImporter(audioEvent, clip, profilePath, eventPath,
                    eventIndex, clipIndex, targetGroup, errors);
            }
        }

        private static void ValidateImporter(
            OrpheusAudioEvent audioEvent,
            AudioClip clip,
            string profilePath,
            string eventPath,
            int eventIndex,
            int clipIndex,
            BuildTargetGroup targetGroup,
            List<OrpheusAudioValidationError> errors)
        {
            var clipPath = AssetDatabase.GetAssetPath(clip);
            var importer = AssetImporter.GetAtPath(clipPath) as AudioImporter;
            if (importer == null)
            {
                Add(errors, OrpheusAudioValidationErrorCode.MissingAudioImporter,
                    profilePath, eventPath, eventIndex, clipIndex);
                return;
            }

            var mismatch = EvaluateImporterPolicy(
                importer, audioEvent.PlaybackKind, audioEvent.LoadPolicy, targetGroup);
            AddImportPolicyErrors(errors, mismatch, profilePath, eventPath, eventIndex, clipIndex);
        }

        private static OrpheusAudioImportLoadType MapLoadType(AudioClipLoadType loadType)
        {
            switch (loadType)
            {
                case AudioClipLoadType.DecompressOnLoad:
                    return OrpheusAudioImportLoadType.DecompressOnLoad;
                case AudioClipLoadType.CompressedInMemory:
                    return OrpheusAudioImportLoadType.CompressedInMemory;
                case AudioClipLoadType.Streaming:
                    return OrpheusAudioImportLoadType.Streaming;
                default:
                    return OrpheusAudioImportLoadType.Invalid;
            }
        }

        private static void AddEventPolicyErrors(
            List<OrpheusAudioValidationError> errors,
            OrpheusAudioEventPolicyMismatch mismatch,
            string profilePath,
            string eventPath,
            int eventIndex)
        {
            var raw = (uint)mismatch;
            for (var bit = 0; bit < 32; bit++)
            {
                var flag = 1u << bit;
                if ((raw & flag) != 0)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidEventPolicy,
                        profilePath, eventPath, eventIndex, -1, flag);
                }
            }
        }

        private static void AddImportPolicyErrors(
            List<OrpheusAudioValidationError> errors,
            OrpheusAudioImportPolicyMismatch mismatch,
            string profilePath,
            string eventPath,
            int eventIndex,
            int clipIndex)
        {
            var raw = (byte)mismatch;
            for (var bit = 0; bit < 8; bit++)
            {
                var flag = (byte)(1 << bit);
                if ((raw & flag) != 0)
                {
                    Add(errors, OrpheusAudioValidationErrorCode.InvalidAudioImporterPolicy,
                        profilePath, eventPath, eventIndex, clipIndex, flag);
                }
            }
        }

        private static void Add(
            List<OrpheusAudioValidationError> errors,
            OrpheusAudioValidationErrorCode code,
            string profilePath,
            string assetPath,
            int eventIndex = -1,
            int relatedIndex = -1,
            uint detail = 0)
        {
            errors.Add(new OrpheusAudioValidationError(
                code, profilePath, assetPath, eventIndex, relatedIndex, detail));
        }
    }
}
