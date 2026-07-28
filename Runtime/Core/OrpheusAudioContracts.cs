using System;

namespace Orpheus.Audio.Core
{
    internal enum OrpheusPlaybackKind : byte
    {
        Invalid = 0,
        OneShot2D = 1,
        OneShot3D = 2,
        GlobalLoop2D = 3,
        Bgm = 4,
        ProfileAmbience = 5
    }

    internal enum OrpheusLoadPolicy : byte
    {
        Invalid = 0,
        BootstrapTransient = 1,
        ExplicitTransient = 2,
        PersistentStream = 3
    }

    internal enum OrpheusCategory : byte
    {
        Invalid = 0,
        Music = 1,
        SfxCombat = 2,
        SfxWorld = 3,
        SfxUi = 4,
        Ambience = 5
    }

    public enum OrpheusBus : byte
    {
        Invalid = 0,
        Master = 1,
        Music = 2,
        SfxCombat = 3,
        SfxWorld = 4,
        SfxUi = 5,
        Ambience = 6
    }

    public enum OrpheusBaseState : byte
    {
        Invalid = 0,
        Peace = 1,
        Combat = 2
    }

    [Flags]
    public enum OrpheusOverlay : byte
    {
        None = 0,
        Menu = 1,
        Pause = 2
    }

    public enum OrpheusClipLoadState : byte
    {
        Unloaded = 0,
        Loading = 1,
        Loaded = 2,
        Failed = 3,
        Invalid = 255
    }
}
