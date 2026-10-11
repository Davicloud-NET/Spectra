using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

// Brings a vector or a quaternion to unit length. The largest component is
// divided out first: the square of a very long or a very short length is past
// what a float holds, and the plain way would read it as no length.
internal static class UnitLength
{
    // False for no length and for a component that is not finite.
    public static bool TryOf(Vector3 vector, out Vector3 unit)
    {
        float largest = MathF.Max(MathF.Abs(vector.X), MathF.Max(MathF.Abs(vector.Y), MathF.Abs(vector.Z)));
        if (!IsLength(largest))
        {
            unit = default;
            return false;
        }

        unit = Vector3.Normalize(vector / largest);
        return true;
    }

    // False for no length and for a component that is not finite.
    public static bool TryOf(Quaternion rotation, out Quaternion unit)
    {
        float largest = MathF.Max(
            MathF.Max(MathF.Abs(rotation.X), MathF.Abs(rotation.Y)),
            MathF.Max(MathF.Abs(rotation.Z), MathF.Abs(rotation.W)));
        if (!IsLength(largest))
        {
            unit = default;
            return false;
        }

        unit = Quaternion.Normalize(new Quaternion(
            rotation.X / largest, rotation.Y / largest, rotation.Z / largest, rotation.W / largest));
        return true;
    }

    // Written this way round so a NaN is no length.
    private static bool IsLength(float largest) => largest > 0f && float.IsFinite(largest);
}
