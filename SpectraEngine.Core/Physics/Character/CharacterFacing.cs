using System;
using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// A character's yaw as a frame, and back. A character looks along its
/// frame's +Z, as a player start does. Yaw zero looks along +X and a quarter
/// turn along +Z, the way a character command reads it.
/// </summary>
public static class CharacterFacing
{
    /// <summary>The frame of a character with its feet at a point, looking along a yaw in radians.</summary>
    public static CFrame FrameAt(Vector3 feet, float yaw) =>
        new(feet, Quaternion.CreateFromAxisAngle(Vector3.UnitY, (MathF.PI / 2f) - yaw));

    /// <summary>
    /// The yaw a frame looks along, in radians. Null for a frame that looks
    /// straight up or down, which has no heading.
    /// </summary>
    public static float? YawOf(in CFrame frame) => YawAlong(frame.ZVector);

    /// <summary>
    /// The yaw that looks along a direction, in radians. Null for a direction
    /// that points straight up or down, or has no length.
    /// </summary>
    public static float? YawAlong(Vector3 direction) =>
        (direction.X * direction.X) + (direction.Z * direction.Z) > 1e-8f
            ? MathF.Atan2(direction.Z, direction.X)
            : null;
}
