using UnityEngine;

namespace Orpheus.Audio
{
    internal static class OrpheusAudioSourceNormalizer
    {
        internal static void Normalize(AudioSource source)
        {
            source.Stop();
            if (source.clip != null)
            {
                source.time = 0f;
            }

            source.clip = null;
            source.playOnAwake = false;
            source.loop = false;
            source.mute = false;
            source.volume = 0f;
            source.pitch = 1f;
            source.priority = 128;
            source.outputAudioMixerGroup = null;
            source.spatialBlend = 0f;
            source.panStereo = 0f;
            source.spread = 0f;
            source.dopplerLevel = 0f;
            source.reverbZoneMix = 1f;
            source.minDistance = 1f;
            source.maxDistance = 500f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.bypassEffects = false;
            source.bypassListenerEffects = false;
            source.bypassReverbZones = false;
            source.ignoreListenerPause = false;
            source.ignoreListenerVolume = false;
            source.spatialize = false;
            source.spatializePostEffects = false;
            source.velocityUpdateMode = AudioVelocityUpdateMode.Fixed;
        }

        internal static void NormalizeAll(AudioSource[] sources)
        {
            for (var sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
            {
                Normalize(sources[sourceIndex]);
            }
        }
    }
}
