using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using Orpheus.Audio.Core;
using Orpheus.ContractRegression;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioPublicContractTests
    {
        private const string PublicApiBaselinePath =
            "Packages/com.orpheus.audio/Tests/Baselines/PublicApi.v1.txt";
        private const string SerializationBaselinePath =
            "Packages/com.orpheus.audio/Tests/Baselines/SerializationAbi.v1.txt";
        private const string GeneratedKeyGoldenPath =
            "Packages/com.orpheus.audio/Tests/Baselines/GeneratedKeys.v1.golden.cs.txt";
        private const string GeneratedKeyConsumerProjectionPath =
            "Packages/com.orpheus.audio/Tests/ContractConsumer/Generated/Projection/" +
            "OrpheusAudioKeys.g.cs";
        private const string AndroidConsumerProbeAssemblyName =
            "Orpheus.Audio.Verification.PublicContractConsumer";
        private const string TestOnlyConsumerAssemblyName =
            "Orpheus.Audio.ContractConsumer.Runtime";

        private static readonly Type[] SerializedOwners =
        {
            typeof(OrpheusAudioEvent),
            typeof(OrpheusAudioCatalog),
            typeof(OrpheusAudioSettings),
            typeof(OrpheusAudioRuntimeHost),
            typeof(OrpheusAudioSourceBank),
            typeof(OrpheusAudioKeyManifest),
            typeof(OrpheusAudioValidationProfile)
        };

        [Test]
        public void PublicApi_MatchesV1Baseline()
        {
            var actual = BuildPublicApiBaseline();
            Assert.That(actual, Is.EqualTo(ReadBaselineLines(PublicApiBaselinePath)));
        }

        [Test]
        public void ExportDiscovery_IncludesNonCanonicalNamespaces()
        {
            var exportedTypes = new List<Type>();

            AddExportedTypes(
                exportedTypes,
                typeof(OrpheusNonCanonicalExportProbe).Assembly);

            Assert.That(exportedTypes, Does.Contain(typeof(OrpheusNonCanonicalExportProbe)));
        }

        [Test]
        public void AndroidConsumerProbe_UsesTheNormalPlayerCompilationGraph()
        {
            var playerAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            var playerAssemblyNames = playerAssemblies
                .Select(assembly => assembly.name)
                .ToArray();

            Assert.That(playerAssemblyNames, Does.Contain(AndroidConsumerProbeAssemblyName));
            Assert.That(
                ReadDefineConstraints(AndroidConsumerProbeAssemblyName),
                Does.Not.Contain("UNITY_INCLUDE_TESTS"));
            Assert.That(
                ReadDefineConstraints(TestOnlyConsumerAssemblyName),
                Does.Contain("UNITY_INCLUDE_TESTS"));
        }

        private static string[] ReadDefineConstraints(string assemblyName)
        {
            var path = CompilationPipeline
                .GetAssemblyDefinitionFilePathFromAssemblyName(assemblyName);
            Assert.That(path, Is.Not.Null.And.Not.Empty);

            var definition = JsonUtility.FromJson<AssemblyDefinitionContract>(
                File.ReadAllText(path));
            return definition.defineConstraints ?? Array.Empty<string>();
        }

        [Serializable]
        private sealed class AssemblyDefinitionContract
        {
            public string[] defineConstraints;
        }

        [Test]
        public void SerializedFields_MatchV1AbiBaseline()
        {
            var actual = BuildSerializationAbiBaseline();
            Assert.That(actual, Is.EqualTo(ReadBaselineLines(SerializationBaselinePath)));
        }

        internal static string[] BuildSerializationAbiBaseline()
        {
            var actual = new List<string>();
            var serializedEnums = new HashSet<Type>();
            for (var ownerIndex = 0; ownerIndex < SerializedOwners.Length; ownerIndex++)
            {
                var owner = SerializedOwners[ownerIndex];
                actual.Add(
                    "A|" + owner.FullName + "|guid=" +
                    GetMonoScriptGuid(owner) + "|schema=" +
                    GetSchemaVersion(owner));

                AddSerializedFields(actual, serializedEnums, owner);
                if (owner == typeof(OrpheusAudioKeyManifest))
                {
                    AddSerializedFields(
                        actual,
                        serializedEnums,
                        typeof(OrpheusAudioKeyManifestEntry));
                }
            }

            foreach (var enumType in serializedEnums)
            {
                var names = Enum.GetNames(enumType);
                var values = Enum.GetValues(enumType);
                for (var index = 0; index < names.Length; index++)
                {
                    actual.Add(
                        "SE|" + enumType.FullName +
                        "|" + names[index] +
                        "=" + FormatEnumValue(
                            values.GetValue(index),
                            Enum.GetUnderlyingType(enumType)));
                }
            }

            actual.Sort(StringComparer.Ordinal);
            return actual.ToArray();
        }

        internal static string[] BuildPublicApiBaseline()
        {
            var exportedTypes = new List<Type>();
            AddExportedTypes(
                exportedTypes,
                typeof(OrpheusAudioManager).Assembly);
            AddExportedTypes(
                exportedTypes,
                typeof(OrpheusAudioKey).Assembly);
            AddExportedTypes(
                exportedTypes,
                typeof(OrpheusAudioKeyManifest).Assembly);

            var actual = new List<string>();
            for (var index = 0; index < exportedTypes.Count; index++)
            {
                AddPublicType(actual, exportedTypes[index]);
            }

            actual.Sort(StringComparer.Ordinal);
            return actual.ToArray();
        }

        private static void AddSerializedFields(
            ICollection<string> destination,
            ISet<Type> serializedEnums,
            Type owner)
        {
            var fields = owner.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
            for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                var field = fields[fieldIndex];
                if (!field.IsDefined(typeof(SerializeField), false))
                {
                    continue;
                }

                destination.Add(
                    "S|" + owner.FullName + "|" + field.Name + "|" +
                    GetTypeName(field.FieldType));
                if (field.FieldType.IsEnum)
                {
                    serializedEnums.Add(field.FieldType);
                }
            }
        }

        [Test]
        public void GeneratedKeys_MatchV1Golden()
        {
            var manifest = OrpheusAudioEditorContractTests.CreateManifest(
                OrpheusAudioEditorContractTests.CreateEntry(
                    300,
                    "RetiredCue",
                    OrpheusAudioKeyStatus.Retired),
                OrpheusAudioEditorContractTests.CreateEntry(
                    100,
                    "ActiveCue",
                    OrpheusAudioKeyStatus.Active),
                OrpheusAudioEditorContractTests.CreateEntry(
                    200,
                    "ReservedCue",
                    OrpheusAudioKeyStatus.Reserved));

            try
            {
                Assert.That(
                    OrpheusAudioTypedKeyProjection.TryCreateExpectedSource(
                        manifest,
                        out var source,
                        out _),
                    Is.True);
                Assert.That(
                    NormalizeNewlines(source),
                    Is.EqualTo(ReadTextAsset(GeneratedKeyGoldenPath)));
                Assert.That(
                    NormalizeNewlines(File.ReadAllText(GeneratedKeyConsumerProjectionPath)),
                    Is.EqualTo(ReadTextAsset(GeneratedKeyGoldenPath)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(manifest);
            }
        }

        private static void AddExportedTypes(
            ICollection<Type> destination,
            System.Reflection.Assembly assembly)
        {
            var types = assembly.GetExportedTypes();
            for (var index = 0; index < types.Length; index++)
            {
                destination.Add(types[index]);
            }
        }

        private static void AddPublicType(ICollection<string> destination, Type type)
        {
            var interfaces = type.GetInterfaces()
                .Select(GetTypeName)
                .OrderBy(name => name, StringComparer.Ordinal);
            destination.Add(
                "T|" + type.FullName +
                "|kind=" + GetTypeKind(type) +
                "|abstract=" + FormatBoolean(type.IsAbstract) +
                "|sealed=" + FormatBoolean(type.IsSealed) +
                "|base=" + GetTypeName(type.BaseType) +
                "|interfaces=" + string.Join(",", interfaces) +
                (type.IsEnum ? "|underlying=" + GetTypeName(Enum.GetUnderlyingType(type)) : ""));

            if (type.IsEnum)
            {
                AddEnumValues(destination, type);
                return;
            }

            if (type.IsValueType)
            {
                AddStructLayout(destination, type);
            }

            AddPublicConstructors(destination, type);
            AddPublicMethods(destination, type);
            AddPublicProperties(destination, type);
            AddPublicFields(destination, type);
            AddPublicEvents(destination, type);
        }

        private static void AddStructLayout(ICollection<string> destination, Type type)
        {
            var layout = type.StructLayoutAttribute;
            destination.Add(
                "L|" + type.FullName +
                "|kind=" + layout.Value +
                "|pack=" + layout.Pack.ToString(CultureInfo.InvariantCulture) +
                "|size=" + layout.Size.ToString(CultureInfo.InvariantCulture));

            var fields = type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly)
                .OrderBy(field => field.MetadataToken)
                .ToArray();
            for (var index = 0; index < fields.Length; index++)
            {
                destination.Add(
                    "LF|" + type.FullName +
                    "|order=" + index.ToString(CultureInfo.InvariantCulture) +
                    "|name=" + fields[index].Name +
                    "|type=" + GetTypeName(fields[index].FieldType));
            }
        }

        private static void AddEnumValues(ICollection<string> destination, Type type)
        {
            var names = Enum.GetNames(type);
            var values = Enum.GetValues(type);
            for (var index = 0; index < names.Length; index++)
            {
                destination.Add(
                    "E|" + type.FullName +
                    "|" + names[index] +
                    "=" + FormatEnumValue(values.GetValue(index), Enum.GetUnderlyingType(type)));
            }
        }

        private static void AddPublicConstructors(ICollection<string> destination, Type owner)
        {
            var constructors = owner.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            for (var index = 0; index < constructors.Length; index++)
            {
                destination.Add(
                    "C|" + owner.FullName + "|" +
                    FormatParameters(constructors[index].GetParameters()));
            }
        }

        private static void AddPublicMethods(ICollection<string> destination, Type owner)
        {
            var methods = owner.GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
            for (var index = 0; index < methods.Length; index++)
            {
                var method = methods[index];
                if (method.IsSpecialName &&
                    !method.Name.StartsWith("op_", StringComparison.Ordinal))
                {
                    continue;
                }

                destination.Add(
                    "M|" + owner.FullName + "." + method.Name + "|" +
                    (method.IsStatic ? "static" : "instance") +
                    "|generic=" + method.GetGenericArguments().Length.ToString(CultureInfo.InvariantCulture) +
                    "|params=" + FormatParameters(method.GetParameters()) +
                    "|return=" + GetTypeName(method.ReturnType));
            }
        }

        private static void AddPublicProperties(ICollection<string> destination, Type owner)
        {
            var properties = owner.GetProperties(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
            for (var index = 0; index < properties.Length; index++)
            {
                var property = properties[index];
                var getter = property.GetGetMethod(false);
                var setter = property.GetSetMethod(false);
                destination.Add(
                    "P|" + owner.FullName + "." + property.Name +
                    "|type=" + GetTypeName(property.PropertyType) +
                    "|index=" + FormatParameters(property.GetIndexParameters()) +
                    "|get=" + FormatBoolean(getter != null) +
                    "|set=" + FormatBoolean(setter != null) +
                    "|static=" + FormatBoolean(
                        (getter != null && getter.IsStatic) || (setter != null && setter.IsStatic)));
            }
        }

        private static void AddPublicFields(ICollection<string> destination, Type owner)
        {
            var fields = owner.GetFields(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
            for (var index = 0; index < fields.Length; index++)
            {
                var field = fields[index];
                destination.Add(
                    "F|" + owner.FullName + "." + field.Name +
                    "|type=" + GetTypeName(field.FieldType) +
                    "|static=" + FormatBoolean(field.IsStatic) +
                    "|readonly=" + FormatBoolean(field.IsInitOnly) +
                    "|const=" + FormatBoolean(field.IsLiteral) +
                    (field.IsLiteral
                        ? "|value=" + FormatConstant(field.GetRawConstantValue())
                        : ""));
            }
        }

        private static void AddPublicEvents(ICollection<string> destination, Type owner)
        {
            var events = owner.GetEvents(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.DeclaredOnly);
            for (var index = 0; index < events.Length; index++)
            {
                var eventInfo = events[index];
                var addMethod = eventInfo.GetAddMethod(false);
                destination.Add(
                    "V|" + owner.FullName + "." + eventInfo.Name +
                    "|type=" + GetTypeName(eventInfo.EventHandlerType) +
                    "|static=" + FormatBoolean(addMethod != null && addMethod.IsStatic));
            }
        }

        private static string FormatParameters(ParameterInfo[] parameters)
        {
            var builder = new StringBuilder();
            for (var index = 0; index < parameters.Length; index++)
            {
                if (index != 0)
                {
                    builder.Append(',');
                }

                var parameter = parameters[index];
                builder.Append(index.ToString(CultureInfo.InvariantCulture));
                builder.Append(':');
                builder.Append(parameter.Name);
                builder.Append(':');
                builder.Append(GetParameterModifier(parameter));
                builder.Append(GetTypeName(
                    parameter.ParameterType.IsByRef
                        ? parameter.ParameterType.GetElementType()
                        : parameter.ParameterType));
                if (parameter.IsOptional)
                {
                    builder.Append(":default=");
                    builder.Append(FormatConstant(parameter.DefaultValue));
                }
            }

            return builder.ToString();
        }

        private static string GetParameterModifier(ParameterInfo parameter)
        {
            var parameterType = parameter.ParameterType;
            if (!parameterType.IsByRef)
            {
                return "";
            }

            if (parameter.IsOut)
            {
                return "out ";
            }

            return parameter.IsIn ? "in " : "ref ";
        }

        private static string GetTypeName(Type type)
        {
            if (type == null)
            {
                return "none";
            }

            if (type.IsArray)
            {
                return GetTypeName(type.GetElementType()) + "[]";
            }

            if (type.IsGenericType)
            {
                var genericName = type.GetGenericTypeDefinition().FullName;
                genericName = genericName.Substring(0, genericName.IndexOf('`'));
                return genericName + "<" +
                       string.Join(",", type.GetGenericArguments().Select(GetTypeName)) + ">";
            }

            return type.FullName;
        }

        private static string GetTypeKind(Type type)
        {
            if (type.IsEnum)
            {
                return "enum";
            }

            if (type.IsValueType)
            {
                return "struct";
            }

            return type.IsInterface ? "interface" : "class";
        }

        private static string GetSchemaVersion(Type owner)
        {
            var field = owner.GetField(
                "_schemaVersion",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null)
            {
                return "none";
            }

            UnityEngine.Object instance;
            GameObject gameObject = null;
            if (typeof(ScriptableObject).IsAssignableFrom(owner))
            {
                instance = ScriptableObject.CreateInstance(owner);
            }
            else
            {
                gameObject = new GameObject("OrpheusContractSchemaProbe");
                instance = gameObject.AddComponent(owner);
            }

            try
            {
                return Convert.ToString(
                    field.GetValue(instance),
                    CultureInfo.InvariantCulture);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject != null ? gameObject : instance);
            }
        }

        private static string GetMonoScriptGuid(Type owner)
        {
            UnityEngine.Object instance;
            GameObject gameObject = null;
            MonoScript script;
            if (typeof(ScriptableObject).IsAssignableFrom(owner))
            {
                instance = ScriptableObject.CreateInstance(owner);
                script = MonoScript.FromScriptableObject((ScriptableObject)instance);
            }
            else
            {
                gameObject = new GameObject("OrpheusContractScriptProbe");
                instance = gameObject.AddComponent(owner);
                script = MonoScript.FromMonoBehaviour((MonoBehaviour)instance);
            }

            try
            {
                Assert.That(script, Is.Not.Null, "Missing MonoScript for " + owner.FullName);
                var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
                Assert.That(guid, Does.Match("^[0-9a-f]{32}$"));
                return guid;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject != null ? gameObject : instance);
            }
        }

        private static string FormatEnumValue(object value, Type underlyingType)
        {
            return underlyingType == typeof(sbyte) ||
                   underlyingType == typeof(short) ||
                   underlyingType == typeof(int) ||
                   underlyingType == typeof(long)
                ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture)
                : Convert.ToUInt64(value, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatConstant(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (ReferenceEquals(value, Missing.Value))
            {
                return "missing";
            }

            if (value is string text)
            {
                return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }

            if (value is char character)
            {
                return "'" + character + "'";
            }

            if (value is bool boolean)
            {
                return FormatBoolean(boolean);
            }

            if (value.GetType().IsEnum)
            {
                return FormatEnumValue(value, Enum.GetUnderlyingType(value.GetType()));
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string FormatBoolean(bool value)
        {
            return value ? "true" : "false";
        }

        private static string[] ReadBaselineLines(string path)
        {
            return ReadTextAsset(path)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && line[0] != '#')
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToArray();
        }

        private static string ReadTextAsset(string path)
        {
            var baseline = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(baseline, Is.Not.Null, "Missing checked-in contract baseline: " + path);
            return NormalizeNewlines(baseline.text);
        }

        private static string NormalizeNewlines(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }

    }
}

namespace Orpheus.ContractRegression
{
    public sealed class OrpheusNonCanonicalExportProbe
    {
    }
}
