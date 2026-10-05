using System;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// What is worked out for a sound on its way to the listener. Each part can be
/// switched off by itself, for one sound and for the whole engine.
/// </summary>
[Flags]
public enum SoundSimulation
{
    /// <summary>Nothing: the sound plays in both ears, as loud everywhere, at its own pitch.</summary>
    None = 0,

    /// <summary>The sound is heard from where it stands. Off, it plays in both ears.</summary>
    Placed = 1,

    /// <summary>The sound gets quieter with distance. Off, it is as loud everywhere as it is up close.</summary>
    Fades = 2,

    /// <summary>What lies between the sound and the listener makes it quieter and duller.</summary>
    // Read by WallPropagation. DirectPropagation knows no walls.
    Walls = 4,

    /// <summary>The pitch rises as the sound and the listener close in and falls as they part.</summary>
    Doppler = 8,

    /// <summary>All four.</summary>
    All = Placed | Fades | Walls | Doppler,
}
