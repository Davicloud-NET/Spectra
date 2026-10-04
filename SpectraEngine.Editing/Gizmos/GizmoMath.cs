using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// The geometry drawing, picking and dragging share: the pixel-to-world scale,
/// ray queries, and ray-onto-line and ray-onto-plane projections.
/// </summary>
public static class GizmoMath
{
    // A ray nearer than this to parallel with the constraint is refused: the
    // solve stays finite but the selection flies to the horizon. Squared sine
    // of the angle, about 1.8 degrees. The line and plane guards must both use
    // sin squared or they refuse at very different angles.
    private const float ParallelSineSquaredEpsilon = 1e-3f;

    private const float DegenerateEpsilon = 1e-9f;

    /// <summary>
    /// How many world units one viewport pixel spans at
    /// <paramref name="viewDepth"/> in front of the camera. The depth is along
    /// the view axis, not the distance to the eye.
    /// </summary>
    public static float WorldPerPixel(Camera camera, float viewportHeight, float viewDepth)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (viewportHeight <= 0f)
            return 0f; // viewport not sized yet

        // Orthographic: a pixel is worth the same at every depth.
        if (camera.ProjectionKind == CameraProjectionKind.Orthographic)
            return camera.OrthographicHeight / viewportHeight;

        return 2f * viewDepth * MathF.Tan(camera.FieldOfView * 0.5f) / viewportHeight;
    }

    /// <summary>
    /// The depth of a point along the camera's view axis. Negative means
    /// outside the view on the near side.
    /// </summary>
    public static float ViewDepth(Camera camera, Vector3 point)
    {
        ArgumentNullException.ThrowIfNull(camera);

        float depth = Vector3.Dot(point - camera.Position, camera.Forward);

        // An orthographic slab is symmetric about the eye, so a point behind
        // the eye is still in view. Measure from the slab's near face.
        return camera.ProjectionKind == CameraProjectionKind.Orthographic
            ? depth + camera.FarPlane
            : depth;
    }

    /// <summary>
    /// Closest approach between a ray and a segment: the nearest point on each,
    /// and how far along the ray that is. The ray direction must be unit length.
    /// </summary>
    public static void ClosestApproachToSegment(
        in Ray3 ray,
        Vector3 a,
        Vector3 b,
        out float rayDistance,
        out Vector3 rayPoint,
        out Vector3 segmentPoint)
    {
        Vector3 u = ray.Direction; // unit, so dot(u, u) = 1
        Vector3 v = b - a;
        Vector3 w = ray.Origin - a;

        float vv = Vector3.Dot(v, v);
        float uv = Vector3.Dot(u, v);
        float uw = Vector3.Dot(w, u);
        float vw = Vector3.Dot(w, v);

        // Zero when the ray and segment are parallel or the segment is a point.
        float denominator = vv - uv * uv;

        float t = denominator <= DegenerateEpsilon || vv <= DegenerateEpsilon
            ? 0f
            : Math.Clamp((vw - uv * uw) / denominator, 0f, 1f);

        float s = t * uv - uw;
        if (s <= 0f)
        {
            // Behind the ray origin: pin to the origin and re-minimise over
            // the segment.
            s = 0f;
            t = vv <= DegenerateEpsilon ? 0f : Math.Clamp(vw / vv, 0f, 1f);
        }

        rayDistance = s;
        rayPoint = ray.Origin + u * s;
        segmentPoint = a + v * t;
    }

    /// <summary>
    /// Intersects a ray with a plane given by a point and a unit normal. False
    /// for a grazing ray or a hit behind the ray origin.
    /// </summary>
    public static bool TryRayPlane(in Ray3 ray, Vector3 planePoint, Vector3 planeNormal, out float rayDistance)
    {
        // The dot is the sine of the ray/plane angle; squared to match the
        // line guard. Negated >= instead of <, so a NaN ray is refused.
        float denominator = Vector3.Dot(ray.Direction, planeNormal);
        if (!(denominator * denominator >= ParallelSineSquaredEpsilon))
        {
            rayDistance = 0f;
            return false;
        }

        float distance = Vector3.Dot(planePoint - ray.Origin, planeNormal) / denominator;
        if (!(distance >= 0f))
        {
            rayDistance = 0f;
            return false;
        }

        rayDistance = distance;
        return true;
    }

    /// <summary>
    /// Intersects a ray with a rectangle spanning <paramref name="uLength"/>
    /// and <paramref name="vLength"/> from <paramref name="corner"/>. Both axes
    /// must be unit length and perpendicular.
    /// </summary>
    public static bool TryRayQuad(
        in Ray3 ray,
        Vector3 corner,
        Vector3 uAxis,
        float uLength,
        Vector3 vAxis,
        float vLength,
        out float rayDistance)
    {
        Vector3 normal = Vector3.Cross(uAxis, vAxis);
        if (!TryRayPlane(in ray, corner, normal, out rayDistance))
            return false;

        Vector3 local = ray.PointAt(rayDistance) - corner;
        float u = Vector3.Dot(local, uAxis);
        float v = Vector3.Dot(local, vAxis);
        if (u < 0f || u > uLength || v < 0f || v > vLength)
        {
            rayDistance = 0f;
            return false;
        }

        return true;
    }

    /// <summary>Intersects a ray with a disc given by centre, unit normal and radius.</summary>
    public static bool TryRayDisc(in Ray3 ray, Vector3 centre, Vector3 normal, float radius, out float rayDistance)
    {
        if (!TryRayPlane(in ray, centre, normal, out rayDistance))
            return false;

        if (Vector3.DistanceSquared(ray.PointAt(rayDistance), centre) > radius * radius)
        {
            rayDistance = 0f;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The point on an infinite line closest to the ray. The line direction
    /// must be unit length. False when the ray is nearly parallel to the line.
    /// </summary>
    // Not clamped: a drag must run past the arrow's end and behind the pivot.
    public static bool TryClosestPointOnLine(
        in Ray3 ray, Vector3 linePoint, Vector3 lineDirection, out Vector3 point)
    {
        Vector3 u = ray.Direction;
        Vector3 w = ray.Origin - linePoint;

        float uv = Vector3.Dot(u, lineDirection);
        // 1 - cos squared = sin squared of the ray/line angle.
        // Negated >= instead of <, so a NaN ray is refused.
        float denominator = 1f - uv * uv;
        if (!(denominator >= ParallelSineSquaredEpsilon))
        {
            point = linePoint;
            return false;
        }

        float uw = Vector3.Dot(w, u);
        float vw = Vector3.Dot(w, lineDirection);
        float t = (vw - uv * uw) / denominator;

        point = linePoint + lineDirection * t;
        return true;
    }
}
