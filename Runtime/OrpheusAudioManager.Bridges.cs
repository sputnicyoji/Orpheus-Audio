using Orpheus.Audio.Core;

namespace Orpheus.Audio
{
    public sealed partial class OrpheusAudioManager
    {
        internal bool IsAvailableForBridgeBinding()
        {
            return _lifecycle == OrpheusAudioLifecycle.Running;
        }

        internal bool TryResolveRawBridgeKey(int rawKey, out OrpheusAudioKey key)
        {
            key = OrpheusAudioKey.Invalid;
            if (rawKey <= 0 || rawKey > ushort.MaxValue)
            {
                SaturatingIncrement(ref _invalidRawKeyRejectedBits);
                return false;
            }

            var rawIdentity = (ushort)rawKey;
            if (!_catalogSnapshot.CoreIndex.TryGetRaw(rawIdentity, out _, out _))
            {
                SaturatingIncrement(ref _invalidKeyRejectedBits);
                return false;
            }

            key = new OrpheusAudioKey(rawIdentity);
            return true;
        }

        private void UnbindStaticBridges()
        {
            OrpheusAudioBridge.Unbind(this);
            OrpheusAudioRawBridge.Unbind(this);
        }
    }
}
