using System.Collections.Generic;
using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioLoadingPolicyTests
    {
        private static IEnumerable<TestCaseData> AggregateCases
        {
            get
            {
                yield return new TestCaseData(
                        new[]
                        {
                            OrpheusClipLoadState.Loaded,
                            OrpheusClipLoadState.Loaded,
                            OrpheusClipLoadState.Loaded
                        },
                        OrpheusClipLoadState.Loaded)
                    .SetName("Aggregate_AllLoaded_IsLoaded");
                yield return new TestCaseData(
                        new[]
                        {
                            OrpheusClipLoadState.Unloaded,
                            OrpheusClipLoadState.Loaded
                        },
                        OrpheusClipLoadState.Unloaded)
                    .SetName("Aggregate_UnloadedAndLoaded_IsUnloaded");
                yield return new TestCaseData(
                        new[]
                        {
                            OrpheusClipLoadState.Loaded,
                            OrpheusClipLoadState.Loading,
                            OrpheusClipLoadState.Unloaded
                        },
                        OrpheusClipLoadState.Loading)
                    .SetName("Aggregate_AnyLoadingWithoutFailed_IsLoading");
                yield return new TestCaseData(
                        new[]
                        {
                            OrpheusClipLoadState.Loaded,
                            OrpheusClipLoadState.Loading,
                            OrpheusClipLoadState.Failed,
                            OrpheusClipLoadState.Unloaded
                        },
                        OrpheusClipLoadState.Failed)
                    .SetName("Aggregate_AnyFailed_IsFailed");
                yield return new TestCaseData(
                        new[]
                        {
                            OrpheusClipLoadState.Failed,
                            OrpheusClipLoadState.Unloaded,
                            OrpheusClipLoadState.Loading,
                            OrpheusClipLoadState.Loaded
                        },
                        OrpheusClipLoadState.Failed)
                    .SetName("Aggregate_FailedPrecedence_IsOrderIndependent");
            }
        }

        [TestCaseSource(nameof(AggregateCases))]
        public void ClipLoadAggregation_UsesFixedPrecedence(
            OrpheusClipLoadState[] states,
            OrpheusClipLoadState expected)
        {
            var aggregation = default(OrpheusClipLoadAggregation);

            for (var index = 0; index < states.Length; index++)
            {
                aggregation.Include(states[index]);
            }

            Assert.That(aggregation.Result, Is.EqualTo(expected));
        }

        [TestCase(
            OrpheusPlaybackKind.OneShot2D,
            OrpheusLoadPolicy.BootstrapTransient,
            OrpheusAudioImportLoadType.DecompressOnLoad,
            true,
            false)]
        [TestCase(
            OrpheusPlaybackKind.OneShot2D,
            OrpheusLoadPolicy.ExplicitTransient,
            OrpheusAudioImportLoadType.DecompressOnLoad,
            false,
            true)]
        [TestCase(
            OrpheusPlaybackKind.GlobalLoop2D,
            OrpheusLoadPolicy.PersistentStream,
            OrpheusAudioImportLoadType.Streaming,
            false,
            true)]
        public void ImportPolicy_ExactMatrixRowsHaveNoMismatch(
            object playbackKindValue,
            object loadPolicyValue,
            object loadTypeValue,
            bool preloadAudioData,
            bool loadInBackground)
        {
            var playbackKind = (OrpheusPlaybackKind)playbackKindValue;
            var loadPolicy = (OrpheusLoadPolicy)loadPolicyValue;
            var values = new OrpheusAudioImportPolicyValues(
                playbackKind,
                loadPolicy,
                (OrpheusAudioImportLoadType)loadTypeValue,
                preloadAudioData,
                loadInBackground,
                false);

            Assert.That(
                OrpheusAudioImportPolicyEvaluator.Evaluate(values),
                Is.EqualTo(OrpheusAudioImportPolicyMismatch.None));
        }

        [Test]
        public void ImportPolicy_ReportsEveryScalarMismatchWithoutShortCircuiting()
        {
            var values = new OrpheusAudioImportPolicyValues(
                OrpheusPlaybackKind.OneShot3D,
                OrpheusLoadPolicy.BootstrapTransient,
                OrpheusAudioImportLoadType.Streaming,
                false,
                true,
                false);

            var expected = OrpheusAudioImportPolicyMismatch.LoadType |
                           OrpheusAudioImportPolicyMismatch.PreloadAudioData |
                           OrpheusAudioImportPolicyMismatch.LoadInBackground |
                           OrpheusAudioImportPolicyMismatch.ForceToMono;

            Assert.That(OrpheusAudioImportPolicyEvaluator.Evaluate(values), Is.EqualTo(expected));
        }

        [Test]
        public void ImportPolicy_ReportsEachMatrixFieldIndependently()
        {
            AssertMismatch(
                OrpheusAudioImportLoadType.Streaming,
                false,
                true,
                OrpheusAudioImportPolicyMismatch.LoadType);
            AssertMismatch(
                OrpheusAudioImportLoadType.CompressedInMemory,
                false,
                true,
                OrpheusAudioImportPolicyMismatch.LoadType);
            AssertMismatch(
                OrpheusAudioImportLoadType.Invalid,
                false,
                true,
                OrpheusAudioImportPolicyMismatch.LoadType);
            AssertMismatch(
                OrpheusAudioImportLoadType.DecompressOnLoad,
                true,
                true,
                OrpheusAudioImportPolicyMismatch.PreloadAudioData);
            AssertMismatch(
                OrpheusAudioImportLoadType.DecompressOnLoad,
                false,
                false,
                OrpheusAudioImportPolicyMismatch.LoadInBackground);
        }

        [Test]
        public void ImportPolicy_InvalidLoadPolicyHasOneDeterministicMismatch()
        {
            var values = new OrpheusAudioImportPolicyValues(
                OrpheusPlaybackKind.OneShot2D,
                OrpheusLoadPolicy.Invalid,
                OrpheusAudioImportLoadType.DecompressOnLoad,
                false,
                false,
                false);

            Assert.That(
                OrpheusAudioImportPolicyEvaluator.Evaluate(values),
                Is.EqualTo(OrpheusAudioImportPolicyMismatch.LoadPolicy));
        }

        [Test]
        public void ImportPolicy_InvalidLoadPolicyStillReportsIndependentForceToMonoMismatch()
        {
            var values = new OrpheusAudioImportPolicyValues(
                OrpheusPlaybackKind.OneShot3D,
                OrpheusLoadPolicy.Invalid,
                OrpheusAudioImportLoadType.DecompressOnLoad,
                false,
                false,
                false);

            var expected = OrpheusAudioImportPolicyMismatch.LoadPolicy |
                           OrpheusAudioImportPolicyMismatch.ForceToMono;

            Assert.That(OrpheusAudioImportPolicyEvaluator.Evaluate(values), Is.EqualTo(expected));
        }

        [Test]
        public void ImportPolicy_RequiresForceToMonoOnlyForOneShot3D()
        {
            var missingForceToMono = new OrpheusAudioImportPolicyValues(
                OrpheusPlaybackKind.OneShot3D,
                OrpheusLoadPolicy.ExplicitTransient,
                OrpheusAudioImportLoadType.DecompressOnLoad,
                false,
                true,
                false);
            var valid3D = new OrpheusAudioImportPolicyValues(
                OrpheusPlaybackKind.OneShot3D,
                OrpheusLoadPolicy.ExplicitTransient,
                OrpheusAudioImportLoadType.DecompressOnLoad,
                false,
                true,
                true);
            var valid2DWithoutForceToMono = new OrpheusAudioImportPolicyValues(
                OrpheusPlaybackKind.OneShot2D,
                OrpheusLoadPolicy.ExplicitTransient,
                OrpheusAudioImportLoadType.DecompressOnLoad,
                false,
                true,
                false);
            var valid2DWithForceToMono = new OrpheusAudioImportPolicyValues(
                OrpheusPlaybackKind.OneShot2D,
                OrpheusLoadPolicy.ExplicitTransient,
                OrpheusAudioImportLoadType.DecompressOnLoad,
                false,
                true,
                true);

            Assert.That(
                OrpheusAudioImportPolicyEvaluator.Evaluate(missingForceToMono),
                Is.EqualTo(OrpheusAudioImportPolicyMismatch.ForceToMono));
            Assert.That(
                OrpheusAudioImportPolicyEvaluator.Evaluate(valid3D),
                Is.EqualTo(OrpheusAudioImportPolicyMismatch.None));
            Assert.That(
                OrpheusAudioImportPolicyEvaluator.Evaluate(valid2DWithoutForceToMono),
                Is.EqualTo(OrpheusAudioImportPolicyMismatch.None));
            Assert.That(
                OrpheusAudioImportPolicyEvaluator.Evaluate(valid2DWithForceToMono),
                Is.EqualTo(OrpheusAudioImportPolicyMismatch.None));
        }

        [Test]
        public void LoadStallTracker_DoesNotObserveWithoutAnIntentGeneration()
        {
            var tracker = default(OrpheusLoadStallTracker);

            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 0d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 10d), Is.False);
            Assert.That(tracker.Generation, Is.Zero);
            Assert.That(tracker.IsTracking, Is.False);
        }

        [Test]
        public void LoadStallTracker_UsesStrictThresholdAndReportsOncePerGeneration()
        {
            var tracker = default(OrpheusLoadStallTracker);
            Assert.That(tracker.Generation, Is.Zero);
            Assert.That(tracker.IsTracking, Is.False);

            tracker.BeginGeneration(10d);

            Assert.That(tracker.Generation, Is.EqualTo(1u));
            Assert.That(tracker.IsTracking, Is.True);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 10d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 12d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 12.000001d), Is.True);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 20d), Is.False);
        }

        [Test]
        public void LoadStallTracker_UnloadedResetsIntervalWithoutEndingGeneration()
        {
            var tracker = default(OrpheusLoadStallTracker);
            tracker.BeginGeneration(1d);

            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 1d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 2.9d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Unloaded, 3d), Is.False);
            Assert.That(tracker.IsTracking, Is.True);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 100d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 102d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 102.000001d), Is.True);
        }

        [TestCase(OrpheusClipLoadState.Loaded)]
        [TestCase(OrpheusClipLoadState.Failed)]
        public void LoadStallTracker_TerminalStateEndsGenerationObservation(
            OrpheusClipLoadState terminalState)
        {
            var tracker = default(OrpheusLoadStallTracker);
            tracker.BeginGeneration(1d);

            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 1d), Is.False);
            Assert.That(tracker.Observe(terminalState, 2d), Is.False);
            Assert.That(tracker.Generation, Is.EqualTo(1u));
            Assert.That(tracker.IsTracking, Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 100d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 200d), Is.False);
        }

        [Test]
        public void LoadStallTracker_InvalidBackendStateEndsGenerationObservation()
        {
            var tracker = default(OrpheusLoadStallTracker);
            tracker.BeginGeneration(1d);

            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 1d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Invalid, 2d), Is.False);
            Assert.That(tracker.Generation, Is.EqualTo(1u));
            Assert.That(tracker.IsTracking, Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 100d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 200d), Is.False);
        }

        [Test]
        public void LoadStallTracker_SameGenerationCannotReportAfterReenteringLoading()
        {
            var tracker = default(OrpheusLoadStallTracker);
            tracker.BeginGeneration(0d);

            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 0d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 2.1d), Is.True);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Unloaded, 3d), Is.False);
            Assert.That(tracker.IsTracking, Is.True);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 4d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 10d), Is.False);
        }

        [Test]
        public void LoadStallTracker_NewGenerationCanReportAgain()
        {
            var tracker = default(OrpheusLoadStallTracker);
            tracker.BeginGeneration(1d);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 1d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 3.1d), Is.True);

            tracker.BeginGeneration(50d);

            Assert.That(tracker.Generation, Is.EqualTo(2u));
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 50d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 52d), Is.False);
            Assert.That(tracker.Observe(OrpheusClipLoadState.Loading, 52.1d), Is.True);
        }

        private static void AssertMismatch(
            OrpheusAudioImportLoadType loadType,
            bool preloadAudioData,
            bool loadInBackground,
            OrpheusAudioImportPolicyMismatch expected)
        {
            var values = new OrpheusAudioImportPolicyValues(
                OrpheusPlaybackKind.OneShot2D,
                OrpheusLoadPolicy.ExplicitTransient,
                loadType,
                preloadAudioData,
                loadInBackground,
                false);

            Assert.That(OrpheusAudioImportPolicyEvaluator.Evaluate(values), Is.EqualTo(expected));
        }
    }
}
