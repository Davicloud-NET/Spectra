using SpectraEngine.Core.Graphics;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Gizmos;

/// <summary>
/// Draws the scale gizmo into a <see cref="DebugDraw"/>: a cube-capped shaft per
/// axis handle the style offers, plus a centre cube for a uniform resize.
/// Render thread only.
/// </summary>
public static class ScaleGizmoRenderer
{
    /// <summary>Pushes the whole gizmo into <paramref name="output"/>.</summary>
    /// <param name="highlighted">The hovered or active handle, or <see cref="GizmoHandle.None"/>.</param>
    public static void Draw(DebugDraw output, in GizmoGeometry geometry, GizmoHandle highlighted)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (geometry.IsBehindCamera || geometry.AxisLength <= 0f)
            return;

        for (GizmoHandle handle = GizmoHandle.AxisX; handle <= geometry.LastAxisHandle; handle++)
        {
            Vector3 color = GizmoColors.For(handle, highlighted);

            if (geometry.TryGetAxisSegment(handle, out Vector3 start, out Vector3 tip))
                output.Line(start, tip, color);

            if (geometry.TryGetHandleBox(handle, out Vector3 centre, out float radius))
                GizmoColors.DrawBox(output, in geometry, centre, radius, color);
        }

        if (geometry.TryGetHandleBox(GizmoHandle.Screen, out Vector3 uniform, out float uniformRadius))
        {
            GizmoColors.DrawBox(
                output, in geometry, uniform, uniformRadius, GizmoColors.For(GizmoHandle.Screen, highlighted));
        }
    }
}
