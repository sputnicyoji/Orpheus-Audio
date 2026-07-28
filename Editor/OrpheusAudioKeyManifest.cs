using System;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio.Editor
{
    [Serializable]
    internal struct OrpheusAudioKeyManifestEntry
    {
        [SerializeField] private ushort _id;
        [SerializeField] private string _symbol;
        [SerializeField] private OrpheusAudioKeyStatus _status;

        internal OrpheusAudioKeyManifestEntryValue CaptureValue()
        {
            return new OrpheusAudioKeyManifestEntryValue(_id, _symbol, _status);
        }
    }

    [CreateAssetMenu(fileName = "OrpheusAudioKeyManifest", menuName = "Orpheus/Audio Key Manifest")]
    public sealed class OrpheusAudioKeyManifest : ScriptableObject
    {
        [SerializeField] private int _schemaVersion = OrpheusAudioAuthoringSchema.Current;
        [SerializeField] private OrpheusAudioKeyManifestEntry[] _entries =
            Array.Empty<OrpheusAudioKeyManifestEntry>();

        internal int SchemaVersion => _schemaVersion;
        internal int EntryCount => _entries == null ? 0 : _entries.Length;
        internal bool HasEntryStorage => _entries != null;

        internal OrpheusAudioKeyManifestEntryValue GetEntry(int index)
        {
            return _entries[index].CaptureValue();
        }

        internal OrpheusAudioKeyManifestEntryValue[] CaptureEntries()
        {
            if (_entries == null)
            {
                return null;
            }

            var values = new OrpheusAudioKeyManifestEntryValue[_entries.Length];
            for (var index = 0; index < _entries.Length; index++)
            {
                values[index] = _entries[index].CaptureValue();
            }

            return values;
        }
    }
}
