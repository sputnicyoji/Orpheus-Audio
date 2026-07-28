using System;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio
{
    internal sealed class OrpheusRuntimeCatalogSnapshot
    {
        private readonly AudioClip[] _flattenedClips;

        internal OrpheusRuntimeCatalogSnapshot(
            OrpheusAudioCatalog catalog,
            OrpheusCatalogLookupIndex coreIndex,
            AudioClip[] flattenedClips)
        {
            Catalog = catalog;
            CoreIndex = coreIndex;
            _flattenedClips = flattenedClips;
        }

        internal OrpheusAudioCatalog Catalog { get; }
        internal OrpheusCatalogLookupIndex CoreIndex { get; }
        internal int FlattenedClipCount => _flattenedClips.Length;

        internal bool TryGetEntry(OrpheusAudioKey key, out OrpheusCatalogEntry entry)
        {
            return CoreIndex.TryGet(key, out entry);
        }

        internal AudioClip GetClip(int flattenedClipIndex)
        {
            if ((uint)flattenedClipIndex >= (uint)_flattenedClips.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(flattenedClipIndex));
            }

            return _flattenedClips[flattenedClipIndex];
        }
    }
}
