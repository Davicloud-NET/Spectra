using System;
using System.Numerics;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// Converts a face's texture axes to and from what a person edits: an
/// alignment and an angle. Everything here is in world space; map a
/// brush-local face in with <c>face.Transformed(node.WorldMatrix)</c> first.
/// </summary>
// Angles are measured from one base frame for every face: the dominant-axis
// projection flattened into the surface. A separate in-plane base for
// face-aligned surfaces reads back 90 degrees off on axis-aligned faces.
public static class FaceAxes
{
    private const float Epsilon = 1e-4f;

    /// <summary>A brush plane's normal in world space.</summary>
    // Placements are rigid, so no inverse transpose.
    public static Vector3 WorldNormal(in Plane localPlane, in Matrix4x4 world)
    {
        Vector3 normal = Vector3.TransformNormal(localPlane.Normal, world);
        return normal.LengthSquared() > Epsilon ? Vector3.Normalize(normal) : localPlane.Normal;
    }

    /// <summary>
    /// How far the face's texture is turned from its base frame, in degrees.
    /// A world-aligned face reports 0.
    /// </summary>
    public static float RotationDegrees(in FaceSurface face, Vector3 normal)
    {
        if (face.IsWorldAligned) return 0f;

        FaceFrame(normal, out Vector3 u0, out _);
        Vector3 u = Normalize(face.UAxis);
        if (u == Vector3.Zero || u0 == Vector3.Zero) return 0f;

        Vector3 n = Normalize(normal);
        float sin = Vector3.Dot(Vector3.Cross(u0, u), n);
        float cos = Vector3.Dot(u0, u);

        return float.RadiansToDegrees(MathF.Atan2(sin, cos));
    }

    /// <summary>
    /// Turns the face's texture to an absolute angle. A world-aligned face
    /// becomes face-aligned, since it has no axes to turn.
    /// </summary>
    public static FaceSurface WithRotation(in FaceSurface face, Vector3 normal, float degrees)
    {
        // Turn from the base, not from the current axes, and use the same
        // base RotationDegrees reads, or an angle does not round-trip.
        FaceFrame(normal, out Vector3 u0, out Vector3 v0);

        Vector3 n = Normalize(normal);
        float radians = float.DegreesToRadians(degrees);

        return face.WithAxes(
            Rotate(u0, n, radians),
            Rotate(v0, n, radians),
            face.UOffset, face.VOffset,
            face.UScale, face.VScale);
    }

    /// <summary>Returns the face projecting from the world axes, the default for a brush face.</summary>
    public static FaceSurface AlignedToWorld(in FaceSurface face) =>
        face.WithAxes(Vector3.Zero, Vector3.Zero, face.UOffset, face.VOffset, face.UScale, face.VScale);

    /// <summary>Returns the face with axes in its own plane, so the texture follows the surface.</summary>
    public static FaceSurface AlignedToFace(in FaceSurface face, Vector3 normal)
    {
        FaceFrame(normal, out Vector3 u, out Vector3 v);
        return face.WithAxes(u, v, face.UOffset, face.VOffset, face.UScale, face.VScale);
    }

    /// <summary>
    /// Whether this face stores its own axes. On an axis-aligned face both
    /// alignments give the same numbers, so only the storage can tell.
    /// </summary>
    public static bool IsFaceAligned(in FaceSurface face) => !face.IsWorldAligned;

    /// <summary>Which alignment this face is using, as the panel names it.</summary>
    public static string AlignmentLabel(in FaceSurface face) =>
        face.IsWorldAligned ? "World" : "Face";

    // The dominant-axis projection. Asked of a default surface, not the one
    // being measured: the base must not turn with the face.
    private static void BaseAxes(Vector3 normal, out Vector3 u, out Vector3 v) =>
        FaceSurface.Default.ResolveAxes(normal, out u, out v, out _, out _);

    // The base frame flattened into the face's plane. On an axis-aligned face
    // it equals the base, so switching alignment does not spin the texture.
    private static void FaceFrame(Vector3 normal, out Vector3 u, out Vector3 v)
    {
        Vector3 n = Normalize(normal);
        BaseAxes(n, out Vector3 u0, out Vector3 v0);

        Vector3 flattened = u0 - (n * Vector3.Dot(n, u0));
        if (flattened.LengthSquared() <= Epsilon)
        {
            // u0 parallel to the normal. Should not happen; avoid a NaN frame.
            Vector3 seed = MathF.Abs(n.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
            u = Normalize(Vector3.Cross(seed, n));
            v = Vector3.Cross(n, u);
            return;
        }

        u = Vector3.Normalize(flattened);

        // Keep the base's handedness, or the texture mirrors.
        v = Vector3.Cross(n, u);
        if (Vector3.Dot(v, v0) < 0f) v = -v;
    }

    // Rodrigues, about a unit axis.
    private static Vector3 Rotate(Vector3 value, Vector3 axis, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        return (value * cos)
            + (Vector3.Cross(axis, value) * sin)
            + (axis * Vector3.Dot(axis, value) * (1f - cos));
    }

    private static Vector3 Normalize(Vector3 value) =>
        value.LengthSquared() > Epsilon ? Vector3.Normalize(value) : Vector3.Zero;
}
