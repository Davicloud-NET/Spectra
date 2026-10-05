using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Draws the edges of every part brush whose
/// <see cref="SceneNode.IsRendered"/> is off, such as a trigger volume or a
/// clip brush.
/// </summary>
// These render nothing of their own, so this is the only place one can be seen.
public sealed class VolumeOverlay
{
    /// <summary>The default outline colour, yellow.</summary>
    public static readonly Vector3 DefaultColor = new(0.95f, 0.85f, 0.25f);

    /// <summary>Whether the overlay draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Outline colour.</summary>
    public Vector3 Color { get; set; } = DefaultColor;

    /// <summary>How many volumes may be outlined in one frame.</summary>
    public int MaxOutlines { get; set; } = 256;

    /// <summary>Volumes the last <see cref="Draw"/> outlined.</summary>
    public int DrawnLastDraw { get; private set; }

    /// <summary>
    /// Volumes the last <see cref="Draw"/> skipped because
    /// <see cref="MaxOutlines"/> was reached.
    /// </summary>
    public int SkippedLastDraw { get; private set; }

    /// <summary>
    /// Outlines every hidden part brush in <paramref name="scene"/> into the
    /// depth-off line pass. Render thread, once per frame.
    /// </summary>
    public void Draw(DebugDraw output, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scene);

        DrawnLastDraw = 0;
        SkippedLastDraw = 0;
        if (!Enabled)
            return;

        foreach (SceneNode node in scene.HiddenBrushNodes)
        {
            if (node.Brush is not { } brush)
                continue;

            if (DrawnLastDraw >= MaxOutlines)
            {
                SkippedLastDraw++;
                continue;
            }

            DrawnLastDraw++;
            PartBrushOverlay.DrawBrushEdges(output, brush, node.WorldMatrix, Color);
        }
    }
}
