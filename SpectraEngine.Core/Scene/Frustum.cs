using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// A view frustum as six normalized planes whose normals point inward: a point
/// is inside when its signed distance to every plane is non-negative.
/// </summary>
public readonly struct Frustum
{
    /// <summary>Left clip plane (inward normal points right).</summary>
    public readonly Plane Left;

    /// <summary>Right clip plane (inward normal points left).</summary>
    public readonly Plane Right;

    /// <summary>Bottom clip plane (inward normal points up).</summary>
    public readonly Plane Bottom;

    /// <summary>Top clip plane (inward normal points down).</summary>
    public readonly Plane Top;

    /// <summary>Near clip plane (inward normal points away from the camera).</summary>
    public readonly Plane Near;

    /// <summary>Far clip plane (inward normal points toward the camera).</summary>
    public readonly Plane Far;

    private Frustum(Plane left, Plane right, Plane bottom, Plane top, Plane near, Plane far)
    {
        Left = left;
        Right = right;
        Bottom = bottom;
        Top = top;
        Near = near;
        Far = far;
    }

    /// <summary>
    /// Extracts the six planes from a row-vector view-projection matrix
    /// (<c>clip = world · viewProjection</c>, i.e. <see cref="Camera.GetViewProjection"/>).
    /// </summary>
    public static Frustum FromViewProjection(in Matrix4x4 m)
    {
        // Gribb-Hartmann for row vectors: clip x/y/z/w are dots with columns
        // 1..4, so the planes are column 4 plus or minus columns 1-3.
        // Near is column 3 alone because clip depth is [0, 1], not [-1, 1].
        return new Frustum(
            left: Plane.Normalize(new Plane(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41)),
            right: Plane.Normalize(new Plane(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41)),
            bottom: Plane.Normalize(new Plane(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42)),
            top: Plane.Normalize(new Plane(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42)),
            near: Plane.Normalize(new Plane(m.M13, m.M23, m.M33, m.M43)),
            far: Plane.Normalize(new Plane(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43)));
    }

    /// <summary>
    /// Conservative box test for culling: false only when the box lies fully
    /// outside one plane. May return true for a box just outside a corner.
    /// </summary>
    public bool Intersects(in Aabb box) =>
        !OutsidePlane(Left, box) && !OutsidePlane(Right, box) &&
        !OutsidePlane(Bottom, box) && !OutsidePlane(Top, box) &&
        !OutsidePlane(Near, box) && !OutsidePlane(Far, box);

    /// <summary>True when the point is on the inner side of all six planes (boundary counts as inside).</summary>
    public bool Contains(Vector3 point) =>
        Plane.DotCoordinate(Left, point) >= 0f &&
        Plane.DotCoordinate(Right, point) >= 0f &&
        Plane.DotCoordinate(Bottom, point) >= 0f &&
        Plane.DotCoordinate(Top, point) >= 0f &&
        Plane.DotCoordinate(Near, point) >= 0f &&
        Plane.DotCoordinate(Far, point) >= 0f;

    /// <summary>True only when the complete box lies inside all six planes.</summary>
    public bool Contains(in Aabb box) =>
        InsidePlane(Left, box) && InsidePlane(Right, box) &&
        InsidePlane(Bottom, box) && InsidePlane(Top, box) &&
        InsidePlane(Near, box) && InsidePlane(Far, box);

    private static bool InsidePlane(in Plane plane, in Aabb box) =>
        Plane.DotCoordinate(plane, new Vector3(
            plane.Normal.X >= 0 ? box.Min.X : box.Max.X,
            plane.Normal.Y >= 0 ? box.Min.Y : box.Max.Y,
            plane.Normal.Z >= 0 ? box.Min.Z : box.Max.Z)) >= 0;

    private static bool OutsidePlane(in Plane plane, in Aabb box)
    {
        // Only the corner farthest along the normal needs testing.
        var positive = new Vector3(
            plane.Normal.X >= 0f ? box.Max.X : box.Min.X,
            plane.Normal.Y >= 0f ? box.Max.Y : box.Min.Y,
            plane.Normal.Z >= 0f ? box.Max.Z : box.Min.Z);
        return Plane.DotCoordinate(plane, positive) < 0f;
    }
}
