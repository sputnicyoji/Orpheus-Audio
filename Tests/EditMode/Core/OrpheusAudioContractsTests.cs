using System;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Orpheus.Audio.Core.Tests
{
    public sealed class OrpheusAudioContractsTests
    {
        [Test]
        public void AudioKey_PreservesUshortIdentityAndInvalidSentinel()
        {
            var invalid = new OrpheusAudioKey(0);
            var first = new OrpheusAudioKey(1);
            var last = new OrpheusAudioKey(ushort.MaxValue);

            Assert.That(invalid.IsValid, Is.False);
            Assert.That(first.IsValid, Is.True);
            Assert.That(last.IsValid, Is.True);
            Assert.That(invalid.Value, Is.EqualTo(0));
            Assert.That(first.Value, Is.EqualTo(1));
            Assert.That(last.Value, Is.EqualTo(ushort.MaxValue));
        }

        [Test]
        public void AudioKey_UsesStableValueEquality()
        {
            var left = new OrpheusAudioKey(2048);
            var equal = new OrpheusAudioKey(2048);
            var different = new OrpheusAudioKey(2049);

            Assert.That(left, Is.EqualTo(equal));
            Assert.That(left.GetHashCode(), Is.EqualTo(equal.GetHashCode()));
            Assert.That(left == equal, Is.True);
            Assert.That(left != different, Is.True);
        }

        [Test]
        public void AudioKey_DefinesNoImplicitIntegralConversionsOrCategoryInference()
        {
            var implicitOperators = typeof(OrpheusAudioKey)
                .GetMethods()
                .Where(method => method.Name == "op_Implicit")
                .ToArray();

            Assert.That(implicitOperators, Is.Empty);
            Assert.That(typeof(OrpheusAudioKey).GetProperty("Category"), Is.Null);
        }

        [Test]
        public void SerializedContracts_UseExactNumericAbi()
        {
            Assert.That((byte)OrpheusPlaybackKind.Invalid, Is.EqualTo(0));
            Assert.That((byte)OrpheusPlaybackKind.OneShot2D, Is.EqualTo(1));
            Assert.That((byte)OrpheusPlaybackKind.OneShot3D, Is.EqualTo(2));
            Assert.That((byte)OrpheusPlaybackKind.GlobalLoop2D, Is.EqualTo(3));
            Assert.That((byte)OrpheusPlaybackKind.Bgm, Is.EqualTo(4));
            Assert.That((byte)OrpheusPlaybackKind.ProfileAmbience, Is.EqualTo(5));

            Assert.That((byte)OrpheusLoadPolicy.Invalid, Is.EqualTo(0));
            Assert.That((byte)OrpheusLoadPolicy.BootstrapTransient, Is.EqualTo(1));
            Assert.That((byte)OrpheusLoadPolicy.ExplicitTransient, Is.EqualTo(2));
            Assert.That((byte)OrpheusLoadPolicy.PersistentStream, Is.EqualTo(3));

            Assert.That((byte)OrpheusCategory.Invalid, Is.EqualTo(0));
            Assert.That((byte)OrpheusCategory.Music, Is.EqualTo(1));
            Assert.That((byte)OrpheusCategory.SfxCombat, Is.EqualTo(2));
            Assert.That((byte)OrpheusCategory.SfxWorld, Is.EqualTo(3));
            Assert.That((byte)OrpheusCategory.SfxUi, Is.EqualTo(4));
            Assert.That((byte)OrpheusCategory.Ambience, Is.EqualTo(5));

            Assert.That((byte)OrpheusBus.Invalid, Is.EqualTo(0));
            Assert.That((byte)OrpheusBus.Master, Is.EqualTo(1));
            Assert.That((byte)OrpheusBus.Music, Is.EqualTo(2));
            Assert.That((byte)OrpheusBus.SfxCombat, Is.EqualTo(3));
            Assert.That((byte)OrpheusBus.SfxWorld, Is.EqualTo(4));
            Assert.That((byte)OrpheusBus.SfxUi, Is.EqualTo(5));
            Assert.That((byte)OrpheusBus.Ambience, Is.EqualTo(6));

            Assert.That((byte)OrpheusBaseState.Invalid, Is.EqualTo(0));
            Assert.That((byte)OrpheusBaseState.Peace, Is.EqualTo(1));
            Assert.That((byte)OrpheusBaseState.Combat, Is.EqualTo(2));

            Assert.That((byte)OrpheusOverlay.None, Is.EqualTo(0));
            Assert.That((byte)OrpheusOverlay.Menu, Is.EqualTo(1));
            Assert.That((byte)OrpheusOverlay.Pause, Is.EqualTo(2));
            Assert.That((byte)(OrpheusOverlay.Menu | OrpheusOverlay.Pause), Is.EqualTo(3));

            Assert.That((byte)OrpheusClipLoadState.Unloaded, Is.EqualTo(0));
            Assert.That((byte)OrpheusClipLoadState.Loading, Is.EqualTo(1));
            Assert.That((byte)OrpheusClipLoadState.Loaded, Is.EqualTo(2));
            Assert.That((byte)OrpheusClipLoadState.Failed, Is.EqualTo(3));
            Assert.That((byte)OrpheusClipLoadState.Invalid, Is.EqualTo(255));
        }

        [Test]
        public void AudioKey_HasFixedTwoByteSequentialLayout()
        {
            var attribute = typeof(OrpheusAudioKey).StructLayoutAttribute;

            Assert.That(attribute.Value, Is.EqualTo(LayoutKind.Sequential));
            Assert.That(attribute.Pack, Is.EqualTo(1));
            Assert.That(attribute.Size, Is.EqualTo(2));
            Assert.That(Marshal.SizeOf<OrpheusAudioKey>(), Is.EqualTo(2));
        }

        [Test]
        public void Overlay_DeclaresFlagsSemantics()
        {
            Assert.That(typeof(OrpheusOverlay).IsDefined(typeof(FlagsAttribute), false), Is.True);
            Assert.That(OrpheusOverlay.Menu | OrpheusOverlay.Pause, Is.EqualTo((OrpheusOverlay)3));
        }
    }
}
