using SpectraEngine.Core.Graphics;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>The palette and line primitives the manipulators share.</summary>
// Axis colours match DebugVisualizations' scene-graph axes.
public static class GizmoColors
{
    private static readonly Vector3 AxisXColor = new(1f, 0.25f, 0.25f);
    private static readonly Vector3 AxisYColor = new(0.25f, 1f, 0.25f);
    private static readonly Vector3 AxisZColor = new(0.35f, 0.55f, 1f);
    private static readonly Vector3 ScreenColor = new(0.85f, 0.85f, 0.9f);

    // Yellow: no other viewport overlay uses it.
    private static readonly Vector3 HighlightColor = new(1f, 0.9f, 0.2f);

    /// <summary>
    /// The colour a handle is drawn in: its axis colour, or the highlight when
    /// it is the hovered or active one.
    /// </summary>
    public static Vector3 For(GizmoHandle handle, GizmoHandle highlighted)
    {
        if (handle == highlighted && handle != GizmoHandle.None)
            return HighlightColor;

        return GizmoHandles.PositiveAxis(handle) switch
        {
            GizmoHandle.AxisX => AxisXColor,
            GizmoHandle.AxisY => AxisYColor,
            GizmoHandle.AxisZ => AxisZColor,
            _ => handle switch
            {
                // A plane quad takes the colour of its normal axis.
                GizmoHandle.PlaneYZ => AxisXColor,
                GizmoHandle.PlaneZX => AxisYColor,
                GizmoHandle.PlaneXY => AxisZColor,
                _ => ScreenColor,
            },
        };
    }

    /// <summary>The colour any highlighted handle is drawn in.</summary>
    public static Vector3 Highlight => HighlightColor;

    /// <summary>
    /// Draws a circle in the plane perpendicular to <paramref name="axis"/>,
    /// using the same segments and basis the hit tester walks.
    /// </summary>
    public static void DrawCircle(DebugDraw output, Vector3 centre, Vector3 axis, float radius, Vector3 color)
    {
        ArgumentNullException.ThrowIfNull(output);
        GizmoHitTesting.BuildRingBasis(axis, out Vector3 u, out Vector3 v);
        DrawCircle(output, centre, u, v, radius, color);
    }

    /// <summary>Draws a circle in the plane spanned by two unit vectors.</summary>
    public static void DrawCircle(
        DebugDraw output, Vector3 centre, Vector3 first, Vector3 second, float radius, Vector3 color)
    {
        ArgumentNullException.ThrowIfNull(output);

        Vector3 previous = centre + first * radius;
        for (int i = 1; i <= GizmoHitTesting.RingSegments; i++)
        {
            float angle = MathF.Tau * i / GizmoHitTesting.RingSegments;
            Vector3 point = centre + (first * MathF.Cos(angle) + second * MathF.Sin(angle)) * radius;
            output.Line(previous, point, color);
            previous = point;
        }
    }

    /// <summary>
    /// Draws a wire cube of half-extent <paramref name="radius"/> oriented by
    /// the gizmo frame. The same box the scale gizmo's pick tests against.
    /// </summary>
    public static void DrawBox(
        DebugDraw output, in GizmoGeometry geometry, Vector3 centre, float radius, Vector3 color)
    {
        ArgumentNullException.ThrowIfNull(output);

        Vector3 x = geometry.AxisX * radius;
        Vector3 y = geometry.AxisY * radius;
        Vector3 z = geometry.AxisZ * radius;

        Vector3 a = centre - x - y - z;
        Vector3 b = centre + x - y - z;
        Vector3 c = centre + x + y - z;
        Vector3 d = centre - x + y - z;
        Vector3 e = centre - x - y + z;
        Vector3 f = centre + x - y + z;
        Vector3 g = centre + x + y + z;
        Vector3 h = centre - x + y + z;

        output.Line(a, b, color);
        output.Line(b, c, color);
        output.Line(c, d, color);
        output.Line(d, a, color);

        output.Line(e, f, color);
        output.Line(f, g, color);
        output.Line(g, h, color);
        output.Line(h, e, color);

        output.Line(a, e, color);
        output.Line(b, f, color);
        output.Line(c, g, color);
        output.Line(d, h, color);
    }
}
