using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Draws a marker for every entity node that has nothing else to show it: its
/// class's icon in its group's colour, and an arrow along the way it faces
/// when the class has a facing.
/// </summary>
// A point entity has no brush, mesh or light and is not in the spatial index,
// so the marker is the only thing to see or click. It is sized by the light
// icon's rule and drawn with the same pen, so the two sit alike. The icon is
// the file the shell shows for the class.
public sealed class EntityMarkerOverlay
{
    // Under this radius an icon is a smudge, so a far marker is a small
    // diamond and nothing else.
    private const float DetailPixels = 6.5f;

    private Mark[] _marks = [];
    private float[] _nearness = [];

    /// <summary>Whether the overlay draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many markers may be drawn in one frame. The rest are counted in
    /// <see cref="SkippedLastDraw"/>.
    /// </summary>
    public int MaxMarkers { get; set; } = 256;

    /// <summary>
    /// How many markers may be drawn as an icon in one frame. When more are
    /// near enough to read, the nearest get the icon and the rest a diamond.
    /// </summary>
    // An icon costs ten times the lines of a diamond.
    public int MaxIcons { get; set; } = 64;

    /// <summary>Markers the last draw drew.</summary>
    public int DrawnLastDraw { get; private set; }

    /// <summary>Markers the last draw drew as an icon.</summary>
    public int IconsLastDraw { get; private set; }

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
        IconsLastDraw = 0;
        SkippedLastDraw = 0;

        if (!Enabled || viewportSize.Y <= 0f)
            return;

        IReadOnlyList<SceneNode> nodes = scene.EntityNodes;
        int count = Measure(nodes, camera, viewportSize);
        float farthestIcon = FarthestIcon(count);

        for (int i = 0; i < count; i++)
        {
            ref readonly Mark mark = ref _marks[i];
            SceneNode node = nodes[mark.Node];

            string? className = node.Entity?.ClassName;
            EntitySchema? schema = null;
            scene.EntitySchemas?.TryGetSchema(className, out schema);

            ViewportGlyph? icon = null;
            if (mark.IsDetailed && mark.Pixel <= farthestIcon && IconsLastDraw < MaxIcons &&
                TryFindIcon(WorldIcons.ForEntity(scene.EntitySchemas, className), out icon))
            {
                IconsLastDraw++;
            }

            var pen = new IconPen(camera, node.WorldPosition, mark.Radius, mark.Pixel, EntityGroupTints.For(schema?.Group));

            // A class no schema knows gets no arrow: nothing says it faces anywhere.
            bool faces = icon is not null && schema is { HasFacing: true };

            // Every backing line first, or one stroke's backing crosses out
            // the colour of the stroke before it.
            pen.Backing = true;
            DrawMarker(output, node, in pen, icon, faces);
            pen.Backing = false;
            DrawMarker(output, node, in pen, icon, faces);
        }

        DrawnLastDraw = count;
    }

    // Fills _marks with the markers this frame draws, in scene order. A node
    // is named by its place in the list, so nothing here outlives the frame.
    private int Measure(IReadOnlyList<SceneNode> nodes, Camera camera, Vector2 viewportSize)
    {
        if (_marks.Length < MaxMarkers)
        {
            _marks = new Mark[MaxMarkers];
            _nearness = new float[MaxMarkers];
        }

        int count = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode node = nodes[i];
            if (!EntityMarkerPicking.HasMarker(node))
                continue;

            if (count >= MaxMarkers)
            {
                SkippedLastDraw++;
                continue;
            }

            if (LightPicking.TryMeasure(camera, viewportSize, node.WorldPosition, out float radius, out float pixel))
                _marks[count++] = new Mark(i, radius, pixel, radius >= DetailPixels * pixel);
        }

        return count;
    }

    // The largest pixel size a marker may have and still get an icon. A pixel
    // covers more of the world the farther off it is, so this picks the nearest.
    private float FarthestIcon(int count)
    {
        int detailed = 0;
        for (int i = 0; i < count; i++)
        {
            if (_marks[i].IsDetailed)
                _nearness[detailed++] = _marks[i].Pixel;
        }

        if (detailed <= MaxIcons)
            return float.PositiveInfinity;

        if (MaxIcons <= 0)
            return float.NegativeInfinity;

        Array.Sort(_nearness, 0, detailed);
        return _nearness[MaxIcons - 1];
    }

    // The icon's own glyph, or the plain entity one when its file gave none.
    private static bool TryFindIcon(string key, out ViewportGlyph? icon) =>
        ViewportGlyphs.TryGet(key, out icon) || ViewportGlyphs.TryGet(WorldIcons.Entity, out icon);

    private static void DrawMarker(DebugDraw output, SceneNode node, in IconPen pen, ViewportGlyph? icon, bool faces)
    {
        if (icon is null)
        {
            // Hairlines here: the doubled stroke would fill a diamond this small.
            float small = pen.Radius * 0.8f;
            if (pen.Backing)
            {
                ThinDiamond(output, in pen, small + pen.Pixel, IconPen.BackingColor);
            }
            else
            {
                ThinDiamond(output, in pen, small, pen.Color);
                ThinDiamond(output, in pen, small - pen.Pixel, pen.Color);
            }

            return;
        }

        pen.Glyph(output, icon);

        if (faces)
            pen.Pointer(output, Forward(node));
    }

    // The node's local +Z in the world, the row a light's direction is read from.
    private static Vector3 Forward(SceneNode node)
    {
        Matrix4x4 world = node.WorldMatrix;
        var forward = new Vector3(world.M31, world.M32, world.M33);
        return forward.LengthSquared() > 1e-12f ? Vector3.Normalize(forward) : Vector3.UnitZ;
    }

    private static void ThinDiamond(DebugDraw output, in IconPen pen, float radius, Vector3 color)
    {
        Vector3 across = pen.Right * radius;
        Vector3 along = pen.Up * radius;

        output.Line(pen.At + across, pen.At + along, color);
        output.Line(pen.At + along, pen.At - across, color);
        output.Line(pen.At - across, pen.At - along, color);
        output.Line(pen.At - along, pen.At + across, color);
    }

    // One marker of this frame: which node, how big, and whether it is near
    // enough for an icon.
    private readonly record struct Mark(int Node, float Radius, float Pixel, bool IsDetailed);
}
