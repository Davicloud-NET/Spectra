using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// Rotations to and from three angles in radians, in the two orders a CFrame
/// offers. Rx, Ry and Rz are right-handed turns about X, Y and Z.
/// </summary>
internal static class EulerRotation
{
    // Below this cosine the middle angle is a quarter turn, and the two outer
    // angles turn about the same axis. Set where the rounding of a float
    // matrix, about 1e-7 divided by the cosine, grows past the cosine itself.
    private const float PoleCosine = 3e-4f;

    /// <summary>Rx * Ry * Rz: the Z turn is applied first.</summary>
    public static Quaternion FromXyz(float rx, float ry, float rz) =>
        AboutX(rx) * AboutY(ry) * AboutZ(rz);

    /// <summary>Ry * Rx * Rz: the Z turn is applied first, then X, then Y.</summary>
    public static Quaternion FromYxz(float rx, float ry, float rz) =>
        AboutY(ry) * AboutX(rx) * AboutZ(rz);

    /// <summary>The angles (rx, ry, rz) of <see cref="FromXyz"/>, ry within a quarter turn of zero.</summary>
    /// <param name="basis">The rotation as a matrix whose rows are the turned X, Y and Z axes.</param>
    // With R = Rx * Ry * Rz on column vectors: R02 = sin ry, R12 = -sin rx cos ry,
    // R22 = cos rx cos ry, R01 = -cos ry sin rz, R00 = cos ry cos rz.
    public static Vector3 ToXyz(in Matrix4x4 basis)
    {
        // From the cosine and not Asin of the sine, which loses half its
        // digits near a quarter turn.
        float cosine = MathF.Sqrt((basis.M11 * basis.M11) + (basis.M21 * basis.M21));
        float ry = MathF.Atan2(basis.M31, cosine);

        // At the pole only rx + rz or rx - rz is fixed. All of it goes to rx.
        if (cosine < PoleCosine)
            return new Vector3(MathF.Atan2(basis.M23, basis.M22), ry, 0f);

        return new Vector3(
            MathF.Atan2(-basis.M32, basis.M33),
            ry,
            MathF.Atan2(-basis.M21, basis.M11));
    }

    /// <summary>The angles (rx, ry, rz) of <see cref="FromYxz"/>, rx within a quarter turn of zero.</summary>
    /// <param name="basis">The rotation as a matrix whose rows are the turned X, Y and Z axes.</param>
    // With R = Ry * Rx * Rz on column vectors: R12 = -sin rx, R02 = sin ry cos rx,
    // R22 = cos ry cos rx, R10 = cos rx sin rz, R11 = cos rx cos rz.
    public static Vector3 ToYxz(in Matrix4x4 basis)
    {
        float cosine = MathF.Sqrt((basis.M12 * basis.M12) + (basis.M22 * basis.M22));
        float rx = MathF.Atan2(-basis.M32, cosine);

        // At the pole only ry + rz or ry - rz is fixed. All of it goes to ry.
        if (cosine < PoleCosine)
            return new Vector3(rx, MathF.Atan2(-basis.M13, basis.M11), 0f);

        return new Vector3(
            rx,
            MathF.Atan2(basis.M31, basis.M33),
            MathF.Atan2(basis.M12, basis.M22));
    }

    private static Quaternion AboutX(float angle) => Quaternion.CreateFromAxisAngle(Vector3.UnitX, angle);

    private static Quaternion AboutY(float angle) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);

    private static Quaternion AboutZ(float angle) => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
}
