using System;
using System.Runtime.InteropServices;

namespace Orpheus.Audio.Core
{
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 2)]
    public readonly struct OrpheusAudioKey : IEquatable<OrpheusAudioKey>
    {
        private readonly ushort _value;

        public OrpheusAudioKey(ushort value)
        {
            _value = value;
        }

        public static OrpheusAudioKey Invalid => default;

        public ushort Value => _value;

        public bool IsValid => _value != 0;

        public bool Equals(OrpheusAudioKey other)
        {
            return _value == other._value;
        }

        public override bool Equals(object obj)
        {
            return obj is OrpheusAudioKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public static bool operator ==(OrpheusAudioKey left, OrpheusAudioKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(OrpheusAudioKey left, OrpheusAudioKey right)
        {
            return !left.Equals(right);
        }
    }
}
