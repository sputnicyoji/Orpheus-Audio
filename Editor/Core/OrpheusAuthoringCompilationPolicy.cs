using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Orpheus.Audio.Core;

namespace Orpheus.Audio.Editor
{
    internal static class OrpheusAuthoringCompilationPolicy
    {
        private const char AssetPathSeparator = (char)47;
        private const int CompilerSchemaVersion = 1;
        private const string FingerprintDomain =
            "Orpheus.Audio.AuthoringCompilation\0v1\0";
        private const string TypedKeyRoot = "Assets/OrpheusGenerated";
        private const string TypedKeyAssemblyPath =
            "Assets/OrpheusGenerated/Orpheus.Audio.Generated.asmdef";
        private const string TypedKeySourcePath =
            "Assets/OrpheusGenerated/OrpheusAudioKeys.g.cs";

        internal static bool TryCompile(
            OrpheusAuthoringCompilationInput input,
            out OrpheusAuthoringCompilationPlan plan,
            out OrpheusAuthoringCompilationError[] errors)
        {
            if (input == null)
            {
                plan = OrpheusAuthoringCompilationPlan.Empty;
                errors = new[]
                {
                    Error(OrpheusAuthoringErrorCode.Invalid, detail: "Input")
                };
                return false;
            }

            var collectedErrors = new List<OrpheusAuthoringCompilationError>();
            ValidateRoot(input, collectedErrors);
            ValidateCatalogAndEnrollment(input, collectedErrors);
            ValidateOwnershipBaseline(input, collectedErrors);
            var manifestByKey = ValidateManifest(input, collectedErrors);
            ValidateLifecycle(input, manifestByKey, collectedErrors);
            var resolved = ResolveRecipes(input, manifestByKey, collectedErrors);
            ValidateActiveOwnership(manifestByKey, resolved, collectedErrors);
            ValidateOutputConflicts(input, resolved, collectedErrors);

            if (collectedErrors.Count != 0)
            {
                collectedErrors.Sort(CompareErrors);
                plan = OrpheusAuthoringCompilationPlan.Empty;
                errors = collectedErrors.ToArray();
                return false;
            }

            resolved.Sort(CompareResolvedByKey);
            var compiledEvents = BuildCompiledEvents(input.GeneratedRoot, resolved);
            var catalogKeys = new ushort[compiledEvents.Length];
            for (var index = 0; index < compiledEvents.Length; index++)
            {
                catalogKeys[index] = compiledEvents[index].Key;
            }

            var futureDelivery = BuildFutureDelivery(resolved);
            var inputFingerprint = ComputeInputFingerprint(input, manifestByKey, resolved);
            var outputFingerprint = ComputeOutputFingerprint(
                input,
                compiledEvents,
                catalogKeys);
            var renameKeys = GetManifestRenameKeys(input, resolved);
            var orphans = BuildOrphans(input, compiledEvents);
            var operations = BuildWriteOperations(input, compiledEvents, renameKeys);

            plan = new OrpheusAuthoringCompilationPlan(
                compiledEvents,
                catalogKeys,
                futureDelivery,
                operations,
                inputFingerprint,
                outputFingerprint,
                input.Catalog.Guid,
                input.Catalog.AssetPath,
                input.TypedKeyAssemblyText,
                input.TypedKeySourceText,
                orphans);
            errors = Array.Empty<OrpheusAuthoringCompilationError>();
            return true;
        }

        private static Dictionary<ushort, OrpheusAuthoringManifestEntryValue> ValidateManifest(
            OrpheusAuthoringCompilationInput input,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            var byKey = new Dictionary<ushort, OrpheusAuthoringManifestEntryValue>();
            if (input.AuthoringSchemaVersion != OrpheusAudioAuthoringSchema.Current)
            {
                errors.Add(
                    Error(
                        OrpheusAuthoringErrorCode.SchemaVersionMismatch,
                        detail: "AuthoringProfile"));
            }

            if (input.Manifest == null || !input.Manifest.HasEntryStorage)
            {
                errors.Add(Error(OrpheusAuthoringErrorCode.MissingManifest));
                return byKey;
            }

            if (input.Manifest.SchemaVersion != OrpheusAudioAuthoringSchema.Current)
            {
                errors.Add(
                    Error(
                        OrpheusAuthoringErrorCode.SchemaVersionMismatch,
                        detail: "Manifest"));
            }

            var manifestEntries = new List<OrpheusAuthoringManifestEntryValue>();
            for (var index = 0; index < input.Manifest.EntryCount; index++)
            {
                manifestEntries.Add(input.Manifest.GetEntry(index));
            }

            manifestEntries.Sort(CompareManifestByKey);
            var symbols = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < manifestEntries.Count; index++)
            {
                var entry = manifestEntries[index];
                if (byKey.ContainsKey(entry.Key))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.DuplicateManifestId,
                            entry.Key,
                            symbol: entry.Symbol));
                }
                else
                {
                    byKey.Add(entry.Key, entry);
                }

                if (!symbols.Add(entry.Symbol))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.DuplicateManifestSymbol,
                            entry.Key,
                            symbol: entry.Symbol));
                }

                var mismatch = OrpheusAudioManifestPolicy.Evaluate(
                    new OrpheusAudioKeyManifestEntryValue(
                        entry.Key,
                        entry.Symbol,
                        entry.Status));
                if ((mismatch & OrpheusAudioManifestEntryMismatch.Status) != 0)
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.InvalidManifestStatus,
                            entry.Key,
                            symbol: entry.Symbol,
                            detail: ((byte)entry.Status).ToString()));
                }

                if ((mismatch &
                     (OrpheusAudioManifestEntryMismatch.Id |
                      OrpheusAudioManifestEntryMismatch.Symbol)) != 0)
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.InvalidManifestStatus,
                            entry.Key,
                            symbol: entry.Symbol,
                            detail: "Identity"));
                }
            }

            return byKey;
        }

        private static void ValidateLifecycle(
            OrpheusAuthoringCompilationInput input,
            IDictionary<ushort, OrpheusAuthoringManifestEntryValue> currentByKey,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            if (!input.HasAcceptedManifest)
            {
                return;
            }

            var previousByKey =
                new Dictionary<ushort, OrpheusAuthoringManifestEntryValue>();
            var previousSymbolOwners = new Dictionary<string, ushort>(StringComparer.Ordinal);
            var previousEntries = new List<OrpheusAuthoringManifestEntryValue>();
            for (var index = 0; index < input.AcceptedManifestCount; index++)
            {
                previousEntries.Add(input.GetAcceptedManifestEntry(index));
            }

            previousEntries.Sort(CompareManifestByKey);
            for (var index = 0; index < previousEntries.Count; index++)
            {
                var previous = previousEntries[index];
                if (previousByKey.ContainsKey(previous.Key) ||
                    previousSymbolOwners.ContainsKey(previous.Symbol))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                            previous.Key,
                            symbol: previous.Symbol,
                            detail: "AcceptedManifest"));
                    continue;
                }

                previousByKey.Add(previous.Key, previous);
                previousSymbolOwners.Add(previous.Symbol, previous.Key);
            }

            foreach (var pair in previousByKey)
            {
                var previous = pair.Value;
                if (!currentByKey.TryGetValue(previous.Key, out var current))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.ManifestIdentityRemoved,
                            previous.Key,
                            symbol: previous.Symbol));
                    continue;
                }

                if (previous.Status == OrpheusAudioKeyStatus.Active &&
                    current.Status == OrpheusAudioKeyStatus.Reserved ||
                    previous.Status == OrpheusAudioKeyStatus.Retired &&
                    (current.Status == OrpheusAudioKeyStatus.Active ||
                     current.Status == OrpheusAudioKeyStatus.Reserved))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.ManifestLifecycleRegression,
                            current.Key,
                            symbol: current.Symbol,
                            detail: previous.Status + "->" + current.Status));
                }

                if (string.Equals(previous.Symbol, current.Symbol, StringComparison.Ordinal))
                {
                    continue;
                }

                if (previous.Status == OrpheusAudioKeyStatus.Retired)
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.ManifestIdReuse,
                            current.Key,
                            symbol: current.Symbol,
                            detail: previous.Symbol));
                    continue;
                }

                if (previousSymbolOwners.TryGetValue(current.Symbol, out var oldKey) &&
                    oldKey != current.Key)
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.InvalidSymbolRename,
                            current.Key,
                            symbol: current.Symbol,
                            detail: previous.Symbol));
                }
            }
        }

        private static List<ResolvedRecipe> ResolveRecipes(
            OrpheusAuthoringCompilationInput input,
            IDictionary<ushort, OrpheusAuthoringManifestEntryValue> manifestByKey,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            var resolved = new List<ResolvedRecipe>();
            if (!input.HasRecipeStorage)
            {
                errors.Add(
                    Error(
                        OrpheusAuthoringErrorCode.NullRecipe,
                        detail: "RecipeStorage"));
                return resolved;
            }

            var manifestBySymbol =
                new Dictionary<string, OrpheusAuthoringManifestEntryValue>(
                    StringComparer.Ordinal);
            foreach (var pair in manifestByKey)
            {
                if (!manifestBySymbol.ContainsKey(pair.Value.Symbol))
                {
                    manifestBySymbol.Add(pair.Value.Symbol, pair.Value);
                }
            }

            var orderedRecipes = new List<OrpheusAuthoringModuleRecipeValue>();
            for (var recipeIndex = 0; recipeIndex < input.RecipeCount; recipeIndex++)
            {
                var recipe = input.GetRecipe(recipeIndex);
                if (recipe == null)
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.NullRecipe));
                    continue;
                }

                orderedRecipes.Add(recipe);
            }

            orderedRecipes.Sort(CompareModuleRecipes);
            var moduleIds = new HashSet<string>(StringComparer.Ordinal);
            var owners = new Dictionary<ushort, ResolvedRecipe>();
            for (var recipeIndex = 0; recipeIndex < orderedRecipes.Count; recipeIndex++)
            {
                var recipe = orderedRecipes[recipeIndex];
                if (recipe.SchemaVersion != OrpheusAudioAuthoringSchema.Current)
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.SchemaVersionMismatch,
                            moduleId: recipe.ModuleId,
                            detail: "Recipe"));
                }

                if (!IsSlug(recipe.ModuleId))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.InvalidModuleId,
                            moduleId: recipe.ModuleId));
                }

                if (!moduleIds.Add(recipe.ModuleId))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.DuplicateModuleId,
                            moduleId: recipe.ModuleId));
                }

                if (!recipe.HasEventStorage)
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.NullRecipe,
                            moduleId: recipe.ModuleId,
                            detail: "EventStorage"));
                    continue;
                }

                for (var eventIndex = 0; eventIndex < recipe.EventCount; eventIndex++)
                {
                    var eventRecipe = recipe.GetEvent(eventIndex);
                    if (eventRecipe == null)
                    {
                        errors.Add(
                            Error(
                                OrpheusAuthoringErrorCode.InvalidEventPolicy,
                                moduleId: recipe.ModuleId,
                                detail: "NullEvent"));
                        continue;
                    }

                    if (!manifestBySymbol.TryGetValue(
                            eventRecipe.Symbol,
                            out var manifestEntry))
                    {
                        errors.Add(
                            Error(
                                OrpheusAuthoringErrorCode.MissingRecipeSymbol,
                                moduleId: recipe.ModuleId,
                                symbol: eventRecipe.Symbol));
                        continue;
                    }

                    if (manifestEntry.Status != OrpheusAudioKeyStatus.Active)
                    {
                        errors.Add(
                            Error(
                                OrpheusAuthoringErrorCode.InactiveRecipeSymbol,
                                manifestEntry.Key,
                                recipe.ModuleId,
                                eventRecipe.Symbol,
                                detail: manifestEntry.Status.ToString()));
                        continue;
                    }

                    var candidate = new ResolvedRecipe(
                        manifestEntry.Key,
                        recipe.ModuleId,
                        eventRecipe);
                    if (owners.TryGetValue(manifestEntry.Key, out var firstOwner))
                    {
                        errors.Add(
                            Error(
                                OrpheusAuthoringErrorCode.DuplicateRecipeOwner,
                                manifestEntry.Key,
                                recipe.ModuleId,
                                eventRecipe.Symbol,
                                detail: firstOwner.ModuleId));
                    }
                    else
                    {
                        owners.Add(manifestEntry.Key, candidate);
                    }

                    ValidateEvent(candidate, errors);
                    resolved.Add(candidate);
                }
            }

            return resolved;
        }

        private static void ValidateEvent(
            ResolvedRecipe resolved,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            var recipe = resolved.Recipe;
            if (!recipe.HasClipStorage)
            {
                errors.Add(
                    Error(
                        OrpheusAuthoringErrorCode.InvalidClipIdentity,
                        resolved.Key,
                        resolved.ModuleId,
                        recipe.Symbol,
                        clipIndex: -1,
                        detail: "ClipStorage"));
            }

            var mismatch = OrpheusAudioEventPolicy.Evaluate(
                new OrpheusAudioEventPolicyValues(
                    OrpheusAudioAuthoringSchema.Current,
                    resolved.Key,
                    recipe.PlaybackKind,
                    recipe.Category,
                    recipe.LoadPolicy,
                    recipe.ClipCount,
                    recipe.VolumeMinimum,
                    recipe.VolumeMaximum,
                    recipe.PitchMinimum,
                    recipe.PitchMaximum,
                    recipe.PolyphonyCap,
                    recipe.CooldownSeconds,
                    recipe.MinimumDistance,
                    recipe.MaximumDistance,
                    recipe.RolloffMode));
            if (mismatch != OrpheusAudioEventPolicyMismatch.None ||
                !IsOptionalSlug(recipe.ProfileHint) ||
                !IsOptionalSlug(recipe.CandidateContentBankId))
            {
                errors.Add(
                    Error(
                        OrpheusAuthoringErrorCode.InvalidEventPolicy,
                        resolved.Key,
                        resolved.ModuleId,
                        recipe.Symbol,
                        detail: ((uint)mismatch).ToString()));
            }

            var clipIdentities = new HashSet<string>(StringComparer.Ordinal);
            for (var clipIndex = 0; clipIndex < recipe.ClipCount; clipIndex++)
            {
                var clip = recipe.GetClip(clipIndex);
                if (!IsLowerGuid(clip.Guid) ||
                    clip.LocalFileId == 0 ||
                    !IsCanonicalAssetPath(clip.AssetPath))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.InvalidClipIdentity,
                            resolved.Key,
                            resolved.ModuleId,
                            recipe.Symbol,
                            clip.AssetPath,
                            clipIndex,
                            clip.Guid));
                }

                var identity = clip.Guid + ":" + clip.LocalFileId;
                if (!clipIdentities.Add(identity))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.DuplicateClip,
                            resolved.Key,
                            resolved.ModuleId,
                            recipe.Symbol,
                            clip.AssetPath,
                            clipIndex,
                            identity));
                }
            }
        }

        private static void ValidateActiveOwnership(
            IDictionary<ushort, OrpheusAuthoringManifestEntryValue> manifestByKey,
            IList<ResolvedRecipe> resolved,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            var ownerCounts = new Dictionary<ushort, int>();
            for (var index = 0; index < resolved.Count; index++)
            {
                var key = resolved[index].Key;
                ownerCounts.TryGetValue(key, out var count);
                ownerCounts[key] = count + 1;
            }

            foreach (var pair in manifestByKey)
            {
                if (pair.Value.Status == OrpheusAudioKeyStatus.Active &&
                    !ownerCounts.ContainsKey(pair.Key))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.ActiveManifestIdentityUnowned,
                            pair.Key,
                            symbol: pair.Value.Symbol));
                }
            }
        }

        private static void ValidateRoot(
            OrpheusAuthoringCompilationInput input,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            if (!IsCanonicalAssetPath(input.GeneratedRoot))
            {
                errors.Add(
                    Error(
                        OrpheusAuthoringErrorCode.InvalidGeneratedRoot,
                        assetPath: input.GeneratedRoot));
                return;
            }

            if (PathsOverlap(input.GeneratedRoot, TypedKeyRoot))
            {
                errors.Add(
                    Error(
                        OrpheusAuthoringErrorCode.GeneratedRootOverlap,
                        assetPath: input.GeneratedRoot,
                        detail: TypedKeyRoot));
            }

            for (var index = 0; index < input.OtherGeneratedRootCount; index++)
            {
                var other = input.GetOtherGeneratedRoot(index);
                if (IsCanonicalAssetPath(other) &&
                    PathsOverlap(input.GeneratedRoot, other))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.GeneratedRootOverlap,
                            assetPath: input.GeneratedRoot,
                            detail: other));
                }
            }

            if (input.TypedKeyOwnershipConflict)
            {
                errors.Add(
                    Error(
                        OrpheusAuthoringErrorCode.TypedKeyOwnershipConflict,
                        assetPath: TypedKeyRoot));
            }
        }

        private static void ValidateCatalogAndEnrollment(
            OrpheusAuthoringCompilationInput input,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            if (input.Catalog == null ||
                !IsLowerGuid(input.Catalog.Guid) ||
                !IsCanonicalAssetPath(input.Catalog.AssetPath))
            {
                errors.Add(
                    Error(
                        OrpheusAuthoringErrorCode.CatalogMismatch,
                        assetPath: input.Catalog == null
                            ? string.Empty
                            : input.Catalog.AssetPath,
                        detail: "Identity"));
            }
            else if (input.Catalog.OwnershipConflict)
            {
                errors.Add(
                    Error(
                        OrpheusAuthoringErrorCode.CatalogMismatch,
                        assetPath: input.Catalog.AssetPath,
                        detail: "Ownership"));
            }

            if (input.WriteEnrollmentIdentity)
            {
                if (!IsCanonicalAssetPath(input.ValidationProfileAssetPath))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.InvalidValidationProfile,
                            assetPath: input.ValidationProfileAssetPath,
                            detail: "EnrollmentTarget"));
                }

                if (input.Catalog != null && input.Catalog.EventCount != 0)
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.CatalogNotEmptyAtEnrollment,
                            assetPath: input.Catalog.AssetPath,
                            detail: input.Catalog.EventCount.ToString()));
                }
            }
        }

        private static void ValidateOwnershipBaseline(
            OrpheusAuthoringCompilationInput input,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            if (!input.HasAcceptedManifest)
            {
                if (input.OwnedEventCount != 0)
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.LostOwnershipState,
                            detail: "AcceptedManifest"));
                }

                return;
            }

            var acceptedByKey =
                new Dictionary<ushort, OrpheusAuthoringManifestEntryValue>();
            var acceptedSymbols = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < input.AcceptedManifestCount; index++)
            {
                var entry = input.GetAcceptedManifestEntry(index);
                var mismatch = OrpheusAudioManifestPolicy.Evaluate(
                    new OrpheusAudioKeyManifestEntryValue(
                        entry.Key,
                        entry.Symbol,
                        entry.Status));
                if (mismatch != OrpheusAudioManifestEntryMismatch.None ||
                    acceptedByKey.ContainsKey(entry.Key) ||
                    !acceptedSymbols.Add(entry.Symbol))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                            entry.Key,
                            symbol: entry.Symbol,
                            detail: "AcceptedManifest"));
                    continue;
                }

                acceptedByKey.Add(entry.Key, entry);
            }

            var ownedKeys = new HashSet<ushort>();
            for (var index = 0; index < input.OwnedEventCount; index++)
            {
                var owned = input.GetOwnedEvent(index);
                ownedKeys.Add(owned.Key);
                if (!acceptedByKey.TryGetValue(owned.Key, out var acceptedEntry))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                            owned.Key,
                            owned.ModuleId,
                            owned.Symbol,
                            owned.AssetPath,
                            detail: "UnboundOwnedEvent"));
                }
                else if (!string.Equals(
                             owned.Symbol,
                             acceptedEntry.Symbol,
                             StringComparison.Ordinal))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                            owned.Key,
                            owned.ModuleId,
                            owned.Symbol,
                            owned.AssetPath,
                            detail: "AcceptedSymbol"));
                }
            }

            foreach (var pair in acceptedByKey)
            {
                if (pair.Value.Status == OrpheusAudioKeyStatus.Active &&
                    !ownedKeys.Contains(pair.Key))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.LostOwnershipState,
                            pair.Key,
                            symbol: pair.Value.Symbol,
                            detail: "OwnedEvent"));
                }
            }
        }

        private static void ValidateOutputConflicts(
            OrpheusAuthoringCompilationInput input,
            IList<ResolvedRecipe> resolved,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            if (!IsCanonicalAssetPath(input.GeneratedRoot))
            {
                return;
            }

            var expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var expectedEventPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < resolved.Count; index++)
            {
                var path = GetEventPath(input.GeneratedRoot, resolved[index].Recipe.Symbol);
                if (!expectedPaths.Add(path))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.OutputPathCollision,
                            resolved[index].Key,
                            resolved[index].ModuleId,
                            resolved[index].Recipe.Symbol,
                            path));
                }

                expectedEventPaths.Add(path);
            }

            AddExpectedPath(
                expectedPaths,
                input.GeneratedRoot + AssetPathSeparator +
                "OrpheusAuthoringOwnership.json",
                errors);
            AddExpectedPath(
                expectedPaths,
                input.GeneratedRoot + AssetPathSeparator +
                "OrpheusAuthoringClosure.json",
                errors);
            AddExpectedPath(expectedPaths, TypedKeyAssemblyPath, errors);
            AddExpectedPath(expectedPaths, TypedKeySourcePath, errors);

            var ownedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ownedKeys = new HashSet<ushort>();
            var actualOutputs =
                new Dictionary<string, OrpheusAuthoringExistingOutputValue>(
                    StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < input.ExistingOutputCount; index++)
            {
                var output = input.GetExistingOutput(index);
                if (actualOutputs.ContainsKey(output.AssetPath))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.OutputPathCollision,
                            assetPath: output.AssetPath,
                            detail: "ActualDuplicate"));
                }
                else
                {
                    actualOutputs.Add(output.AssetPath, output);
                }
            }

            for (var index = 0; index < input.OwnedEventCount; index++)
            {
                var owned = input.GetOwnedEvent(index);
                if (!ownedKeys.Add(owned.Key) ||
                    !ownedPaths.Add(owned.AssetPath) ||
                    !IsCanonicalAssetPath(owned.AssetPath) ||
                    !IsLowerGuid(owned.Guid) ||
                    !string.Equals(
                        owned.AssetPath,
                        GetEventPath(input.GeneratedRoot, owned.Symbol),
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                            owned.Key,
                            owned.ModuleId,
                            owned.Symbol,
                            owned.AssetPath,
                            detail: "OwnedEvent"));
                }

                if (!actualOutputs.TryGetValue(owned.AssetPath, out var actual) ||
                    !actual.Exists ||
                    !actual.CompilerOwned ||
                    !string.Equals(actual.Guid, owned.Guid, StringComparison.Ordinal) ||
                    !string.Equals(
                        actual.MainType,
                        "Orpheus.Audio.OrpheusAudioEvent",
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                            owned.Key,
                            owned.ModuleId,
                            owned.Symbol,
                            owned.AssetPath,
                            detail: "ActualEvent"));
                }
            }

            for (var index = 0; index < input.ExistingOutputCount; index++)
            {
                var output = input.GetExistingOutput(index);
                if (!expectedPaths.Contains(output.AssetPath))
                {
                    continue;
                }

                if (!output.CompilerOwned)
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.OutputPathCollision,
                            assetPath: output.AssetPath,
                            detail: "Foreign"));
                }
                else if (expectedEventPaths.Contains(output.AssetPath) &&
                         !ownedPaths.Contains(output.AssetPath))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                            assetPath: output.AssetPath,
                            detail: "Untracked"));
                }
            }

            ValidateRenames(input, resolved, errors);
        }

        private static void ValidateRenames(
            OrpheusAuthoringCompilationInput input,
            IList<ResolvedRecipe> resolved,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            if (!input.HasAcceptedManifest)
            {
                return;
            }

            var accepted = new Dictionary<ushort, OrpheusAuthoringManifestEntryValue>();
            for (var index = 0; index < input.AcceptedManifestCount; index++)
            {
                var entry = input.GetAcceptedManifestEntry(index);
                if (!accepted.ContainsKey(entry.Key))
                {
                    accepted.Add(entry.Key, entry);
                }
            }

            var ownedByKey = new Dictionary<ushort, OrpheusAuthoringOwnedEventValue>();
            for (var index = 0; index < input.OwnedEventCount; index++)
            {
                var owned = input.GetOwnedEvent(index);
                if (!ownedByKey.ContainsKey(owned.Key))
                {
                    ownedByKey.Add(owned.Key, owned);
                }
            }

            for (var index = 0; index < resolved.Count; index++)
            {
                var current = resolved[index];
                if (!accepted.TryGetValue(current.Key, out var previous))
                {
                    continue;
                }

                var manifestRename =
                    previous.Status == OrpheusAudioKeyStatus.Active &&
                    !string.Equals(
                        previous.Symbol,
                        current.Recipe.Symbol,
                        StringComparison.Ordinal);
                if (!ownedByKey.TryGetValue(current.Key, out var tracked))
                {
                    if (manifestRename)
                    {
                        errors.Add(
                            Error(
                                OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                                current.Key,
                                current.ModuleId,
                                current.Recipe.Symbol,
                                detail: "RenameSource"));
                    }

                    continue;
                }

                var expectedTrackedSymbol = manifestRename
                    ? previous.Symbol
                    : current.Recipe.Symbol;
                var expectedPath = GetEventPath(
                    input.GeneratedRoot,
                    expectedTrackedSymbol);
                if (!string.Equals(
                        tracked.Symbol,
                        expectedTrackedSymbol,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        tracked.AssetPath,
                        expectedPath,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        Error(
                            OrpheusAuthoringErrorCode.OwnershipStateInvalid,
                            current.Key,
                            current.ModuleId,
                            current.Recipe.Symbol,
                            tracked.AssetPath,
                            detail: manifestRename ? "RenameSource" : "TrackedIdentity"));
                }
            }
        }

        private static OrpheusAuthoringCompiledEventValue[] BuildCompiledEvents(
            string generatedRoot,
            IList<ResolvedRecipe> resolved)
        {
            var events = new OrpheusAuthoringCompiledEventValue[resolved.Count];
            for (var index = 0; index < resolved.Count; index++)
            {
                var candidate = resolved[index];
                var path = GetEventPath(generatedRoot, candidate.Recipe.Symbol);
                var fingerprint = ComputeEventFingerprint(candidate);
                events[index] = new OrpheusAuthoringCompiledEventValue(
                    candidate.Key,
                    candidate.ModuleId,
                    candidate.Recipe,
                    path,
                    fingerprint);
            }

            return events;
        }

        private static OrpheusAuthoringFutureDeliveryValue[] BuildFutureDelivery(
            IList<ResolvedRecipe> resolved)
        {
            var ordered = new List<ResolvedRecipe>(resolved);
            ordered.Sort(CompareResolvedByModuleThenKey);
            var result = new OrpheusAuthoringFutureDeliveryValue[ordered.Count];
            for (var index = 0; index < ordered.Count; index++)
            {
                var value = ordered[index];
                result[index] = new OrpheusAuthoringFutureDeliveryValue(
                    value.ModuleId,
                    value.Recipe.ProfileHint,
                    value.Recipe.CandidateContentBankId,
                    value.Key,
                    value.Recipe.Symbol);
            }

            return result;
        }

        private static OrpheusAuthoringWriteOperationValue[] BuildWriteOperations(
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringCompiledEventValue[] events,
            ISet<ushort> manifestRenameKeys)
        {
            var operations = new List<OrpheusAuthoringWriteOperationValue>
            {
                Operation(
                    OrpheusAuthoringWriteKind.CreateDirectory,
                    assetPath: input.GeneratedRoot),
                Operation(
                    OrpheusAuthoringWriteKind.CreateDirectory,
                    assetPath:
                    input.GeneratedRoot + AssetPathSeparator + "Events")
            };
            var ownedByKey = new Dictionary<ushort, OrpheusAuthoringOwnedEventValue>();
            for (var index = 0; index < input.OwnedEventCount; index++)
            {
                var owned = input.GetOwnedEvent(index);
                if (!ownedByKey.ContainsKey(owned.Key))
                {
                    ownedByKey.Add(owned.Key, owned);
                }
            }

            var compiledKeys = new HashSet<ushort>();
            for (var index = 0; index < events.Length; index++)
            {
                var compiled = events[index];
                compiledKeys.Add(compiled.Key);
                if (!ownedByKey.TryGetValue(compiled.Key, out var owned))
                {
                    operations.Add(
                        Operation(
                            OrpheusAuthoringWriteKind.CreateEvent,
                            compiled.Key,
                            compiled.ModuleId,
                            compiled.Symbol,
                            assetPath: compiled.AssetPath,
                            contentFingerprint: compiled.ContentFingerprint));
                    continue;
                }

                if (manifestRenameKeys.Contains(compiled.Key))
                {
                    operations.Add(
                        Operation(
                            OrpheusAuthoringWriteKind.MoveRenamedEvent,
                            compiled.Key,
                            compiled.ModuleId,
                            compiled.Symbol,
                            owned.AssetPath,
                            compiled.AssetPath,
                            compiled.ContentFingerprint));
                    continue;
                }

                var kind = string.Equals(
                        owned.ContentFingerprint,
                        compiled.ContentFingerprint,
                        StringComparison.Ordinal)
                    ? OrpheusAuthoringWriteKind.KeepEvent
                    : OrpheusAuthoringWriteKind.UpdateEvent;
                operations.Add(
                    Operation(
                        kind,
                        compiled.Key,
                        compiled.ModuleId,
                        compiled.Symbol,
                        owned.AssetPath,
                        compiled.AssetPath,
                        compiled.ContentFingerprint));
            }

            List<OrpheusAuthoringOwnedEventValue> orphansToDelete = null;
            if (input.DeleteTrackedOrphans)
            {
                orphansToDelete =
                    new List<OrpheusAuthoringOwnedEventValue>();
                for (var index = 0; index < input.OwnedEventCount; index++)
                {
                    var owned = input.GetOwnedEvent(index);
                    if (!compiledKeys.Contains(owned.Key))
                    {
                        orphansToDelete.Add(owned);
                    }
                }

                orphansToDelete.Sort(CompareOwnedByKey);
            }

            operations.Add(
                Operation(
                    OrpheusAuthoringWriteKind.WriteCatalog,
                    assetPath: input.Catalog.AssetPath));
            operations.Add(
                Operation(
                    OrpheusAuthoringWriteKind.WriteTypedKeys,
                    assetPath: TypedKeySourcePath));
            if (input.WriteEnrollmentIdentity)
            {
                operations.Add(
                    Operation(
                        OrpheusAuthoringWriteKind.WriteEnrollmentIdentity,
                        assetPath: input.ValidationProfileAssetPath));
            }
            operations.Add(
                Operation(
                    OrpheusAuthoringWriteKind.WriteOwnershipIndex,
                    assetPath:
                    input.GeneratedRoot + AssetPathSeparator +
                    "OrpheusAuthoringOwnership.json"));
            if (orphansToDelete != null)
            {
                for (var index = 0; index < orphansToDelete.Count; index++)
                {
                    var orphan = orphansToDelete[index];
                    operations.Add(
                        Operation(
                            OrpheusAuthoringWriteKind.DeleteTrackedOrphan,
                            orphan.Key,
                            orphan.ModuleId,
                            orphan.Symbol,
                            orphan.AssetPath,
                            orphan.AssetPath));
                }
            }

            operations.Add(
                Operation(
                    OrpheusAuthoringWriteKind.WriteClosureReport,
                    assetPath:
                    input.GeneratedRoot + AssetPathSeparator +
                    "OrpheusAuthoringClosure.json"));
            return operations.ToArray();
        }

        private static OrpheusAuthoringOwnedEventValue[] BuildOrphans(
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringCompiledEventValue[] events)
        {
            var currentKeys = new HashSet<ushort>();
            for (var index = 0; index < events.Length; index++)
            {
                currentKeys.Add(events[index].Key);
            }

            var result = new List<OrpheusAuthoringOwnedEventValue>();
            for (var index = 0; index < input.OwnedEventCount; index++)
            {
                var owned = input.GetOwnedEvent(index);
                if (!currentKeys.Contains(owned.Key))
                {
                    result.Add(owned);
                }
            }

            result.Sort(CompareOwnedByKey);
            return result.ToArray();
        }

        private static string ComputeInputFingerprint(
            OrpheusAuthoringCompilationInput input,
            IDictionary<ushort, OrpheusAuthoringManifestEntryValue> manifestByKey,
            IList<ResolvedRecipe> resolved)
        {
            var writer = new FingerprintWriter();
            writer.WriteString(FingerprintDomain);
            writer.WriteInt32(CompilerSchemaVersion);
            writer.WriteInt32(input.AuthoringSchemaVersion);
            writer.WriteInt32(input.Manifest.SchemaVersion);
            writer.WriteString(input.GeneratedRoot);
            writer.WriteString(input.Catalog.Guid);
            writer.WriteString(input.Catalog.AssetPath);

            var manifest = new List<OrpheusAuthoringManifestEntryValue>(manifestByKey.Values);
            manifest.Sort(CompareManifestByKey);
            writer.WriteInt32(manifest.Count);
            for (var index = 0; index < manifest.Count; index++)
            {
                WriteManifestEntry(writer, manifest[index]);
            }

            var recipes = new List<ResolvedRecipe>(resolved);
            recipes.Sort(CompareResolvedByModuleThenKey);
            writer.WriteInt32(recipes.Count);
            for (var index = 0; index < recipes.Count; index++)
            {
                WriteResolvedRecipe(writer, recipes[index]);
            }

            var accepted = new List<OrpheusAuthoringManifestEntryValue>();
            for (var index = 0; index < input.AcceptedManifestCount; index++)
            {
                accepted.Add(input.GetAcceptedManifestEntry(index));
            }

            accepted.Sort(CompareManifestByKey);
            writer.WriteBoolean(input.HasAcceptedManifest);
            writer.WriteInt32(accepted.Count);
            for (var index = 0; index < accepted.Count; index++)
            {
                WriteManifestEntry(writer, accepted[index]);
            }

            var owned = new List<OrpheusAuthoringOwnedEventValue>();
            for (var index = 0; index < input.OwnedEventCount; index++)
            {
                owned.Add(input.GetOwnedEvent(index));
            }

            owned.Sort(CompareOwnedByKey);
            writer.WriteInt32(owned.Count);
            for (var index = 0; index < owned.Count; index++)
            {
                writer.WriteUInt16(owned[index].Key);
                writer.WriteString(owned[index].ModuleId);
                writer.WriteString(owned[index].Symbol);
                writer.WriteString(owned[index].Guid);
                writer.WriteString(owned[index].AssetPath);
                writer.WriteString(owned[index].ContentFingerprint);
            }

            return writer.Finish();
        }

        private static string ComputeEventFingerprint(ResolvedRecipe resolved)
        {
            var writer = new FingerprintWriter();
            writer.WriteString(FingerprintDomain);
            writer.WriteString("Event");
            WriteResolvedRecipe(writer, resolved);
            return writer.Finish();
        }

        private static string ComputeOutputFingerprint(
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringCompiledEventValue[] events,
            ushort[] catalogKeys)
        {
            var writer = new FingerprintWriter();
            writer.WriteString(FingerprintDomain);
            writer.WriteString("Output");
            writer.WriteInt32(events.Length);
            for (var index = 0; index < events.Length; index++)
            {
                var value = events[index];
                writer.WriteUInt16(value.Key);
                writer.WriteString(value.ModuleId);
                writer.WriteString(value.Symbol);
                writer.WriteString(value.AssetPath);
                writer.WriteString(value.ContentFingerprint);
            }

            writer.WriteInt32(catalogKeys.Length);
            for (var index = 0; index < catalogKeys.Length; index++)
            {
                writer.WriteUInt16(catalogKeys[index]);
            }

            writer.WriteString(input.Catalog.Guid);
            writer.WriteString(input.Catalog.AssetPath);
            writer.WriteString(input.TypedKeyAssemblyText);
            writer.WriteString(input.TypedKeySourceText);
            var finalOwnership = new List<OwnershipFingerprintRecord>();
            var compiledKeys = new HashSet<ushort>();
            for (var index = 0; index < events.Length; index++)
            {
                var compiled = events[index];
                compiledKeys.Add(compiled.Key);
                finalOwnership.Add(
                    new OwnershipFingerprintRecord(
                        compiled.Key,
                        compiled.ModuleId,
                        compiled.Symbol,
                        compiled.AssetPath));
            }

            if (!input.DeleteTrackedOrphans)
            {
                for (var index = 0; index < input.OwnedEventCount; index++)
                {
                    var owned = input.GetOwnedEvent(index);
                    if (!compiledKeys.Contains(owned.Key))
                    {
                        finalOwnership.Add(
                            new OwnershipFingerprintRecord(
                                owned.Key,
                                owned.ModuleId,
                                owned.Symbol,
                                owned.AssetPath));
                    }
                }
            }

            finalOwnership.Sort(CompareOwnershipFingerprintRecords);
            // Event GUID is meta-derived. The output contract excludes .meta
            // while ownership identity is verified separately before planning.
            writer.WriteString("OwnershipWithoutMetaGuid");
            writer.WriteInt32(finalOwnership.Count);
            for (var index = 0; index < finalOwnership.Count; index++)
            {
                var record = finalOwnership[index];
                writer.WriteUInt16(record.Key);
                writer.WriteString(record.ModuleId);
                writer.WriteString(record.Symbol);
                writer.WriteString(record.AssetPath);
            }

            return writer.Finish();
        }

        private static void WriteManifestEntry(
            FingerprintWriter writer,
            OrpheusAuthoringManifestEntryValue entry)
        {
            writer.WriteUInt16(entry.Key);
            writer.WriteString(entry.Symbol);
            writer.WriteByte((byte)entry.Status);
        }

        private static void WriteResolvedRecipe(
            FingerprintWriter writer,
            ResolvedRecipe resolved)
        {
            var recipe = resolved.Recipe;
            writer.WriteString(resolved.ModuleId);
            writer.WriteUInt16(resolved.Key);
            writer.WriteString(recipe.Symbol);
            writer.WriteByte((byte)recipe.PlaybackKind);
            writer.WriteByte((byte)recipe.Category);
            writer.WriteByte((byte)recipe.LoadPolicy);
            writer.WriteSingle(recipe.VolumeMinimum);
            writer.WriteSingle(recipe.VolumeMaximum);
            writer.WriteSingle(recipe.PitchMinimum);
            writer.WriteSingle(recipe.PitchMaximum);
            writer.WriteByte(recipe.Priority);
            writer.WriteByte(recipe.PolyphonyCap);
            writer.WriteSingle(recipe.CooldownSeconds);
            writer.WriteSingle(recipe.MinimumDistance);
            writer.WriteSingle(recipe.MaximumDistance);
            writer.WriteByte((byte)recipe.RolloffMode);
            writer.WriteString(recipe.ProfileHint);
            writer.WriteString(recipe.CandidateContentBankId);
            writer.WriteInt32(recipe.ClipCount);
            for (var index = 0; index < recipe.ClipCount; index++)
            {
                var clip = recipe.GetClip(index);
                writer.WriteString(clip.Guid);
                writer.WriteInt64(clip.LocalFileId);
            }
        }

        private static HashSet<ushort> GetManifestRenameKeys(
            OrpheusAuthoringCompilationInput input,
            IList<ResolvedRecipe> resolved)
        {
            var result = new HashSet<ushort>();
            if (!input.HasAcceptedManifest)
            {
                return result;
            }

            var accepted = new Dictionary<ushort, OrpheusAuthoringManifestEntryValue>();
            for (var index = 0; index < input.AcceptedManifestCount; index++)
            {
                var entry = input.GetAcceptedManifestEntry(index);
                if (!accepted.ContainsKey(entry.Key))
                {
                    accepted.Add(entry.Key, entry);
                }
            }

            for (var index = 0; index < resolved.Count; index++)
            {
                var current = resolved[index];
                if (accepted.TryGetValue(current.Key, out var previous) &&
                    previous.Status == OrpheusAudioKeyStatus.Active &&
                    !string.Equals(
                        previous.Symbol,
                        current.Recipe.Symbol,
                        StringComparison.Ordinal))
                {
                    result.Add(current.Key);
                }
            }

            return result;
        }

        private static void AddExpectedPath(
            ISet<string> expectedPaths,
            string path,
            ICollection<OrpheusAuthoringCompilationError> errors)
        {
            if (!expectedPaths.Add(path))
            {
                errors.Add(
                    Error(
                        OrpheusAuthoringErrorCode.OutputPathCollision,
                        assetPath: path));
            }
        }

        private static bool IsSlug(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value[0] == '-' ||
                value[value.Length - 1] == '-')
            {
                return false;
            }

            var previousHyphen = false;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                var valid = character >= 'a' && character <= 'z' ||
                            character >= '0' && character <= '9' ||
                            character == '-';
                if (!valid || character == '-' && previousHyphen)
                {
                    return false;
                }

                previousHyphen = character == '-';
            }

            return true;
        }

        private static bool IsOptionalSlug(string value)
        {
            return string.IsNullOrEmpty(value) || IsSlug(value);
        }

        private static bool IsLowerGuid(string value)
        {
            if (value == null || value.Length != 32)
            {
                return false;
            }

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

        private static bool IsCanonicalAssetPath(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                !value.StartsWith("Assets/", StringComparison.Ordinal) ||
                value[value.Length - 1] == AssetPathSeparator ||
                value.IndexOf('\\') >= 0 ||
                value.IndexOf(':') >= 0 ||
                value.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            var segments = value.Split(AssetPathSeparator);
            for (var index = 0; index < segments.Length; index++)
            {
                if (segments[index].Length == 0 ||
                    segments[index] == "." ||
                    segments[index] == "..")
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PathsOverlap(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
                   left.StartsWith(
                       right + AssetPathSeparator,
                       StringComparison.OrdinalIgnoreCase) ||
                   right.StartsWith(
                       left + AssetPathSeparator,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string GetEventPath(string root, string symbol)
        {
            return root + AssetPathSeparator +
                   "Events/AE_" + symbol + ".asset";
        }

        private static int CompareResolvedByKey(ResolvedRecipe left, ResolvedRecipe right)
        {
            var result = left.Key.CompareTo(right.Key);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(left.ModuleId, right.ModuleId, StringComparison.Ordinal);
            return result != 0
                ? result
                : string.Compare(
                    left.Recipe.Symbol,
                    right.Recipe.Symbol,
                    StringComparison.Ordinal);
        }

        private static int CompareModuleRecipes(
            OrpheusAuthoringModuleRecipeValue left,
            OrpheusAuthoringModuleRecipeValue right)
        {
            var result = string.Compare(
                left.ModuleId,
                right.ModuleId,
                StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = left.SchemaVersion.CompareTo(right.SchemaVersion);
            return result != 0 ? result : left.EventCount.CompareTo(right.EventCount);
        }

        private static int CompareResolvedByModuleThenKey(
            ResolvedRecipe left,
            ResolvedRecipe right)
        {
            var result = string.Compare(
                left.ModuleId,
                right.ModuleId,
                StringComparison.Ordinal);
            return result != 0 ? result : left.Key.CompareTo(right.Key);
        }

        private static int CompareManifestByKey(
            OrpheusAuthoringManifestEntryValue left,
            OrpheusAuthoringManifestEntryValue right)
        {
            var result = left.Key.CompareTo(right.Key);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(left.Symbol, right.Symbol, StringComparison.Ordinal);
            return result != 0
                ? result
                : ((byte)left.Status).CompareTo((byte)right.Status);
        }

        private static int CompareOwnedByKey(
            OrpheusAuthoringOwnedEventValue left,
            OrpheusAuthoringOwnedEventValue right)
        {
            var result = left.Key.CompareTo(right.Key);
            return result != 0
                ? result
                : string.Compare(left.AssetPath, right.AssetPath, StringComparison.Ordinal);
        }

        private static int CompareOwnershipFingerprintRecords(
            OwnershipFingerprintRecord left,
            OwnershipFingerprintRecord right)
        {
            var result = left.Key.CompareTo(right.Key);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(left.ModuleId, right.ModuleId, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(left.Symbol, right.Symbol, StringComparison.Ordinal);
            return result != 0
                ? result
                : string.Compare(left.AssetPath, right.AssetPath, StringComparison.Ordinal);
        }

        private static int CompareErrors(
            OrpheusAuthoringCompilationError left,
            OrpheusAuthoringCompilationError right)
        {
            var result = ((ushort)left.Code).CompareTo((ushort)right.Code);
            if (result != 0)
            {
                return result;
            }

            result = left.Key.CompareTo(right.Key);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(left.ModuleId, right.ModuleId, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(left.Symbol, right.Symbol, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(left.AssetPath, right.AssetPath, StringComparison.Ordinal);
            if (result != 0)
            {
                return result;
            }

            result = left.ClipIndex.CompareTo(right.ClipIndex);
            return result != 0
                ? result
                : string.Compare(left.Detail, right.Detail, StringComparison.Ordinal);
        }

        private static OrpheusAuthoringCompilationError Error(
            OrpheusAuthoringErrorCode code,
            ushort key = 0,
            string moduleId = "",
            string symbol = "",
            string assetPath = "",
            int clipIndex = -1,
            string detail = "")
        {
            return new OrpheusAuthoringCompilationError(
                code,
                key,
                moduleId,
                symbol,
                assetPath,
                clipIndex,
                detail);
        }

        private static OrpheusAuthoringWriteOperationValue Operation(
            OrpheusAuthoringWriteKind kind,
            ushort key = 0,
            string moduleId = "",
            string symbol = "",
            string sourcePath = "",
            string assetPath = "",
            string contentFingerprint = "")
        {
            return new OrpheusAuthoringWriteOperationValue(
                kind,
                key,
                moduleId,
                symbol,
                sourcePath,
                assetPath,
                contentFingerprint);
        }

        private sealed class ResolvedRecipe
        {
            internal ResolvedRecipe(
                ushort key,
                string moduleId,
                OrpheusAuthoringEventRecipeValue recipe)
            {
                Key = key;
                ModuleId = moduleId;
                Recipe = recipe;
            }

            internal ushort Key { get; }

            internal string ModuleId { get; }

            internal OrpheusAuthoringEventRecipeValue Recipe { get; }
        }

        private sealed class OwnershipFingerprintRecord
        {
            internal OwnershipFingerprintRecord(
                ushort key,
                string moduleId,
                string symbol,
                string assetPath)
            {
                Key = key;
                ModuleId = moduleId;
                Symbol = symbol;
                AssetPath = assetPath;
            }

            internal ushort Key { get; }

            internal string ModuleId { get; }

            internal string Symbol { get; }

            internal string AssetPath { get; }
        }

        private sealed class FingerprintWriter
        {
            private readonly MemoryStream _stream = new MemoryStream();

            internal void WriteBoolean(bool value)
            {
                WriteByte(value ? (byte)1 : (byte)0);
            }

            internal void WriteByte(byte value)
            {
                WriteBytes(new[] { value });
            }

            internal void WriteUInt16(ushort value)
            {
                WriteBytes(
                    new[]
                    {
                        (byte)value,
                        (byte)(value >> 8)
                    });
            }

            internal void WriteInt32(int value)
            {
                WriteBytes(
                    new[]
                    {
                        (byte)value,
                        (byte)(value >> 8),
                        (byte)(value >> 16),
                        (byte)(value >> 24)
                    });
            }

            internal void WriteInt64(long value)
            {
                WriteBytes(
                    new[]
                    {
                        (byte)value,
                        (byte)(value >> 8),
                        (byte)(value >> 16),
                        (byte)(value >> 24),
                        (byte)(value >> 32),
                        (byte)(value >> 40),
                        (byte)(value >> 48),
                        (byte)(value >> 56)
                    });
            }

            internal void WriteSingle(float value)
            {
                var normalized = value == 0f ? 0f : value;
                var bytes = BitConverter.GetBytes(normalized);
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                WriteBytes(bytes);
            }

            internal void WriteString(string value)
            {
                WriteBytes(Encoding.UTF8.GetBytes(value ?? string.Empty));
            }

            internal string Finish()
            {
                using (var sha256 = SHA256.Create())
                {
                    var digest = sha256.ComputeHash(_stream.ToArray());
                    var result = new StringBuilder(digest.Length * 2);
                    for (var index = 0; index < digest.Length; index++)
                    {
                        result.Append(digest[index].ToString("x2"));
                    }

                    return result.ToString();
                }
            }

            private void WriteBytes(byte[] value)
            {
                var length = value.Length;
                _stream.WriteByte((byte)length);
                _stream.WriteByte((byte)(length >> 8));
                _stream.WriteByte((byte)(length >> 16));
                _stream.WriteByte((byte)(length >> 24));
                _stream.Write(value, 0, value.Length);
            }
        }
    }
}
