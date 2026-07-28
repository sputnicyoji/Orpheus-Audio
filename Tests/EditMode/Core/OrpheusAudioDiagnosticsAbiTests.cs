using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioDiagnosticsAbiTests
    {
        [Test]
        public void Diagnostics_HasExactPhysicalLayout()
        {
            Assert.That(Marshal.SizeOf<OrpheusAudioDiagnostics>(), Is.EqualTo(224));
            Assert.That(Marshal.SizeOf<OrpheusAudioDiagnosticsCounters>(), Is.EqualTo(176));
            AssertLayout(typeof(OrpheusAudioDiagnostics), 8, 224);
            AssertLayout(typeof(OrpheusAudioDiagnosticsCounters), 8, 176);

            var diagnosticsNames = new[]
            {
                "SchemaVersion", "ProfileId", "ConfiguredRealVoiceLimit", "ConfiguredVirtualVoiceLimit",
                "DesiredBgmKey", "CurrentBgmKey", "TargetBgmKey", "DesiredProfileAmbienceKey",
                "CurrentProfileAmbienceKey", "TargetProfileAmbienceKey", "Lifecycle", "DisableReason",
                "TransportState", "SuspensionReasons", "Readiness", "Overlay", "EffectiveSnapshot",
                "RecoveryPendingReasons", "BaseState", "VoiceBudgetDegraded", "Transient3DActiveCount",
                "Transient2DActiveCount", "FadingCount", "PendingCount", "BgmActiveCount",
                "ProfileAmbienceActiveCount", "GlobalLoopActiveCount", "Reserved0", "Reserved1", "Reserved2",
                "Counters"
            };
            var diagnosticsOffsets = new[]
            {
                0, 4, 8, 12, 16, 18, 20, 22, 24, 26, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37,
                38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48
            };
            for (var index = 0; index < diagnosticsNames.Length; index++)
            {
                Assert.That(
                    Marshal.OffsetOf(typeof(OrpheusAudioDiagnostics), diagnosticsNames[index]).ToInt32(),
                    Is.EqualTo(diagnosticsOffsets[index]),
                    diagnosticsNames[index]);
            }

            var counterNames = new[]
            {
                "PoolCapacityRejected", "Stolen", "CooldownRejected", "PolyphonyRejected", "DistanceRejected",
                "PreReadyRejected", "SuspendedRejected", "UnavailableRejected", "WrongThreadRejected",
                "LoadNotReadyRejected", "LoadFailed", "LoadStalled", "InvalidKeyRejected", "InvalidRawKeyRejected",
                "InvalidPositionRejected", "InvalidValueRejected", "PlaybackKindRejected", "LoopRegistryFull",
                "SetFloatFailed", "RecoveryFailed", "StaleBootstrapTokenRejected", "UnexpectedException"
            };
            for (var index = 0; index < counterNames.Length; index++)
            {
                Assert.That(
                    Marshal.OffsetOf(typeof(OrpheusAudioDiagnosticsCounters), counterNames[index]).ToInt32(),
                    Is.EqualTo(index * sizeof(ulong)),
                    counterNames[index]);
            }
        }

        [Test]
        public void Diagnostics_StorageIsPublicReadonlyAndUnmanaged()
        {
            var diagnosticsFields = typeof(OrpheusAudioDiagnostics)
                .GetFields(BindingFlags.Instance | BindingFlags.Public);
            var counterFields = typeof(OrpheusAudioDiagnosticsCounters)
                .GetFields(BindingFlags.Instance | BindingFlags.Public);

            Assert.That(diagnosticsFields.Length, Is.EqualTo(31));
            Assert.That(counterFields.Length, Is.EqualTo(22));
            Assert.That(diagnosticsFields.All(field => field.IsInitOnly), Is.True);
            Assert.That(counterFields.All(field => field.IsInitOnly && field.FieldType == typeof(ulong)), Is.True);
            Assert.That(ContainsManagedReference(typeof(OrpheusAudioDiagnostics)), Is.False);
        }

        [Test]
        public void LifecycleContracts_HaveExactByteAbi()
        {
            AssertByteEnum<OrpheusAudioLifecycle>(0, 1, 2, 3);
            AssertByteEnum<OrpheusTransportState>(0, 1, 2, 3, 4);
            AssertByteEnum<OrpheusSuspensionReason>(0, 1, 2, 4);
            Assert.That((byte)(OrpheusSuspensionReason.FocusLost | OrpheusSuspensionReason.ApplicationPaused |
                               OrpheusSuspensionReason.ListenerMissing), Is.EqualTo(7));
            AssertByteEnum<OrpheusReadiness>(0, 1, 2, 4, 8);
            AssertByteEnum<OrpheusEffectiveSnapshot>(0, 1, 2, 3, 4);
            AssertByteEnum<OrpheusRecoveryPendingReason>(0, 1, 2);
            Assert.That((byte)(OrpheusRecoveryPendingReason.ExternalConfiguration |
                               OrpheusRecoveryPendingReason.SelfResetNotification), Is.EqualTo(3));
            AssertByteEnum<OrpheusAudioInitErrorCode>(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
            AssertByteEnum<OrpheusCompleteHostReadyResult>(0, 1, 2, 3, 4, 5, 6, 7);
            AssertByteEnum<OrpheusAudioPrepareResult>(0, 1, 2, 3, 4, 5, 6);
            AssertByteEnum<OrpheusAudioDisableReason>(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);
        }

        [Test]
        public void InitResult_ExposesOnlyReadonlyObservationSurface()
        {
            var type = typeof(OrpheusAudioInitResult);
            Assert.That(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public), Is.Empty);
            Assert.That(type.GetMethods(BindingFlags.Static | BindingFlags.Public), Is.Empty);
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
            Assert.That(properties.Select(property => property.Name), Is.EquivalentTo(new[]
            {
                "Success", "ErrorCode", "RelatedKey", "RelatedIndex"
            }));
            Assert.That(properties.All(property => property.CanRead && !property.CanWrite), Is.True);
        }

        private static void AssertLayout(Type type, int pack, int size)
        {
            var layout = type.StructLayoutAttribute;
            Assert.That(layout.Value, Is.EqualTo(LayoutKind.Sequential));
            Assert.That(layout.Pack, Is.EqualTo(pack));
            Assert.That(layout.Size, Is.EqualTo(size));
        }

        private static void AssertByteEnum<T>(params byte[] expectedValues) where T : struct, Enum
        {
            Assert.That(Enum.GetUnderlyingType(typeof(T)), Is.EqualTo(typeof(byte)));
            var actual = Enum.GetValues(typeof(T)).Cast<T>().Select(value => Convert.ToByte(value)).ToArray();
            Assert.That(actual, Is.EqualTo(expectedValues), typeof(T).Name);
        }

        private static bool ContainsManagedReference(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type.IsPointer)
            {
                return false;
            }

            if (!type.IsValueType)
            {
                return true;
            }

            return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(field => ContainsManagedReference(field.FieldType));
        }
    }
}
