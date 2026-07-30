using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Orpheus.Audio.Core;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioAuthoringCompilationPolicyTests
    {
        private const char AssetPathSeparator = (char)47;
        private const string ClipA = "11111111111111111111111111111111";
        private const string ClipB = "22222222222222222222222222222222";

        [Test]
        public void ErrorAbi_IsAppendOnlyAndExact()
        {
            var names = Enum.GetNames(typeof(OrpheusAuthoringErrorCode));
            var values = Enum.GetValues(typeof(OrpheusAuthoringErrorCode))
                .Cast<OrpheusAuthoringErrorCode>()
                .Select(value => (ushort)value)
                .ToArray();

            Assert.That(names, Is.EqualTo(new[]
            {
                "Invalid",
                "CompilerNotImplemented",
                "InvalidValidationProfile",
                "DisabledValidationProfile",
                "ManualProfileNotCompilable",
                "SchemaVersionMismatch",
                "MissingManifest",
                "DuplicateManifestId",
                "DuplicateManifestSymbol",
                "InvalidManifestStatus",
                "ManifestIdentityRemoved",
                "ManifestLifecycleRegression",
                "ManifestIdReuse",
                "InvalidSymbolRename",
                "NullRecipe",
                "InvalidModuleId",
                "DuplicateModuleId",
                "MissingRecipeSymbol",
                "InactiveRecipeSymbol",
                "DuplicateRecipeOwner",
                "ActiveManifestIdentityUnowned",
                "InvalidEventPolicy",
                "InvalidClipIdentity",
                "DuplicateClip",
                "InvalidGeneratedRoot",
                "OutputPathCollision",
                "GeneratedRootOverlap",
                "TypedKeyOwnershipConflict",
                "EnrollmentIdentityMismatch",
                "LostOwnershipState",
                "CatalogMismatch",
                "CatalogNotEmptyAtEnrollment",
                "OwnershipStateInvalid",
                "ImporterPolicyMismatch",
                "ActualOutputFingerprintMismatch",
                "TransactionRecoveryRequired",
                "ConcurrentCompilation",
                "RollbackFailed",
                "AuthoringProfileConflict",
                "ManualProfileWouldBecomeStale",
                "UnsupportedBuildTarget",
                "InvalidBatchArguments"
            }));
            Assert.That(values, Is.EqualTo(Enumerable.Range(0, 42).Select(value => (ushort)value)));
            Assert.That(Enum.GetUnderlyingType(typeof(OrpheusAuthoringErrorCode)), Is.EqualTo(typeof(ushort)));
        }

        [Test]
        public void ErrorPayload_HasExactNormalizedSurface()
        {
            var properties = typeof(OrpheusAuthoringCompilationError)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var error = new OrpheusAuthoringCompilationError(
                OrpheusAuthoringErrorCode.Invalid,
                0,
                null,
                null,
                null,
                -1,
                null);

            Assert.That(properties, Is.EqualTo(new[]
            {
                "AssetPath", "ClipIndex", "Code", "Detail", "Key", "ModuleId", "Symbol"
            }));
            Assert.That(error.ModuleId, Is.EqualTo(string.Empty));
            Assert.That(error.Symbol, Is.EqualTo(string.Empty));
            Assert.That(error.AssetPath, Is.EqualTo(string.Empty));
            Assert.That(error.Detail, Is.EqualTo(string.Empty));
        }

        [Test]
        public void TryCompile_CanonicalizesInputOrderAndPreservesAuthoredClipOrder()
        {
            var clips = new[]
            {
                Clip(ClipB, 22, "Assets/Audio/B.wav"),
                Clip(ClipA, 11, "Assets/Audio/A.wav")
            };
            var first = Input(
                new[]
                {
                    Manifest(200, "WorldCue"),
                    Manifest(100, "UiCue")
                },
                new[]
                {
                    Module("world", Event("WorldCue", clips)),
                    Module("ui", Event("UiCue", new[] { Clip(ClipA, 1, "Assets/Audio/UI.wav") }))
                });
            var second = Input(
                new[]
                {
                    Manifest(100, "UiCue"),
                    Manifest(200, "WorldCue")
                },
                new[]
                {
                    Module("ui", Event("UiCue", new[] { Clip(ClipA, 1, "Assets/Audio/UI.wav") })),
                    Module("world", Event("WorldCue", clips))
                });

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(first, out var firstPlan, out var firstErrors),
                Is.True);
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(second, out var secondPlan, out var secondErrors),
                Is.True);
            Assert.That(firstErrors, Is.Empty);
            Assert.That(secondErrors, Is.Empty);
            Assert.That(firstPlan.InputFingerprint, Is.EqualTo(secondPlan.InputFingerprint));
            Assert.That(firstPlan.OutputFingerprint, Is.EqualTo(secondPlan.OutputFingerprint));
            Assert.That(firstPlan.CatalogKeys, Is.EqualTo(new ushort[] { 100, 200 }));
            Assert.That(firstPlan.GetEvent(0).Key, Is.EqualTo(100));
            Assert.That(firstPlan.GetEvent(1).Key, Is.EqualTo(200));
            Assert.That(firstPlan.GetEvent(1).GetClip(0).Guid, Is.EqualTo(ClipB));
            Assert.That(firstPlan.GetEvent(1).GetClip(1).Guid, Is.EqualTo(ClipA));
        }

        [Test]
        public void TryCompile_ProjectsCandidateContentBanksInModuleThenKeyOrder()
        {
            var input = Input(
                new[] { Manifest(200, "B"), Manifest(100, "A") },
                new[]
                {
                    Module("z-module", Event("B", profileHint: "late", bankId: "bank-z")),
                    Module("a-module", Event("A", profileHint: "early", bankId: "bank-a"))
                });

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(input, out var plan, out var errors),
                Is.True);
            Assert.That(errors, Is.Empty);
            Assert.That(plan.FutureDeliveryCount, Is.EqualTo(2));
            Assert.That(plan.GetFutureDelivery(0).ModuleId, Is.EqualTo("a-module"));
            Assert.That(plan.GetFutureDelivery(0).Key, Is.EqualTo(100));
            Assert.That(plan.GetFutureDelivery(1).ModuleId, Is.EqualTo("z-module"));
            Assert.That(plan.GetFutureDelivery(1).Key, Is.EqualTo(200));
        }

        [Test]
        public void TryCompile_ManifestRenameEmitsGuidPreservingMove()
        {
            var input = Input(
                new[] { Manifest(100, "NewCue") },
                new[] { Module("ui", Event("NewCue")) },
                acceptedManifest: new[] { Manifest(100, "OldCue") },
                ownedEvents: new[]
                {
                    new OrpheusAuthoringOwnedEventValue(
                        100,
                        "ui",
                        "OldCue",
                        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        "Assets/Audio/OrpheusGenerated/Events/AE_OldCue.asset",
                        string.Empty)
                });

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(input, out var plan, out var errors),
                Is.True);
            Assert.That(errors, Is.Empty);
            var move = Enumerable.Range(0, plan.WriteOperationCount)
                .Select(plan.GetWriteOperation)
                .Single(operation => operation.Kind == OrpheusAuthoringWriteKind.MoveRenamedEvent);
            Assert.That(move.Key, Is.EqualTo(100));
            Assert.That(
                move.SourcePath,
                Does.EndWith(AssetPathSeparator + "AE_OldCue.asset"));
            Assert.That(
                move.AssetPath,
                Does.EndWith(AssetPathSeparator + "AE_NewCue.asset"));
            var change = OrpheusAudioAuthoringCompiler.BuildChanges(
                    input,
                    plan,
                    OrpheusAudioAuthoringCompileMode.Analyze,
                    false)
                .Single(value => string.Equals(
                    value.Path,
                    move.AssetPath,
                    StringComparison.Ordinal));
            Assert.That(change.Action, Is.EqualTo("moved"));
            Assert.That(change.FieldChangeCount, Is.EqualTo(1));
            Assert.That(change.GetFieldChange(0).Field, Is.EqualTo("symbol"));
            Assert.That(change.GetFieldChange(0).Before, Is.EqualTo("OldCue"));
            Assert.That(change.GetFieldChange(0).After, Is.EqualTo("NewCue"));
        }

        [Test]
        public void TryCompile_RejectsRenameWhenTrackedSourceDoesNotMatchAcceptedIdentity()
        {
            var input = Input(
                new[] { Manifest(100, "NewCue") },
                new[] { Module("ui", Event("NewCue")) },
                acceptedManifest: new[] { Manifest(100, "OldCue") },
                ownedEvents: new[]
                {
                    new OrpheusAuthoringOwnedEventValue(
                        100,
                        "ui",
                        "DifferentCue",
                        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        "Assets/Audio/OrpheusGenerated/Events/AE_DifferentCue.asset",
                        string.Empty)
                });

            AssertRejected(input, OrpheusAuthoringErrorCode.OwnershipStateInvalid);
        }

        [Test]
        public void TryCompile_RejectsActiveManifestRenameWithoutTrackedSource()
        {
            var input = Input(
                new[] { Manifest(100, "NewCue") },
                new[] { Module("ui", Event("NewCue")) },
                acceptedManifest: new[] { Manifest(100, "OldCue") });

            AssertRejected(input, OrpheusAuthoringErrorCode.OwnershipStateInvalid);
        }

        [Test]
        public void TryCompile_RejectsTrackedEventOutsideGeneratedEventRoot()
        {
            var input = Input(
                new[] { Manifest(100, "Cue") },
                new[] { Module("ui", Event("Cue")) },
                acceptedManifest: new[] { Manifest(100, "Cue") },
                ownedEvents: new[]
                {
                    new OrpheusAuthoringOwnedEventValue(
                        100,
                        "ui",
                        "Cue",
                        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        "Assets/Foreign/AE_Cue.asset",
                        string.Empty)
                });

            AssertRejected(input, OrpheusAuthoringErrorCode.OwnershipStateInvalid);
        }

        [Test]
        public void TryCompile_DoesNotMoveWithoutManifestAuthoredRename()
        {
            var input = Input(
                new[] { Manifest(100, "Cue") },
                new[] { Module("ui", Event("Cue")) },
                acceptedManifest: new[] { Manifest(100, "Cue") },
                ownedEvents: new[]
                {
                    new OrpheusAuthoringOwnedEventValue(
                        100,
                        "ui",
                        "DifferentCue",
                        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        "Assets/Audio/OrpheusGenerated/Events/AE_DifferentCue.asset",
                        string.Empty)
                });

            AssertRejected(input, OrpheusAuthoringErrorCode.OwnershipStateInvalid);
        }

        [Test]
        public void TryCompile_WritesHostOwnedCatalogAtCapturedPath()
        {
            var catalog = new OrpheusAuthoringCatalogValue(
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "Assets/HostAudio/MainCatalog.asset",
                0,
                false);
            var input = Input(
                new[] { Manifest(100, "Cue") },
                new[] { Module("ui", Event("Cue")) },
                catalog: catalog,
                existingOutputs: new[]
                {
                    new OrpheusAuthoringExistingOutputValue(
                        catalog.AssetPath,
                        false,
                        catalog.Guid,
                        "Orpheus.Audio.OrpheusAudioCatalog",
                        true)
                });

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(input, out var plan, out var errors),
                Is.True,
                string.Join("\n", errors.Select(FormatError)));
            var write = Enumerable.Range(0, plan.WriteOperationCount)
                .Select(plan.GetWriteOperation)
                .Single(operation => operation.Kind == OrpheusAuthoringWriteKind.WriteCatalog);
            Assert.That(write.AssetPath, Is.EqualTo(catalog.AssetPath));
        }

        [Test]
        public void TryCompile_RejectsOrphanDeleteWithoutActualGuidAndTypeProof()
        {
            var owned = new OrpheusAuthoringOwnedEventValue(
                100,
                "ui",
                "Cue",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "Assets/Audio/OrpheusGenerated/Events/AE_Cue.asset",
                string.Empty);
            var input = Input(
                new[] { Manifest(100, "Cue", OrpheusAudioKeyStatus.Retired) },
                Array.Empty<OrpheusAuthoringModuleRecipeValue>(),
                acceptedManifest: new[] { Manifest(100, "Cue") },
                ownedEvents: new[] { owned },
                existingOutputs: new[]
                {
                    new OrpheusAuthoringExistingOutputValue(
                        owned.AssetPath,
                        true,
                        "cccccccccccccccccccccccccccccccc",
                        "Orpheus.Audio.OrpheusAudioCatalog",
                        true)
                },
                deleteTrackedOrphans: true);

            AssertRejected(input, OrpheusAuthoringErrorCode.OwnershipStateInvalid);
        }

        [Test]
        public void TryCompile_RejectsTrackedOwnershipWithoutAcceptedManifestSnapshot()
        {
            var owned = new OrpheusAuthoringOwnedEventValue(
                100,
                "ui",
                "Cue",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "Assets/Audio/OrpheusGenerated/Events/AE_Cue.asset",
                string.Empty);
            var input = Input(
                new[] { Manifest(100, "Cue", OrpheusAudioKeyStatus.Retired) },
                Array.Empty<OrpheusAuthoringModuleRecipeValue>(),
                ownedEvents: new[] { owned },
                deleteTrackedOrphans: true);

            AssertRejected(input, OrpheusAuthoringErrorCode.LostOwnershipState);
        }

        [Test]
        public void TryCompile_RejectsOwnedSymbolThatDisagreesWithAcceptedManifest()
        {
            var owned = new OrpheusAuthoringOwnedEventValue(
                100,
                "ui",
                "DifferentCue",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "Assets/Audio/OrpheusGenerated/Events/AE_DifferentCue.asset",
                string.Empty);
            var input = Input(
                new[] { Manifest(100, "Cue", OrpheusAudioKeyStatus.Retired) },
                Array.Empty<OrpheusAuthoringModuleRecipeValue>(),
                acceptedManifest: new[] { Manifest(100, "Cue") },
                ownedEvents: new[] { owned },
                deleteTrackedOrphans: true);

            AssertRejected(input, OrpheusAuthoringErrorCode.OwnershipStateInvalid);
        }

        [Test]
        public void OutputFingerprint_TracksPreservedVersusDeletedOrphanOwnership()
        {
            var owned = new OrpheusAuthoringOwnedEventValue(
                100,
                "ui",
                "Cue",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "Assets/Audio/OrpheusGenerated/Events/AE_Cue.asset",
                string.Empty);
            var preserve = Input(
                new[] { Manifest(100, "Cue", OrpheusAudioKeyStatus.Retired) },
                Array.Empty<OrpheusAuthoringModuleRecipeValue>(),
                acceptedManifest: new[] { Manifest(100, "Cue") },
                ownedEvents: new[] { owned });
            var delete = Input(
                new[] { Manifest(100, "Cue", OrpheusAudioKeyStatus.Retired) },
                Array.Empty<OrpheusAuthoringModuleRecipeValue>(),
                acceptedManifest: new[] { Manifest(100, "Cue") },
                ownedEvents: new[] { owned },
                deleteTrackedOrphans: true);

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(
                    preserve,
                    out var preservePlan,
                    out _),
                Is.True);
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(
                    delete,
                    out var deletePlan,
                    out _),
                Is.True);
            Assert.That(
                preservePlan.OutputFingerprint,
                Is.Not.EqualTo(deletePlan.OutputFingerprint));
        }

        [Test]
        public void TryCompile_ProducesCreateUpdateKeepAndDeleteOperations()
        {
            var createInput = Input(
                new[] { Manifest(100, "Cue") },
                new[] { Module("ui", Event("Cue")) });
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(
                    createInput,
                    out var createPlan,
                    out _),
                Is.True);
            var create = Enumerable.Range(0, createPlan.WriteOperationCount)
                .Select(createPlan.GetWriteOperation)
                .Single(operation => operation.Kind == OrpheusAuthoringWriteKind.CreateEvent);

            var owned = new OrpheusAuthoringOwnedEventValue(
                100,
                "ui",
                "Cue",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                create.AssetPath,
                create.ContentFingerprint);
            var keepInput = Input(
                new[] { Manifest(100, "Cue") },
                new[] { Module("ui", Event("Cue")) },
                acceptedManifest: new[] { Manifest(100, "Cue") },
                ownedEvents: new[] { owned });
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(
                    keepInput,
                    out var keepPlan,
                    out _),
                Is.True);
            Assert.That(
                Enumerable.Range(0, keepPlan.WriteOperationCount)
                    .Select(keepPlan.GetWriteOperation)
                    .Any(operation => operation.Kind == OrpheusAuthoringWriteKind.KeepEvent),
                Is.True);
            Assert.That(
                OrpheusAudioAuthoringTransaction.GetSuccessStatus(keepPlan),
                Is.EqualTo(
                    OrpheusAudioAuthoringCompileStatus.SucceededUnchanged));

            var updateInput = Input(
                new[] { Manifest(100, "Cue") },
                new[]
                {
                    Module(
                        "ui",
                        Event(
                            "Cue",
                            clips: new[] { Clip(ClipB, 2, "Assets/Audio/Changed.wav") }))
                },
                acceptedManifest: new[] { Manifest(100, "Cue") },
                ownedEvents: new[] { owned });
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(
                    updateInput,
                    out var updatePlan,
                    out _),
                Is.True);
            Assert.That(
                Enumerable.Range(0, updatePlan.WriteOperationCount)
                    .Select(updatePlan.GetWriteOperation)
                    .Any(operation => operation.Kind == OrpheusAuthoringWriteKind.UpdateEvent),
                Is.True);

            var deleteInput = Input(
                new[] { Manifest(100, "Cue", OrpheusAudioKeyStatus.Retired) },
                Array.Empty<OrpheusAuthoringModuleRecipeValue>(),
                acceptedManifest: new[] { Manifest(100, "Cue") },
                ownedEvents: new[] { owned },
                deleteTrackedOrphans: true);
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(
                    deleteInput,
                    out var deletePlan,
                    out _),
                Is.True);
            Assert.That(
                Enumerable.Range(0, deletePlan.WriteOperationCount)
                    .Select(deletePlan.GetWriteOperation)
                    .Any(operation => operation.Kind == OrpheusAuthoringWriteKind.DeleteTrackedOrphan),
                Is.True);
            var deleteOperations = Enumerable.Range(
                    0,
                    deletePlan.WriteOperationCount)
                .Select(deletePlan.GetWriteOperation)
                .ToArray();
            Assert.That(
                Array.FindIndex(
                    deleteOperations,
                    operation =>
                        operation.Kind ==
                        OrpheusAuthoringWriteKind.DeleteTrackedOrphan),
                Is.GreaterThan(
                    Array.FindIndex(
                        deleteOperations,
                        operation =>
                            operation.Kind ==
                            OrpheusAuthoringWriteKind.WriteOwnershipIndex)));
            Assert.That(
                OrpheusAudioAuthoringTransaction.GetSuccessStatus(deletePlan),
                Is.EqualTo(
                    OrpheusAudioAuthoringCompileStatus.SucceededChanged));
            var failedChanges = OrpheusAudioAuthoringCompiler.BuildChanges(
                deleteInput,
                deletePlan,
                OrpheusAudioAuthoringCompileMode
                    .CompileAndDeleteTrackedOrphans,
                true);
            Assert.That(
                failedChanges.Count(change =>
                    string.Equals(
                        change.Path,
                        owned.AssetPath,
                        StringComparison.Ordinal)),
                Is.EqualTo(1));
            Assert.That(
                failedChanges.Single(change =>
                    string.Equals(
                        change.Path,
                        owned.AssetPath,
                        StringComparison.Ordinal)).Action,
                Is.EqualTo("blocked"));

            var reportOnlyInput = Input(
                new[] { Manifest(100, "Cue", OrpheusAudioKeyStatus.Retired) },
                Array.Empty<OrpheusAuthoringModuleRecipeValue>(),
                acceptedManifest: new[] { Manifest(100, "Cue") },
                ownedEvents: new[] { owned });
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(
                    reportOnlyInput,
                    out var reportOnlyPlan,
                    out _),
                Is.True);
            Assert.That(reportOnlyPlan.OrphanCount, Is.EqualTo(1));
            Assert.That(
                OrpheusAudioAuthoringCompiler.GetSuccessStatus(
                    reportOnlyInput,
                    reportOnlyPlan),
                Is.EqualTo(
                    OrpheusAudioAuthoringCompileStatus.SucceededChanged));
            Assert.That(reportOnlyPlan.GetOrphan(0).Guid, Is.EqualTo(owned.Guid));
            Assert.That(
                Enumerable.Range(0, reportOnlyPlan.WriteOperationCount)
                    .Select(reportOnlyPlan.GetWriteOperation)
                    .Any(operation =>
                        operation.Kind ==
                        OrpheusAuthoringWriteKind.DeleteTrackedOrphan),
                Is.False);
        }

        [Test]
        public void SuccessStatus_IsOrderStableAndDetectsCatalogReferenceDrift()
        {
            var accepted = new[]
            {
                Manifest(100, "CueA"),
                Manifest(200, "CueB")
            };
            var recipes = new[]
            {
                Module("ui", Event("CueA"), Event("CueB"))
            };
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(
                    Input(accepted, recipes),
                    out var initialPlan,
                    out var initialErrors),
                Is.True,
                string.Join("\n", initialErrors.Select(FormatError)));

            var owned = Enumerable.Range(0, initialPlan.EventCount)
                .Select(index =>
                {
                    var compiled = initialPlan.GetEvent(index);
                    return new OrpheusAuthoringOwnedEventValue(
                        compiled.Key,
                        compiled.ModuleId,
                        compiled.Symbol,
                        new string((char)('a' + index), 32),
                        compiled.AssetPath,
                        compiled.ContentFingerprint);
                })
                .ToArray();
            var catalogPaths = Enumerable.Range(0, initialPlan.EventCount)
                .Select(index => initialPlan.GetEvent(index).AssetPath)
                .ToArray();
            var reordered = new[]
            {
                Manifest(200, "CueB"),
                Manifest(100, "CueA")
            };
            var catalog = new OrpheusAuthoringCatalogValue(
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "Assets/HostAudio/OrpheusAudioCatalog.asset",
                catalogPaths.Length,
                false,
                catalogPaths);
            var stableInput = Input(
                reordered,
                recipes,
                acceptedManifest: accepted,
                ownedEvents: owned,
                catalog: catalog);
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(
                    stableInput,
                    out var stablePlan,
                    out var stableErrors),
                Is.True,
                string.Join("\n", stableErrors.Select(FormatError)));
            Assert.That(
                OrpheusAudioAuthoringCompiler.GetSuccessStatus(
                    stableInput,
                    stablePlan),
                Is.EqualTo(
                    OrpheusAudioAuthoringCompileStatus.SucceededUnchanged));

            Array.Reverse(catalogPaths);
            var driftedCatalog = new OrpheusAuthoringCatalogValue(
                catalog.Guid,
                catalog.AssetPath,
                catalogPaths.Length,
                false,
                catalogPaths);
            var driftedInput = Input(
                reordered,
                recipes,
                acceptedManifest: accepted,
                ownedEvents: owned,
                catalog: driftedCatalog);
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(
                    driftedInput,
                    out var driftedPlan,
                    out var driftedErrors),
                Is.True,
                string.Join("\n", driftedErrors.Select(FormatError)));
            Assert.That(
                OrpheusAudioAuthoringCompiler.GetSuccessStatus(
                    driftedInput,
                    driftedPlan),
                Is.EqualTo(
                    OrpheusAudioAuthoringCompileStatus.SucceededChanged));
        }

        [TestCase(2, 1)]
        [TestCase(2, 3)]
        [TestCase(1, 3)]
        public void TryCompile_AcceptsAllowedLifecycleTransitions(
            byte previousRaw,
            byte currentRaw)
        {
            var previous = (OrpheusAudioKeyStatus)previousRaw;
            var current = (OrpheusAudioKeyStatus)currentRaw;
            var recipes = current == OrpheusAudioKeyStatus.Active
                ? new[] { Module("ui", Event("Cue")) }
                : Array.Empty<OrpheusAuthoringModuleRecipeValue>();
            var ownedEvents = previous == OrpheusAudioKeyStatus.Active
                ? new[]
                {
                    new OrpheusAuthoringOwnedEventValue(
                        100,
                        "ui",
                        "Cue",
                        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        "Assets/Audio/OrpheusGenerated/Events/AE_Cue.asset",
                        string.Empty)
                }
                : null;
            var input = Input(
                new[] { Manifest(100, "Cue", current) },
                recipes,
                acceptedManifest: new[] { Manifest(100, "Cue", previous) },
                ownedEvents: ownedEvents);

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(input, out _, out var errors),
                Is.True,
                string.Join("\n", errors.Select(error => error.Code + ":" + error.Detail)));
        }

        [TestCase(1, 2)]
        [TestCase(3, 1)]
        [TestCase(3, 2)]
        public void TryCompile_RejectsForbiddenLifecycleTransitions(
            byte previousRaw,
            byte currentRaw)
        {
            var previous = (OrpheusAudioKeyStatus)previousRaw;
            var current = (OrpheusAudioKeyStatus)currentRaw;
            var recipes = current == OrpheusAudioKeyStatus.Active
                ? new[] { Module("ui", Event("Cue")) }
                : Array.Empty<OrpheusAuthoringModuleRecipeValue>();
            var input = Input(
                new[] { Manifest(100, "Cue", current) },
                recipes,
                acceptedManifest: new[] { Manifest(100, "Cue", previous) });

            AssertRejected(input, OrpheusAuthoringErrorCode.ManifestLifecycleRegression);
        }

        [Test]
        public void TryCompile_RejectsRemovedManifestIdentity()
        {
            var input = Input(
                new[] { Manifest(100, "A") },
                new[] { Module("a", Event("A")) },
                acceptedManifest: new[] { Manifest(100, "A"), Manifest(200, "B", OrpheusAudioKeyStatus.Retired) });

            AssertRejected(input, OrpheusAuthoringErrorCode.ManifestIdentityRemoved);
        }

        [Test]
        public void TryCompile_RejectsRetiredIdReuse()
        {
            var input = Input(
                new[] { Manifest(100, "NewCue", OrpheusAudioKeyStatus.Retired) },
                Array.Empty<OrpheusAuthoringModuleRecipeValue>(),
                acceptedManifest: new[] { Manifest(100, "OldCue", OrpheusAudioKeyStatus.Retired) });

            AssertRejected(input, OrpheusAuthoringErrorCode.ManifestIdReuse);
        }

        [TestCaseSource(nameof(RejectionCases))]
        public void TryCompile_RejectsInvalidNormalizedInput(
            string name,
            object inputValue,
            ushort expectedValue)
        {
            AssertRejected(
                (OrpheusAuthoringCompilationInput)inputValue,
                (OrpheusAuthoringErrorCode)expectedValue,
                name);
        }

        [Test]
        public void TryCompile_SortsErrorsAndEmitsNoOperationsOnRejection()
        {
            var input = Input(
                new[] { Manifest(200, "B"), Manifest(100, "A") },
                new[]
                {
                    Module("bad_Module", Event("B", clips: new[] { Clip("BAD", 0, string.Empty) })),
                    Module("bad_Module", Event("Missing"))
                });

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(input, out var plan, out var errors),
                Is.False);
            Assert.That(plan.WriteOperationCount, Is.EqualTo(0));
            var actual = errors.Select(ErrorSortKey).ToArray();
            var sorted = (string[])actual.Clone();
            Array.Sort(sorted, StringComparer.Ordinal);
            Assert.That(actual, Is.EqualTo(sorted));
        }

        [Test]
        public void TryCompile_CanonicalizesConflictErrorsAcrossInputOrder()
        {
            var first = Input(
                new[] { Manifest(100, "Cue") },
                new[] { Module("z-owner", Event("Cue")), Module("a-owner", Event("Cue")) });
            var second = Input(
                new[] { Manifest(100, "Cue") },
                new[] { Module("a-owner", Event("Cue")), Module("z-owner", Event("Cue")) });

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(first, out _, out var firstErrors),
                Is.False);
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(second, out _, out var secondErrors),
                Is.False);
            Assert.That(
                firstErrors.Select(FormatError),
                Is.EqualTo(secondErrors.Select(FormatError)));
        }

        [Test]
        public void Fingerprints_AreLengthPrefixedTargetNeutralSha256()
        {
            var left = Input(
                new[] { Manifest(100, "C") },
                new[] { Module("a-b", Event("C", profileHint: "c")) });
            var right = Input(
                new[] { Manifest(100, "C") },
                new[] { Module("a", Event("C", profileHint: "b-c")) });

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(left, out var leftPlan, out _),
                Is.True);
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(right, out var rightPlan, out _),
                Is.True);
            Assert.That(leftPlan.InputFingerprint, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(leftPlan.OutputFingerprint, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(leftPlan.InputFingerprint, Is.Not.EqualTo(rightPlan.InputFingerprint));
        }

        [Test]
        public void TryCompile_AcceptsDigitLeadingContractSlugs()
        {
            var input = Input(
                new[] { Manifest(100, "Cue") },
                new[]
                {
                    Module(
                        "1-ui",
                        Event(
                            "Cue",
                            profileHint: "2d",
                            bankId: "3-bank"))
                });

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(
                    input,
                    out _,
                    out var errors),
                Is.True);
            Assert.That(errors, Is.Empty);
        }

        [Test]
        public void Fingerprints_UseClipIdentityRatherThanAdvisoryAssetPath()
        {
            var first = Input(
                new[] { Manifest(100, "Cue") },
                new[]
                {
                    Module(
                        "ui",
                        Event(
                            "Cue",
                            clips: new[] { Clip(ClipA, -42, "Assets/Audio/First.wav") }))
                });
            var second = Input(
                new[] { Manifest(100, "Cue") },
                new[]
                {
                    Module(
                        "ui",
                        Event(
                            "Cue",
                            clips: new[] { Clip(ClipA, -42, "Assets/Moved/Second.wav") }))
                });

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(first, out var firstPlan, out _),
                Is.True);
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(second, out var secondPlan, out _),
                Is.True);
            Assert.That(firstPlan.InputFingerprint, Is.EqualTo(secondPlan.InputFingerprint));
            Assert.That(firstPlan.OutputFingerprint, Is.EqualTo(secondPlan.OutputFingerprint));
        }

        [Test]
        public void OutputFingerprint_IncludesTypedKeyTextAndOwnershipRecords()
        {
            var first = Input(
                new[] { Manifest(100, "Cue") },
                new[] { Module("ui", Event("Cue")) },
                typedKeySourceText: "source-a");
            var second = Input(
                new[] { Manifest(100, "Cue") },
                new[] { Module("ui", Event("Cue")) },
                typedKeySourceText: "source-b");

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(first, out var firstPlan, out _),
                Is.True);
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(second, out var secondPlan, out _),
                Is.True);
            Assert.That(firstPlan.OutputFingerprint, Is.Not.EqualTo(secondPlan.OutputFingerprint));
        }

        [Test]
        public void TryCompile_WritesEnrollmentOnlyForExplicitAcceptanceTarget()
        {
            var ordinary = Input(
                new[] { Manifest(100, "Cue") },
                new[] { Module("ui", Event("Cue")) });
            var accepting = Input(
                new[] { Manifest(100, "Cue") },
                new[] { Module("ui", Event("Cue")) },
                writeEnrollmentIdentity: true,
                validationProfileAssetPath: "Assets/HostAudio/ValidationProfile.asset");

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(ordinary, out var ordinaryPlan, out _),
                Is.True);
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(accepting, out var acceptingPlan, out _),
                Is.True);
            Assert.That(
                Enumerable.Range(0, ordinaryPlan.WriteOperationCount)
                    .Select(ordinaryPlan.GetWriteOperation)
                    .Any(operation =>
                        operation.Kind == OrpheusAuthoringWriteKind.WriteEnrollmentIdentity),
                Is.False);
            var enrollment = Enumerable.Range(0, acceptingPlan.WriteOperationCount)
                .Select(acceptingPlan.GetWriteOperation)
                .Single(operation =>
                    operation.Kind == OrpheusAuthoringWriteKind.WriteEnrollmentIdentity);
            Assert.That(
                enrollment.AssetPath,
                Is.EqualTo("Assets/HostAudio/ValidationProfile.asset"));
        }

        [Test]
        public void TryCompile_CanonicalizesDuplicateManifestStatusTie()
        {
            var first = Input(
                new[]
                {
                    Manifest(100, "Cue", OrpheusAudioKeyStatus.Reserved),
                    Manifest(100, "Cue", OrpheusAudioKeyStatus.Active)
                },
                new[] { Module("ui", Event("Cue")) });
            var second = Input(
                new[]
                {
                    Manifest(100, "Cue", OrpheusAudioKeyStatus.Active),
                    Manifest(100, "Cue", OrpheusAudioKeyStatus.Reserved)
                },
                new[] { Module("ui", Event("Cue")) });

            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(first, out _, out var firstErrors),
                Is.False);
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(second, out _, out var secondErrors),
                Is.False);
            Assert.That(
                firstErrors.Select(FormatError),
                Is.EqualTo(secondErrors.Select(FormatError)));
        }

        [Test]
        public void AssemblyBoundary_IsUnityFreeAndInternal()
        {
            var assembly = typeof(OrpheusAuthoringCompilationPolicy).Assembly;
            var definition = System.IO.File.ReadAllText(
                "Packages/com.orpheus.audio/Editor/Core/Orpheus.Audio.Editor.Core.asmdef");

            Assert.That(assembly.GetExportedTypes(), Is.Empty);
            Assert.That(definition, Does.Contain("\"noEngineReferences\": true"));
            Assert.That(definition, Does.Contain("\"autoReferenced\": false"));
            Assert.That(definition, Does.Not.Contain("UnityEngine"));
            Assert.That(
                typeof(OrpheusAuthoringManifestEntryValue)
                    .GetProperty("Status", BindingFlags.Instance | BindingFlags.NonPublic)
                    .PropertyType,
                Is.EqualTo(typeof(OrpheusAudioKeyStatus)));
            Assert.That(typeof(OrpheusAuthoringClipValue).Assembly, Is.EqualTo(assembly));
        }

        private static object[] RejectionCases()
        {
            var validManifest = new[] { Manifest(100, "Cue") };
            var validRecipes = new[] { Module("ui", Event("Cue")) };
            return new object[]
            {
                Case("null input", null, OrpheusAuthoringErrorCode.Invalid),
                Case(
                    "missing manifest",
                    new OrpheusAuthoringCompilationInput(
                        1, null, validRecipes, "Assets/Audio/OrpheusGenerated"),
                    OrpheusAuthoringErrorCode.MissingManifest),
                Case(
                    "null recipe storage",
                    new OrpheusAuthoringCompilationInput(
                        1,
                        new OrpheusAuthoringManifestValue(1, validManifest),
                        null,
                        "Assets/Audio/OrpheusGenerated"),
                    OrpheusAuthoringErrorCode.NullRecipe),
                Case(
                    "null recipe",
                    Input(validManifest, new OrpheusAuthoringModuleRecipeValue[] { null }),
                    OrpheusAuthoringErrorCode.NullRecipe),
                Case(
                    "invalid module",
                    Input(validManifest, new[] { Module("Bad_Module", Event("Cue")) }),
                    OrpheusAuthoringErrorCode.InvalidModuleId),
                Case(
                    "duplicate module",
                    Input(
                        new[] { Manifest(100, "A"), Manifest(200, "B") },
                        new[] { Module("same", Event("A")), Module("same", Event("B")) }),
                    OrpheusAuthoringErrorCode.DuplicateModuleId),
                Case(
                    "missing symbol",
                    Input(validManifest, new[] { Module("ui", Event("Missing")) }),
                    OrpheusAuthoringErrorCode.MissingRecipeSymbol),
                Case(
                    "inactive symbol",
                    Input(
                        new[] { Manifest(100, "Cue", OrpheusAudioKeyStatus.Reserved) },
                        validRecipes),
                    OrpheusAuthoringErrorCode.InactiveRecipeSymbol),
                Case(
                    "duplicate owner",
                    Input(
                        validManifest,
                        new[] { Module("a", Event("Cue")), Module("b", Event("Cue")) }),
                    OrpheusAuthoringErrorCode.DuplicateRecipeOwner),
                Case(
                    "unowned active",
                    Input(validManifest, Array.Empty<OrpheusAuthoringModuleRecipeValue>()),
                    OrpheusAuthoringErrorCode.ActiveManifestIdentityUnowned),
                Case(
                    "invalid scalar",
                    Input(validManifest, new[] { Module("ui", Event("Cue", volumeMaximum: 2f)) }),
                    OrpheusAuthoringErrorCode.InvalidEventPolicy),
                Case(
                    "invalid clip",
                    Input(
                        validManifest,
                        new[] { Module("ui", Event("Cue", clips: new[] { Clip("ABC", 0, "") })) }),
                    OrpheusAuthoringErrorCode.InvalidClipIdentity),
                Case(
                    "duplicate clip",
                    Input(
                        validManifest,
                        new[]
                        {
                            Module(
                                "ui",
                                Event(
                                    "Cue",
                                    clips: new[]
                                    {
                                        Clip(ClipA, 1, "Assets/A.wav"),
                                        Clip(ClipA, 1, "Assets/A.wav")
                                    }))
                        }),
                    OrpheusAuthoringErrorCode.DuplicateClip),
                Case(
                    "duplicate manifest id",
                    Input(
                        new[] { Manifest(100, "A"), Manifest(100, "B") },
                        new[] { Module("a", Event("A")), Module("b", Event("B")) }),
                    OrpheusAuthoringErrorCode.DuplicateManifestId),
                Case(
                    "duplicate manifest symbol",
                    Input(
                        new[] { Manifest(100, "A"), Manifest(200, "A") },
                        new[] { Module("a", Event("A")) }),
                    OrpheusAuthoringErrorCode.DuplicateManifestSymbol),
                Case(
                    "invalid manifest status",
                    Input(
                        new[] { Manifest(100, "Cue", (OrpheusAudioKeyStatus)99) },
                        Array.Empty<OrpheusAuthoringModuleRecipeValue>()),
                    OrpheusAuthoringErrorCode.InvalidManifestStatus),
                Case(
                    "invalid root",
                    Input(validManifest, validRecipes, generatedRoot: "../Audio"),
                    OrpheusAuthoringErrorCode.InvalidGeneratedRoot),
                Case(
                    "case insensitive event collision",
                    Input(
                        new[] { Manifest(100, "Cue"), Manifest(200, "cue") },
                        new[] { Module("a", Event("Cue")), Module("b", Event("cue")) }),
                    OrpheusAuthoringErrorCode.OutputPathCollision),
                Case(
                    "generated root overlap",
                    Input(
                        validManifest,
                        validRecipes,
                        otherGeneratedRoots: new[] { "Assets/Audio" }),
                    OrpheusAuthoringErrorCode.GeneratedRootOverlap),
                Case(
                    "typed key conflict",
                    Input(validManifest, validRecipes, typedKeyOwnershipConflict: true),
                    OrpheusAuthoringErrorCode.TypedKeyOwnershipConflict),
                Case(
                    "foreign output",
                    Input(
                        validManifest,
                        validRecipes,
                        existingOutputs: new[]
                        {
                            new OrpheusAuthoringExistingOutputValue(
                                "Assets/Audio/OrpheusGenerated/Events/AE_Cue.asset",
                                false)
                        }),
                    OrpheusAuthoringErrorCode.OutputPathCollision)
            };
        }

        private static object[] Case(
            string name,
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringErrorCode expected)
        {
            return new object[] { name, input, (ushort)expected };
        }

        private static void AssertRejected(
            OrpheusAuthoringCompilationInput input,
            OrpheusAuthoringErrorCode expected,
            string message = null)
        {
            Assert.That(
                OrpheusAuthoringCompilationPolicy.TryCompile(input, out var plan, out var errors),
                Is.False,
                message);
            Assert.That(errors.Select(error => error.Code), Does.Contain(expected), message);
            Assert.That(plan.WriteOperationCount, Is.EqualTo(0), message);
        }

        private static string ErrorSortKey(OrpheusAuthoringCompilationError error)
        {
            return ((ushort)error.Code).ToString("D5") + "|" +
                   error.Key.ToString("D5") + "|" +
                   error.ModuleId + "|" +
                   error.Symbol + "|" +
                   error.AssetPath + "|" +
                   error.ClipIndex.ToString("D10") + "|" +
                   error.Detail;
        }

        private static string FormatError(OrpheusAuthoringCompilationError error)
        {
            return error.Code + "|" +
                   error.Key + "|" +
                   error.ModuleId + "|" +
                   error.Symbol + "|" +
                   error.AssetPath + "|" +
                   error.ClipIndex + "|" +
                   error.Detail;
        }

        private static OrpheusAuthoringCompilationInput Input(
            OrpheusAuthoringManifestEntryValue[] manifest,
            OrpheusAuthoringModuleRecipeValue[] recipes,
            string generatedRoot = "Assets/Audio/OrpheusGenerated",
            OrpheusAuthoringManifestEntryValue[] acceptedManifest = null,
            OrpheusAuthoringOwnedEventValue[] ownedEvents = null,
            OrpheusAuthoringExistingOutputValue[] existingOutputs = null,
            string[] otherGeneratedRoots = null,
            bool typedKeyOwnershipConflict = false,
            bool deleteTrackedOrphans = false,
            OrpheusAuthoringCatalogValue catalog = null,
            bool writeEnrollmentIdentity = false,
            string validationProfileAssetPath = "",
            string typedKeyAssemblyText = "assembly-v1",
            string typedKeySourceText = "source-v1")
        {
            if (catalog == null)
            {
                catalog = new OrpheusAuthoringCatalogValue(
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    "Assets/HostAudio/OrpheusAudioCatalog.asset",
                    0,
                    false);
            }

            if (existingOutputs == null && ownedEvents != null)
            {
                existingOutputs = ownedEvents
                    .Select(owned => new OrpheusAuthoringExistingOutputValue(
                        owned.AssetPath,
                        true,
                        owned.Guid,
                        "Orpheus.Audio.OrpheusAudioEvent",
                        true))
                    .ToArray();
            }

            return new OrpheusAuthoringCompilationInput(
                1,
                new OrpheusAuthoringManifestValue(1, manifest),
                recipes,
                generatedRoot,
                acceptedManifest,
                ownedEvents,
                existingOutputs,
                otherGeneratedRoots,
                typedKeyOwnershipConflict,
                deleteTrackedOrphans,
                catalog,
                writeEnrollmentIdentity,
                validationProfileAssetPath,
                typedKeyAssemblyText,
                typedKeySourceText);
        }

        private static OrpheusAuthoringManifestEntryValue Manifest(
            ushort key,
            string symbol,
            OrpheusAudioKeyStatus status = OrpheusAudioKeyStatus.Active)
        {
            return new OrpheusAuthoringManifestEntryValue(key, symbol, status);
        }

        private static OrpheusAuthoringModuleRecipeValue Module(
            string moduleId,
            params OrpheusAuthoringEventRecipeValue[] events)
        {
            return new OrpheusAuthoringModuleRecipeValue(1, moduleId, events);
        }

        private static OrpheusAuthoringEventRecipeValue Event(
            string symbol,
            OrpheusAuthoringClipValue[] clips = null,
            float volumeMaximum = 1f,
            string profileHint = "",
            string bankId = "")
        {
            return new OrpheusAuthoringEventRecipeValue(
                symbol,
                OrpheusPlaybackKind.OneShot2D,
                OrpheusCategory.SfxUi,
                OrpheusLoadPolicy.BootstrapTransient,
                clips ?? new[] { Clip(ClipA, 1, "Assets/Audio/Cue.wav") },
                1f,
                volumeMaximum,
                1f,
                1f,
                128,
                1,
                0f,
                0f,
                0f,
                OrpheusRolloffMode.Logarithmic,
                profileHint,
                bankId);
        }

        private static OrpheusAuthoringClipValue Clip(
            string guid,
            long localFileId,
            string path)
        {
            return new OrpheusAuthoringClipValue(guid, localFileId, path);
        }
    }
}
