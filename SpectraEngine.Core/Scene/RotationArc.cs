using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

// The rotation part of the way from one unit rotation to another, as Roblox
// turns it: along the shortest arc between them.
internal static class RotationArc
{
    // Above this dot of the two quaternions, about 2.2 degrees apart, Roblox
    // blends them in a straight line and brings the result back to unit
    // length. Between the two that is the arc to seven digits. Past either
    // end it turns less. Found by asking Studio, to ten digits of the dot.
    private const float StraightAboveDot = 0.99981f;

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

        // The straight blend is (axis * alpha, 1 - (1 - w) * alpha) before it
        // is brought to unit length. Its half angle is read off those two,
        // which no alpha can overflow. 1 - w is written as sine squared over
        // 1 + w, which keeps its digits where w is all but 1.
        float half = step.W > StraightAboveDot
            ? MathF.Atan2(sine * alpha, 1f - (sine * sine / (1f + step.W) * alpha))
            : MathF.Atan2(sine, step.W) * alpha;
        return from * new Quaternion(axis * (MathF.Sin(half) / sine), MathF.Cos(half));
    }
}
