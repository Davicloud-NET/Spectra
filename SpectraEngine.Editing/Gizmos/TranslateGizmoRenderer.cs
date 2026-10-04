using SpectraEngine.Core.Graphics;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// Draws the translate gizmo into a <see cref="DebugDraw"/>: an arrow per axis
/// handle the style offers, its plane quads, and its screen-facing centre disc,
/// with the hovered or active handle highlighted. Render thread only.
/// </summary>
public static class TranslateGizmoRenderer
{
    /// <summary>Pushes the whole gizmo into <paramref name="output"/>.</summary>
    /// <param name="highlighted">The hovered or active handle, or <see cref="GizmoHandle.None"/>.</param>
    public static void Draw(DebugDraw output, in GizmoGeometry geometry, GizmoHandle highlighted)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (geometry.IsBehindCamera || geometry.AxisLength <= 0f)
            return;

        for (GizmoHandle handle = GizmoHandle.AxisX; handle <= geometry.LastAxisHandle; handle++)
            DrawAxis(output, in geometry, handle, GizmoColors.For(handle, highlighted));

        for (GizmoHandle handle = GizmoHandle.PlaneYZ; handle <= GizmoHandle.PlaneXY; handle++)
            DrawPlane(output, in geometry, handle, GizmoColors.For(handle, highlighted));

        if (geometry.Offers(GizmoHandle.Screen))
        {
            GizmoColors.DrawCircle(
                output,
                geometry.Pivot,
                geometry.ViewRight,
                geometry.ViewUp,
                geometry.ScreenRadius,
                GizmoColors.For(GizmoHandle.Screen, highlighted));
        }
    }

    private static void DrawAxis(
        DebugDraw output, in GizmoGeometry geometry, GizmoHandle handle, Vector3 color)
    {
        if (!geometry.TryGetAxisSegment(handle, out Vector3 start, out Vector3 tip))
            return;

        output.Line(start, tip, color);

        // Four blades, not DebugDraw.Arrow's two: a two-line head vanishes edge-on.
        Vector3 direction = geometry.Axis(handle);
        geometry.AxisPerpendiculars(handle, out Vector3 first, out Vector3 second);

        Vector3 baseCentre = tip - direction * geometry.HeadLength;
        float radius = geometry.HeadRadius;
        Vector3 offsetA = first * radius;
        Vector3 offsetB = second * radius;

        Vector3 cornerA = baseCentre + offsetA;
        Vector3 cornerB = baseCentre + offsetB;
        Vector3 cornerC = baseCentre - offsetA;
        Vector3 cornerD = baseCentre - offsetB;

        output.Line(tip, cornerA, color);
        output.Line(tip, cornerB, color);
        output.Line(tip, cornerC, color);
        output.Line(tip, cornerD, color);

        output.Line(cornerA, cornerB, color);
        output.Line(cornerB, cornerC, color);
        output.Line(cornerC, cornerD, color);
        output.Line(cornerD, cornerA, color);
    }

    private static void DrawPlane(
        DebugDraw output, in GizmoGeometry geometry, GizmoHandle handle, Vector3 color)
    {
        if (!geometry.TryGetPlaneQuad(handle, out Vector3 corner, out Vector3 first, out Vector3 second, out float size))
            return;

        Vector3 u = first * size;
        Vector3 v = second * size;

        output.Line(corner, corner + u, color);
        output.Line(corner + u, corner + u + v, color);
        output.Line(corner + u + v, corner + v, color);
        output.Line(corner + v, corner, color);
    }
}
