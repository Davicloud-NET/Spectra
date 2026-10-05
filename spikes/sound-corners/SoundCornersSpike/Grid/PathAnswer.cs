using System.Numerics;

namespace SoundCornersSpike.Grid;

// What a flood says about one sound.
internal struct PathAnswer
{
    public bool Found;

    // The flood has the source in straight sight of the listener.
    public bool Direct;

    public float Length;

    // Unit vector from the listener along the path's first leg.
    public Vector3 FirstLeg;

    // Where the first leg ends.
    public Vector3 BendPoint;

    // Everything the path turns by, in degrees, summed corner by corner.
    public float BendDegrees;

    // The angle between the straight line and the path's first leg, plus the
    // same at the source, both found with rays. Only set by a refined read.
    public float EndsBendDegrees;

    public int Legs;

    // Where the engine would play the sound from.
    public readonly Vector3 ApparentPosition(Vector3 listener) => listener + (FirstLeg * Length);
}
