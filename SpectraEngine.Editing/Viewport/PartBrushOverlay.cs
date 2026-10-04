using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Draws the edges of every <see cref="BrushKind.Part"/> brush, so a part can
/// be told from world geometry.
/// </summary>
// A part looks like a world brush at rest but does not carve or weld, so
// overlapping parts interpenetrate and coplanar faces z-fight.
public sealed class PartBrushOverlay
{
    /// <summary>The default outline colour, cyan.</summary>
    public static readonly Vector3 DefaultColor = new(0.25f, 0.85f, 0.95f);

    /// <summary>Whether the overlay draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Outline colour.</summary>
    public Vector3 Color { get; set; } = DefaultColor;

    /// <summary>How many part brushes may be outlined in one frame.</summary>
    public int MaxOutlines { get; set; } = 256;

    /// <summary>Part brushes the last <see cref="Draw"/> outlined.</summary>
    public int DrawnLastDraw { get; private set; }

    /// <summary>
    /// Part brushes the last <see cref="Draw"/> skipped because
    /// <see cref="MaxOutlines"/> was reached.
    /// </summary>
    public int SkippedLastDraw { get; private set; }

    /// <summary>
    /// Outlines every part brush in <paramref name="scene"/> into the depth-off
    /// line pass. Render thread, once per frame.
    /// </summary>
    public void Draw(DebugDraw output, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scene);

        DrawnLastDraw = 0;
        SkippedLastDraw = 0;
        if (!Enabled)
            return;

        // The scene's part set, not a graph walk over the whole world.
        foreach (SceneNode node in scene.PartBrushNodes)
        {
            if (node.Brush is not { } brush)
                continue;

            if (DrawnLastDraw >= MaxOutlines)
            {
                SkippedLastDraw++;
                continue;
            }

            DrawnLastDraw++;
            DrawBrushEdges(output, brush, node.WorldMatrix, Color);
        }
    }

    /// <summary>Draws one brush's face loops under <paramref name="world"/>.</summary>
    // Shared edges are drawn twice; cheaper than building an edge set per frame.
    public static void DrawBrushEdges(DebugDraw output, Brush brush, Matrix4x4 world, Vector3 color)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(brush);

        IReadOnlyList<Polygon> faces = brush.LocalFaces;
        for (int f = 0; f < faces.Count; f++)
        {
            ReadOnlySpan<Vector3> verts = faces[f].VertexSpan;
            if (verts.Length < 2)
                continue;

            Vector3 previous = Vector3.Transform(verts[^1], world);
            for (int v = 0; v < verts.Length; v++)
            {
                Vector3 current = Vector3.Transform(verts[v], world);
                output.Line(previous, current, color);
                previous = current;
            }
        }
    }
}
