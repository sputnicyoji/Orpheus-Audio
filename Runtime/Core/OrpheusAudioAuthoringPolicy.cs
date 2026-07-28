using System;

namespace Orpheus.Audio.Core
{
    internal static class OrpheusAudioAuthoringSchema
    {
        internal const int Current = 1;
    }

    internal enum OrpheusAudioKeyStatus : byte
    {
        Invalid = 0,
        Active = 1,
        Reserved = 2,
        Retired = 3
    }

    internal readonly struct OrpheusAudioKeyManifestEntryValue
    {
        internal OrpheusAudioKeyManifestEntryValue(
            ushort id,
            string symbol,
            OrpheusAudioKeyStatus status)
        {
            Id = id;
            Symbol = symbol;
            Status = status;
        }

        internal ushort Id { get; }
        internal string Symbol { get; }
        internal OrpheusAudioKeyStatus Status { get; }
    }

    [Flags]
    internal enum OrpheusAudioManifestEntryMismatch : byte
    {
        None = 0,
        Id = 1 << 0,
        Symbol = 1 << 1,
        Status = 1 << 2
    }

    internal static class OrpheusAudioManifestPolicy
    {
        internal const string GeneratedTypeName = "OrpheusAudioKeys";
        internal const string GeneratedSchemaMemberName = "ManifestSchemaVersion";
        internal const string GeneratedHashMemberName = "ManifestContentHash";

        internal static OrpheusAudioManifestEntryMismatch Evaluate(
            OrpheusAudioKeyManifestEntryValue value)
        {
            var mismatch = OrpheusAudioManifestEntryMismatch.None;
            if (value.Id == 0)
            {
                mismatch |= OrpheusAudioManifestEntryMismatch.Id;
            }

            if (!IsLegalSymbol(value.Symbol))
            {
                mismatch |= OrpheusAudioManifestEntryMismatch.Symbol;
            }

            if (value.Status < OrpheusAudioKeyStatus.Active ||
                value.Status > OrpheusAudioKeyStatus.Retired)
            {
                mismatch |= OrpheusAudioManifestEntryMismatch.Status;
            }

            return mismatch;
        }

        internal static bool IsLegalSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol) ||
                !(IsAsciiLetter(symbol[0]) || symbol[0] == '_') ||
                IsCSharpKeyword(symbol) || IsGeneratedName(symbol))
            {
                return false;
            }

            for (var index = 1; index < symbol.Length; index++)
            {
                var character = symbol[index];
                if (!(IsAsciiLetter(character) || character == '_' ||
                      (character >= '0' && character <= '9')))
                {
                    return false;
                }

                if (character == '_' && symbol[index - 1] == '_')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAsciiLetter(char value)
        {
            return (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');
        }

        private static bool IsGeneratedName(string value)
        {
            return string.Equals(value, GeneratedTypeName, StringComparison.Ordinal) ||
                   string.Equals(value, GeneratedSchemaMemberName, StringComparison.Ordinal) ||
                   string.Equals(value, GeneratedHashMemberName, StringComparison.Ordinal);
        }

        private static bool IsCSharpKeyword(string value)
        {
            switch (value)
            {
                case "abstract":
                case "add":
                case "alias":
                case "and":
                case "as":
                case "ascending":
                case "async":
                case "await":
                case "base":
                case "bool":
                case "break":
                case "by":
                case "byte":
                case "case":
                case "catch":
                case "char":
                case "checked":
                case "class":
                case "const":
                case "continue":
                case "decimal":
                case "default":
                case "delegate":
                case "descending":
                case "do":
                case "double":
                case "dynamic":
                case "else":
                case "enum":
                case "equals":
                case "event":
                case "explicit":
                case "extern":
                case "false":
                case "file":
                case "finally":
                case "fixed":
                case "float":
                case "for":
                case "foreach":
                case "from":
                case "get":
                case "global":
                case "goto":
                case "group":
                case "if":
                case "implicit":
                case "in":
                case "init":
                case "int":
                case "interface":
                case "internal":
                case "into":
                case "is":
                case "join":
                case "let":
                case "lock":
                case "long":
                case "managed":
                case "nameof":
                case "namespace":
                case "new":
                case "not":
                case "notnull":
                case "null":
                case "object":
                case "on":
                case "operator":
                case "or":
                case "orderby":
                case "out":
                case "override":
                case "params":
                case "partial":
                case "private":
                case "protected":
                case "public":
                case "readonly":
                case "record":
                case "ref":
                case "remove":
                case "required":
                case "return":
                case "sbyte":
                case "scoped":
                case "sealed":
                case "select":
                case "set":
                case "short":
                case "sizeof":
                case "stackalloc":
                case "static":
                case "string":
                case "struct":
                case "switch":
                case "this":
                case "throw":
                case "true":
                case "try":
                case "typeof":
                case "uint":
                case "ulong":
                case "unchecked":
                case "unmanaged":
                case "unsafe":
                case "ushort":
                case "using":
                case "value":
                case "var":
                case "virtual":
                case "void":
                case "volatile":
                case "when":
                case "where":
                case "while":
                case "with":
                case "yield":
                    return true;
                default:
                    return false;
            }
        }
    }

    internal readonly struct OrpheusAudioEventPolicyValues
    {
        internal OrpheusAudioEventPolicyValues(
            int schemaVersion,
            ushort rawKey,
            OrpheusPlaybackKind playbackKind,
            OrpheusCategory category,
            OrpheusLoadPolicy loadPolicy,
            int clipCount,
            float volumeMinimum,
            float volumeMaximum,
            float pitchMinimum,
            float pitchMaximum,
            byte polyphonyCap,
            float cooldownSeconds,
            float minimumDistance,
            float maximumDistance,
            OrpheusRolloffMode rolloffMode)
        {
            SchemaVersion = schemaVersion;
            RawKey = rawKey;
            PlaybackKind = playbackKind;
            Category = category;
            LoadPolicy = loadPolicy;
            ClipCount = clipCount;
            VolumeMinimum = volumeMinimum;
            VolumeMaximum = volumeMaximum;
            PitchMinimum = pitchMinimum;
            PitchMaximum = pitchMaximum;
            PolyphonyCap = polyphonyCap;
            CooldownSeconds = cooldownSeconds;
            MinimumDistance = minimumDistance;
            MaximumDistance = maximumDistance;
            RolloffMode = rolloffMode;
        }

        internal int SchemaVersion { get; }
        internal ushort RawKey { get; }
        internal OrpheusPlaybackKind PlaybackKind { get; }
        internal OrpheusCategory Category { get; }
        internal OrpheusLoadPolicy LoadPolicy { get; }
        internal int ClipCount { get; }
        internal float VolumeMinimum { get; }
        internal float VolumeMaximum { get; }
        internal float PitchMinimum { get; }
        internal float PitchMaximum { get; }
        internal byte PolyphonyCap { get; }
        internal float CooldownSeconds { get; }
        internal float MinimumDistance { get; }
        internal float MaximumDistance { get; }
        internal OrpheusRolloffMode RolloffMode { get; }
    }

    [Flags]
    internal enum OrpheusAudioEventPolicyMismatch : uint
    {
        None = 0,
        SchemaVersion = 1u << 0,
        Key = 1u << 1,
        PlaybackKind = 1u << 2,
        Category = 1u << 3,
        LoadPolicy = 1u << 4,
        ClipCount = 1u << 5,
        VolumeRange = 1u << 6,
        PitchRange = 1u << 7,
        Cooldown = 1u << 8,
        CategoryForPlaybackKind = 1u << 9,
        Polyphony = 1u << 10,
        LoadPolicyForPlaybackKind = 1u << 11,
        PersistentClipCount = 1u << 12,
        PersistentVolume = 1u << 13,
        PersistentPitch = 1u << 14,
        PersistentCooldown = 1u << 15,
        DistanceRange = 1u << 16,
        RolloffMode = 1u << 17
    }

    internal static class OrpheusAudioEventPolicy
    {
        internal static OrpheusAudioEventPolicyMismatch Evaluate(
            OrpheusAudioEventPolicyValues values)
        {
            var mismatch = OrpheusAudioEventPolicyMismatch.None;
            if (values.SchemaVersion != OrpheusAudioAuthoringSchema.Current)
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.SchemaVersion;
            }

            if (values.RawKey == 0)
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.Key;
            }

            var playbackKindValid = values.PlaybackKind >= OrpheusPlaybackKind.OneShot2D &&
                                    values.PlaybackKind <= OrpheusPlaybackKind.ProfileAmbience;
            if (!playbackKindValid)
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.PlaybackKind;
            }

            var categoryValid = values.Category >= OrpheusCategory.Music &&
                                values.Category <= OrpheusCategory.Ambience;
            if (!categoryValid)
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.Category;
            }

            var loadPolicyValid = values.LoadPolicy >= OrpheusLoadPolicy.BootstrapTransient &&
                                  values.LoadPolicy <= OrpheusLoadPolicy.PersistentStream;
            if (!loadPolicyValid)
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.LoadPolicy;
            }

            if (values.ClipCount < 1 || values.ClipCount > 8)
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.ClipCount;
            }

            if (!IsFiniteRange(values.VolumeMinimum, values.VolumeMaximum, 0f, 1f))
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.VolumeRange;
            }

            if (!IsFiniteRange(values.PitchMinimum, values.PitchMaximum, 0.5f, 2f))
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.PitchRange;
            }

            if (!IsFinite(values.CooldownSeconds) || values.CooldownSeconds < 0f)
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.Cooldown;
            }

            if (playbackKindValid && categoryValid &&
                !IsCategoryAllowed(values.PlaybackKind, values.Category))
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.CategoryForPlaybackKind;
            }

            if (!playbackKindValid)
            {
                return mismatch;
            }

            var persistent = values.PlaybackKind == OrpheusPlaybackKind.GlobalLoop2D ||
                             values.PlaybackKind == OrpheusPlaybackKind.Bgm ||
                             values.PlaybackKind == OrpheusPlaybackKind.ProfileAmbience;
            if (persistent)
            {
                if (values.PolyphonyCap != 1)
                {
                    mismatch |= OrpheusAudioEventPolicyMismatch.Polyphony;
                }

                if (values.LoadPolicy != OrpheusLoadPolicy.PersistentStream)
                {
                    mismatch |= OrpheusAudioEventPolicyMismatch.LoadPolicyForPlaybackKind;
                }

                if (values.ClipCount != 1)
                {
                    mismatch |= OrpheusAudioEventPolicyMismatch.PersistentClipCount;
                }

                if (!IsFinite(values.VolumeMinimum) ||
                    !IsFinite(values.VolumeMaximum) ||
                    values.VolumeMinimum != values.VolumeMaximum)
                {
                    mismatch |= OrpheusAudioEventPolicyMismatch.PersistentVolume;
                }

                if (values.PitchMinimum != 1f || values.PitchMaximum != 1f)
                {
                    mismatch |= OrpheusAudioEventPolicyMismatch.PersistentPitch;
                }

                if (values.CooldownSeconds != 0f)
                {
                    mismatch |= OrpheusAudioEventPolicyMismatch.PersistentCooldown;
                }

                return mismatch;
            }

            var maximumPolyphony = values.PlaybackKind == OrpheusPlaybackKind.OneShot2D
                ? 4
                : 12;
            if (values.PolyphonyCap < 1 || values.PolyphonyCap > maximumPolyphony)
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.Polyphony;
            }

            if (values.LoadPolicy != OrpheusLoadPolicy.BootstrapTransient &&
                values.LoadPolicy != OrpheusLoadPolicy.ExplicitTransient)
            {
                mismatch |= OrpheusAudioEventPolicyMismatch.LoadPolicyForPlaybackKind;
            }

            if (values.PlaybackKind == OrpheusPlaybackKind.OneShot3D)
            {
                if (!IsFinite(values.MinimumDistance) ||
                    !IsFinite(values.MaximumDistance) ||
                    values.MinimumDistance < 0f ||
                    values.MinimumDistance >= values.MaximumDistance)
                {
                    mismatch |= OrpheusAudioEventPolicyMismatch.DistanceRange;
                }

                if (values.RolloffMode != OrpheusRolloffMode.Logarithmic &&
                    values.RolloffMode != OrpheusRolloffMode.Linear)
                {
                    mismatch |= OrpheusAudioEventPolicyMismatch.RolloffMode;
                }
            }

            return mismatch;
        }

        private static bool IsCategoryAllowed(
            OrpheusPlaybackKind playbackKind,
            OrpheusCategory category)
        {
            switch (playbackKind)
            {
                case OrpheusPlaybackKind.OneShot2D:
                    return true;
                case OrpheusPlaybackKind.OneShot3D:
                    return category == OrpheusCategory.SfxCombat ||
                           category == OrpheusCategory.SfxWorld ||
                           category == OrpheusCategory.Ambience;
                case OrpheusPlaybackKind.GlobalLoop2D:
                    return category == OrpheusCategory.SfxCombat ||
                           category == OrpheusCategory.SfxWorld ||
                           category == OrpheusCategory.SfxUi ||
                           category == OrpheusCategory.Ambience;
                case OrpheusPlaybackKind.Bgm:
                    return category == OrpheusCategory.Music;
                case OrpheusPlaybackKind.ProfileAmbience:
                    return category == OrpheusCategory.Ambience;
                default:
                    return false;
            }
        }

        private static bool IsFiniteRange(float minimum, float maximum, float lower, float upper)
        {
            return IsFinite(minimum) && IsFinite(maximum) &&
                   minimum >= lower && maximum <= upper && minimum <= maximum;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
