using System;

namespace Orpheus.Audio.Core
{
    internal enum OrpheusAudioImportLoadType : byte
    {
        Invalid = 0,
        DecompressOnLoad = 1,
        CompressedInMemory = 2,
        Streaming = 3
    }

    [Flags]
    internal enum OrpheusAudioImportPolicyMismatch : byte
    {
        None = 0,
        LoadPolicy = 1 << 0,
        LoadType = 1 << 1,
        PreloadAudioData = 1 << 2,
        LoadInBackground = 1 << 3,
        ForceToMono = 1 << 4
    }

    internal readonly struct OrpheusAudioImportPolicyValues
    {
        internal OrpheusAudioImportPolicyValues(
            OrpheusPlaybackKind playbackKind,
            OrpheusLoadPolicy loadPolicy,
            OrpheusAudioImportLoadType loadType,
            bool preloadAudioData,
            bool loadInBackground,
            bool forceToMono)
        {
            PlaybackKind = playbackKind;
            LoadPolicy = loadPolicy;
            LoadType = loadType;
            PreloadAudioData = preloadAudioData;
            LoadInBackground = loadInBackground;
            ForceToMono = forceToMono;
        }

        internal OrpheusPlaybackKind PlaybackKind { get; }
        internal OrpheusLoadPolicy LoadPolicy { get; }
        internal OrpheusAudioImportLoadType LoadType { get; }
        internal bool PreloadAudioData { get; }
        internal bool LoadInBackground { get; }
        internal bool ForceToMono { get; }
    }

    internal static class OrpheusAudioImportPolicyEvaluator
    {
        internal static OrpheusAudioImportPolicyMismatch Evaluate(
            OrpheusAudioImportPolicyValues values)
        {
            var expectedLoadType = OrpheusAudioImportLoadType.Invalid;
            var expectedPreload = false;
            var expectedBackground = false;
            var hasPolicyMatrix = true;
            var mismatch = OrpheusAudioImportPolicyMismatch.None;

            switch (values.LoadPolicy)
            {
                case OrpheusLoadPolicy.BootstrapTransient:
                    expectedLoadType = OrpheusAudioImportLoadType.DecompressOnLoad;
                    expectedPreload = true;
                    expectedBackground = false;
                    break;
                case OrpheusLoadPolicy.ExplicitTransient:
                    expectedLoadType = OrpheusAudioImportLoadType.DecompressOnLoad;
                    expectedPreload = false;
                    expectedBackground = true;
                    break;
                case OrpheusLoadPolicy.PersistentStream:
                    expectedLoadType = OrpheusAudioImportLoadType.Streaming;
                    expectedPreload = false;
                    expectedBackground = true;
                    break;
                default:
                    mismatch |= OrpheusAudioImportPolicyMismatch.LoadPolicy;
                    hasPolicyMatrix = false;
                    break;
            }

            if (hasPolicyMatrix && values.LoadType != expectedLoadType)
            {
                mismatch |= OrpheusAudioImportPolicyMismatch.LoadType;
            }

            if (hasPolicyMatrix && values.PreloadAudioData != expectedPreload)
            {
                mismatch |= OrpheusAudioImportPolicyMismatch.PreloadAudioData;
            }

            if (hasPolicyMatrix && values.LoadInBackground != expectedBackground)
            {
                mismatch |= OrpheusAudioImportPolicyMismatch.LoadInBackground;
            }

            if (values.PlaybackKind == OrpheusPlaybackKind.OneShot3D && !values.ForceToMono)
            {
                mismatch |= OrpheusAudioImportPolicyMismatch.ForceToMono;
            }

            return mismatch;
        }
    }

    internal struct OrpheusClipLoadAggregation
    {
        private int _count;
        private int _loadedCount;
        private bool _hasLoading;
        private bool _hasFailed;
        private bool _hasInvalid;

        internal void Include(OrpheusClipLoadState state)
        {
            _count++;
            switch (state)
            {
                case OrpheusClipLoadState.Unloaded:
                    break;
                case OrpheusClipLoadState.Loading:
                    _hasLoading = true;
                    break;
                case OrpheusClipLoadState.Loaded:
                    _loadedCount++;
                    break;
                case OrpheusClipLoadState.Failed:
                    _hasFailed = true;
                    break;
                default:
                    _hasInvalid = true;
                    break;
            }
        }

        internal OrpheusClipLoadState Result
        {
            get
            {
                if (_count == 0 || _hasInvalid)
                {
                    return OrpheusClipLoadState.Invalid;
                }

                if (_hasFailed)
                {
                    return OrpheusClipLoadState.Failed;
                }

                if (_loadedCount == _count)
                {
                    return OrpheusClipLoadState.Loaded;
                }

                return _hasLoading
                    ? OrpheusClipLoadState.Loading
                    : OrpheusClipLoadState.Unloaded;
            }
        }
    }

    internal struct OrpheusLoadStallTracker
    {
        private const double StallThresholdSeconds = 2d;

        private uint _generation;
        private double _loadingSinceRealtime;
        private bool _isTracking;
        private bool _loadingObserved;
        private bool _stalledReported;

        internal uint Generation => _generation;
        internal bool IsTracking => _isTracking;

        internal void BeginGeneration(double realtime)
        {
            _generation = unchecked(_generation + 1u);
            if (_generation == 0)
            {
                _generation = 1;
            }

            _loadingSinceRealtime = realtime;
            _isTracking = true;
            _loadingObserved = true;
            _stalledReported = false;
        }

        internal bool Observe(OrpheusClipLoadState state, double realtime)
        {
            if (!_isTracking)
            {
                return false;
            }

            if (state == OrpheusClipLoadState.Loaded ||
                state == OrpheusClipLoadState.Failed ||
                state == OrpheusClipLoadState.Invalid)
            {
                _isTracking = false;
                _loadingObserved = false;
                return false;
            }

            if (state != OrpheusClipLoadState.Loading)
            {
                _loadingObserved = false;
                return false;
            }

            if (!_loadingObserved)
            {
                _loadingSinceRealtime = realtime;
                _loadingObserved = true;
                return false;
            }

            if (_stalledReported || realtime - _loadingSinceRealtime <= StallThresholdSeconds)
            {
                return false;
            }

            _stalledReported = true;
            return true;
        }
    }
}
