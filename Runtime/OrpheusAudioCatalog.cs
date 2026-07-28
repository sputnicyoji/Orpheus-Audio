using System;
using Orpheus.Audio.Core;
using UnityEngine;

namespace Orpheus.Audio
{
    [CreateAssetMenu(fileName = "OrpheusAudioCatalog", menuName = "Orpheus/Audio Catalog")]
    public sealed class OrpheusAudioCatalog : ScriptableObject
    {
        [SerializeField] private int _schemaVersion = OrpheusAudioAuthoringSchema.Current;
        [SerializeField] private OrpheusAudioEvent[] _events = new OrpheusAudioEvent[0];

        internal int SchemaVersion => _schemaVersion;
        internal bool HasEventList => _events != null;
        internal int EventCount => _events == null ? 0 : _events.Length;

        internal OrpheusAudioEvent GetEvent(int eventIndex)
        {
            return _events[eventIndex];
        }

        internal bool TryBuildSnapshot(
            out OrpheusRuntimeCatalogSnapshot snapshot,
            out OrpheusAudioInitErrorCode errorCode,
            out OrpheusAudioKey relatedKey,
            out int relatedIndex)
        {
            snapshot = null;
            relatedKey = OrpheusAudioKey.Invalid;
            relatedIndex = -1;
            errorCode = OrpheusAudioInitErrorCode.None;

            try
            {
                if (_schemaVersion != OrpheusAudioAuthoringSchema.Current || _events == null)
                {
                    errorCode = OrpheusAudioInitErrorCode.InvalidCatalog;
                    return false;
                }

                var previousKey = OrpheusAudioKey.Invalid;
                for (var eventIndex = 0; eventIndex < _events.Length; eventIndex++)
                {
                    var candidate = _events[eventIndex];
                    if (candidate == null || !candidate.Key.IsValid)
                    {
                        continue;
                    }

                    relatedIndex = eventIndex;
                    if (previousKey.IsValid && previousKey.Value > candidate.Key.Value)
                    {
                        errorCode = OrpheusAudioInitErrorCode.InvalidCatalog;
                        relatedKey = candidate.Key;
                        return false;
                    }

                    previousKey = candidate.Key;
                }
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                errorCode = OrpheusAudioInitErrorCode.InvalidCatalog;
                relatedKey = OrpheusAudioKey.Invalid;
                return false;
            }

            relatedIndex = -1;
            try
            {
                for (var eventIndex = 0; eventIndex < _events.Length; eventIndex++)
                {
                    relatedIndex = eventIndex;
                    var candidate = _events[eventIndex];
                    if (candidate == null || !candidate.Key.IsValid)
                    {
                        continue;
                    }

                    for (var previousIndex = 0; previousIndex < eventIndex; previousIndex++)
                    {
                        var previous = _events[previousIndex];
                        if (previous != null && previous.Key == candidate.Key)
                        {
                            errorCode = OrpheusAudioInitErrorCode.DuplicateKey;
                            relatedKey = candidate.Key;
                            return false;
                        }
                    }
                }
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                errorCode = OrpheusAudioInitErrorCode.DuplicateKey;
                relatedKey = OrpheusAudioKey.Invalid;
                return false;
            }

            var flattenedClipCount = 0;
            OrpheusCatalogEntry[] entries;
            relatedIndex = -1;
            try
            {
                entries = new OrpheusCatalogEntry[_events.Length];
                for (var eventIndex = 0; eventIndex < _events.Length; eventIndex++)
                {
                    relatedIndex = eventIndex;
                    var audioEvent = _events[eventIndex];
                    if (audioEvent == null || !audioEvent.HasValidScalarShape() ||
                        !audioEvent.HasUniqueClipReferences() ||
                        flattenedClipCount > int.MaxValue - audioEvent.ClipCount)
                    {
                        errorCode = OrpheusAudioInitErrorCode.InvalidEvent;
                        relatedKey = audioEvent == null ? OrpheusAudioKey.Invalid : audioEvent.Key;
                        return false;
                    }

                    entries[eventIndex] = audioEvent.BuildEntry(flattenedClipCount);
                    flattenedClipCount += audioEvent.ClipCount;
                }
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                errorCode = OrpheusAudioInitErrorCode.InvalidEvent;
                relatedKey = OrpheusAudioKey.Invalid;
                return false;
            }

            AudioClip[] clips;
            relatedIndex = -1;
            try
            {
                clips = new AudioClip[flattenedClipCount];
                var clipOffset = 0;
                for (var eventIndex = 0; eventIndex < _events.Length; eventIndex++)
                {
                    relatedIndex = eventIndex;
                    var audioEvent = _events[eventIndex];
                    relatedKey = audioEvent.Key;
                    for (var clipIndex = 0; clipIndex < audioEvent.ClipCount; clipIndex++)
                    {
                        var clip = audioEvent.GetClip(clipIndex);
                        if (clip == null)
                        {
                            errorCode = OrpheusAudioInitErrorCode.InvalidClipReference;
                            return false;
                        }

                        clips[clipOffset++] = clip;
                    }
                }
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                errorCode = OrpheusAudioInitErrorCode.InvalidClipReference;
                return false;
            }

            relatedKey = OrpheusAudioKey.Invalid;
            relatedIndex = -1;
            try
            {
                if (!OrpheusCatalogLookupIndex.TryCreate(
                        entries,
                        flattenedClipCount,
                        out var index,
                        out var buildError))
                {
                    errorCode = buildError == OrpheusCatalogBuildError.DuplicateKey
                        ? OrpheusAudioInitErrorCode.DuplicateKey
                        : OrpheusAudioInitErrorCode.InvalidCatalog;
                    return false;
                }

                snapshot = new OrpheusRuntimeCatalogSnapshot(this, index, clips);
                errorCode = OrpheusAudioInitErrorCode.None;
                return true;
            }
            catch (Exception exception) when (!OrpheusAudioManager.IsCatastrophic(exception))
            {
                errorCode = OrpheusAudioInitErrorCode.InvalidCatalog;
                return false;
            }
        }
    }
}
