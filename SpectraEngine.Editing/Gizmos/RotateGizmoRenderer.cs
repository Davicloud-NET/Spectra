using SpectraEngine.Core.Graphics;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// Draws the rotate gizmo into a <see cref="DebugDraw"/>: three axis rings plus
/// a view-aligned ring, with the hovered or active one highlighted.
/// </summary>
// Circles use the same basis and segment count RotateGizmoHitTester walks,
// so the drawn polygon is the picked one.
public static class RotateGizmoRenderer
{
    /// <summary>Draws the whole gizmo into <paramref name="output"/>.</summary>
    public static void Draw(DebugDraw output, in GizmoGeometry geometry, GizmoHandle highlighted)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (geometry.IsBehindCamera || geometry.AxisLength <= 0f)
            return;

        for (GizmoHandle handle = GizmoHandle.AxisX; handle <= GizmoHandle.AxisZ; handle++)
        {
            if (geometry.TryGetRing(handle, out Vector3 axis, out float radius))
                GizmoColors.DrawCircle(output, geometry.Pivot, axis, radius, GizmoColors.For(handle, highlighted));
        }

        // View ring uses the camera basis, not its normal: a derived basis
        // would shift the vertex phase and the drawn and picked polygons would
        // differ by half a chord.
        if (geometry.Offers(GizmoHandle.Screen))
        {
            GizmoColors.DrawCircle(
                output,
                geometry.Pivot,
                geometry.ViewRight,
                geometry.ViewUp,
                geometry.ScreenRingRadius,
                GizmoColors.For(GizmoHandle.Screen, highlighted));
        }
    }

    /// <summary>
    /// Draws the drag's protractor: a spoke at the grab point, a spoke at the
    /// current angle, and the arc between them.
    /// </summary>
    /// <param name="reference">Unit vector from the pivot to the grab point, in the ring's plane.</param>
    /// <param name="angle">The swept angle in radians, signed about <paramref name="axis"/>.</param>
    public static void DrawSweep(
        DebugDraw output, in GizmoGeometry geometry, Vector3 axis, Vector3 reference, float radius, float angle)
    {
        ArgumentNullException.ThrowIfNull(output);

        Vector3 pivot = geometry.Pivot;
        Vector3 perpendicular = Vector3.Cross(axis, reference);
        Vector3 color = GizmoColors.Highlight;

        output.Line(pivot, pivot + reference * radius, color);

        // Same chord length as the ring, so the arc lies on it.
        int steps = Math.Max(1, (int)MathF.Ceiling(
            MathF.Abs(angle) / MathF.Tau * GizmoHitTesting.RingSegments));

        Vector3 previous = pivot + reference * radius;
        for (int i = 1; i <= steps; i++)
        {
            float step = angle * i / steps;
            Vector3 direction = reference * MathF.Cos(step) + perpendicular * MathF.Sin(step);
            Vector3 point = pivot + direction * radius;
            output.Line(previous, point, color);
            previous = point;
        }

        output.Line(pivot, previous, color);
    }
}
