using System;
using System.Numerics;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// A face's texture frame as a person edits it: an alignment and an angle,
/// rather than two vectors.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nobody types a U axis.</b> <see cref="FaceSurface"/> stores two vectors
/// because that is what the UV projection needs; what an author wants is "turn
/// this 45 degrees" and "line it up with the face". This converts between the
/// two, and is the only place that conversion exists.
/// </para>
/// <para>
/// <b>Everything here is in WORLD space.</b> A face's stored axes are
/// brush-local; a rotation somebody asks for is about the normal they can see.
/// The caller maps in with <c>face.Transformed(node.WorldMatrix)</c> and back
/// with the inverse, which is exact for the rigid matrices brush placements are
/// required to have.
/// </para>
/// <para>
/// <b>ONE base frame, and it is the world projection.</b> Measuring a
/// face-aligned surface against an in-plane frame instead is the obvious design
/// and it is wrong by exactly 90 degrees on every axis-aligned face: for a
/// normal like +Y the world projection ALREADY lies in the face's plane, so
/// "which frame is this measured against" cannot be recovered from the axes, and
/// a surface written at 15 degrees reads back at 105. Measured, not reasoned
/// about. So there is one base frame for every explicit face: the dominant-axis
/// projection flattened into the surface, which on an axis-aligned face IS the
/// dominant-axis projection. The two alignments then agree exactly on every
/// axis-aligned face, which is also what Hammer does, and a written angle reads
/// back as itself because the read and the write ask the same function.
/// </para>
/// </remarks>
public static class FaceAxes
{
    /// <summary>Below this, two axes count as the same direction.</summary>
    private const float Epsilon = 1e-4f;

    /// <summary>A brush plane's normal in world space.</summary>
    /// <remarks>
    /// The rotation part only: a plane normal is a direction, and translating
    /// one is meaningless. Brush placements are rigid by contract, so this needs
    /// no inverse transpose.
    /// </remarks>
    public static Vector3 WorldNormal(in Plane localPlane, in Matrix4x4 world)
    {
        Vector3 normal = Vector3.TransformNormal(localPlane.Normal, world);
        return normal.LengthSquared() > Epsilon ? Vector3.Normalize(normal) : localPlane.Normal;
    }

    /// <summary>
    /// How far the face's texture is turned from its base frame, in degrees.
    /// </summary>
    /// <remarks>
    /// A world-aligned face reports 0: it has no axes at all, so there is
    /// nothing to measure.
    /// </remarks>
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

    /// <summary>Turns the face's texture to an absolute angle.</summary>
    /// <remarks>
    /// <b>A world-aligned face becomes explicit, and the panel says so.</b> It
    /// has no axes to turn, so asking for an angle resolves its base frame
    /// first and the alignment chip moves from World to Face. That is what
    /// texture lock means: the frame is now carried on the surface rather than
    /// re-derived from the normal, so moving the brush takes the texture with
    /// it. The alternative is an angle that reads back as 0.
    /// </remarks>
    public static FaceSurface WithRotation(in FaceSurface face, Vector3 normal, float degrees)
    {
        // Turned from the BASE, never from where the face happens to be: every
        // write is absolute, so a round trip through any number of angles
        // leaves no residue. The same rule the gizmos follow, for the same
        // reason. And the SAME base the read uses, or a written angle comes
        // back as a different number.
        FaceFrame(normal, out Vector3 u0, out Vector3 v0);

        Vector3 n = Normalize(normal);
        float radians = float.DegreesToRadians(degrees);

        return face.WithAxes(
            Rotate(u0, n, radians),
            Rotate(v0, n, radians),
            face.UOffset, face.VOffset,
            face.UScale, face.VScale);
    }

    /// <summary>
    /// Projects from the world axes, which is what an untouched brush face does.
    /// </summary>
    public static FaceSurface AlignedToWorld(in FaceSurface face) =>
        face.WithAxes(Vector3.Zero, Vector3.Zero, face.UOffset, face.VOffset, face.UScale, face.VScale);

    /// <summary>
    /// Lies in the face's own plane, so the texture follows the surface however
    /// it is turned.
    /// </summary>
    public static FaceSurface AlignedToFace(in FaceSurface face, Vector3 normal)
    {
        FaceFrame(normal, out Vector3 u, out Vector3 v);
        return face.WithAxes(u, v, face.UOffset, face.VOffset, face.UScale, face.VScale);
    }

    /// <summary>
    /// Whether this face carries its own frame rather than projecting from the
    /// world axes.
    /// </summary>
    /// <remarks>
    /// <b>A fact about the STORAGE, not a measurement of the axes.</b> Having
    /// axes at all is what makes a texture follow its surface; on an
    /// axis-aligned face the two modes produce the same numbers, so no
    /// geometric test could tell them apart, and one that tried would report the
    /// alignment differently depending on which way a wall happened to face.
    /// </remarks>
    public static bool IsFaceAligned(in FaceSurface face) => !face.IsWorldAligned;

    /// <summary>Which alignment this face is using, as the panel names it.</summary>
    /// <remarks>
    /// It takes no normal, deliberately: the answer is a fact about the stored
    /// axes, and a signature asking for a direction would suggest there is a
    /// geometric test behind it that could disagree with the storage.
    /// </remarks>
    public static string AlignmentLabel(in FaceSurface face) =>
        face.IsWorldAligned ? "World" : "Face";

    /// <summary>
    /// The frame an angle is measured from: Hammer's dominant-axis projection,
    /// which is what a world-aligned face resolves to.
    /// </summary>
    /// <remarks>
    /// Asked of a DEFAULT surface rather than of the one being measured,
    /// because that one may already be turned and the base must not move with
    /// it.
    /// </remarks>
    private static void BaseAxes(Vector3 normal, out Vector3 u, out Vector3 v) =>
        FaceSurface.Default.ResolveAxes(normal, out u, out v, out _, out _);

    /// <summary>
    /// An orthonormal frame lying in the face's plane, as close to the base
    /// frame as an in-plane frame can be.
    /// </summary>
    /// <remarks>
    /// <b>Derived from the base rather than from an arbitrary seed axis.</b> On
    /// an axis-aligned face the base already lies in the plane, so this is the
    /// identity and the two alignments agree exactly - which is what stops the
    /// texture jumping a quarter turn when somebody switches the chip. On a
    /// slope it is the base flattened into the surface, so switching to Face
    /// alignment changes how the texture is projected without spinning it.
    /// </remarks>
    private static void FaceFrame(Vector3 normal, out Vector3 u, out Vector3 v)
    {
        Vector3 n = Normalize(normal);
        BaseAxes(n, out Vector3 u0, out Vector3 v0);

        // Gram-Schmidt: drop the part of u0 that points along the normal.
        Vector3 flattened = u0 - (n * Vector3.Dot(n, u0));
        if (flattened.LengthSquared() <= Epsilon)
        {
            // u0 is parallel to the normal, which the dominant-axis rule does
            // not produce. Fall back to any stable in-plane axis rather than
            // normalising a zero vector into a NaN frame that draws nothing.
            Vector3 seed = MathF.Abs(n.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
            u = Normalize(Vector3.Cross(seed, n));
            v = Vector3.Cross(n, u);
            return;
        }

        u = Vector3.Normalize(flattened);

        // The handedness of the base is kept: v flipped would mirror the
        // texture, which is a change nobody asked for and one that reads as the
        // artwork being wrong rather than the frame.
        v = Vector3.Cross(n, u);
        if (Vector3.Dot(v, v0) < 0f) v = -v;
    }

    /// <summary>Rodrigues, about a unit axis.</summary>
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
