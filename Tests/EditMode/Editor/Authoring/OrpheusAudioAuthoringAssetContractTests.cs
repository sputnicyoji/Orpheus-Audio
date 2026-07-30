using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Orpheus.Audio.Core;
using UnityEditor;
using UnityEngine;

namespace Orpheus.Audio.Editor.Tests
{
    public sealed class OrpheusAudioAuthoringAssetContractTests
    {
        [Test]
        public void ModuleRecipe_HasExactSerializedShapeAndDefaults()
        {
            AssertSerializedFieldsInDeclarationOrder(
                typeof(OrpheusAudioModuleRecipe),
                "_schemaVersion",
                "_moduleId",
                "_events");

            var recipe = ScriptableObject.CreateInstance<OrpheusAudioModuleRecipe>();
            try
            {
                Assert.That(GetField<int>(recipe, "_schemaVersion"), Is.EqualTo(1));
                Assert.That(GetField<string>(recipe, "_moduleId"), Is.EqualTo(string.Empty));
                Assert.That(
                    GetField<OrpheusAudioModuleEventRecipe[]>(recipe, "_events"),
                    Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(recipe);
            }
        }

        [Test]
        public void EventRecipe_HasExactSerializedShapeAndEmptyMetadataDefaults()
        {
            Assert.That(
                typeof(OrpheusAudioModuleEventRecipe).IsDefined(
                    typeof(SerializableAttribute),
                    false),
                Is.True);
            AssertSerializedFieldsInDeclarationOrder(
                typeof(OrpheusAudioModuleEventRecipe),
                "_symbol",
                "_playbackKind",
                "_category",
                "_loadPolicy",
                "_clips",
                "_volumeMin",
                "_volumeMax",
                "_pitchMin",
                "_pitchMax",
                "_priority",
                "_polyphonyCap",
                "_cooldownSeconds",
                "_minimumDistance",
                "_maximumDistance",
                "_rolloffMode",
                "_profileHint",
                "_candidateContentBankId");

            var value = default(OrpheusAudioModuleEventRecipe);
            Assert.That(GetField<string>(value, "_symbol"), Is.Null);
            Assert.That(GetField<AudioClip[]>(value, "_clips"), Is.Null);
            Assert.That(GetField<string>(value, "_profileHint"), Is.Null);
            Assert.That(GetField<string>(value, "_candidateContentBankId"), Is.Null);

            var captured = value.CaptureValue();
            Assert.That(captured.Symbol, Is.EqualTo(string.Empty));
            Assert.That(captured.HasClipStorage, Is.False);
            Assert.That(captured.ProfileHint, Is.EqualTo(string.Empty));
            Assert.That(captured.CandidateContentBankId, Is.EqualTo(string.Empty));
        }

        [Test]
        public void AuthoringProfile_HasExactSerializedShapeAndDefaults()
        {
            AssertSerializedFieldsInDeclarationOrder(
                typeof(OrpheusAudioAuthoringProfile),
                "_schemaVersion",
                "_keyManifest",
                "_moduleRecipes",
                "_catalog",
                "_generatedRoot");

            var profile = ScriptableObject.CreateInstance<OrpheusAudioAuthoringProfile>();
            try
            {
                Assert.That(GetField<int>(profile, "_schemaVersion"), Is.EqualTo(1));
                Assert.That(
                    GetField<OrpheusAudioModuleRecipe[]>(profile, "_moduleRecipes"),
                    Is.Empty);
                Assert.That(
                    GetField<string>(profile, "_generatedRoot"),
                    Is.EqualTo("Assets/Audio/OrpheusGenerated"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ValidationProfile_AddsOnlyEnrollmentFieldsWithEmptyIdentity()
        {
            var names = GetSerializedFields(typeof(OrpheusAudioValidationProfile))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(names, Is.EqualTo(new[]
            {
                "_authoringEnrollmentGuid",
                "_authoringProfile",
                "_catalog",
                "_enabled",
                "_keyManifest",
                "_listenerScenes",
                "_runtimeHostPrefab",
                "_schemaVersion",
                "_settings"
            }));

            var profile = ScriptableObject.CreateInstance<OrpheusAudioValidationProfile>();
            try
            {
                Assert.That(
                    GetField<OrpheusAudioAuthoringProfile>(profile, "_authoringProfile"),
                    Is.Null);
                Assert.That(
                    GetField<string>(profile, "_authoringEnrollmentGuid"),
                    Is.EqualTo(string.Empty));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void SerializedEnums_HaveExactNumericValues()
        {
            Assert.That((byte)OrpheusPlaybackKind.Invalid, Is.EqualTo(0));
            Assert.That((byte)OrpheusPlaybackKind.OneShot2D, Is.EqualTo(1));
            Assert.That((byte)OrpheusPlaybackKind.OneShot3D, Is.EqualTo(2));
            Assert.That((byte)OrpheusPlaybackKind.GlobalLoop2D, Is.EqualTo(3));
            Assert.That((byte)OrpheusPlaybackKind.Bgm, Is.EqualTo(4));
            Assert.That((byte)OrpheusPlaybackKind.ProfileAmbience, Is.EqualTo(5));
            Assert.That((byte)OrpheusCategory.Invalid, Is.EqualTo(0));
            Assert.That((byte)OrpheusCategory.Music, Is.EqualTo(1));
            Assert.That((byte)OrpheusCategory.SfxCombat, Is.EqualTo(2));
            Assert.That((byte)OrpheusCategory.SfxWorld, Is.EqualTo(3));
            Assert.That((byte)OrpheusCategory.SfxUi, Is.EqualTo(4));
            Assert.That((byte)OrpheusCategory.Ambience, Is.EqualTo(5));
            Assert.That((byte)OrpheusLoadPolicy.Invalid, Is.EqualTo(0));
            Assert.That((byte)OrpheusLoadPolicy.BootstrapTransient, Is.EqualTo(1));
            Assert.That((byte)OrpheusLoadPolicy.ExplicitTransient, Is.EqualTo(2));
            Assert.That((byte)OrpheusLoadPolicy.PersistentStream, Is.EqualTo(3));
            Assert.That((byte)OrpheusRolloffMode.Invalid, Is.EqualTo(0));
            Assert.That((byte)OrpheusRolloffMode.Logarithmic, Is.EqualTo(1));
            Assert.That((byte)OrpheusRolloffMode.Linear, Is.EqualTo(2));
        }

        [Test]
        public void CompilerShell_IsInternalAndRejectsAsNotImplemented()
        {
            Assert.That(typeof(OrpheusAudioAuthoringCompiler).IsPublic, Is.False);
            Assert.That(typeof(OrpheusAudioAuthoringCompileMode).IsPublic, Is.False);
            Assert.That(typeof(OrpheusAudioAuthoringCompileStatus).IsPublic, Is.False);
            Assert.That(typeof(OrpheusAudioAuthoringCompileResult).IsPublic, Is.False);

            var result = OrpheusAudioAuthoringCompiler.Run(
                null,
                OrpheusAudioAuthoringCompileMode.Analyze,
                BuildTargetGroup.Standalone);

            Assert.That(result.Status, Is.EqualTo(OrpheusAudioAuthoringCompileStatus.Rejected));
            Assert.That(result.RejectedCount, Is.EqualTo(1));
            Assert.That(result.ErrorCount, Is.EqualTo(1));
            Assert.That(result.ReportPath, Is.EqualTo(string.Empty));
        }

        private static void AssertSerializedFieldsInDeclarationOrder(
            Type owner,
            params string[] expected)
        {
            Assert.That(
                GetSerializedFields(owner)
                    .OrderBy(field => field.MetadataToken)
                    .Select(field => field.Name)
                    .ToArray(),
                Is.EqualTo(expected));
        }

        private static FieldInfo[] GetSerializedFields(Type owner)
        {
            return owner
                .GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly)
                .Where(field => field.IsDefined(typeof(SerializeField), false))
                .ToArray();
        }

        private static T GetField<T>(object owner, string fieldName)
        {
            var field = owner.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing field " + fieldName);
            return (T)field.GetValue(owner);
        }
    }
}
