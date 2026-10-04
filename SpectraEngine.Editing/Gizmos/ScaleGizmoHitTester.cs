using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// Picks the scale gizmo's cube handles from a viewport ray: the uniform centre
/// cube first, then the axis cubes. Render thread only.
/// </summary>
public static class ScaleGizmoHitTester
{
    /// <summary>
    /// Picks the handle under <paramref name="ray"/>, or
    /// <see cref="GizmoPick.Miss"/> when the ray comes near none of them.
    /// </summary>
    /// <param name="tolerancePixels">Screen-space slack for the axis shafts. The cubes are tested exactly.</param>
    public static GizmoPick Pick(in GizmoGeometry geometry, in Ray3 ray, float tolerancePixels)
    {
        if (geometry.IsBehindCamera)
            return GizmoPick.Miss;

        // Centre wins ties: it is the smallest target and sits among the others.
        if (geometry.TryGetHandleBox(GizmoHandle.Screen, out Vector3 centre, out float radius) &&
            GizmoHitTesting.TryRayHandleBox(in geometry, in ray, centre, radius, out float centreDistance))
        {
            return new GizmoPick(GizmoHandle.Screen, centreDistance, 0f, ray.PointAt(centreDistance));
        }

        float tolerance = MathF.Max(tolerancePixels, 0f);
        GizmoPick best = GizmoPick.Miss;

        for (GizmoHandle handle = GizmoHandle.AxisX; handle <= geometry.LastAxisHandle; handle++)
        {
            if (!geometry.TryGetHandleBox(handle, out Vector3 boxCentre, out float boxRadius))
                continue;

            // The drag measures travel with the same projection, so an axis
            // it refuses (viewed near end-on) must not be pickable.
            if (!GizmoMath.TryClosestPointOnLine(in ray, geometry.Pivot, geometry.Axis(handle), out _))
                continue;

            // Cube hits report zero pixel distance so they beat a nearby shaft.
            if (GizmoHitTesting.TryRayHandleBox(in geometry, in ray, boxCentre, boxRadius, out float boxDistance) &&
                (0f < best.PixelDistance || boxDistance < best.RayDistance))
            {
                best = new GizmoPick(handle, boxDistance, 0f, ray.PointAt(boxDistance));
                continue;
            }

            // A shaft can have no length when handles stand on the bounds.
            // Skip it, or the pivot becomes pickable as an axis handle.
            if (!geometry.TryGetAxisSegment(handle, out Vector3 start, out Vector3 end))
                continue;

            float pixels = GizmoHitTesting.SegmentPixelDistance(
                in geometry, in ray, start, end, out float distance);
            if (pixels > tolerance)
                continue;

            if (pixels < best.PixelDistance ||
                (pixels == best.PixelDistance && distance < best.RayDistance))
            {
                best = new GizmoPick(handle, distance, pixels, ray.PointAt(distance));
            }
        }

        return best;
    }
}
