using Orpheus.Audio.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace Orpheus.Audio
{
    internal sealed class OrpheusAudioCarrierException : System.Exception
    {
        internal OrpheusAudioCarrierException(OrpheusAudioDisableReason disableReason)
        {
            DisableReason = disableReason;
        }

        internal OrpheusAudioDisableReason DisableReason { get; }
    }

    internal interface IOrpheusAudioMixerPort
    {
        object LeaseIdentity { get; }
        bool SetFloat(string parameterName, float decibels);
        void TransitionTo(OrpheusEffectiveSnapshot snapshot, float transitionSeconds);
    }

    internal readonly struct OrpheusAudioSystemConfiguration
    {
        internal OrpheusAudioSystemConfiguration(
            int dspBufferSize,
            int sampleRate,
            int numRealVoices,
            int numVirtualVoices)
        {
            DspBufferSize = dspBufferSize;
            SampleRate = sampleRate;
            NumRealVoices = numRealVoices;
            NumVirtualVoices = numVirtualVoices;
        }

        internal int DspBufferSize { get; }
        internal int SampleRate { get; }
        internal int NumRealVoices { get; }
        internal int NumVirtualVoices { get; }
    }

    internal interface IOrpheusAudioSystemPort
    {
        OrpheusAudioSystemConfiguration GetConfiguration();
        bool ResetCurrentConfiguration();
    }

    internal sealed class OrpheusUnityClipReadiness : IOrpheusAudioClipReadiness
    {
        private readonly OrpheusRuntimeCatalogSnapshot _snapshot;

        internal OrpheusUnityClipReadiness(OrpheusRuntimeCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public OrpheusClipLoadState GetLoadState(int flattenedClipIndex)
        {
            var state = _snapshot.GetClip(flattenedClipIndex).loadState;
            switch (state)
            {
                case AudioDataLoadState.Unloaded:
                    return OrpheusClipLoadState.Unloaded;
                case AudioDataLoadState.Loading:
                    return OrpheusClipLoadState.Loading;
                case AudioDataLoadState.Loaded:
                    return OrpheusClipLoadState.Loaded;
                case AudioDataLoadState.Failed:
                    return OrpheusClipLoadState.Failed;
                default:
                    return OrpheusClipLoadState.Invalid;
            }
        }

        public bool RequestLoad(int flattenedClipIndex)
        {
            return _snapshot.GetClip(flattenedClipIndex).LoadAudioData();
        }
    }

    internal sealed class OrpheusUnityMixerPort : IOrpheusAudioMixerPort
    {
        private readonly AudioMixer _mixer;
        private readonly OrpheusAudioSnapshotSet _snapshots;

        internal OrpheusUnityMixerPort(OrpheusAudioSettings settings)
        {
            _mixer = settings.Mixer;
            _snapshots = settings.CaptureSnapshots();
        }

        public object LeaseIdentity
        {
            get
            {
                ValidateMixerReference();
                return _mixer;
            }
        }

        public bool SetFloat(string parameterName, float decibels)
        {
            ValidateMixerReference();
            return _mixer.SetFloat(parameterName, decibels);
        }

        public void TransitionTo(OrpheusEffectiveSnapshot snapshot, float transitionSeconds)
        {
            ValidateMixerReference();
            var target = _snapshots.Get(snapshot);
            if (target == null)
            {
                throw new OrpheusAudioCarrierException(
                    OrpheusAudioDisableReason.SnapshotReferenceInvalid);
            }

            target.TransitionTo(transitionSeconds);
        }

        private void ValidateMixerReference()
        {
            if (_mixer == null)
            {
                throw new OrpheusAudioCarrierException(
                    OrpheusAudioDisableReason.MixerReferenceInvalid);
            }
        }

        internal void ValidateSnapshotReferences()
        {
            if (!_snapshots.HasAllSnapshotReferences ||
                !_snapshots.HasValidMixerIdentity(_mixer))
            {
                throw new OrpheusAudioCarrierException(
                    OrpheusAudioDisableReason.SnapshotReferenceInvalid);
            }
        }
    }

    internal sealed class OrpheusUnityAudioSystemPort : IOrpheusAudioSystemPort
    {
        public OrpheusAudioSystemConfiguration GetConfiguration()
        {
            var configuration = AudioSettings.GetConfiguration();
            return new OrpheusAudioSystemConfiguration(
                configuration.dspBufferSize,
                configuration.sampleRate,
                configuration.numRealVoices,
                configuration.numVirtualVoices);
        }

        public bool ResetCurrentConfiguration()
        {
            return AudioSettings.Reset(AudioSettings.GetConfiguration());
        }
    }
}
