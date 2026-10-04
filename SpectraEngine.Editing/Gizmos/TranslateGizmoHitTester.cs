using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// Picks the translate gizmo's handles from a viewport ray, in screen space
/// with a pixel tolerance. Render thread only.
/// </summary>
public static class TranslateGizmoHitTester
{
    /// <summary>How far from an axis arrow, in pixels, the cursor may sit and still grab it.</summary>
    public const float DefaultTolerancePixels = GizmoHitTesting.DefaultTolerancePixels;

    /// <summary>
    /// Picks the handle under <paramref name="ray"/>, or
    /// <see cref="GizmoPick.Miss"/> when the ray comes near none of them.
    /// </summary>
    /// <param name="tolerancePixels">Screen-space slack for the axis arrows. The disc and quads are tested exactly.</param>
    public static GizmoPick Pick(in GizmoGeometry geometry, in Ray3 ray, float tolerancePixels)
    {
        if (geometry.IsBehindCamera)
            return GizmoPick.Miss;

        // Fixed priority, since handles overlap near the pivot: the centre is
        // the smallest target, and an arrow runs under its two quads.
        GizmoPick centre = PickScreenHandle(in geometry, in ray);
        if (centre.IsHit)
            return centre;

        GizmoPick plane = PickPlaneHandles(in geometry, in ray);
        if (plane.IsHit)
            return plane;

        return PickAxisHandles(in geometry, in ray, tolerancePixels);
    }

    // Tested in the camera-facing plane through the pivot, the plane its drag
    // is constrained to.
    private static GizmoPick PickScreenHandle(in GizmoGeometry geometry, in Ray3 ray)
    {
        if (!geometry.Offers(GizmoHandle.Screen))
            return GizmoPick.Miss;

        if (!GizmoMath.TryRayDisc(
                in ray, geometry.Pivot, geometry.ViewNormal, geometry.ScreenRadius, out float distance))
        {
            return GizmoPick.Miss;
        }

        return new GizmoPick(GizmoHandle.Screen, distance, 0f, ray.PointAt(distance));
    }

    private static GizmoPick PickPlaneHandles(in GizmoGeometry geometry, in Ray3 ray)
    {
        GizmoPick best = GizmoPick.Miss;

        // Nearest quad to the camera wins.
        for (GizmoHandle handle = GizmoHandle.PlaneYZ; handle <= GizmoHandle.PlaneXY; handle++)
        {
            if (!geometry.TryGetPlaneQuad(handle, out Vector3 corner, out Vector3 u, out Vector3 v, out float size))
                continue;

            if (!GizmoMath.TryRayQuad(in ray, corner, u, size, v, size, out float distance))
                continue;

            if (distance < best.RayDistance)
                best = new GizmoPick(handle, distance, 0f, ray.PointAt(distance));
        }

        return best;
    }

    private static GizmoPick PickAxisHandles(
        in GizmoGeometry geometry, in Ray3 ray, float tolerancePixels)
    {
        GizmoPick best = GizmoPick.Miss;
        float tolerance = MathF.Max(tolerancePixels, 0f);

        for (GizmoHandle handle = GizmoHandle.AxisX; handle <= geometry.LastAxisHandle; handle++)
        {
            if (!geometry.TryGetAxisSegment(handle, out Vector3 start, out Vector3 end))
                continue;

            // The drag projects through the same function, so an axis it
            // refuses (viewed near end-on) must not be pickable. Otherwise the
            // arrow highlights and then swallows the press.
            if (!GizmoMath.TryClosestPointOnLine(in ray, geometry.Pivot, geometry.Axis(handle), out _))
                continue;

            float pixels = GizmoHitTesting.SegmentPixelDistance(
                in geometry, in ray, start, end, out float distance);
            if (pixels > tolerance)
                continue;

            // Arrows share the pivot, so cursor proximity decides; depth breaks ties.
            if (pixels < best.PixelDistance ||
                (pixels == best.PixelDistance && distance < best.RayDistance))
            {
                best = new GizmoPick(handle, distance, pixels, ray.PointAt(distance));
            }
        }

        return best;
    }
}
