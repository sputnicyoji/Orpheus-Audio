using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Orpheus.Audio
{
    internal enum OrpheusAudioLeaseConflict : byte
    {
        None = 0,
        RuntimeHost = 1,
        SourceBank = 2,
        Mixer = 3
    }

    internal static class OrpheusMainThread
    {
        private static int _mainThreadId;

        internal static void Initialize()
        {
            Volatile.Write(ref _mainThreadId, Thread.CurrentThread.ManagedThreadId);
        }

        internal static bool IsCurrent
        {
            get
            {
                var mainThreadId = Volatile.Read(ref _mainThreadId);
                return mainThreadId != 0 && mainThreadId == Thread.CurrentThread.ManagedThreadId;
            }
        }
    }

    internal static class OrpheusAudioLeaseRegistry
    {
        private static readonly object Gate = new object();
        private static readonly List<OrpheusAudioLeaseWitness> Leases =
            new List<OrpheusAudioLeaseWitness>();

        internal static bool IsRuntimeHostReserved(OrpheusAudioRuntimeHost runtimeHost)
        {
            lock (Gate)
            {
                for (var index = 0; index < Leases.Count; index++)
                {
                    if (Leases[index].HostHeld && ReferenceEquals(Leases[index].RuntimeHost, runtimeHost))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal static OrpheusAudioLeaseConflict PreflightMixer(
            OrpheusAudioRuntimeHost runtimeHost,
            object mixerIdentity)
        {
            lock (Gate)
            {
                for (var index = 0; index < Leases.Count; index++)
                {
                    var lease = Leases[index];
                    if (lease.HostHeld && ReferenceEquals(lease.RuntimeHost, runtimeHost))
                    {
                        return OrpheusAudioLeaseConflict.RuntimeHost;
                    }

                    if (lease.MixerHeld && ReferenceEquals(lease.MixerIdentity, mixerIdentity))
                    {
                        return OrpheusAudioLeaseConflict.Mixer;
                    }
                }

                return OrpheusAudioLeaseConflict.None;
            }
        }

        internal static OrpheusAudioLeaseConflict PreflightSources(
            OrpheusAudioSourceBank sourceBank,
            AudioSource[] sources)
        {
            lock (Gate)
            {
                for (var index = 0; index < Leases.Count; index++)
                {
                    var lease = Leases[index];
                    if (!lease.SourceHeld)
                    {
                        continue;
                    }

                    if (ReferenceEquals(lease.SourceBank, sourceBank) || HasOverlap(lease.Sources, sources))
                    {
                        return OrpheusAudioLeaseConflict.SourceBank;
                    }
                }

                return OrpheusAudioLeaseConflict.None;
            }
        }

        internal static bool TryAcquire(
            OrpheusAudioRuntimeHost runtimeHost,
            OrpheusAudioSourceBank sourceBank,
            AudioSource[] sources,
            object mixerIdentity,
            OrpheusAudioManager owner,
            out OrpheusAudioLeaseConflict conflict)
        {
            conflict = PreflightMixer(runtimeHost, mixerIdentity);
            if (conflict != OrpheusAudioLeaseConflict.None)
            {
                return false;
            }

            conflict = PreflightSources(sourceBank, sources);
            if (conflict != OrpheusAudioLeaseConflict.None)
            {
                return false;
            }

            lock (Gate)
            {
                // All production callers are main-thread-only. Recheck under one lock keeps test misuse atomic.
                for (var index = 0; index < Leases.Count; index++)
                {
                    var lease = Leases[index];
                    if (lease.HostHeld && ReferenceEquals(lease.RuntimeHost, runtimeHost))
                    {
                        conflict = OrpheusAudioLeaseConflict.RuntimeHost;
                        return false;
                    }

                    if (lease.MixerHeld && ReferenceEquals(lease.MixerIdentity, mixerIdentity))
                    {
                        conflict = OrpheusAudioLeaseConflict.Mixer;
                        return false;
                    }

                    if (lease.SourceHeld &&
                        (ReferenceEquals(lease.SourceBank, sourceBank) || HasOverlap(lease.Sources, sources)))
                    {
                        conflict = OrpheusAudioLeaseConflict.SourceBank;
                        return false;
                    }
                }

                Leases.Add(new OrpheusAudioLeaseWitness(
                    runtimeHost,
                    sourceBank,
                    sources,
                    mixerIdentity,
                    owner));
                return true;
            }
        }

        internal static OrpheusAudioLeaseWitness GetLeaseWitness(OrpheusAudioManager owner)
        {
            lock (Gate)
            {
                return Find(owner);
            }
        }

        internal static void ReleaseSourceBank(OrpheusAudioManager owner)
        {
            lock (Gate)
            {
                var lease = Find(owner);
                if (lease == null)
                {
                    return;
                }

                lease.SourceHeld = false;
                lease.SourceBank = null;
                lease.Sources = null;
                RemoveIfReleased(lease);
            }
        }

        internal static void ReleaseMixerAndHost(OrpheusAudioManager owner)
        {
            lock (Gate)
            {
                var lease = Find(owner);
                if (lease == null)
                {
                    return;
                }

                lease.MixerHeld = false;
                lease.MixerIdentity = null;
                lease.HostHeld = false;
                lease.RuntimeHost = null;
                RemoveIfReleased(lease);
            }
        }

        internal static bool IsSourceBankHeldBy(OrpheusAudioManager owner)
        {
            lock (Gate)
            {
                var lease = Find(owner);
                return lease != null && lease.SourceHeld;
            }
        }

        internal static bool IsMixerHeldBy(OrpheusAudioManager owner)
        {
            lock (Gate)
            {
                var lease = Find(owner);
                return lease != null && lease.MixerHeld;
            }
        }

        // Domain-reload / test isolation only. Callers: OrpheusAudioRuntimeStatics.Reset
        // (SubsystemRegistration) and test teardown. Must not run while a live Host Session
        // still expects held leases — that would silently unbind unrelated Managers.
        internal static void Reset()
        {
            UnityEngine.Debug.Assert(
                OrpheusMainThread.IsCurrent,
                "OrpheusAudioLeaseRegistry.Reset requires the Unity main thread.");

            lock (Gate)
            {
                for (var index = 0; index < Leases.Count; index++)
                {
                    var lease = Leases[index];
                    lease.HostHeld = false;
                    lease.SourceHeld = false;
                    lease.MixerHeld = false;
                    lease.RuntimeHost = null;
                    lease.SourceBank = null;
                    lease.Sources = null;
                    lease.MixerIdentity = null;
                }

                Leases.Clear();
            }
        }

        private static OrpheusAudioLeaseWitness Find(OrpheusAudioManager owner)
        {
            for (var index = 0; index < Leases.Count; index++)
            {
                if (ReferenceEquals(Leases[index].Owner, owner))
                {
                    return Leases[index];
                }
            }

            return null;
        }

        private static void RemoveIfReleased(OrpheusAudioLeaseWitness lease)
        {
            if (!lease.HostHeld && !lease.SourceHeld && !lease.MixerHeld)
            {
                Leases.Remove(lease);
            }
        }

        private static bool HasOverlap(AudioSource[] left, AudioSource[] right)
        {
            for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
            {
                for (var rightIndex = 0; rightIndex < right.Length; rightIndex++)
                {
                    if (ReferenceEquals(left[leftIndex], right[rightIndex]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Held flags are mutated only under Gate on the Unity main thread. Hot-path readers
        // (Manager.HasSourceBankLease/HasMixerLease) observe SourceHeld/MixerHeld without
        // re-entering Gate. volatile keeps release visibility if a future callback ever
        // mutates outside the current single-threaded call graph.
        internal sealed class OrpheusAudioLeaseWitness
        {
            internal OrpheusAudioLeaseWitness(
                OrpheusAudioRuntimeHost runtimeHost,
                OrpheusAudioSourceBank sourceBank,
                AudioSource[] sources,
                object mixerIdentity,
                OrpheusAudioManager owner)
            {
                RuntimeHost = runtimeHost;
                SourceBank = sourceBank;
                Sources = sources;
                MixerIdentity = mixerIdentity;
                Owner = owner;
                HostHeld = true;
                SourceHeld = true;
                MixerHeld = true;
            }

            internal OrpheusAudioRuntimeHost RuntimeHost;
            internal OrpheusAudioSourceBank SourceBank;
            internal AudioSource[] Sources;
            internal object MixerIdentity;
            internal readonly OrpheusAudioManager Owner;
            internal volatile bool HostHeld;
            internal volatile bool SourceHeld;
            internal volatile bool MixerHeld;
        }
    }
}
