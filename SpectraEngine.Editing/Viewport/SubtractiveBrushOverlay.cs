using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Draws the edges of every subtractive brush, with an inward tick per face
/// showing which way the solid is being removed.
/// </summary>
// A subtractive brush renders nothing of its own, world or part kind, so this
// is the only place it can be seen.
public sealed class SubtractiveBrushOverlay
{
    /// <summary>The default outline colour, magenta.</summary>
    public static readonly Vector3 DefaultColor = new(0.95f, 0.25f, 0.75f);

    /// <summary>Whether the overlay draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Outline colour.</summary>
    public Vector3 Color { get; set; } = DefaultColor;

    /// <summary>Inward tick length, as a fraction of the brush's bounding radius.</summary>
    public float TickFraction { get; set; } = 0.18f;

    /// <summary>How many subtractive brushes may be outlined in one frame.</summary>
    public int MaxOutlines { get; set; } = 256;

    /// <summary>Subtractive brushes the last <see cref="Draw"/> outlined.</summary>
    public int DrawnLastDraw { get; private set; }

    /// <summary>
    /// Subtractive brushes the last <see cref="Draw"/> skipped because
    /// <see cref="MaxOutlines"/> was reached.
    /// </summary>
    public int SkippedLastDraw { get; private set; }

    /// <summary>
    /// Outlines every subtractive brush in <paramref name="scene"/>. Render
    /// thread, once per frame, into the depth-off line pass.
    /// </summary>
    public void Draw(DebugDraw output, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scene);

        DrawnLastDraw = 0;
        SkippedLastDraw = 0;
        if (!Enabled)
            return;

        foreach (SceneNode node in scene.SubtractiveBrushNodes)
        {
            if (node.Brush is not { } brush)
                continue;

            if (DrawnLastDraw >= MaxOutlines)
            {
                SkippedLastDraw++;
                continue;
            }

            DrawnLastDraw++;
            Matrix4x4 world = node.WorldMatrix;
            PartBrushOverlay.DrawBrushEdges(output, brush, world, Color);
            DrawInwardTicks(output, brush, world, Color, TickFraction);
        }
    }

    private static void DrawInwardTicks(
        DebugDraw output, Brush brush, Matrix4x4 world, Vector3 color, float tickFraction)
    {
        Aabb bounds = brush.LocalBounds;
        float radius = (bounds.Max - bounds.Min).Length() * 0.5f;
        float tick = radius * tickFraction;
        if (tick <= 0f)
            return;

        IReadOnlyList<Polygon> faces = brush.LocalFaces;
        for (int f = 0; f < faces.Count; f++)
        {
            ReadOnlySpan<Vector3> verts = faces[f].VertexSpan;
            if (verts.Length == 0)
                continue;

            var centroid = Vector3.Zero;
            for (int v = 0; v < verts.Length; v++)
                centroid += verts[v];
            centroid /= verts.Length;

            // Face normals point outward, so the removed volume is behind them.
            Vector3 inward = -faces[f].Surface.Normal * tick;

            output.Line(
                Vector3.Transform(centroid, world),
                Vector3.Transform(centroid + inward, world),
                color);
        }
    }
}
