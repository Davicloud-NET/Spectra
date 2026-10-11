using System.Numerics;

namespace SpectraEngine.Core.Scene;

// Rotations built from directions, for a frame that looks along its -Z with
// +X right and +Y up. No input gives a NaN.
internal static class LookRotation
{
    // Below this sine of the angle between them, two directions count as one line.
    private const float ParallelSine = 1e-5f;

    // The rotation that looks along a direction: right is look cross up, up
    // is right cross look. A direction of no length gives no rotation.
    public static Quaternion Along(Vector3 direction, Vector3 up)
    {
        if (!UnitLength.TryOf(direction, out Vector3 look))
            return Quaternion.Identity;

        Vector3 right = RightOf(look, up);
        return FromColumns(right, Vector3.Cross(right, look), -look);
    }

    // The rotation whose first column runs along vX and whose third is
    // vX cross vY. Columns that span no plane give no rotation.
    public static Quaternion FromAxes(Vector3 vX, Vector3 vY)
    {
        if (!UnitLength.TryOf(vX, out Vector3 x) || !TryUnitCross(x, vY, out Vector3 z))
            return Quaternion.Identity;

        return FromColumns(x, Vector3.Cross(z, x), z);
    }

    // A look along up leaves look cross up with no direction. The frame is then
    // the one +Y as up gives, and with the look along Y as well the right
    // vector stays on +X: the identity pitched a quarter turn up or down.
    private static Vector3 RightOf(Vector3 look, Vector3 up)
    {
        if (TryUnitCross(look, up, out Vector3 right) || TryUnitCross(look, Vector3.UnitY, out right))
            return right;

        // The look is along Y only to within the limit, so square +X up to it.
        return Vector3.Normalize(Vector3.UnitX - (look * look.X));
    }

    // The unit vector along a unit vector crossed with another of any length.
    // False when the other has no length, is not finite, or runs along the first.
    private static bool TryUnitCross(Vector3 unit, Vector3 other, out Vector3 cross)
    {
        cross = default;
        if (!UnitLength.TryOf(other, out Vector3 otherUnit))
            return false;

        // Between two unit vectors the cross product is as long as the sine.
        Vector3 raw = Vector3.Cross(unit, otherUnit);
        if (!(raw.LengthSquared() > ParallelSine * ParallelSine))
            return false;

        cross = Vector3.Normalize(raw);
        return true;
    }

    // System.Numerics reads a rotation matrix by rows, and its rows are where
    // the three axes go.
    private static Quaternion FromColumns(Vector3 x, Vector3 y, Vector3 z) =>
        Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            0f, 0f, 0f, 1f));
}
