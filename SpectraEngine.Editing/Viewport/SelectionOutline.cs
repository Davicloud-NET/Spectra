using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Hosting;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Outlines the selection in each node's own shape: brush edges, an oriented
/// mesh box, or a cross for a group.
/// </summary>
public sealed class SelectionOutline
{
    /// <summary>Selected, at full weight.</summary>
    public static readonly Vector3 SelectedColor = new(1f, 0.50f, 0.12f);

    /// <summary>Hovered: the same hue, lower value.</summary>
    public static readonly Vector3 HoverColor = new(0.42f, 0.21f, 0.05f);

    /// <summary>Whether the outline draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many nodes may be outlined in full before the pass falls back to
    /// bounds boxes.
    /// </summary>
    public int MaxOutlines { get; set; } = 64;

    /// <summary>Nodes the last draw outlined in their own shape.</summary>
    public int DrawnLastDraw { get; private set; }

    /// <summary>Nodes the last draw could only box.</summary>
    public int SkippedLastDraw { get; private set; }

    private const float GroupCrossPixels = 9f;

    /// <summary>Outlines the scene's selection, and the hovered node beneath it.</summary>
    /// <param name="hovered">What the cursor is over, or null. Skipped when already selected.</param>
    public void Draw(DebugDraw output, Scene scene, Camera camera, Vector2 viewportSize, SceneNode? hovered) =>
        Draw(output, scene, camera, viewportSize, new OutlineFocus(hovered, -1, null));

    /// <summary>
    /// Draws the selection, the hover, the picked face and what a material drag
    /// would land on. A drag's target replaces the hover outline.
    /// </summary>
    public void Draw(
        DebugDraw output, Scene scene, Camera camera, Vector2 viewportSize, in OutlineFocus focus)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        DrawnLastDraw = 0;
        SkippedLastDraw = 0;

        if (!Enabled)
            return;

        IReadOnlyList<SceneNode> selection = scene.Selection.Items;
        SceneNode? hovered = focus.Hovered;

        if (focus.MaterialDrag is { } scope && hovered?.Brush is { } dragged)
        {
            // Only what letting go would paint.
            if (scope == MaterialDropScope.Brush || focus.HoveredPlane < 0)
            {
                PartBrushOverlay.DrawBrushEdges(output, dragged, hovered.WorldMatrix, SelectedColor);
            }
            else
            {
                DrawFaceLoop(output, dragged, hovered.WorldMatrix, focus.HoveredPlane, SelectedColor);
            }

            DrawnLastDraw++;
        }
        else if (hovered is not null && !selection.Contains(hovered))
        {
            DrawNode(output, scene, camera, viewportSize, hovered, HoverColor);
        }

        for (int i = 0; i < selection.Count; i++)
        {
            SceneNode node = selection[i];

            if (DrawnLastDraw >= MaxOutlines)
            {
                if (scene.TryGetWorldBounds(node, out Aabb fallback))
                    output.Box(fallback.Min, fallback.Max, SelectedColor);

                SkippedLastDraw++;
                continue;
            }

            // A picked face outranks its brush: the brush drops to hover weight.
            bool picked = focus.MaterialDrag is null &&
                focus.PickedFaceNode is not null &&
                ReferenceEquals(focus.PickedFaceNode, node) &&
                node.Brush is not null;

            DrawNode(output, scene, camera, viewportSize, node, picked ? HoverColor : SelectedColor);

            if (picked && node.Brush is { } pickedBrush)
                DrawFaceLoop(output, pickedBrush, node.WorldMatrix, focus.PickedFacePlane, SelectedColor);

            DrawnLastDraw++;
        }

        // One faint box around a multi-selection: the box the Studio handles
        // stand on.
        if (selection.Count > 1)
        {
            Vector3 lo = new(float.PositiveInfinity);
            Vector3 hi = new(float.NegativeInfinity);
            bool any = false;

            for (int i = 0; i < selection.Count; i++)
            {
                if (!scene.TryGetWorldBounds(selection[i], out Aabb next))
                    continue;

                lo = Vector3.Min(lo, next.Min);
                hi = Vector3.Max(hi, next.Max);
                any = true;
            }

            if (any)
                output.Box(lo, hi, SelectedColor * 0.4f);
        }
    }

    /// <summary>
    /// Draws one plane's face loop. Draws nothing for a plane its neighbours
    /// clipped away, which has no polygon.
    /// </summary>
    public static void DrawFaceLoop(
        DebugDraw output, Brush brush, Matrix4x4 world, int planeIndex, Vector3 color)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(brush);

        if (!brush.TryGetPlaneFace(planeIndex, out Polygon face)) return;

        ReadOnlySpan<Vector3> verts = face.VertexSpan;
        if (verts.Length < 2) return;

        Vector3 previous = Vector3.Transform(verts[^1], world);
        for (int v = 0; v < verts.Length; v++)
        {
            Vector3 current = Vector3.Transform(verts[v], world);
            output.Line(previous, current, color);
            previous = current;
        }
    }

    private static void DrawNode(
        DebugDraw output, Scene scene, Camera camera, Vector2 viewportSize, SceneNode node, Vector3 color)
    {
        if (node.Brush is { } brush)
        {
            PartBrushOverlay.DrawBrushEdges(output, brush, node.WorldMatrix, color);
            return;
        }

        // Local bounds under the world matrix: an oriented box, so a rotated
        // mesh is not over-claimed.
        if (node.MeshRenderer?.Mesh is { HasLocalBounds: true } mesh)
        {
            DrawOrientedBox(output, mesh.LocalBounds, node.WorldMatrix, color);
            return;
        }

        // A group, or a mesh with no bounds: a cross at the origin, sized in
        // screen space so it is visible from any distance.
        float depth = MathF.Max(GizmoMath.ViewDepth(camera, node.WorldPosition), 0.01f);
        float size = GroupCrossPixels * GizmoMath.WorldPerPixel(camera, viewportSize.Y, depth);
        output.Cross(node.WorldPosition, MathF.Max(size, 0.01f), color);

        // Plus the subtree's extent, faint.
        if (scene.TryGetWorldBounds(node, out Aabb bounds))
            output.Box(bounds.Min, bounds.Max, color * 0.4f);
    }

    /// <summary>Draws the twelve edges of a local box transformed by <paramref name="world"/>.</summary>
    public static void DrawOrientedBox(DebugDraw output, Aabb local, Matrix4x4 world, Vector3 color)
    {
        ArgumentNullException.ThrowIfNull(output);

        Span<Vector3> corners = stackalloc Vector3[8];
        Vector3 lo = local.Min;
        Vector3 hi = local.Max;

        for (int i = 0; i < 8; i++)
        {
            var p = new Vector3(
                (i & 1) == 0 ? lo.X : hi.X,
                (i & 2) == 0 ? lo.Y : hi.Y,
                (i & 4) == 0 ? lo.Z : hi.Z);

            corners[i] = Vector3.Transform(p, world);
        }

        // Two corners share an edge when their indices differ in one bit.
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit <= 4; bit <<= 1)
            {
                if ((i & bit) == 0)
                    output.Line(corners[i], corners[i | bit], color);
            }
        }
    }
}
