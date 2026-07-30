using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor
{
    internal readonly struct OrpheusAudioAuthoringUnityIdentity
    {
        internal OrpheusAudioAuthoringUnityIdentity(
            string kind,
            string assetPath,
            string guid,
            long localFileId)
        {
            Kind = kind ?? string.Empty;
            AssetPath = assetPath ?? string.Empty;
            Guid = guid ?? string.Empty;
            LocalFileId = localFileId;
        }

        internal string Kind { get; }
        internal string AssetPath { get; }
        internal string Guid { get; }
        internal long LocalFileId { get; }
    }

    internal sealed class OrpheusAudioAuthoringUnityIdentitySnapshot
    {
        private readonly List<OrpheusAudioAuthoringUnityIdentity> _items =
            new List<OrpheusAudioAuthoringUnityIdentity>();

        internal int Count => _items.Count;

        internal OrpheusAudioAuthoringUnityIdentity Get(int index)
        {
            return _items[index];
        }

        internal void Add(
            string kind,
            string path,
            string guid,
            long localFileId)
        {
            _items.Add(
                new OrpheusAudioAuthoringUnityIdentity(
                    kind,
                    path,
                    guid,
                    localFileId));
        }
    }

    internal static class OrpheusAudioAuthoringCapture
    {
        private const char AssetPathSeparator = (char)47;
        private const string OwnershipFileName = "OrpheusAuthoringOwnership.json";
        private const string CatalogFileName = "OrpheusAudioCatalog.asset";
        private const string TypedKeyRoot = "Assets/OrpheusGenerated";
        private const string TransactionsRoot =
            "Library/Orpheus/AuthoringTransactions";

        internal static bool TryCapture(
            OrpheusAudioValidationProfile selected,
            OrpheusAudioAuthoringCompileMode mode,
            BuildTargetGroup targetGroup,
            out OrpheusAuthoringCompilationInput input,
            out OrpheusAuthoringCompilationPlan plan,
            out OrpheusAuthoringCompilationError[] errors)
        {
            return TryCapture(
                selected,
                mode,
                targetGroup,
                null,
                out input,
                out plan,
                out errors);
        }

        internal static bool TryCapture(
            OrpheusAudioValidationProfile selected,
            OrpheusAudioAuthoringCompileMode mode,
            BuildTargetGroup targetGroup,
            OrpheusAudioValidationProfile[] discoveredProfiles,
            out OrpheusAuthoringCompilationInput input,
            out OrpheusAuthoringCompilationPlan plan,
            out OrpheusAuthoringCompilationError[] errors)
        {
            return TryCaptureDetailed(
                selected,
                mode,
                targetGroup,
                discoveredProfiles,
                out input,
                out plan,
                out _,
                out errors);
        }

        internal static bool TryCaptureDetailed(
            OrpheusAudioValidationProfile selected,
            OrpheusAudioAuthoringCompileMode mode,
            BuildTargetGroup targetGroup,
            OrpheusAudioValidationProfile[] discoveredProfiles,
            out OrpheusAuthoringCompilationInput input,
            out OrpheusAuthoringCompilationPlan plan,
            out OrpheusAudioAuthoringUnityIdentitySnapshot identities,
            out OrpheusAuthoringCompilationError[] errors)
        {
            return TryCaptureDetailed(
                selected,
                mode,
                targetGroup,
                discoveredProfiles,
                string.Empty,
                out input,
                out plan,
                out identities,
                out errors);
        }

        internal static bool TryCaptureDetailed(
            OrpheusAudioValidationProfile selected,
            OrpheusAudioAuthoringCompileMode mode,
            BuildTargetGroup targetGroup,
            OrpheusAudioValidationProfile[] discoveredProfiles,
            string allowedValidatingTransactionId,
            out OrpheusAuthoringCompilationInput input,
            out OrpheusAuthoringCompilationPlan plan,
            out OrpheusAudioAuthoringUnityIdentitySnapshot identities,
            out OrpheusAuthoringCompilationError[] errors)
        {
            input = null;
            plan = OrpheusAuthoringCompilationPlan.Empty;
            identities = new OrpheusAudioAuthoringUnityIdentitySnapshot();
            var collected = new List<OrpheusAuthoringCompilationError>();
            ValidateJournal(collected, allowedValidatingTransactionId);

            if (selected == null)
            {
                Add(collected, OrpheusAuthoringErrorCode.InvalidValidationProfile);
                return Fail(collected, out errors);
            }

            var selectedPath = GetSavedMainAssetPath(selected);
            if (selectedPath.Length == 0)
            {
                Add(
                    collected,
                    OrpheusAuthoringErrorCode.InvalidValidationProfile,
                    assetPath: AssetDatabase.GetAssetPath(selected),
                    detail: "SavedMainAsset");
            }

            if (!selected.Enabled)
            {
                Add(
                    collected,
                    OrpheusAuthoringErrorCode.DisabledValidationProfile,
                    assetPath: selectedPath);
            }

            if (selected.SchemaVersion != OrpheusAudioAuthoringSchema.Current)
            {
                Add(
                    collected,
                    OrpheusAuthoringErrorCode.SchemaVersionMismatch,
                    assetPath: selectedPath,
                    detail: "ValidationProfile");
            }

            var authoring = selected.AuthoringProfile;
            var enrollment = selected.AuthoringEnrollmentGuid ?? string.Empty;
            if (authoring == null)
            {
                Add(
                    collected,
                    enrollment.Length == 0
                        ? OrpheusAuthoringErrorCode.ManualProfileNotCompilable
                        : OrpheusAuthoringErrorCode.EnrollmentIdentityMismatch,
                    assetPath: selectedPath);
                return Fail(collected, out errors);
            }

            var authoringPath = GetSavedMainAssetPath(authoring);
            if (authoringPath.Length == 0)
            {
                Add(
                    collected,
                    OrpheusAuthoringErrorCode.InvalidValidationProfile,
                    assetPath: AssetDatabase.GetAssetPath(authoring),
                    detail: "AuthoringSavedMainAsset");
            }

            if (!TryGetIdentity(
                    authoring,
                    authoringPath,
                    out var authoringGuid,
                    out var authoringLocalId))
            {
                Add(
                    collected,
                    OrpheusAuthoringErrorCode.InvalidValidationProfile,
                    assetPath: authoringPath,
                    detail: "AuthoringIdentity");
            }
            else
            {
                identities.Add(
                    "authoring-profile",
                    authoringPath,
                    authoringGuid,
                    authoringLocalId);
            }

            if (!TryGetIdentity(
                    selected,
                    selectedPath,
                    out var validationGuid,
                    out var validationLocalId))
            {
                Add(
                    collected,
                    OrpheusAuthoringErrorCode.InvalidValidationProfile,
                    assetPath: selectedPath,
                    detail: "ValidationIdentity");
            }
            else
            {
                identities.Add(
                    "validation-profile",
                    selectedPath,
                    validationGuid,
                    validationLocalId);
            }
            var pending = enrollment.Length == 0;
            var enrolled = !pending &&
                           string.Equals(enrollment, authoringGuid, StringComparison.Ordinal);
            if (!pending && !enrolled)
            {
                Add(
                    collected,
                    OrpheusAuthoringErrorCode.EnrollmentIdentityMismatch,
                    assetPath: selectedPath,
                    detail: enrollment);
            }

            if (pending &&
                mode != OrpheusAudioAuthoringCompileMode.Analyze &&
                mode != OrpheusAudioAuthoringCompileMode.CompileAndAcceptEnrollment ||
                enrolled && mode == OrpheusAudioAuthoringCompileMode.CompileAndAcceptEnrollment ||
                mode == OrpheusAudioAuthoringCompileMode.Invalid)
            {
                Add(
                    collected,
                    OrpheusAuthoringErrorCode.EnrollmentIdentityMismatch,
                    assetPath: selectedPath,
                    detail: "Mode");
            }

            var capturedProfile = authoring.CaptureValue();
            ValidateSourceAssetIdentities(
                authoring,
                capturedProfile,
                identities,
                collected);
            if (!TryCanonicalAssetDirectory(
                    capturedProfile.GeneratedRoot,
                    out var generatedRoot,
                    out var rootDetail))
            {
                Add(
                    collected,
                    OrpheusAuthoringErrorCode.InvalidGeneratedRoot,
                    assetPath: capturedProfile.GeneratedRoot,
                    detail: rootDetail);
                generatedRoot = capturedProfile.GeneratedRoot ?? string.Empty;
            }

            ValidateProjectProfiles(
                selected,
                selectedPath,
                authoring,
                generatedRoot,
                discoveredProfiles,
                identities,
                collected,
                out var otherRoots);

            if (capturedProfile.KeyManifest == null)
            {
                Add(collected, OrpheusAuthoringErrorCode.MissingManifest, assetPath: authoringPath);
            }

            if (!ReferenceEquals(capturedProfile.KeyManifest, selected.KeyManifest))
            {
                Add(
                    collected,
                    OrpheusAuthoringErrorCode.AuthoringProfileConflict,
                    assetPath: selectedPath,
                    detail: "Manifest");
            }

            if (!ReferenceEquals(capturedProfile.Catalog, selected.Catalog))
            {
                Add(
                    collected,
                    OrpheusAuthoringErrorCode.CatalogMismatch,
                    assetPath: selectedPath,
                    detail: "Reference");
            }

            var manifest = CaptureManifest(capturedProfile.KeyManifest);
            CaptureRecipes(
                capturedProfile,
                targetGroup,
                identities,
                collected,
                out var recipes);

            var expectedCatalogPath =
                generatedRoot + AssetPathSeparator + CatalogFileName;
            var catalog = CaptureCatalog(
                capturedProfile.Catalog,
                expectedCatalogPath,
                pending,
                identities,
                collected,
                out var catalogGuid);

            var accepted = (OrpheusAuthoringManifestEntryValue[])null;
            var owned = Array.Empty<OrpheusAuthoringOwnedEventValue>();
            var ownershipArtifacts = new Dictionary<string, OwnershipArtifact>(
                StringComparer.OrdinalIgnoreCase);
            if (enrolled)
            {
                ReadOwnership(
                    generatedRoot,
                    authoringGuid,
                    validationGuid,
                    GetGuid(capturedProfile.KeyManifest),
                    catalogGuid,
                    collected,
                    out accepted,
                    out owned,
                    ownershipArtifacts);
                owned = CaptureOwnedEventFingerprints(
                    owned,
                    recipes,
                    collected);
            }
            else
            {
                ValidateNoCatalogOwnership(catalogGuid, generatedRoot, collected);
            }

            var typedAssembly = OrpheusAudioTypedKeyProjection.ExpectedAssembly;
            var typedSource = string.Empty;
            if (capturedProfile.KeyManifest == null ||
                !OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                    capturedProfile.KeyManifest,
                    out typedSource,
                    out _))
            {
                typedSource = string.Empty;
            }

            var existing = CaptureExistingOutputs(
                generatedRoot,
                recipes,
                ownershipArtifacts,
                pending,
                typedAssembly,
                typedSource,
                collected);

            var typedConflict = CaptureTypedKeyConflict(
                pending,
                ownershipArtifacts,
                typedAssembly,
                typedSource,
                collected);

            input = new OrpheusAuthoringCompilationInput(
                capturedProfile.SchemaVersion,
                manifest,
                recipes,
                generatedRoot,
                accepted,
                owned,
                existing,
                otherRoots,
                typedConflict,
                mode == OrpheusAudioAuthoringCompileMode.CompileAndDeleteTrackedOrphans,
                catalog,
                pending && mode ==
                    OrpheusAudioAuthoringCompileMode.CompileAndAcceptEnrollment,
                selectedPath,
                typedAssembly,
                typedSource);

            if (collected.Count != 0)
            {
                return Fail(collected, out errors);
            }

            if (!OrpheusAuthoringCompilationPolicy.TryCompile(
                    input,
                    out plan,
                    out errors))
            {
                return false;
            }

            return true;
        }

        internal static OrpheusAuthoringOwnedEventValue[] CaptureOwnedEventFingerprints(
            OrpheusAuthoringOwnedEventValue[] owned,
            OrpheusAuthoringModuleRecipeValue[] recipes,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            if (owned == null || owned.Length == 0)
            {
                return Array.Empty<OrpheusAuthoringOwnedEventValue>();
            }

            var result = new OrpheusAuthoringOwnedEventValue[owned.Length];
            for (var ownedIndex = 0; ownedIndex < owned.Length; ownedIndex++)
            {
                var tracked = owned[ownedIndex];
                var fingerprint = string.Empty;
                var recipe = FindEventRecipe(
                    recipes,
                    tracked.ModuleId,
                    tracked.Symbol);
                var actual = recipe == null
                    ? null
                    : AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(
                        tracked.AssetPath);
                if (actual != null)
                {
                    var serialized = new SerializedObject(actual);
                    if (serialized.FindProperty("_schemaVersion").intValue !=
                            OrpheusAudioAuthoringSchema.Current ||
                        serialized.FindProperty("_key").intValue != tracked.Key)
                    {
                        Add(
                            errors,
                            OrpheusAuthoringErrorCode.ActualOutputFingerprintMismatch,
                            tracked.Key,
                            tracked.ModuleId,
                            tracked.Symbol,
                            tracked.AssetPath);
                    }
                    else
                    {
                        var expected = new OrpheusAuthoringCompiledEventValue(
                            tracked.Key,
                            tracked.ModuleId,
                            recipe,
                            tracked.AssetPath,
                            string.Empty);
                        fingerprint =
                            OrpheusAudioAuthoringTransaction
                                .ComputeActualEventFingerprint(actual, expected);
                    }
                }

                result[ownedIndex] = new OrpheusAuthoringOwnedEventValue(
                    tracked.Key,
                    tracked.ModuleId,
                    tracked.Symbol,
                    tracked.Guid,
                    tracked.AssetPath,
                    fingerprint);
            }

            return result;
        }

        private static OrpheusAuthoringEventRecipeValue FindEventRecipe(
            OrpheusAuthoringModuleRecipeValue[] recipes,
            string moduleId,
            string symbol)
        {
            if (recipes == null)
            {
                return null;
            }

            for (var recipeIndex = 0; recipeIndex < recipes.Length; recipeIndex++)
            {
                var module = recipes[recipeIndex];
                if (module == null ||
                    !string.Equals(
                        module.ModuleId,
                        moduleId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                for (var eventIndex = 0; eventIndex < module.EventCount; eventIndex++)
                {
                    var recipe = module.GetEvent(eventIndex);
                    if (recipe != null &&
                        string.Equals(
                            recipe.Symbol,
                            symbol,
                            StringComparison.Ordinal))
                    {
                        return recipe;
                    }
                }
            }

            return null;
        }

        private static OrpheusAuthoringManifestValue CaptureManifest(
            OrpheusAudioKeyManifest manifest)
        {
            if (manifest == null)
            {
                return null;
            }

            var source = manifest.CaptureEntries();
            if (source == null)
            {
                return new OrpheusAuthoringManifestValue(manifest.SchemaVersion, null);
            }

            var values = new OrpheusAuthoringManifestEntryValue[source.Length];
            for (var index = 0; index < source.Length; index++)
            {
                values[index] = new OrpheusAuthoringManifestEntryValue(
                    source[index].Id,
                    source[index].Symbol,
                    source[index].Status);
            }

            return new OrpheusAuthoringManifestValue(manifest.SchemaVersion, values);
        }

        private static void ValidateSourceAssetIdentities(
            OrpheusAudioAuthoringProfile authoring,
            OrpheusAudioAuthoringProfileValue captured,
            OrpheusAudioAuthoringUnityIdentitySnapshot identities,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            if (captured.KeyManifest != null)
            {
                var manifestPath = GetSavedMainAssetPath(captured.KeyManifest);
                if (!TryGetIdentity(
                        captured.KeyManifest,
                        manifestPath,
                        out var manifestGuid,
                        out var manifestLocalId))
                {
                    Add(
                        errors,
                        OrpheusAuthoringErrorCode.MissingManifest,
                        assetPath: manifestPath,
                        detail: "Identity");
                }
                else
                {
                    identities.Add(
                        "manifest",
                        manifestPath,
                        manifestGuid,
                        manifestLocalId);
                }
            }

            var serialized = new SerializedObject(authoring);
            var recipes = serialized.FindProperty("_moduleRecipes");
            if (recipes == null || !recipes.isArray)
            {
                return;
            }

            for (var index = 0; index < recipes.arraySize; index++)
            {
                var recipe =
                    recipes.GetArrayElementAtIndex(index).objectReferenceValue;
                if (recipe == null)
                {
                    continue;
                }

                var path = AssetDatabase.GetAssetPath(recipe)
                    .Replace('\\', AssetPathSeparator);
                if (!TryCanonicalAssetOrGeneratedPath(path, out var canonicalPath))
                {
                    path = string.Empty;
                }
                else
                {
                    path = canonicalPath;
                }

                if (!TryGetIdentity(
                        recipe,
                        path,
                        out var recipeGuid,
                        out var recipeLocalId))
                {
                    Add(
                        errors,
                        OrpheusAuthoringErrorCode.NullRecipe,
                        assetPath: path,
                        detail: "Identity");
                }
                else
                {
                    identities.Add("recipe", path, recipeGuid, recipeLocalId);
                }
            }
        }

        private static void CaptureRecipes(
            OrpheusAudioAuthoringProfileValue profile,
            BuildTargetGroup targetGroup,
            OrpheusAudioAuthoringUnityIdentitySnapshot identities,
            ICollection<OrpheusAuthoringCompilationError> errors,
            out OrpheusAuthoringModuleRecipeValue[] recipes)
        {
            if (!profile.HasModuleRecipeStorage)
            {
                recipes = null;
                return;
            }

            recipes = new OrpheusAuthoringModuleRecipeValue[profile.ModuleRecipeCount];
            for (var recipeIndex = 0; recipeIndex < recipes.Length; recipeIndex++)
            {
                if (!profile.TryGetModuleRecipe(recipeIndex, out var recipe))
                {
                    recipes[recipeIndex] = null;
                    continue;
                }

                OrpheusAuthoringEventRecipeValue[] events = null;
                if (recipe.HasEventStorage)
                {
                    events = new OrpheusAuthoringEventRecipeValue[recipe.EventCount];
                    for (var eventIndex = 0; eventIndex < events.Length; eventIndex++)
                    {
                        var source = recipe.GetEvent(eventIndex);
                        OrpheusAuthoringClipValue[] clips = null;
                        if (source.HasClipStorage)
                        {
                            clips = new OrpheusAuthoringClipValue[source.ClipCount];
                            for (var clipIndex = 0; clipIndex < clips.Length; clipIndex++)
                            {
                                var clip = source.GetClip(clipIndex);
                                var clipPath = clip == null
                                    ? string.Empty
                                    : AssetDatabase.GetAssetPath(clip)
                                        .Replace('\\', AssetPathSeparator);
                                if (!TryCanonicalAssetOrGeneratedPath(
                                        clipPath,
                                        out var canonicalClipPath))
                                {
                                    canonicalClipPath = string.Empty;
                                }

                                clipPath = canonicalClipPath;
                                if (!TryGetIdentity(
                                        clip,
                                        clipPath,
                                        out var guid,
                                        out var localId))
                                {
                                    Add(
                                        errors,
                                        OrpheusAuthoringErrorCode.InvalidClipIdentity,
                                        moduleId: recipe.ModuleId,
                                        symbol: source.Symbol,
                                        assetPath: clipPath,
                                        clipIndex: clipIndex);
                                }

                                if (clipPath.Length == 0 ||
                                    !(AssetImporter.GetAtPath(clipPath) is AudioImporter importer))
                                {
                                    Add(
                                        errors,
                                        OrpheusAuthoringErrorCode.InvalidClipIdentity,
                                        moduleId: recipe.ModuleId,
                                        symbol: source.Symbol,
                                        assetPath: clipPath,
                                        clipIndex: clipIndex,
                                        detail: "AudioImporter");
                                }
                                else
                                {
                                    var mismatch = OrpheusAudioValidator.EvaluateImporterPolicy(
                                        importer,
                                        source.PlaybackKind,
                                        source.LoadPolicy,
                                        targetGroup);
                                    if (mismatch != OrpheusAudioImportPolicyMismatch.None)
                                    {
                                        Add(
                                            errors,
                                            OrpheusAuthoringErrorCode.ImporterPolicyMismatch,
                                            moduleId: recipe.ModuleId,
                                            symbol: source.Symbol,
                                            assetPath: clipPath,
                                            clipIndex: clipIndex,
                                            detail: ((uint)mismatch).ToString(
                                                CultureInfo.InvariantCulture));
                                    }
                                }

                                clips[clipIndex] =
                                    new OrpheusAuthoringClipValue(guid, localId, clipPath);
                                if (guid.Length != 0)
                                {
                                    identities.Add("clip", clipPath, guid, localId);
                                }
                            }
                        }

                        events[eventIndex] = new OrpheusAuthoringEventRecipeValue(
                            source.Symbol,
                            source.PlaybackKind,
                            source.Category,
                            source.LoadPolicy,
                            clips,
                            source.VolumeMin,
                            source.VolumeMax,
                            source.PitchMin,
                            source.PitchMax,
                            source.Priority,
                            source.PolyphonyCap,
                            source.CooldownSeconds,
                            source.MinimumDistance,
                            source.MaximumDistance,
                            source.RolloffMode,
                            source.ProfileHint,
                            source.CandidateContentBankId);
                    }
                }

                recipes[recipeIndex] = new OrpheusAuthoringModuleRecipeValue(
                    recipe.SchemaVersion,
                    recipe.ModuleId,
                    events);
            }
        }

        private static OrpheusAuthoringCatalogValue CaptureCatalog(
            OrpheusAudioCatalog catalog,
            string expectedPath,
            bool pending,
            OrpheusAudioAuthoringUnityIdentitySnapshot identities,
            ICollection<OrpheusAuthoringCompilationError> errors,
            out string catalogGuid)
        {
            catalogGuid = string.Empty;
            if (catalog == null)
            {
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.CatalogMismatch,
                    assetPath: expectedPath,
                    detail: "Missing");
                return null;
            }

            var path = GetSavedMainAssetPath(catalog);
            if (!TryGetIdentity(
                    catalog,
                    path,
                    out catalogGuid,
                    out var catalogLocalId))
            {
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.CatalogMismatch,
                    assetPath: path,
                    detail: "Identity");
            }
            else
            {
                identities.Add("catalog", path, catalogGuid, catalogLocalId);
            }
            if (!string.Equals(path, expectedPath, StringComparison.Ordinal))
            {
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.CatalogMismatch,
                    assetPath: path,
                    detail: expectedPath);
            }

            if (catalog.SchemaVersion != OrpheusAudioAuthoringSchema.Current ||
                !catalog.HasEventList)
            {
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.CatalogMismatch,
                    assetPath: path,
                    detail: "SchemaOrStorage");
            }

            if (pending && catalog.EventCount != 0)
            {
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.CatalogNotEmptyAtEnrollment,
                    assetPath: path,
                    detail: catalog.EventCount.ToString(CultureInfo.InvariantCulture));
            }

            var catalogEventPaths = catalog.HasEventList
                ? new string[catalog.EventCount]
                : null;
            if (catalogEventPaths != null)
            {
                for (var index = 0; index < catalogEventPaths.Length; index++)
                {
                    catalogEventPaths[index] =
                        AssetDatabase.GetAssetPath(catalog.GetEvent(index));
                }
            }

            return new OrpheusAuthoringCatalogValue(
                catalogGuid,
                path,
                catalog.EventCount,
                false,
                catalogEventPaths);
        }

        private static void ValidateProjectProfiles(
            OrpheusAudioValidationProfile selected,
            string selectedPath,
            OrpheusAudioAuthoringProfile selectedAuthoring,
            string selectedRoot,
            OrpheusAudioValidationProfile[] discoveredProfiles,
            OrpheusAudioAuthoringUnityIdentitySnapshot identities,
            ICollection<OrpheusAuthoringCompilationError> errors,
            out string[] otherRoots)
        {
            var roots = new List<string>();
            var owners = 0;
            var profiles = discoveredProfiles ??
                           OrpheusAudioValidationProfileDiscovery.Discover();
            for (var index = 0; index < profiles.Length; index++)
            {
                var profile = profiles[index];
                if (profile == null)
                {
                    continue;
                }

                var path = GetSavedMainAssetPath(profile);
                if (path.Length == 0)
                {
                    Add(
                        errors,
                        OrpheusAuthoringErrorCode.InvalidValidationProfile,
                        assetPath: AssetDatabase.GetAssetPath(profile),
                        detail: "DiscoveredProfilePath");
                }
                else if (!ReferenceEquals(profile, selected) &&
                         TryGetIdentity(
                             profile,
                             path,
                             out var profileGuid,
                             out var profileLocalId))
                {
                    identities.Add(
                        "validation-profile",
                        path,
                        profileGuid,
                        profileLocalId);
                }

                var authoring = profile.AuthoringProfile;
                var enrollment = profile.AuthoringEnrollmentGuid ?? string.Empty;
                if (authoring != null)
                {
                    owners++;
                    var authoringPath = GetSavedMainAssetPath(authoring);
                    if (authoringPath.Length == 0)
                    {
                        Add(
                            errors,
                            OrpheusAuthoringErrorCode.InvalidValidationProfile,
                            assetPath: AssetDatabase.GetAssetPath(authoring),
                            detail: "DiscoveredAuthoringPath");
                    }
                    else if (!ReferenceEquals(authoring, selectedAuthoring) &&
                             TryGetIdentity(
                                 authoring,
                                 authoringPath,
                                 out var authoringGuid,
                                 out var authoringLocalId))
                    {
                        identities.Add(
                            "authoring-profile",
                            authoringPath,
                            authoringGuid,
                            authoringLocalId);
                    }

                    var value = authoring.CaptureValue();
                    if (TryCanonicalAssetDirectory(
                            value.GeneratedRoot,
                            out var root,
                            out _))
                    {
                        if (!ReferenceEquals(profile, selected))
                        {
                            roots.Add(root);
                        }

                        if (!ReferenceEquals(profile, selected) &&
                            RootsOverlap(selectedRoot, root))
                        {
                            Add(
                                errors,
                                OrpheusAuthoringErrorCode.GeneratedRootOverlap,
                                assetPath: path,
                                detail: root);
                        }
                    }
                }
                else if (enrollment.Length != 0)
                {
                    Add(
                        errors,
                        OrpheusAuthoringErrorCode.EnrollmentIdentityMismatch,
                        assetPath: path);
                }

                if (ReferenceEquals(profile, selected) ||
                    !profile.Enabled ||
                    authoring != null)
                {
                    continue;
                }

                if (!ReferenceEquals(profile.KeyManifest, selected.KeyManifest))
                {
                    Add(
                        errors,
                        OrpheusAuthoringErrorCode.ManualProfileWouldBecomeStale,
                        assetPath: path,
                        detail: "Manifest");
                }
                else if (!CatalogUsesOnlyCurrentManifest(
                             profile.Catalog,
                             selected.KeyManifest))
                {
                    Add(
                        errors,
                        OrpheusAuthoringErrorCode.ManualProfileWouldBecomeStale,
                        assetPath: path,
                        detail: "Catalog");
                }
            }

            if (owners != 1)
            {
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.AuthoringProfileConflict,
                    assetPath: selectedPath,
                    detail: owners.ToString(CultureInfo.InvariantCulture));
            }

            roots.Sort(StringComparer.Ordinal);
            otherRoots = roots.ToArray();
        }

        private static bool CatalogUsesOnlyCurrentManifest(
            OrpheusAudioCatalog catalog,
            OrpheusAudioKeyManifest manifest)
        {
            if (catalog == null || manifest == null ||
                !catalog.HasEventList || !manifest.HasEntryStorage)
            {
                return false;
            }

            var active = new HashSet<ushort>();
            var entries = manifest.CaptureEntries();
            for (var index = 0; index < entries.Length; index++)
            {
                if (entries[index].Status == OrpheusAudioKeyStatus.Active)
                {
                    active.Add(entries[index].Id);
                }
            }

            for (var index = 0; index < catalog.EventCount; index++)
            {
                var audioEvent = catalog.GetEvent(index);
                if (audioEvent == null || !active.Contains(audioEvent.Key.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static OrpheusAuthoringExistingOutputValue[] CaptureExistingOutputs(
            string generatedRoot,
            OrpheusAuthoringModuleRecipeValue[] recipes,
            IDictionary<string, OwnershipArtifact> owned,
            bool pending,
            string expectedAssembly,
            string expectedSource,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (recipes != null && generatedRoot.StartsWith("Assets/", StringComparison.Ordinal))
            {
                for (var recipeIndex = 0; recipeIndex < recipes.Length; recipeIndex++)
                {
                    var recipe = recipes[recipeIndex];
                    if (recipe == null)
                    {
                        continue;
                    }

                    for (var eventIndex = 0; eventIndex < recipe.EventCount; eventIndex++)
                    {
                        var item = recipe.GetEvent(eventIndex);
                        if (item != null)
                        {
                            paths.Add(
                                generatedRoot + AssetPathSeparator +
                                "Events/AE_" + item.Symbol + ".asset");
                        }
                    }
                }
            }

            foreach (var pair in owned)
            {
                if (string.Equals(pair.Value.kind, "event", StringComparison.Ordinal))
                {
                    paths.Add(pair.Key);
                }
            }

            paths.Add(
                generatedRoot + AssetPathSeparator +
                "OrpheusAuthoringOwnership.json");
            paths.Add(
                generatedRoot + AssetPathSeparator +
                "OrpheusAuthoringClosure.json");
            paths.Add(OrpheusAudioTypedKeyProjection.AssemblyPath);
            paths.Add(OrpheusAudioTypedKeyProjection.SourcePath);
            var outputs = new List<OrpheusAuthoringExistingOutputValue>();
            foreach (var path in paths)
            {
                var exists = File.Exists(ProjectAbsolute(path)) ||
                             AssetDatabase.LoadMainAssetAtPath(path) != null;
                if (!exists)
                {
                    continue;
                }

                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                var guid = AssetDatabase.AssetPathToGUID(path).ToLowerInvariant();
                var type = asset == null ? string.Empty : asset.GetType().FullName;
                var compilerOwned = false;
                if (owned.TryGetValue(path, out var artifact))
                {
                    compilerOwned =
                        !string.IsNullOrEmpty(artifact.guid) &&
                        string.Equals(
                        artifact.guid,
                        guid,
                        StringComparison.Ordinal);
                }
                if (pending &&
                    string.Equals(
                        path,
                        OrpheusAudioTypedKeyProjection.AssemblyPath,
                        StringComparison.Ordinal))
                {
                    compilerOwned = TextEquals(path, expectedAssembly);
                }
                else if (pending &&
                         string.Equals(
                             path,
                             OrpheusAudioTypedKeyProjection.SourcePath,
                             StringComparison.Ordinal))
                {
                    compilerOwned = TextEquals(path, expectedSource);
                }
                outputs.Add(
                    new OrpheusAuthoringExistingOutputValue(
                        path,
                        compilerOwned,
                        guid,
                        type,
                        true));
            }

            return outputs.ToArray();
        }

        private static bool CaptureTypedKeyConflict(
            bool pending,
            IDictionary<string, OwnershipArtifact> owned,
            string expectedAssembly,
            string expectedSource,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            var conflict = false;
            var paths = new[]
            {
                OrpheusAudioTypedKeyProjection.AssemblyPath,
                OrpheusAudioTypedKeyProjection.SourcePath
            };
            for (var index = 0; index < paths.Length; index++)
            {
                var exists = File.Exists(ProjectAbsolute(paths[index]));
                var tracked = owned.TryGetValue(paths[index], out var artifact) &&
                              string.Equals(
                                  artifact.guid,
                                  AssetDatabase.AssetPathToGUID(paths[index])
                                      .ToLowerInvariant(),
                                  StringComparison.Ordinal);
                var matchesExpected =
                    index == 0
                        ? TextEquals(paths[index], expectedAssembly)
                        : TextEquals(paths[index], expectedSource);
                if (exists && (!pending && !tracked || pending && !matchesExpected))
                {
                    conflict = true;
                }
            }

            if (conflict)
            {
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.TypedKeyOwnershipConflict,
                    assetPath: TypedKeyRoot);
            }

            return conflict;
        }

        private static bool TextEquals(string path, string expected)
        {
            try
            {
                return File.Exists(ProjectAbsolute(path)) &&
                       string.Equals(
                           File.ReadAllText(ProjectAbsolute(path), Encoding.UTF8)
                               .Replace("\r\n", "\n")
                               .Replace('\r', '\n'),
                           expected ?? string.Empty,
                           StringComparison.Ordinal);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void ValidateExistingEventSemantics(
            OrpheusAuthoringCompilationPlan plan,
            IDictionary<string, OwnershipArtifact> owned,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            for (var index = 0; index < plan.EventCount; index++)
            {
                var expected = plan.GetEvent(index);
                if (!owned.ContainsKey(expected.AssetPath))
                {
                    continue;
                }

                var actual =
                    AssetDatabase.LoadAssetAtPath<OrpheusAudioEvent>(expected.AssetPath);
                if (actual == null)
                {
                    continue;
                }

                var serialized = new SerializedObject(actual);
                var mismatch =
                    serialized.FindProperty("_schemaVersion").intValue !=
                    OrpheusAudioAuthoringSchema.Current ||
                    serialized.FindProperty("_key").intValue != expected.Key ||
                    serialized.FindProperty("_playbackKind").intValue !=
                    (int)expected.PlaybackKind ||
                    serialized.FindProperty("_category").intValue !=
                    (int)expected.Category ||
                    serialized.FindProperty("_loadPolicy").intValue !=
                    (int)expected.LoadPolicy ||
                    !serialized.FindProperty("_volumeMin").floatValue.Equals(
                        expected.VolumeMinimum) ||
                    !serialized.FindProperty("_volumeMax").floatValue.Equals(
                        expected.VolumeMaximum) ||
                    !serialized.FindProperty("_pitchMin").floatValue.Equals(
                        expected.PitchMinimum) ||
                    !serialized.FindProperty("_pitchMax").floatValue.Equals(
                        expected.PitchMaximum) ||
                    serialized.FindProperty("_priority").intValue != expected.Priority ||
                    serialized.FindProperty("_polyphonyCap").intValue !=
                    expected.PolyphonyCap ||
                    !serialized.FindProperty("_cooldownSeconds").floatValue.Equals(
                        expected.CooldownSeconds) ||
                    !serialized.FindProperty("_minimumDistance").floatValue.Equals(
                        expected.MinimumDistance) ||
                    !serialized.FindProperty("_maximumDistance").floatValue.Equals(
                        expected.MaximumDistance) ||
                    serialized.FindProperty("_rolloffMode").intValue !=
                    (int)expected.RolloffMode;
                var clips = serialized.FindProperty("_clips");
                mismatch |= !clips.isArray || clips.arraySize != expected.ClipCount;
                if (!mismatch)
                {
                    for (var clipIndex = 0; clipIndex < expected.ClipCount; clipIndex++)
                    {
                        var clip =
                            clips.GetArrayElementAtIndex(clipIndex).objectReferenceValue;
                        if (clip == null ||
                            !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                                clip,
                                out var guid,
                                out long localId) ||
                            !string.Equals(
                                guid.ToLowerInvariant(),
                                expected.GetClip(clipIndex).Guid,
                                StringComparison.Ordinal) ||
                            localId != expected.GetClip(clipIndex).LocalFileId)
                        {
                            mismatch = true;
                            break;
                        }
                    }
                }

                if (mismatch)
                {
                    Add(
                        errors,
                        OrpheusAuthoringErrorCode.ActualOutputFingerprintMismatch,
                        expected.Key,
                        expected.ModuleId,
                        expected.Symbol,
                        expected.AssetPath);
                }
            }
        }

        private static void ReadOwnership(
            string generatedRoot,
            string authoringGuid,
            string validationGuid,
            string manifestGuid,
            string catalogGuid,
            ICollection<OrpheusAuthoringCompilationError> errors,
            out OrpheusAuthoringManifestEntryValue[] accepted,
            out OrpheusAuthoringOwnedEventValue[] owned,
            IDictionary<string, OwnershipArtifact> artifactsByPath)
        {
            accepted = null;
            owned = Array.Empty<OrpheusAuthoringOwnedEventValue>();
            var path = generatedRoot + AssetPathSeparator + OwnershipFileName;
            var absolute = ProjectAbsolute(path);
            if (!File.Exists(absolute))
            {
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.LostOwnershipState,
                    assetPath: path);
                return;
            }

            try
            {
                var json = File.ReadAllText(absolute, Encoding.UTF8);
                if (!OrpheusAudioAuthoringReports.TryValidateOwnership(
                        json,
                        authoringGuid,
                        validationGuid,
                        manifestGuid,
                        catalogGuid,
                        generatedRoot,
                        out var ownershipError))
                {
                    throw new InvalidDataException(ownershipError);
                }

                var value = JsonUtility.FromJson<OwnershipFile>(json);
                if (value == null ||
                    value.schemaVersion != OrpheusAudioAuthoringSchema.Current ||
                    !IsLowerGuid(value.ownerAuthoringProfileGuid) ||
                    !IsLowerGuid(value.enrolledValidationProfileGuid) ||
                    !IsLowerGuid(value.manifestGuid) ||
                    !IsLowerGuid(value.catalogGuid) ||
                    !string.Equals(value.ownerAuthoringProfileGuid, authoringGuid, StringComparison.Ordinal) ||
                    !string.Equals(value.enrolledValidationProfileGuid, validationGuid, StringComparison.Ordinal) ||
                    !string.Equals(value.manifestGuid, manifestGuid, StringComparison.Ordinal) ||
                    !string.Equals(value.catalogGuid, catalogGuid, StringComparison.Ordinal) ||
                    !string.Equals(value.generatedRoot, generatedRoot, StringComparison.Ordinal) ||
                    value.acceptedManifestSnapshot == null ||
                    value.artifacts == null)
                {
                    throw new InvalidDataException("Identity");
                }

                accepted =
                    new OrpheusAuthoringManifestEntryValue[
                        value.acceptedManifestSnapshot.Length];
                for (var index = 0; index < accepted.Length; index++)
                {
                    var entry = value.acceptedManifestSnapshot[index];
                    accepted[index] = new OrpheusAuthoringManifestEntryValue(
                        checked((ushort)entry.id),
                        entry.symbol,
                        (OrpheusAudioKeyStatus)entry.status);
                }

                var eventValues = new List<OrpheusAuthoringOwnedEventValue>();
                var hasCatalog = false;
                var hasOwnership = false;
                var hasTypedAssembly = false;
                var hasTypedSource = false;
                for (var index = 0; index < value.artifacts.Length; index++)
                {
                    var artifact = value.artifacts[index];
                    if (!IsArtifactKind(artifact.kind) ||
                        !TryCanonicalAssetOrGeneratedPath(artifact.path, out var canonical) ||
                        !string.Equals(canonical, artifact.path, StringComparison.Ordinal) ||
                        artifactsByPath.ContainsKey(artifact.path))
                    {
                        throw new InvalidDataException("Artifact");
                    }

                    if (artifact.guid.Length != 0 && !IsLowerGuid(artifact.guid))
                    {
                        throw new InvalidDataException("ArtifactGuid");
                    }

                    artifactsByPath.Add(artifact.path, artifact);
                    if (string.Equals(artifact.kind, "event", StringComparison.Ordinal))
                    {
                        if (!artifact.path.StartsWith(
                                generatedRoot + AssetPathSeparator + "Events/",
                                StringComparison.Ordinal) ||
                            artifact.key <= 0 ||
                            string.IsNullOrEmpty(artifact.moduleId) ||
                            string.IsNullOrEmpty(artifact.symbol))
                        {
                            throw new InvalidDataException("EventArtifact");
                        }

                        eventValues.Add(
                            new OrpheusAuthoringOwnedEventValue(
                                checked((ushort)artifact.key),
                                artifact.moduleId,
                                artifact.symbol,
                                artifact.guid,
                                artifact.path,
                                string.Empty));
                    }
                    else
                    {
                        if (artifact.key != 0 ||
                            !string.IsNullOrEmpty(artifact.moduleId) ||
                            !string.IsNullOrEmpty(artifact.symbol))
                        {
                            throw new InvalidDataException("NonEventArtifact");
                        }

                        if (string.Equals(artifact.kind, "catalog", StringComparison.Ordinal))
                        {
                            hasCatalog =
                                string.Equals(
                                    artifact.path,
                                    generatedRoot + AssetPathSeparator +
                                    CatalogFileName,
                                    StringComparison.Ordinal) &&
                                string.Equals(artifact.guid, catalogGuid, StringComparison.Ordinal);
                        }
                        else if (string.Equals(artifact.kind, "ownership", StringComparison.Ordinal))
                        {
                            hasOwnership =
                                string.Equals(artifact.path, path, StringComparison.Ordinal);
                        }
                        else if (string.Equals(artifact.kind, "typed-key-assembly", StringComparison.Ordinal))
                        {
                            hasTypedAssembly =
                                string.Equals(artifact.path, OrpheusAudioTypedKeyProjection.AssemblyPath, StringComparison.Ordinal);
                        }
                        else if (string.Equals(artifact.kind, "typed-key-source", StringComparison.Ordinal))
                        {
                            hasTypedSource =
                                string.Equals(artifact.path, OrpheusAudioTypedKeyProjection.SourcePath, StringComparison.Ordinal);
                        }
                    }
                }

                if (!hasCatalog || !hasOwnership || !hasTypedAssembly || !hasTypedSource)
                {
                    throw new InvalidDataException("RequiredArtifacts");
                }

                owned = eventValues.ToArray();
            }
            catch (Exception exception) when (
                !(exception is OutOfMemoryException) &&
                !(exception is StackOverflowException))
            {
                accepted = null;
                owned = Array.Empty<OrpheusAuthoringOwnedEventValue>();
                artifactsByPath.Clear();
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                    assetPath: path,
                    detail: exception.Message);
            }
        }

        private static void ValidateNoCatalogOwnership(
            string catalogGuid,
            string generatedRoot,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            if (catalogGuid.Length == 0)
            {
                return;
            }

            var assets = ProjectAbsolute("Assets");
            string[] files;
            try
            {
                files = Directory.GetFiles(
                    assets,
                    OwnershipFileName,
                    SearchOption.AllDirectories);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                    assetPath: "Assets",
                    detail: "OwnershipEnumeration");
                return;
            }

            var own = ProjectAbsolute(
                generatedRoot + AssetPathSeparator + OwnershipFileName);
            for (var index = 0; index < files.Length; index++)
            {
                if (string.Equals(files[index], own, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var text = File.ReadAllText(files[index], Encoding.UTF8);
                    if (!HasOwnershipTopLevelOrder(text))
                    {
                        throw new InvalidDataException("TopLevel");
                    }

                    var ownership = JsonUtility.FromJson<OwnershipFile>(text);
                    if (ownership == null || !IsLowerGuid(ownership.catalogGuid))
                    {
                        throw new InvalidDataException("CatalogGuid");
                    }

                    if (string.Equals(
                            ownership.catalogGuid,
                            catalogGuid,
                            StringComparison.Ordinal))
                    {
                        Add(
                            errors,
                            OrpheusAuthoringErrorCode.CatalogMismatch,
                            assetPath: ToProjectPath(files[index]),
                            detail: "OwnedElsewhere");
                    }
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException ||
                    exception is ArgumentException ||
                    exception is InvalidDataException)
                {
                    Add(
                        errors,
                        OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                        assetPath: ToProjectPath(files[index]),
                        detail: "OwnershipRead");
                }
            }
        }

        private static void ValidateJournal(
            ICollection<OrpheusAuthoringCompilationError> errors,
            string allowedValidatingTransactionId)
        {
            var root = ProjectAbsolute(TransactionsRoot);
            if (!Directory.Exists(root))
            {
                if (!string.IsNullOrEmpty(allowedValidatingTransactionId))
                {
                    Add(
                        errors,
                        OrpheusAuthoringErrorCode.RollbackFailed,
                        assetPath: TransactionsRoot);
                }

                return;
            }

            var nonTerminal = 0;
            var allowedValidating = 0;
            var invalid = false;
            var journals = Directory.GetFiles(root, "journal.json", SearchOption.AllDirectories);
            Array.Sort(journals, StringComparer.Ordinal);
            for (var index = 0; index < journals.Length; index++)
            {
                try
                {
                    var text = File.ReadAllText(journals[index], Encoding.UTF8);
                    if (!HasTopLevelOrder(
                            text,
                            "schemaVersion",
                            "transactionId",
                            "phase",
                            "payload",
                            "payloadSha256"))
                    {
                        invalid = true;
                        continue;
                    }

                    var envelope = JsonUtility.FromJson<JournalEnvelope>(text);
                    var folder = new DirectoryInfo(Path.GetDirectoryName(journals[index])).Name;
                    if (envelope == null ||
                        envelope.schemaVersion != OrpheusAudioAuthoringSchema.Current ||
                        !IsLowerGuid(envelope.transactionId) ||
                        !string.Equals(folder, envelope.transactionId, StringComparison.Ordinal) ||
                        !IsJournalPhase(envelope.phase) ||
                        !IsLowerHash(envelope.payloadSha256) ||
                        !string.Equals(
                            envelope.payloadSha256,
                            Sha256(envelope.payload ?? string.Empty),
                            StringComparison.Ordinal))
                    {
                        invalid = true;
                        continue;
                    }

                    if (string.Equals(envelope.phase, "RollbackFailed", StringComparison.Ordinal))
                    {
                        invalid = true;
                    }
                    else if (!string.Equals(envelope.phase, "Committed", StringComparison.Ordinal) &&
                             !string.Equals(envelope.phase, "RolledBack", StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrEmpty(
                                allowedValidatingTransactionId) &&
                            string.Equals(
                                envelope.transactionId,
                                allowedValidatingTransactionId,
                                StringComparison.Ordinal) &&
                            string.Equals(
                                envelope.phase,
                                "Validating",
                                StringComparison.Ordinal))
                        {
                            allowedValidating++;
                        }
                        else
                        {
                            nonTerminal++;
                        }
                    }
                }
                catch (Exception exception) when (
                    !(exception is OutOfMemoryException) &&
                    !(exception is StackOverflowException))
                {
                    invalid = true;
                }
            }

            if (!string.IsNullOrEmpty(allowedValidatingTransactionId) &&
                allowedValidating != 1)
            {
                invalid = true;
            }

            if (invalid || nonTerminal > 1)
            {
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.RollbackFailed,
                    assetPath: TransactionsRoot);
            }
            else if (nonTerminal == 1)
            {
                Add(
                    errors,
                    OrpheusAuthoringErrorCode.TransactionRecoveryRequired,
                    assetPath: TransactionsRoot);
            }
        }

        private static bool TryCanonicalAssetDirectory(
            string path,
            out string canonical,
            out string detail)
        {
            canonical = string.Empty;
            detail = string.Empty;
            if (!TryCanonicalAssetOrGeneratedPath(path, out canonical))
            {
                detail = "Syntax";
                return false;
            }

            if (!canonical.StartsWith("Assets/", StringComparison.Ordinal) ||
                canonical.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                canonical.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                detail = "Directory";
                return false;
            }

            if (RootsOverlap(canonical, TypedKeyRoot))
            {
                detail = "TypedKeyRoot";
                return false;
            }

            return true;
        }

        private static bool TryCanonicalAssetOrGeneratedPath(
            string path,
            out string canonical)
        {
            canonical = string.Empty;
            if (string.IsNullOrEmpty(path) ||
                path.IndexOf('\\') >= 0 ||
                Path.IsPathRooted(path) ||
                path.StartsWith("//", StringComparison.Ordinal) ||
                path[path.Length - 1] == AssetPathSeparator ||
                path.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            var parts = path.Split(AssetPathSeparator);
            if (parts.Length < 2 ||
                !string.Equals(parts[0], "Assets", StringComparison.Ordinal))
            {
                return false;
            }

            for (var index = 0; index < parts.Length; index++)
            {
                if (parts[index].Length == 0 ||
                    string.Equals(parts[index], ".", StringComparison.Ordinal) ||
                    string.Equals(parts[index], "..", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            var absolute = Path.GetFullPath(ProjectAbsolute(path));
            var project = Path.GetFullPath(ProjectAbsolute(string.Empty));
            if (!absolute.StartsWith(project, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var current = project.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            for (var index = 0; index < parts.Length; index++)
            {
                current = Path.Combine(current, parts[index]);
                if ((Directory.Exists(current) || File.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
            }

            canonical = path;
            return true;
        }

        private static bool TryGetIdentity(
            UnityEngine.Object value,
            string path,
            out string guid,
            out long localId)
        {
            guid = string.Empty;
            localId = 0;
            if (value == null || path.Length == 0 ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    value,
                    out var rawGuid,
                    out localId))
            {
                return false;
            }

            guid = (rawGuid ?? string.Empty).ToLowerInvariant();
            return IsLowerGuid(guid);
        }

        private static string GetSavedMainAssetPath(UnityEngine.Object value)
        {
            if (value == null || AssetDatabase.IsSubAsset(value))
            {
                return string.Empty;
            }

            var path = AssetDatabase.GetAssetPath(value);
            path = path.Replace('\\', AssetPathSeparator);
            return path.Length != 0 &&
                   ReferenceEquals(AssetDatabase.LoadMainAssetAtPath(path), value) &&
                   TryCanonicalAssetOrGeneratedPath(path, out var canonical)
                ? canonical
                : string.Empty;
        }

        private static string GetGuid(UnityEngine.Object value)
        {
            var path = GetSavedMainAssetPath(value);
            return path.Length == 0
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(path).ToLowerInvariant();
        }

        private static bool RootsOverlap(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }

            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
                   left.StartsWith(
                       right + AssetPathSeparator,
                       StringComparison.OrdinalIgnoreCase) ||
                   right.StartsWith(
                       left + AssetPathSeparator,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string ProjectAbsolute(string path)
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            return path.Length == 0
                ? root + Path.DirectorySeparatorChar
                : Path.Combine(
                    root,
                    path.Replace(
                        AssetPathSeparator,
                        Path.DirectorySeparatorChar));
        }

        private static string ToProjectPath(string absolute)
        {
            var root = ProjectAbsolute(string.Empty);
            return absolute.Substring(root.Length)
                .Replace('\\', AssetPathSeparator);
        }

        private static bool HasOwnershipTopLevelOrder(string json)
        {
            return HasTopLevelOrder(
                json,
                "schemaVersion",
                "ownerAuthoringProfileGuid",
                "enrolledValidationProfileGuid",
                "manifestGuid",
                "catalogGuid",
                "generatedRoot",
                "acceptedManifestSnapshot",
                "artifacts");
        }

        private static bool HasTopLevelOrder(
            string json,
            params string[] expected)
        {
            var names = GetTopLevelPropertyNames(json);
            if (names.Count != expected.Length)
            {
                return false;
            }

            for (var index = 0; index < expected.Length; index++)
            {
                if (!string.Equals(
                        names[index],
                        expected[index],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> GetTopLevelPropertyNames(string json)
        {
            var result = new List<string>();
            var depth = 0;
            var inString = false;
            var escaped = false;
            var stringStart = -1;
            for (var index = 0; index < json.Length; index++)
            {
                var character = json[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                        if (depth == 1)
                        {
                            var cursor = index + 1;
                            while (cursor < json.Length && char.IsWhiteSpace(json[cursor]))
                            {
                                cursor++;
                            }

                            if (cursor < json.Length && json[cursor] == ':')
                            {
                                result.Add(json.Substring(stringStart, index - stringStart));
                            }
                        }
                    }

                    continue;
                }

                if (character == '"')
                {
                    inString = true;
                    stringStart = index + 1;
                }
                else if (character == '{' || character == '[')
                {
                    depth++;
                }
                else if (character == '}' || character == ']')
                {
                    depth--;
                }
            }

            return result;
        }

        private static bool IsArtifactKind(string value)
        {
            return string.Equals(value, "event", StringComparison.Ordinal) ||
                   string.Equals(value, "catalog", StringComparison.Ordinal) ||
                   string.Equals(value, "typed-key-assembly", StringComparison.Ordinal) ||
                   string.Equals(value, "typed-key-source", StringComparison.Ordinal) ||
                   string.Equals(value, "ownership", StringComparison.Ordinal) ||
                   string.Equals(value, "closure", StringComparison.Ordinal);
        }

        private static bool IsJournalPhase(string value)
        {
            return string.Equals(value, "Prepared", StringComparison.Ordinal) ||
                   string.Equals(value, "Writing", StringComparison.Ordinal) ||
                   string.Equals(value, "Importing", StringComparison.Ordinal) ||
                   string.Equals(value, "Validating", StringComparison.Ordinal) ||
                   string.Equals(value, "Committed", StringComparison.Ordinal) ||
                   string.Equals(value, "RollingBack", StringComparison.Ordinal) ||
                   string.Equals(value, "RolledBack", StringComparison.Ordinal) ||
                   string.Equals(value, "RollbackFailed", StringComparison.Ordinal);
        }

        private static bool IsLowerGuid(string value)
        {
            return value != null && value.Length == 32 && IsLowerHex(value);
        }

        private static bool IsLowerHash(string value)
        {
            return value != null && value.Length == 64 && IsLowerHex(value);
        }

        private static bool IsLowerHex(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!(character >= '0' && character <= '9') &&
                    !(character >= 'a' && character <= 'f'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Sha256(string value)
        {
            using (var algorithm = SHA256.Create())
            {
                var bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
                var text = new StringBuilder(64);
                for (var index = 0; index < bytes.Length; index++)
                {
                    text.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }
        }

        private static bool Fail(
            List<OrpheusAuthoringCompilationError> collected,
            out OrpheusAuthoringCompilationError[] errors)
        {
            collected.Sort(CompareErrors);
            errors = collected.ToArray();
            return false;
        }

        private static int CompareErrors(
            OrpheusAuthoringCompilationError left,
            OrpheusAuthoringCompilationError right)
        {
            var result = left.Code.CompareTo(right.Code);
            if (result != 0) return result;
            result = left.Key.CompareTo(right.Key);
            if (result != 0) return result;
            result = string.CompareOrdinal(left.ModuleId, right.ModuleId);
            if (result != 0) return result;
            result = string.CompareOrdinal(left.Symbol, right.Symbol);
            if (result != 0) return result;
            result = string.CompareOrdinal(left.AssetPath, right.AssetPath);
            if (result != 0) return result;
            result = left.ClipIndex.CompareTo(right.ClipIndex);
            return result != 0 ? result : string.CompareOrdinal(left.Detail, right.Detail);
        }

        private static void Add(
            ICollection<OrpheusAuthoringCompilationError> errors,
            OrpheusAuthoringErrorCode code,
            ushort key = 0,
            string moduleId = "",
            string symbol = "",
            string assetPath = "",
            int clipIndex = -1,
            string detail = "")
        {
            errors.Add(
                new OrpheusAuthoringCompilationError(
                    code,
                    key,
                    moduleId,
                    symbol,
                    assetPath,
                    clipIndex,
                    detail));
        }

        [Serializable]
        private sealed class OwnershipFile
        {
            public int schemaVersion;
            public string ownerAuthoringProfileGuid;
            public string enrolledValidationProfileGuid;
            public string manifestGuid;
            public string catalogGuid;
            public string generatedRoot;
            public OwnershipManifestEntry[] acceptedManifestSnapshot;
            public OwnershipArtifact[] artifacts;
        }

        [Serializable]
        private sealed class OwnershipManifestEntry
        {
            public int id;
            public string symbol;
            public int status;
        }

        [Serializable]
        private sealed class OwnershipArtifact
        {
            public string kind;
            public string moduleId;
            public string symbol;
            public int key;
            public string guid;
            public string path;
        }

        [Serializable]
        private sealed class JournalEnvelope
        {
            public int schemaVersion;
            public string transactionId;
            public string phase;
            public string payload;
            public string payloadSha256;
        }
    }
}
