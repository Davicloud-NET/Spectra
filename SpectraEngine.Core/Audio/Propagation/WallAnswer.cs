using System;
using System.Numerics;

namespace SpectraEngine.Core.Audio.Propagation;

// What the walls made of one sound when it was last traced, and what it was
// traced for.
internal struct WallAnswer
{
    // The node the sound sits on. Empty for a sound on none.
    public Guid Body;

    // How many lines the answer was traced along. Zero until there is one.
    public int Lines;

    // The two factors the walls leave.
    public float Gain;
    public float GainHf;

    // Where the sound and the listener were, and in which world.
    public Vector3 Sound;
    public Vector3 Listener;
    public long Revision;

    // The clock's reading at the trace.
    public long TracedAt;
}
