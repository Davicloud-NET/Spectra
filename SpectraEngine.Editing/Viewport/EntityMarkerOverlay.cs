using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Draws a marker for every entity node that has nothing else to show it,
/// with an arrow along the way it faces.
/// </summary>
// A point entity has no brush, mesh or light and is not in the spatial index,
// so the marker is the only thing to see or click. It is sized by the light
// icon's rule and drawn with the same pen, so the two sit alike.
public sealed class EntityMarkerOverlay
{
    /// <summary>The default marker colour, green.</summary>
    public static readonly Vector3 DefaultColor = new(0.4f, 0.9f, 0.45f);

    // Under this radius the arrow is a smudge, so a far marker is a small
    // diamond and nothing else.
    private const float DetailPixels = 6.5f;

    private const float BodyRadius = 0.55f;
    private const float ArrowLength = 2.2f;

    /// <summary>Whether the overlay draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Marker colour.</summary>
    public Vector3 Color { get; set; } = DefaultColor;

    /// <summary>
    /// How many markers may be drawn in one frame. The rest are counted in
    /// <see cref="SkippedLastDraw"/>.
    /// </summary>
    public int MaxMarkers { get; set; } = 256;

    /// <summary>Markers the last draw drew.</summary>
    public int DrawnLastDraw { get; private set; }

    /// <summary>Markers the last draw skipped because <see cref="MaxMarkers"/> was reached.</summary>
    public int SkippedLastDraw { get; private set; }

    /// <summary>Draws a marker per entity node that <see cref="EntityMarkerPicking.HasMarker"/> accepts.</summary>
    /// <param name="output">The depth-off overlay buffer.</param>
    public void Draw(DebugDraw output, Scene scene, Camera camera, Vector2 viewportSize)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        DrawnLastDraw = 0;
        SkippedLastDraw = 0;

        if (!Enabled || viewportSize.Y <= 0f)
            return;

        IReadOnlyList<SceneNode> nodes = scene.EntityNodes;

        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode node = nodes[i];
            if (!EntityMarkerPicking.HasMarker(node))
                continue;

            if (DrawnLastDraw >= MaxMarkers)
            {
                SkippedLastDraw++;
                continue;
            }

            Vector3 at = node.WorldPosition;
            if (!LightPicking.TryMeasure(camera, viewportSize, at, out float radius, out float pixel))
                continue;

            var pen = new IconPen(camera, at, radius, pixel, Color);
            bool detailed = radius >= DetailPixels * pixel;
            bool faces = detailed && Faces(scene, node);

            // Every backing line first, or one stroke's backing crosses out
            // the colour of the stroke before it.
            pen.Backing = true;
            DrawMarker(output, node, in pen, detailed, faces);
            pen.Backing = false;
            DrawMarker(output, node, in pen, detailed, faces);

            DrawnLastDraw++;
        }
    }

    // A logic entity's transform means nothing, so an arrow on it would say
    // something false. A class the schemas do not know gets one.
    private static bool Faces(Scene scene, SceneNode node) =>
        scene.EntitySchemas is not { } schemas ||
        !schemas.TryGetSchema(node.Entity?.ClassName, out EntitySchema? schema) ||
        schema.Placement != EntityPlacement.Abstract;

    private static void DrawMarker(DebugDraw output, SceneNode node, in IconPen pen, bool detailed, bool faces)
    {
        if (!detailed)
        {
            // Hairlines here: the doubled stroke would fill a diamond this small.
            float small = pen.Radius * 0.8f;
            if (pen.Backing)
            {
                ThinDiamond(output, in pen, small + pen.Pixel, IconPen.BackingColour);
            }
            else
            {
                ThinDiamond(output, in pen, small, pen.Colour);
                ThinDiamond(output, in pen, small - pen.Pixel, pen.Colour);
            }

            return;
        }

        // A diamond facing the screen. A light's icon is round.
        Vector3 across = pen.Right * (pen.Radius * BodyRadius);
        Vector3 along = pen.Up * (pen.Radius * BodyRadius);

        pen.Stroke(output, pen.At + across, pen.At + along);
        pen.Stroke(output, pen.At + along, pen.At - across);
        pen.Stroke(output, pen.At - across, pen.At - along);
        pen.Stroke(output, pen.At - along, pen.At + across);

        if (!faces)
            return;

        Vector3 forward = Forward(node);
        pen.Arrow(output, pen.At, pen.At + (forward * pen.Radius * ArrowLength), forward);
    }

    // The node's local +Z in the world, the row a light's direction is read from.
    private static Vector3 Forward(SceneNode node)
    {
        Matrix4x4 world = node.WorldMatrix;
        var forward = new Vector3(world.M31, world.M32, world.M33);
        return forward.LengthSquared() > 1e-12f ? Vector3.Normalize(forward) : Vector3.UnitZ;
    }

    private static void ThinDiamond(DebugDraw output, in IconPen pen, float radius, Vector3 colour)
    {
        Vector3 across = pen.Right * radius;
        Vector3 along = pen.Up * radius;

        output.Line(pen.At + across, pen.At + along, colour);
        output.Line(pen.At + along, pen.At - across, colour);
        output.Line(pen.At - across, pen.At - along, colour);
        output.Line(pen.At - along, pen.At + across, colour);
    }
}
