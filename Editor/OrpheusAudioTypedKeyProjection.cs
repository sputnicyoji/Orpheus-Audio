using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Orpheus.Audio.Core;
using UnityEditor;

namespace Orpheus.Audio.Editor
{
    internal readonly struct OrpheusAudioTypedKeyProjectionPaths
    {
        internal OrpheusAudioTypedKeyProjectionPaths(string outputDirectory)
        {
            OutputDirectory = outputDirectory;
        }

        internal string OutputDirectory { get; }
        internal string AssemblyPath => OutputDirectory + "/Orpheus.Audio.Generated.asmdef";
        internal string SourcePath => OutputDirectory + "/OrpheusAudioKeys.g.cs";
    }

    internal static class OrpheusAudioTypedKeyProjection
    {
        internal const string OutputDirectory = "Assets/OrpheusGenerated";
        internal const string AssemblyPath = OutputDirectory + "/Orpheus.Audio.Generated.asmdef";
        internal const string SourcePath = OutputDirectory + "/OrpheusAudioKeys.g.cs";

        internal static OrpheusAudioTypedKeyProjectionPaths ProductionPaths =>
            new OrpheusAudioTypedKeyProjectionPaths(OutputDirectory);

        internal const string ExpectedAssembly =
            "{\n" +
            "  \"name\": \"Orpheus.Audio.Generated\",\n" +
            "  \"rootNamespace\": \"Orpheus.Audio.Generated\",\n" +
            "  \"references\": [\n" +
            "    \"Orpheus.Audio.Core\"\n" +
            "  ],\n" +
            "  \"autoReferenced\": true,\n" +
            "  \"noEngineReferences\": true\n" +
            "}\n";

        internal static bool TryCreateExpectedSource(
            OrpheusAudioKeyManifest manifest,
            out string source,
            out string contentHash)
        {
            source = null;
            contentHash = null;
            if (manifest == null || manifest.SchemaVersion != OrpheusAudioAuthoringSchema.Current ||
                !manifest.HasEntryStorage)
            {
                return false;
            }

            var entries = manifest.CaptureEntries();
            if (!AreValid(entries))
            {
                return false;
            }

            Array.Sort(entries, CompareEntries);
            contentHash = ComputeContentHash(manifest.SchemaVersion, entries);
            source = BuildSource(manifest.SchemaVersion, contentHash, entries);
            return true;
        }

        internal static bool IsCurrent(OrpheusAudioKeyManifest manifest)
        {
            return IsCurrent(manifest, ProductionPaths);
        }

        internal static bool IsCurrent(
            OrpheusAudioKeyManifest manifest,
            OrpheusAudioTypedKeyProjectionPaths paths)
        {
            if (!TryCreateExpectedSource(manifest, out var expectedSource, out _))
            {
                return false;
            }

            return File.Exists(paths.AssemblyPath) && File.Exists(paths.SourcePath) &&
                   NormalizeNewlines(File.ReadAllText(paths.AssemblyPath)) == ExpectedAssembly &&
                   NormalizeNewlines(File.ReadAllText(paths.SourcePath)) == expectedSource;
        }

        internal static bool TryGenerate(OrpheusAudioKeyManifest manifest)
        {
            if (!TryCreateExpectedSource(manifest, out var source, out _))
            {
                return false;
            }

            var paths = ProductionPaths;
            Directory.CreateDirectory(paths.OutputDirectory);
            WriteIfChanged(paths.AssemblyPath, ExpectedAssembly);
            WriteIfChanged(paths.SourcePath, source);
            AssetDatabase.Refresh();
            return true;
        }

        internal static void WriteExpectedNoRefresh(
            string assemblyText,
            string sourceText)
        {
            Directory.CreateDirectory(OutputDirectory);
            WriteIfChanged(AssemblyPath, assemblyText ?? string.Empty);
            WriteIfChanged(SourcePath, sourceText ?? string.Empty);
        }

        private static bool AreValid(OrpheusAudioKeyManifestEntryValue[] entries)
        {
            if (entries == null)
            {
                return false;
            }

            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (OrpheusAudioManifestPolicy.Evaluate(entry) !=
                    OrpheusAudioManifestEntryMismatch.None)
                {
                    return false;
                }

                for (var previousIndex = 0; previousIndex < index; previousIndex++)
                {
                    var previous = entries[previousIndex];
                    if (previous.Id == entry.Id ||
                        string.Equals(previous.Symbol, entry.Symbol, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static int CompareEntries(
            OrpheusAudioKeyManifestEntryValue left,
            OrpheusAudioKeyManifestEntryValue right)
        {
            return left.Id.CompareTo(right.Id);
        }

        private static string ComputeContentHash(
            int schemaVersion,
            OrpheusAudioKeyManifestEntryValue[] entries)
        {
            using (var stream = new MemoryStream())
            {
                var domain = Encoding.ASCII.GetBytes("Orpheus.Audio.KeyManifest\0v1\0");
                stream.Write(domain, 0, domain.Length);
                WriteInt32(stream, schemaVersion);
                WriteInt32(stream, entries.Length);
                for (var index = 0; index < entries.Length; index++)
                {
                    var entry = entries[index];
                    WriteUInt16(stream, entry.Id);
                    stream.WriteByte((byte)entry.Status);
                    var symbolBytes = Encoding.UTF8.GetBytes(entry.Symbol);
                    WriteInt32(stream, symbolBytes.Length);
                    stream.Write(symbolBytes, 0, symbolBytes.Length);
                }

                using (var sha256 = SHA256.Create())
                {
                    var hash = sha256.ComputeHash(stream.ToArray());
                    var text = new StringBuilder(hash.Length * 2);
                    for (var index = 0; index < hash.Length; index++)
                    {
                        text.Append(hash[index].ToString("x2"));
                    }

                    return text.ToString();
                }
            }
        }

        private static string BuildSource(
            int schemaVersion,
            string contentHash,
            OrpheusAudioKeyManifestEntryValue[] entries)
        {
            var source = new StringBuilder(512 + entries.Length * 96);
            source.Append("// <auto-generated />\n");
            source.Append("using Orpheus.Audio.Core;\n\n");
            source.Append("namespace Orpheus.Audio.Generated\n{\n");
            source.Append("    public static class ");
            source.Append(OrpheusAudioManifestPolicy.GeneratedTypeName);
            source.Append("\n    {\n");
            source.Append("        public const int ");
            source.Append(OrpheusAudioManifestPolicy.GeneratedSchemaMemberName);
            source.Append(" = ");
            source.Append(schemaVersion);
            source.Append(";\n");
            source.Append("        public const string ");
            source.Append(OrpheusAudioManifestPolicy.GeneratedHashMemberName);
            source.Append(" = \"");
            source.Append(contentHash);
            source.Append("\";\n");

            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (entry.Status != OrpheusAudioKeyStatus.Active)
                {
                    continue;
                }

                source.Append("        public static readonly OrpheusAudioKey ");
                source.Append(entry.Symbol);
                source.Append(" = new OrpheusAudioKey(");
                source.Append(entry.Id);
                source.Append(");\n");
            }

            source.Append("    }\n}\n");
            return source.ToString();
        }

        private static void WriteInt32(Stream stream, int value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        private static string NormalizeNewlines(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static void WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && NormalizeNewlines(File.ReadAllText(path)) == content)
            {
                return;
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }

    internal static class OrpheusAudioTypedKeyProjectionMenu
    {
        private const string MenuPath = "Tools/Orpheus/Generate Typed Keys";

        [MenuItem(MenuPath, true)]
        private static bool CanGenerate()
        {
            return Selection.activeObject is OrpheusAudioKeyManifest;
        }

        [MenuItem(MenuPath)]
        private static void Generate()
        {
            var manifest = Selection.activeObject as OrpheusAudioKeyManifest;
            if (!OrpheusAudioTypedKeyProjection.TryGenerate(manifest))
            {
                throw new InvalidOperationException("Selected Orpheus Audio Key Manifest is invalid.");
            }
        }
    }
}
