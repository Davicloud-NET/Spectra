using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// The proximity tests the gizmo hit-testers share. Distances come back in
/// pixels, so the tolerance is the same at any camera distance.
/// </summary>
public static class GizmoHitTesting
{
    /// <summary>
    /// How far, in pixels, the cursor may be from an arrow or ring and still
    /// grab it.
    /// </summary>
    public const float DefaultTolerancePixels = 8f;

    /// <summary>
    /// How many segments a rotate ring has, for both drawing and picking.
    /// </summary>
    public const int RingSegments = 48;

    /// <summary>
    /// The distance in pixels from a ray to a segment, plus how far along the
    /// ray the closest approach is.
    /// </summary>
    public static float SegmentPixelDistance(
        in GizmoGeometry geometry, in Ray3 ray, Vector3 a, Vector3 b, out float rayDistance)
    {
        GizmoMath.ClosestApproachToSegment(
            in ray, a, b, out rayDistance, out Vector3 onRay, out Vector3 onSegment);
        return geometry.WorldToPixels(Vector3.Distance(onRay, onSegment));
    }

    /// <summary>
    /// The distance in pixels from a ray to a ring, plus how far along the ray
    /// the closest approach is.
    /// </summary>
    // Walks the drawn segments. A ray/plane intersection fails for a ring seen
    // edge-on and shrinks the tolerance as the ring tilts.
    public static float RingPixelDistance(
        in GizmoGeometry geometry, in Ray3 ray, Vector3 centre, Vector3 axis, float radius, out float rayDistance)
    {
        BuildRingBasis(axis, out Vector3 u, out Vector3 v);

        float bestPixels = float.PositiveInfinity;
        rayDistance = float.PositiveInfinity;

        Vector3 previous = centre + u * radius;
        for (int i = 1; i <= RingSegments; i++)
        {
            float angle = MathF.Tau * i / RingSegments;
            Vector3 point = centre + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;

            float pixels = SegmentPixelDistance(in geometry, in ray, previous, point, out float distance);
            if (pixels < bestPixels)
            {
                bestPixels = pixels;
                rayDistance = distance;
            }

            previous = point;
        }

        return bestPixels;
    }

    /// <summary>
    /// Two unit vectors spanning the plane perpendicular to <paramref name="axis"/>.
    /// The hit tester and the renderer both build rings in this basis.
    /// </summary>
    public static void BuildRingBasis(Vector3 axis, out Vector3 first, out Vector3 second)
    {
        // Reference away from the axis, so the cross product is not near zero.
        Vector3 reference = MathF.Abs(axis.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        first = Vector3.Normalize(Vector3.Cross(reference, axis));
        second = Vector3.Cross(axis, first);
    }

    /// <summary>
    /// Intersects a ray with a cube of half-extent <paramref name="radius"/>
    /// oriented by the geometry's frame. Used for the scale gizmo's handles.
    /// </summary>
    public static bool TryRayHandleBox(
        in GizmoGeometry geometry, in Ray3 ray, Vector3 centre, float radius, out float rayDistance)
    {
        Vector3 origin = ToFrame(in geometry, ray.Origin - centre);
        Vector3 direction = ToFrame(in geometry, ray.Direction);

        float near = 0f;
        float far = float.PositiveInfinity;

        if (!SlabClip(origin.X, direction.X, radius, ref near, ref far) ||
            !SlabClip(origin.Y, direction.Y, radius, ref near, ref far) ||
            !SlabClip(origin.Z, direction.Z, radius, ref near, ref far))
        {
            rayDistance = 0f;
            return false;
        }

        rayDistance = near;
        return true;
    }

    private static Vector3 ToFrame(in GizmoGeometry geometry, Vector3 world) => new(
        Vector3.Dot(world, geometry.AxisX),
        Vector3.Dot(world, geometry.AxisY),
        Vector3.Dot(world, geometry.AxisZ));

    private static bool SlabClip(float origin, float direction, float radius, ref float near, ref float far)
    {
        if (MathF.Abs(direction) < 1e-8f)
        {
            return origin >= -radius && origin <= radius;
        }

        float inverse = 1f / direction;
        float t0 = (-radius - origin) * inverse;
        float t1 = (radius - origin) * inverse;
        if (t0 > t1)
            (t0, t1) = (t1, t0);

        near = MathF.Max(near, t0);
        far = MathF.Min(far, t1);
        return near <= far;
    }
}
