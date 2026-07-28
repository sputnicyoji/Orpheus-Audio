namespace Orpheus.Audio.Core
{
    internal struct OrpheusXorshift32
    {
        private const uint ZeroSeedReplacement = 0x6D2B79F5u;
        private const float UnitFloatScale = 1f / 16777216f;

        private uint _state;

        internal OrpheusXorshift32(uint seed)
        {
            _state = seed == 0u ? ZeroSeedReplacement : seed;
        }

        internal uint State => _state;

        internal uint NextUInt()
        {
            var value = _state;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return value;
        }

        internal uint NextBounded(uint exclusiveUpperBound)
        {
            if (exclusiveUpperBound <= 1u)
            {
                return 0u;
            }

            var threshold = unchecked(0u - exclusiveUpperBound) % exclusiveUpperBound;
            uint value;
            do
            {
                value = NextUInt();
            }
            while (value < threshold);

            return value % exclusiveUpperBound;
        }

        internal float NextUnitFloat()
        {
            return (NextUInt() >> 8) * UnitFloatScale;
        }
    }

    internal static class OrpheusTransientVariationPolicy
    {
        internal const byte UninitializedCursor = byte.MaxValue;

        internal static int SampleClipIndex(
            int sortedEntryIndex,
            int clipOffset,
            int clipCount,
            byte[] shuffleOrder,
            byte[] cursors,
            byte[] previousRoundLast,
            ref OrpheusXorshift32 random)
        {
            if (clipCount <= 1)
            {
                return clipOffset;
            }

            var cursor = cursors[sortedEntryIndex];
            if (cursor == UninitializedCursor)
            {
                for (var relativeIndex = 0; relativeIndex < clipCount; relativeIndex++)
                {
                    shuffleOrder[clipOffset + relativeIndex] = (byte)relativeIndex;
                }

                ShuffleRound(
                    clipOffset,
                    clipCount,
                    shuffleOrder,
                    previousRoundLast[sortedEntryIndex],
                    ref random);
                cursor = 0;
            }
            else if (cursor >= clipCount)
            {
                ShuffleRound(
                    clipOffset,
                    clipCount,
                    shuffleOrder,
                    previousRoundLast[sortedEntryIndex],
                    ref random);
                cursor = 0;
            }

            var relativeClipIndex = shuffleOrder[clipOffset + cursor];
            cursor++;
            cursors[sortedEntryIndex] = cursor;
            if (cursor == clipCount)
            {
                previousRoundLast[sortedEntryIndex] = relativeClipIndex;
            }

            return clipOffset + relativeClipIndex;
        }

        internal static float SampleScalar(
            float minimum,
            float maximum,
            ref OrpheusXorshift32 random)
        {
            if (minimum == maximum)
            {
                return minimum;
            }

            return minimum + ((maximum - minimum) * random.NextUnitFloat());
        }

        private static void ShuffleRound(
            int clipOffset,
            int clipCount,
            byte[] shuffleOrder,
            byte previousRoundLast,
            ref OrpheusXorshift32 random)
        {
            for (var upperIndex = clipCount - 1; upperIndex > 0; upperIndex--)
            {
                var swapIndex = (int)random.NextBounded((uint)(upperIndex + 1));
                Swap(shuffleOrder, clipOffset + upperIndex, clipOffset + swapIndex);
            }

            if (shuffleOrder[clipOffset] == previousRoundLast)
            {
                var swapIndex = 1 + (int)random.NextBounded((uint)(clipCount - 1));
                Swap(shuffleOrder, clipOffset, clipOffset + swapIndex);
            }
        }

        private static void Swap(byte[] values, int leftIndex, int rightIndex)
        {
            var value = values[leftIndex];
            values[leftIndex] = values[rightIndex];
            values[rightIndex] = value;
        }
    }
}
