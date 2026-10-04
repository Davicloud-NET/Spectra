using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// Yaw, pitch and roll in degrees, for editing a rotation the scene stores as
/// a quaternion. Same convention as <c>Quaternion.CreateFromYawPitchRoll</c>:
/// yaw about Y, pitch about X, roll about Z.
/// </summary>
// The round trip preserves the rotation, not the numbers: several triples name
// one rotation, and at pitch ±90 only yaw + roll is recoverable.
public readonly record struct EulerAngles(float Yaw, float Pitch, float Roll)
{
    /// <summary>The angles in degrees, X = pitch, Y = yaw, Z = roll.</summary>
    public Vector3 AsDegrees => new(Pitch, Yaw, Roll);

    /// <summary>Builds from a pitch/yaw/roll degree vector.</summary>
    public static EulerAngles FromDegrees(Vector3 degrees) => new(degrees.Y, degrees.X, degrees.Z);

    /// <summary>The quaternion these angles name.</summary>
    public Quaternion ToQuaternion() => Quaternion.CreateFromYawPitchRoll(
        Yaw * MathF.PI / 180f, Pitch * MathF.PI / 180f, Roll * MathF.PI / 180f);

    /// <summary>Extracts the canonical angles of a rotation.</summary>
    public static EulerAngles FromQuaternion(Quaternion rotation)
    {
        Quaternion q = Quaternion.Normalize(rotation);

        // The clamp keeps rounding from pushing asin out of [-1, 1].
        float sinPitch = 2f * ((q.W * q.X) - (q.Y * q.Z));
        sinPitch = Math.Clamp(sinPitch, -1f, 1f);
        float pitch = MathF.Asin(sinPitch);

        float yaw, roll;
        if (MathF.Abs(sinPitch) > 0.99999f)
        {
            // Gimbal lock: only yaw + roll is recoverable. Put it all in yaw.
            yaw = 2f * MathF.Atan2(q.Y, q.W);
            roll = 0f;
        }
        else
        {
            yaw = MathF.Atan2(2f * ((q.W * q.Y) + (q.X * q.Z)),
                              1f - (2f * ((q.X * q.X) + (q.Y * q.Y))));
            roll = MathF.Atan2(2f * ((q.W * q.Z) + (q.X * q.Y)),
                               1f - (2f * ((q.X * q.X) + (q.Z * q.Z))));
        }

        const float ToDegrees = 180f / MathF.PI;
        return new EulerAngles(yaw * ToDegrees, pitch * ToDegrees, roll * ToDegrees);
    }
}
