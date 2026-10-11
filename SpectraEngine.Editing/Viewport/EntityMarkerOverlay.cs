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
    private readonly SceneIcons _icons;

    /// <summary>Creates the overlay.</summary>
    /// <param name="icons">
    /// Where the markers stand. Give it the one the viewport picks with, so a
    /// budget set here is the budget a click sees.
    /// </param>
    public EntityMarkerOverlay(SceneIcons? icons = null) => _icons = icons ?? new SceneIcons();

    /// <summary>Whether the overlay draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many markers may be drawn in one frame. The rest are counted in
    /// <see cref="SkippedLastDraw"/>.
    /// </summary>
    public int MaxMarkers
    {
        get => _icons.MaxMarkers;
        set => _icons.MaxMarkers = value;
    }

    /// <summary>
    /// How many markers may be drawn as an icon in one frame. When more are
    /// near enough to read, the nearest get the icon and the rest a diamond.
    /// </summary>
    public int MaxIcons
    {
        get => _icons.MaxMarkerGlyphs;
        set => _icons.MaxMarkerGlyphs = value;
    }

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
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        if (Enabled)
            _icons.Place(scene, camera, viewportSize);

        DrawPlaced(output, scene, camera);
    }

    /// <summary>
    /// Draws the markers where they were last placed. For a host that places
    /// the icons once a frame for every overlay that shares them.
    /// </summary>
    /// <param name="output">The depth-off overlay buffer.</param>
    public void DrawPlaced(DebugDraw output, Scene scene, Camera camera)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        DrawnLastDraw = 0;
        IconsLastDraw = 0;
        SkippedLastDraw = 0;

        if (!Enabled)
            return;

        ReadOnlySpan<SceneIcon> markers = _icons.Markers;
        IReadOnlyList<SceneNode> nodes = scene.EntityNodes;
        SkippedLastDraw = _icons.SkippedMarkers;

        // The lines home first, so they lie under every glyph.
        ReadOnlySpan<SceneIcon> all = _icons.All;
        for (int i = 0; i < markers.Length; i++)
        {
            ref readonly SceneIcon marker = ref markers[i];
            if (!marker.IsDrawn || !marker.IsMoved)
                continue;

            IconPen pen = PenFor(scene, camera, nodes[marker.Node], in marker, out _);
            IconHome.Draw(output, in pen, all, all.Length - markers.Length + i);
        }

        foreach (ref readonly SceneIcon marker in markers)
        {
            if (!marker.IsDrawn)
                continue;

            SceneNode node = nodes[marker.Node];
            IconPen pen = PenFor(scene, camera, node, in marker, out EntitySchema? schema);

            ViewportGlyph? icon = null;
            if (marker.IsGlyph && TryFindIcon(WorldIcons.ForEntity(scene.EntitySchemas, node.Entity?.ClassName), out icon))
                IconsLastDraw++;

            // A class no schema knows gets no arrow: nothing says it faces anywhere.
            bool faces = icon is not null && schema is { HasFacing: true };

            // Every backing line first, or one stroke's backing crosses out
            // the colour of the stroke before it.
            pen.Backing = true;
            DrawMarker(output, node, in pen, icon, faces);
            pen.Backing = false;
            DrawMarker(output, node, in pen, icon, faces);

            DrawnLastDraw++;
        }
    }

    // The pen for one marker, in its group's colour, where the marker stands.
    private static IconPen PenFor(
        Scene scene, Camera camera, SceneNode node, in SceneIcon marker, out EntitySchema? schema)
    {
        schema = null;
        scene.EntitySchemas?.TryGetSchema(node.Entity?.ClassName, out schema);

        return new IconPen(camera, marker.At, marker.Radius, marker.Pixel, EntityGroupTints.For(schema?.Group));
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
                pen.ThinDiamond(output, pen.At, small + pen.Pixel, IconPen.BackingColor);
            }
            else
            {
                pen.ThinDiamond(output, pen.At, small, pen.Color);
                pen.ThinDiamond(output, pen.At, small - pen.Pixel, pen.Color);
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
}
