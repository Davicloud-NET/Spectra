using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

// The rotation part of the way from one unit rotation to another, along the
// shortest arc between them.
internal static class RotationArc
{
    // Not Quaternion.Slerp: it blends two close rotations in a straight line.
    // That is right between them and wrong past either end, where a small
    // turn times a large alpha is a large turn.
    public static Quaternion At(Quaternion from, Quaternion to, float alpha)
    {
        // The goal itself, with no rounding from the trip through the angle.
        if (alpha == 1f)
            return to;

        Quaternion step = Quaternion.Conjugate(from) * to;

        // A quaternion and its negative are one rotation. The one with w
        // above zero turns the short way.
        if (step.W < 0f)
            step = Quaternion.Negate(step);

        var axis = new Vector3(step.X, step.Y, step.Z);
        float sine = axis.Length();
        if (!(sine > 0f))
            return from;

        float half = MathF.Atan2(sine, step.W) * alpha;
        return from * new Quaternion(axis * (MathF.Sin(half) / sine), MathF.Cos(half));
    }
}
