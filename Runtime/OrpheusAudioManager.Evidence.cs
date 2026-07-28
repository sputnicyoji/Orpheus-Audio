#if ORPHEUS_ANDROID_EVIDENCE || UNITY_INCLUDE_TESTS
using Orpheus.Audio.Core;

namespace Orpheus.Audio
{
    internal readonly struct OrpheusAudioManagerEvidenceState
    {
        internal OrpheusAudioManagerEvidenceState(
            uint recoveryGeneration,
            byte requestedGlobalLoopMask,
            OrpheusAudioKey globalLoop0Key,
            OrpheusAudioKey globalLoop1Key,
            OrpheusAudioKey globalLoop2Key,
            OrpheusAudioKey globalLoop3Key)
        {
            RecoveryGeneration = recoveryGeneration;
            RequestedGlobalLoopMask = requestedGlobalLoopMask;
            GlobalLoop0Key = globalLoop0Key;
            GlobalLoop1Key = globalLoop1Key;
            GlobalLoop2Key = globalLoop2Key;
            GlobalLoop3Key = globalLoop3Key;
        }

        internal uint RecoveryGeneration { get; }
        internal byte RequestedGlobalLoopMask { get; }
        internal OrpheusAudioKey GlobalLoop0Key { get; }
        internal OrpheusAudioKey GlobalLoop1Key { get; }
        internal OrpheusAudioKey GlobalLoop2Key { get; }
        internal OrpheusAudioKey GlobalLoop3Key { get; }
    }

    public sealed partial class OrpheusAudioManager
    {
        // Cold/evidence observation only. Production Play/Tick paths must not
        // call into this surface.
        internal bool TryGetEvidenceState(
            out OrpheusAudioManagerEvidenceState evidenceState)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                RejectWrongThread();
                evidenceState = default;
                return false;
            }

            var requestedMask = (byte)0;
            var loop0 = GetRequestedGlobalLoopEvidence(0, ref requestedMask);
            var loop1 = GetRequestedGlobalLoopEvidence(1, ref requestedMask);
            var loop2 = GetRequestedGlobalLoopEvidence(2, ref requestedMask);
            var loop3 = GetRequestedGlobalLoopEvidence(3, ref requestedMask);
            evidenceState = new OrpheusAudioManagerEvidenceState(
                _recoveryGeneration,
                requestedMask,
                loop0,
                loop1,
                loop2,
                loop3);
            return true;
        }

        private OrpheusAudioKey GetRequestedGlobalLoopEvidence(
            int slotIndex,
            ref byte requestedMask)
        {
            var loop = _globalLoops[slotIndex];
            if (!loop.RequestedActive)
            {
                return OrpheusAudioKey.Invalid;
            }

            requestedMask |= (byte)(1 << slotIndex);
            return loop.Key;
        }
    }
}
#endif
