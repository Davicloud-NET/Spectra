using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// Picks the rotate gizmo's rings from a viewport ray, by pixel distance to
/// the drawn circle. Nearest miss wins, depth breaks ties.
/// </summary>
public static class RotateGizmoHitTester
{
    /// <summary>
    /// How far from the pivot a grab point must land in the ring's plane, as a
    /// fraction of the ring's radius. Shared with <see cref="RotateGizmo"/> so a
    /// ring the tool would refuse is never picked.
    /// </summary>
    public const float MinimumGrabRadiusFactor = 0.05f;

    /// <summary>
    /// Picks the ring under <paramref name="ray"/>, or
    /// <see cref="GizmoPick.Miss"/> when the ray comes near none of them.
    /// </summary>
    public static GizmoPick Pick(in GizmoGeometry geometry, in Ray3 ray, float tolerancePixels)
    {
        if (geometry.IsBehindCamera)
            return GizmoPick.Miss;

        float tolerance = MathF.Max(tolerancePixels, 0f);
        GizmoPick best = GizmoPick.Miss;

        for (GizmoHandle handle = GizmoHandle.AxisX; handle <= GizmoHandle.AxisZ; handle++)
            best = Consider(in geometry, in ray, handle, tolerance, best);

        return Consider(in geometry, in ray, GizmoHandle.Screen, tolerance, best);
    }

    private static GizmoPick Consider(
        in GizmoGeometry geometry, in Ray3 ray, GizmoHandle handle, float tolerance, GizmoPick best)
    {
        if (!geometry.TryGetRing(handle, out Vector3 axis, out float radius))
            return best;

        // Same two refusals RotateGizmo.TryPrepareDrag makes. An edge-on ring
        // would otherwise highlight, refuse the press, and let it fall through
        // to click-select or a marquee.
        if (!GizmoMath.TryRayPlane(in ray, geometry.Pivot, axis, out float planeDistance))
            return best;

        float spoke = (ray.PointAt(planeDistance) - geometry.Pivot).Length();
        if (spoke < radius * MinimumGrabRadiusFactor)
            return best;

        float pixels = GizmoHitTesting.RingPixelDistance(
            in geometry, in ray, geometry.Pivot, axis, radius, out float distance);

        if (pixels > tolerance)
            return best;

        if (pixels < best.PixelDistance ||
            (pixels == best.PixelDistance && distance < best.RayDistance))
        {
            return new GizmoPick(handle, distance, pixels, ray.PointAt(distance));
        }

        return best;
    }
}
