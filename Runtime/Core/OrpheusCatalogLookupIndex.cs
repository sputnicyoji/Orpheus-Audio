using System;
using System.Collections.Generic;

namespace Orpheus.Audio.Core
{
    internal enum OrpheusCatalogBuildError : byte
    {
        None = 0,
        InvalidArguments = 1,
        InvalidKey = 2,
        InvalidClipRange = 3,
        DuplicateKey = 4
    }

    internal sealed class OrpheusCatalogLookupIndex
    {
        private readonly OrpheusCatalogEntry[] _entries;

        private OrpheusCatalogLookupIndex(OrpheusCatalogEntry[] entries)
        {
            _entries = entries;
        }

        public int Count => _entries.Length;

        public static bool TryCreate(
            OrpheusCatalogEntry[] authoredEntries,
            int flattenedClipCount,
            out OrpheusCatalogLookupIndex index,
            out OrpheusCatalogBuildError error)
        {
            index = null;

            if (authoredEntries == null)
            {
                error = OrpheusCatalogBuildError.InvalidArguments;
                return false;
            }

            if (!HasConsistentClipRanges(authoredEntries, flattenedClipCount))
            {
                error = OrpheusCatalogBuildError.InvalidClipRange;
                return false;
            }

            var sortedEntries = new OrpheusCatalogEntry[authoredEntries.Length];
            Array.Copy(authoredEntries, sortedEntries, authoredEntries.Length);
            Array.Sort(sortedEntries, OrpheusEntryKeyComparer.Instance);

            for (var entryIndex = 0; entryIndex < sortedEntries.Length; entryIndex++)
            {
                if (!sortedEntries[entryIndex].Key.IsValid)
                {
                    error = OrpheusCatalogBuildError.InvalidKey;
                    return false;
                }

                if (entryIndex > 0 && sortedEntries[entryIndex - 1].Key == sortedEntries[entryIndex].Key)
                {
                    error = OrpheusCatalogBuildError.DuplicateKey;
                    return false;
                }
            }

            index = new OrpheusCatalogLookupIndex(sortedEntries);
            error = OrpheusCatalogBuildError.None;
            return true;
        }

        public bool TryGet(OrpheusAudioKey key, out OrpheusCatalogEntry entry)
        {
            return TryGet(key, out entry, out _);
        }

        internal bool TryGet(
            OrpheusAudioKey key,
            out OrpheusCatalogEntry entry,
            out int sortedEntryIndex)
        {
            if (!key.IsValid)
            {
                entry = default;
                sortedEntryIndex = -1;
                return false;
            }

            return TryGetRaw(key.Value, out entry, out sortedEntryIndex);
        }

        internal bool TryGetRaw(
            ushort rawKey,
            out OrpheusCatalogEntry entry,
            out int sortedEntryIndex)
        {
            entry = default;
            sortedEntryIndex = -1;
            if (rawKey == 0)
            {
                return false;
            }

            var lower = 0;
            var upper = _entries.Length - 1;

            while (lower <= upper)
            {
                var middle = lower + ((upper - lower) >> 1);
                var middleRawKey = _entries[middle].Key.Value;

                if (middleRawKey == rawKey)
                {
                    entry = _entries[middle];
                    sortedEntryIndex = middle;
                    return true;
                }

                if (middleRawKey < rawKey)
                {
                    lower = middle + 1;
                }
                else
                {
                    upper = middle - 1;
                }
            }

            return false;
        }

        internal OrpheusCatalogEntry GetAt(int sortedEntryIndex)
        {
            if ((uint)sortedEntryIndex >= (uint)_entries.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(sortedEntryIndex));
            }

            return _entries[sortedEntryIndex];
        }

        private static bool HasConsistentClipRanges(
            OrpheusCatalogEntry[] authoredEntries,
            int flattenedClipCount)
        {
            if (flattenedClipCount < 0)
            {
                return false;
            }

            var expectedOffset = 0;
            for (var entryIndex = 0; entryIndex < authoredEntries.Length; entryIndex++)
            {
                var entry = authoredEntries[entryIndex];
                if (entry.ClipRange.Offset != expectedOffset || entry.ClipRange.Count <= 0)
                {
                    return false;
                }

                if (expectedOffset > int.MaxValue - entry.ClipRange.Count)
                {
                    return false;
                }

                expectedOffset += entry.ClipRange.Count;
            }

            return expectedOffset == flattenedClipCount;
        }

        private sealed class OrpheusEntryKeyComparer : IComparer<OrpheusCatalogEntry>
        {
            public static readonly OrpheusEntryKeyComparer Instance = new OrpheusEntryKeyComparer();

            private OrpheusEntryKeyComparer()
            {
            }

            public int Compare(OrpheusCatalogEntry left, OrpheusCatalogEntry right)
            {
                return left.Key.Value.CompareTo(right.Key.Value);
            }
        }
    }
}
