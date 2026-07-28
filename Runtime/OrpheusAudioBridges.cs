using System.Threading;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio
{
    public static class OrpheusAudioBridge
    {
        private static OrpheusAudioManager s_manager;

        public static bool Bind(OrpheusAudioManager manager)
        {
            return OrpheusAudioBridgeBinding.Bind(ref s_manager, manager);
        }

        public static bool Unbind(OrpheusAudioManager expectedManager)
        {
            return OrpheusAudioBridgeBinding.Unbind(ref s_manager, expectedManager);
        }

        public static void Play(OrpheusAudioKey key)
        {
            var manager = OrpheusAudioBridgeBinding.GetForCall(ref s_manager);
            if (manager != null)
            {
                manager.Play(key);
            }
        }

        public static void PlayAt(OrpheusAudioKey key, Vector3 position)
        {
            var manager = OrpheusAudioBridgeBinding.GetForCall(ref s_manager);
            if (manager != null)
            {
                manager.PlayAt(key, position);
            }
        }

        internal static void ResetBinding()
        {
            Volatile.Write(ref s_manager, null);
        }
    }

    public static class OrpheusAudioRawBridge
    {
        private static OrpheusAudioManager s_manager;

        public static bool Bind(OrpheusAudioManager manager)
        {
            return OrpheusAudioBridgeBinding.Bind(ref s_manager, manager);
        }

        public static bool Unbind(OrpheusAudioManager expectedManager)
        {
            return OrpheusAudioBridgeBinding.Unbind(ref s_manager, expectedManager);
        }

        public static void Play(int rawKey)
        {
            var manager = OrpheusAudioBridgeBinding.GetForCall(ref s_manager);
            if (manager != null && manager.TryResolveRawBridgeKey(rawKey, out var key))
            {
                manager.Play(key);
            }
        }

        public static void PlayAt(int rawKey, Vector3 position)
        {
            var manager = OrpheusAudioBridgeBinding.GetForCall(ref s_manager);
            if (manager != null && manager.TryResolveRawBridgeKey(rawKey, out var key))
            {
                manager.PlayAt(key, position);
            }
        }

        internal static void ResetBinding()
        {
            Volatile.Write(ref s_manager, null);
        }
    }

    internal static class OrpheusAudioBridgeBinding
    {
        internal static bool Bind(
            ref OrpheusAudioManager boundManager,
            OrpheusAudioManager manager)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                var rejectionManager = manager ?? Volatile.Read(ref boundManager);
                if (rejectionManager != null)
                {
                    rejectionManager.RejectWrongThread();
                }

                return false;
            }

            if (manager == null)
            {
                return false;
            }

            var current = Volatile.Read(ref boundManager);
            if (current != null)
            {
                return ReferenceEquals(current, manager);
            }

            if (!manager.IsAvailableForBridgeBinding())
            {
                return false;
            }

            Volatile.Write(ref boundManager, manager);
            return true;
        }

        internal static bool Unbind(
            ref OrpheusAudioManager boundManager,
            OrpheusAudioManager expectedManager)
        {
            if (!OrpheusMainThread.IsCurrent)
            {
                var rejectionManager = expectedManager ?? Volatile.Read(ref boundManager);
                if (rejectionManager != null)
                {
                    rejectionManager.RejectWrongThread();
                }

                return false;
            }

            if (expectedManager == null ||
                !ReferenceEquals(Volatile.Read(ref boundManager), expectedManager))
            {
                return false;
            }

            Volatile.Write(ref boundManager, null);
            return true;
        }

        internal static OrpheusAudioManager GetForCall(ref OrpheusAudioManager boundManager)
        {
            if (OrpheusMainThread.IsCurrent)
            {
                return Volatile.Read(ref boundManager);
            }

            var manager = Volatile.Read(ref boundManager);
            if (manager != null)
            {
                manager.RejectWrongThread();
            }

            return null;
        }
    }

    // Process bootstrap / domain-reload hygiene only. Bridge statics are bind + one-shot
    // Play surfaces — not a session owner or global singleton. Do not expand this Reset
    // into mid-session teardown or add high-privilege convenience APIs on the bridges.
    internal static class OrpheusAudioRuntimeStatics
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Reset()
        {
            OrpheusMainThread.Initialize();
            OrpheusAudioBridge.ResetBinding();
            OrpheusAudioRawBridge.ResetBinding();
            OrpheusAudioLeaseRegistry.Reset();
        }
    }
}
