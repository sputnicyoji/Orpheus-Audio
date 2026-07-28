using UnityEngine;
using Orpheus.Audio.Core;

namespace Orpheus.Audio
{
    public sealed class OrpheusAudioSourceBank : MonoBehaviour
    {
        internal const int Transient3DCount = 12;
        internal const int Transient2DCount = 4;
        internal const int BgmCount = 2;
        internal const int ProfileAmbienceCount = 2;
        internal const int GlobalLoopCount = 4;
        internal const int TotalSourceCount =
            Transient3DCount + Transient2DCount + BgmCount + ProfileAmbienceCount + GlobalLoopCount;

        private const int Transient3DOffset = 0;
        internal const int Transient2DOffset = Transient3DOffset + Transient3DCount;
        internal const int BgmOffset = Transient2DOffset + Transient2DCount;
        internal const int ProfileAmbienceOffset = BgmOffset + BgmCount;
        internal const int GlobalLoopOffset = ProfileAmbienceOffset + ProfileAmbienceCount;

        [SerializeField] private AudioSource[] _oneShot3D = new AudioSource[Transient3DCount];
        [SerializeField] private AudioSource[] _oneShot2D = new AudioSource[Transient2DCount];
        [SerializeField] private AudioSource[] _bgm = new AudioSource[BgmCount];
        [SerializeField] private AudioSource[] _profileAmbience = new AudioSource[ProfileAmbienceCount];
        [SerializeField] private AudioSource[] _globalLoop = new AudioSource[GlobalLoopCount];

        internal bool TryCapture(out AudioSource[] sources)
        {
            sources = null;
            if (!HasExactShape())
            {
                return false;
            }

            var captured = new AudioSource[TotalSourceCount];
            var offset = 0;
            Copy(_oneShot3D, captured, ref offset);
            Copy(_oneShot2D, captured, ref offset);
            Copy(_bgm, captured, ref offset);
            Copy(_profileAmbience, captured, ref offset);
            Copy(_globalLoop, captured, ref offset);

            for (var sourceIndex = 0; sourceIndex < captured.Length; sourceIndex++)
            {
                var source = captured[sourceIndex];
                if (source == null || !HasExpectedName(sourceIndex, source.name))
                {
                    return false;
                }

                for (var previousIndex = 0; previousIndex < sourceIndex; previousIndex++)
                {
                    if (ReferenceEquals(captured[previousIndex], source))
                    {
                        return false;
                    }
                }
            }

            sources = captured;
            return true;
        }

        internal OrpheusAudioDisableReason ValidateCapturedReferences(AudioSource[] expected)
        {
            if (expected == null || expected.Length != TotalSourceCount || !HasExactShape())
            {
                return OrpheusAudioDisableReason.OwnedSourceReferenceChanged;
            }

            for (var sourceIndex = 0; sourceIndex < TotalSourceCount; sourceIndex++)
            {
                if (expected[sourceIndex] == null)
                {
                    return OrpheusAudioDisableReason.OwnedSourceDestroyed;
                }
            }

            for (var sourceIndex = 0; sourceIndex < TotalSourceCount; sourceIndex++)
            {
                var current = GetSource(sourceIndex);
                if (ReferenceEquals(current, null))
                {
                    continue;
                }

                for (var previousIndex = 0; previousIndex < sourceIndex; previousIndex++)
                {
                    if (ReferenceEquals(current, GetSource(previousIndex)))
                    {
                        return OrpheusAudioDisableReason.SourceBankDuplicateReference;
                    }
                }
            }

            for (var sourceIndex = 0; sourceIndex < TotalSourceCount; sourceIndex++)
            {
                if (!ReferenceEquals(GetSource(sourceIndex), expected[sourceIndex]))
                {
                    return OrpheusAudioDisableReason.OwnedSourceReferenceChanged;
                }
            }

            return OrpheusAudioDisableReason.None;
        }

        private bool HasExactShape()
        {
            return _oneShot3D != null && _oneShot3D.Length == Transient3DCount &&
                   _oneShot2D != null && _oneShot2D.Length == Transient2DCount &&
                   _bgm != null && _bgm.Length == BgmCount &&
                   _profileAmbience != null && _profileAmbience.Length == ProfileAmbienceCount &&
                   _globalLoop != null && _globalLoop.Length == GlobalLoopCount;
        }

        private static void Copy(AudioSource[] source, AudioSource[] destination, ref int offset)
        {
            for (var index = 0; index < source.Length; index++)
            {
                destination[offset++] = source[index];
            }
        }

        private AudioSource GetSource(int sourceIndex)
        {
            if (sourceIndex < Transient2DOffset)
            {
                return _oneShot3D[sourceIndex - Transient3DOffset];
            }

            if (sourceIndex < BgmOffset)
            {
                return _oneShot2D[sourceIndex - Transient2DOffset];
            }

            if (sourceIndex < ProfileAmbienceOffset)
            {
                return _bgm[sourceIndex - BgmOffset];
            }

            if (sourceIndex < GlobalLoopOffset)
            {
                return _profileAmbience[sourceIndex - ProfileAmbienceOffset];
            }

            return _globalLoop[sourceIndex - GlobalLoopOffset];
        }

        private static bool HasExpectedName(int sourceIndex, string actualName)
        {
            if (sourceIndex < Transient2DOffset)
            {
                return actualName == "OneShot3D_" + (sourceIndex - Transient3DOffset).ToString("00");
            }

            if (sourceIndex < BgmOffset)
            {
                return actualName == "OneShot2D_" + (sourceIndex - Transient2DOffset).ToString("00");
            }

            if (sourceIndex < ProfileAmbienceOffset)
            {
                return actualName == "Bgm_" + (sourceIndex - BgmOffset).ToString("00");
            }

            if (sourceIndex < GlobalLoopOffset)
            {
                return actualName == "ProfileAmbience_" +
                       (sourceIndex - ProfileAmbienceOffset).ToString("00");
            }

            return actualName == "GlobalLoop_" + (sourceIndex - GlobalLoopOffset).ToString("00");
        }
    }
}
