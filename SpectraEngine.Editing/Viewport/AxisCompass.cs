using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// The three world axes, drawn in a corner of the viewport at a fixed screen
/// size, with a line-drawn letter on each.
/// </summary>
// Overlay lane, not the world lane: nothing may occlude the compass.
// The letters are line segments because the viewport cannot draw text.
public sealed class AxisCompass
{
    /// <summary>Whether the compass draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The compass's arm length, in screen pixels.</summary>
    public float SizePixels { get; set; } = 30f;

    /// <summary>Distance from the viewport's bottom-right corner, in pixels.</summary>
    public float MarginPixels { get; set; } = 52f;

    // Display colours: the overlay lane skips the tone curve.
    // Same hues as the gizmo handles and the inspector's axis letters.
    private static readonly Vector3 XColor = new(1f, 0.35f, 0.33f);
    private static readonly Vector3 YColor = new(0.42f, 0.85f, 0.36f);
    private static readonly Vector3 ZColor = new(0.38f, 0.58f, 1f);

    // Short stub on the negative side, so +X and -X can be told apart.
    private const float NegativeFraction = 0.38f;

    /// <summary>
    /// Emits the compass into <paramref name="output"/> for
    /// <paramref name="camera"/>.
    /// </summary>
    /// <param name="output">The depth-off overlay buffer.</param>
    public void Draw(DebugDraw output, Camera camera, Vector2 viewportSize)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(camera);

        if (!Enabled || viewportSize.X <= 0f || viewportSize.Y <= 0f)
            return;

        // Well clear of the near plane.
        const float Depth = 4f;

        float worldPerPixel = GizmoMath.WorldPerPixel(camera, viewportSize.Y, Depth);
        if (worldPerPixel <= 0f)
            return;

        float arm = SizePixels * worldPerPixel;

        float inset = MarginPixels + SizePixels;
        float right = ((viewportSize.X * 0.5f) - inset) * worldPerPixel;
        float down = ((viewportSize.Y * 0.5f) - inset) * worldPerPixel;

        Vector3 origin = camera.Position
            + (camera.Forward * Depth)
            + (camera.Right * right)
            - (camera.Up * down);

        DrawAxis(output, origin, Vector3.UnitX, arm, XColor);
        DrawAxis(output, origin, Vector3.UnitY, arm, YColor);
        DrawAxis(output, origin, Vector3.UnitZ, arm, ZColor);

        // Letters at the arm tips, billboarded to the camera.
        float glyph = arm * 0.30f;
        DrawX(output, origin + (Vector3.UnitX * (arm + (glyph * 1.6f))), camera, glyph, XColor);
        DrawY(output, origin + (Vector3.UnitY * (arm + (glyph * 1.6f))), camera, glyph, YColor);
        DrawZ(output, origin + (Vector3.UnitZ * (arm + (glyph * 1.6f))), camera, glyph, ZColor);
    }

    private static void DrawAxis(DebugDraw output, Vector3 origin, Vector3 axis, float arm, Vector3 color)
    {
        output.Line(origin, origin + (axis * arm), color);
        output.Line(origin, origin - (axis * arm * NegativeFraction), color * 0.35f);
    }

    private static void DrawX(DebugDraw output, Vector3 at, Camera camera, float s, Vector3 color)
    {
        Vector3 r = camera.Right * s * 0.55f;
        Vector3 u = camera.Up * s;
        output.Line(at - r - u, at + r + u, color);
        output.Line(at - r + u, at + r - u, color);
    }

    private static void DrawY(DebugDraw output, Vector3 at, Camera camera, float s, Vector3 color)
    {
        Vector3 r = camera.Right * s * 0.55f;
        Vector3 u = camera.Up * s;
        output.Line(at - r + u, at, color);
        output.Line(at + r + u, at, color);
        output.Line(at, at - u, color);
    }

    private static void DrawZ(DebugDraw output, Vector3 at, Camera camera, float s, Vector3 color)
    {
        Vector3 r = camera.Right * s * 0.55f;
        Vector3 u = camera.Up * s;
        output.Line(at - r + u, at + r + u, color);
        output.Line(at + r + u, at - r - u, color);
        output.Line(at - r - u, at + r - u, color);
    }
}
