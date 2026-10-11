using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Draws an icon for every light, and the shape of each selected one.
/// </summary>
// Lights have no mesh and are not in the spatial index (that would make them
// collidable), so the icon is the only thing to see or click. It has to say
// three things without being selected: what kind of light, which way it
// points, and what colour it is. The kind is the icon file, the way is the
// arrow and the colour is the light's own.
public sealed class LightOverlay
{
    /// <summary>
    /// The icon's radius in the world, about a lamp's. It gets smaller with
    /// distance like everything else in the scene. Held to a constant screen
    /// size it grew against the level as the camera pulled back, until a far
    /// room was mostly icons.
    /// </summary>
    public const float IconWorldRadius = 0.4f;

    /// <summary>
    /// The largest the icon's radius gets on screen, in pixels, however close
    /// the camera is.
    /// </summary>
    public const float IconPixels = 14f;

    /// <summary>
    /// The smallest it gets, so a far light can still be seen and clicked.
    /// </summary>
    public const float MinIconPixels = 4f;

    /// <summary>
    /// How much of an icon's colour is left in the line from a moved icon
    /// back to its node, and in the mark on the node.
    /// </summary>
    // Half, so the line does not read as one of the icon's strokes.
    public const float HomeLineDim = 0.5f;

    private const float DisabledDim = 0.32f;

    private readonly SceneIcons _icons;

    /// <summary>Creates the overlay.</summary>
    /// <param name="icons">
    /// Where the icons stand. Give it the one the viewport picks with, so a
    /// budget set here is the budget a click sees.
    /// </param>
    public LightOverlay(SceneIcons? icons = null) => _icons = icons ?? new SceneIcons();

    /// <summary>Whether the overlay draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many light icons may be drawn in one frame. The rest are counted in
    /// <see cref="SkippedLastDraw"/>.
    /// </summary>
    public int MaxIcons
    {
        get => _icons.MaxLights;
        set => _icons.MaxLights = value;
    }

    /// <summary>Lights the last draw drew.</summary>
    public int DrawnLastDraw { get; private set; }

    /// <summary>Lights the last draw skipped because <see cref="MaxIcons"/> was reached.</summary>
    public int SkippedLastDraw { get; private set; }

    /// <summary>Draws an icon per light, plus the selected lights' shapes.</summary>
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
    /// Draws the icons where they were last placed, plus the selected lights'
    /// shapes. For a host that places the icons once a frame for every overlay
    /// that shares them.
    /// </summary>
    /// <param name="output">The depth-off overlay buffer.</param>
    public void DrawPlaced(DebugDraw output, Scene scene, Camera camera)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        DrawnLastDraw = 0;
        SkippedLastDraw = 0;

        if (!Enabled)
            return;

        ReadOnlySpan<SceneIcon> icons = _icons.Lights;
        IReadOnlyList<SceneNode> nodes = scene.LightNodes;
        SkippedLastDraw = _icons.SkippedLights;

        // The lines home first, so they lie under every glyph. Lights come
        // first among all the icons.
        for (int i = 0; i < icons.Length; i++)
        {
            ref readonly SceneIcon icon = ref icons[i];
            if (!icon.IsDrawn || !icon.IsMoved || icon.Node >= nodes.Count || nodes[icon.Node].Light is not { } light)
                continue;

            var pen = new IconPen(camera, icon.At, icon.Radius, icon.Pixel, IconColor(light));
            IconHome.Draw(output, in pen, _icons, i);
        }

        foreach (ref readonly SceneIcon icon in icons)
        {
            // A node can have left the scene since the icons were placed.
            if (!icon.IsDrawn || icon.Node >= nodes.Count)
                continue;

            SceneNode node = nodes[icon.Node];
            if (node.Light is not { } light)
                continue;

            var pen = new IconPen(camera, icon.At, icon.Radius, icon.Pixel, IconColor(light));

            ViewportGlyph? glyph = null;
            if (icon.IsGlyph)
                ViewportGlyphs.TryGet(IconOf(node, light, scene.EntitySchemas), out glyph);

            // Every backing line first, or one stroke's backing crosses out
            // the colour of the stroke before it.
            pen.Backing = true;
            DrawIcon(output, node, light, in pen, glyph);
            pen.Backing = false;
            DrawIcon(output, node, light, in pen, glyph);

            DrawnLastDraw++;
        }

        // Shapes for the selection only; one per light would bury the scene.
        // They stand on the light itself, wherever its icon was moved to.
        IReadOnlyList<SceneNode> selection = scene.Selection.Items;
        for (int i = 0; i < selection.Count; i++)
        {
            SceneNode node = selection[i];
            if (node.Light is { } light)
                DrawShape(output, node, light, camera);
        }
    }

    // The light's hue at full strength, so a dim lamp still shows its colour.
    // Grey when switched off.
    private static Vector3 IconColor(Light light)
    {
        if (!light.Enabled)
            return new Vector3(DisabledDim);

        float peak = MathF.Max(light.Color.X, MathF.Max(light.Color.Y, light.Color.Z));
        return peak > 1e-4f ? Vector3.Max(light.Color / peak, new Vector3(0.18f)) : Vector3.One;
    }

    // The file the shell shows for this light. A light that is an entity
    // shows its class, as its row in the scene tree does.
    private static string IconOf(SceneNode node, Light light, EntitySchemaCatalog? schemas) =>
        node.Entity is { } entity
            ? WorldIcons.ForEntity(schemas, entity.ClassName)
            : WorldIcons.ForLight(light.Kind);

    // With no glyph the light is a ring: it is too far off for a drawing.
    private static void DrawIcon(DebugDraw output, SceneNode node, Light light, in IconPen pen, ViewportGlyph? glyph)
    {
        if (glyph is null)
        {
            // Hairlines here: the doubled stroke would fill a ring this small.
            float ring = pen.Radius * 0.8f;
            if (pen.Backing)
            {
                pen.ThinRing(output, ring + pen.Pixel, IconPen.BackingColor);
            }
            else
            {
                pen.ThinRing(output, ring, pen.Color);
                pen.ThinRing(output, ring - pen.Pixel, pen.Color);
            }

            return;
        }

        pen.Glyph(output, glyph);

        // A bulb shines every way. Every other kind points somewhere.
        if (light.Kind == LightKind.Point)
            return;

        Basis(node, out Vector3 forward, out _, out _);
        pen.Pointer(output, forward);
    }

    private static void DrawShape(DebugDraw output, SceneNode node, Light light, Camera camera)
    {
        Vector3 at = node.WorldPosition;
        Vector3 colour = Vector3.Max(light.Color, new Vector3(0.35f));

        switch (light.Kind)
        {
            case LightKind.Directional:
                // Travel direction is the world matrix's third row, read the
                // same way Scene.CollectLights reads it so the arrow cannot
                // disagree with the light.
                Matrix4x4 world = node.WorldMatrix;
                var travel = Vector3.Normalize(new Vector3(world.M31, world.M32, world.M33));
                output.Arrow(at, at + (travel * 2f), colour);
                break;

            case LightKind.Point:
                WireRing.Sphere(output, at, light.Range, colour);
                break;

            case LightKind.Spot:
                DrawCone(output, node, light, colour);
                break;

            case LightKind.Rect:
            case LightKind.Disc:
                DrawArea(output, node, light, colour);
                break;
        }
    }

    // Outer cone is where the light stops, inner where it is at full strength.
    private static void DrawCone(DebugDraw output, SceneNode node, Light light, Vector3 colour)
    {
        Basis(node, out Vector3 forward, out Vector3 right, out Vector3 up);

        Vector3 at = node.WorldPosition;
        float reach = light.Range;

        DrawConeRing(output, at, forward, right, up, reach, light.OuterAngle, colour);
        DrawConeRing(output, at, forward, right, up, reach, light.InnerAngle, colour * 0.45f);

        // Axis stub, so a cone seen end-on is not just two circles.
        output.Line(at, at + (forward * reach * 0.15f), colour);
    }

    private static void DrawConeRing(
        DebugDraw output, Vector3 apex, Vector3 forward, Vector3 right, Vector3 up,
        float reach, float halfAngleDegrees, Vector3 colour)
    {
        float radians = halfAngleDegrees * (MathF.PI / 180f);
        Vector3 centre = apex + (forward * reach * MathF.Cos(radians));
        float radius = reach * MathF.Sin(radians);

        WireRing.Draw(output, centre, right, up, radius, colour);

        // Four rays only; a full fan hides what the light points at.
        for (int i = 0; i < 4; i++)
        {
            float angle = i * (MathF.Tau / 4);
            Vector3 rim = centre + (right * radius * MathF.Cos(angle)) + (up * radius * MathF.Sin(angle));
            output.Line(apex, rim, colour * 0.7f);
        }
    }

    // The arrow matters: an area light is one-sided.
    private static void DrawArea(DebugDraw output, SceneNode node, Light light, Vector3 colour)
    {
        Basis(node, out Vector3 forward, out Vector3 right, out Vector3 up);
        Vector3 at = node.WorldPosition;

        if (light.Kind == LightKind.Disc)
        {
            WireRing.Draw(output, at, right, up, light.Radius, colour);
        }
        else
        {
            Vector3 halfWidth = right * light.Width * 0.5f;
            Vector3 halfHeight = up * light.Height * 0.5f;

            Vector3 a = at - halfWidth - halfHeight;
            Vector3 b = at + halfWidth - halfHeight;
            Vector3 c = at + halfWidth + halfHeight;
            Vector3 d = at - halfWidth + halfHeight;

            output.Line(a, b, colour);
            output.Line(b, c, colour);
            output.Line(c, d, colour);
            output.Line(d, a, colour);
        }

        float reach = MathF.Min(light.Range, MathF.Max(light.Width, light.Height) + 1f);
        output.Arrow(at, at + (forward * reach * 0.5f), colour);
    }

    // Rows of the world matrix, the same ones Scene.CollectLights uploads.
    private static void Basis(SceneNode node, out Vector3 forward, out Vector3 right, out Vector3 up)
    {
        Matrix4x4 world = node.WorldMatrix;
        forward = Vector3.Normalize(new Vector3(world.M31, world.M32, world.M33));
        right = Vector3.Normalize(new Vector3(world.M11, world.M12, world.M13));
        up = Vector3.Normalize(new Vector3(world.M21, world.M22, world.M23));
    }
}
