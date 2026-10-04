using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Draws an icon for every light, and the shape of each selected one.
/// </summary>
// Lights have no mesh and are not in the spatial index (that would make them
// collidable), so the icon is the only thing to see or click. It has to say
// three things without being selected: what kind of light, which way it
// points, and what colour it is.
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

    // Under this radius the kind and the arrows are a smudge, so a far light
    // is drawn as a dot in its colour.
    private const float DetailPixels = 6.5f;

    /// <summary>Whether the overlay draws at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many light icons may be drawn in one frame. The rest are counted in
    /// <see cref="SkippedLastDraw"/>.
    /// </summary>
    public int MaxIcons { get; set; } = 256;

    /// <summary>Lights the last draw drew.</summary>
    public int DrawnLastDraw { get; private set; }

    /// <summary>Lights the last draw skipped because <see cref="MaxIcons"/> was reached.</summary>
    public int SkippedLastDraw { get; private set; }

    private const int RingSegments = 32;
    private const int IconRingSegments = 12;
    private const float DisabledDim = 0.32f;

    // Drawn under each stroke, so an icon reads over sky, floor or a lit wall.
    private static readonly Vector3 Backing = new(0.02f, 0.02f, 0.025f);

    /// <summary>Draws an icon per light, plus the selected lights' shapes.</summary>
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

        IReadOnlyList<SceneNode> nodes = scene.LightNodes;

        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode node = nodes[i];
            if (node.Light is not { } light)
                continue;

            if (DrawnLastDraw >= MaxIcons)
            {
                SkippedLastDraw++;
                continue;
            }

            Vector3 at = node.WorldPosition;
            if (!LightPicking.TryMeasure(camera, viewportSize, at, out float radius, out float pixel))
                continue;

            var pen = new IconPen(camera, at, radius, pixel, IconColour(light));
            bool detailed = radius >= DetailPixels * pixel;

            // Every backing line first, or one stroke's backing crosses out
            // the colour of the stroke before it.
            pen.Backing = true;
            DrawIcon(output, node, light, in pen, detailed);
            pen.Backing = false;
            DrawIcon(output, node, light, in pen, detailed);

            DrawnLastDraw++;
        }

        // Shapes for the selection only; one per light would bury the scene.
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
    private static Vector3 IconColour(Light light)
    {
        if (!light.Enabled)
            return new Vector3(DisabledDim);

        float peak = MathF.Max(light.Color.X, MathF.Max(light.Color.Y, light.Color.Z));
        return peak > 1e-4f ? Vector3.Max(light.Color / peak, new Vector3(0.18f)) : Vector3.One;
    }

    // What an icon is drawn with: the screen plane at the light, one pixel's
    // world size there, and which of the two passes is running.
    private struct IconPen(Camera camera, Vector3 at, float radius, float pixel, Vector3 colour)
    {
        public readonly Vector3 At = at;
        public readonly Vector3 Right = camera.Right;
        public readonly Vector3 Up = camera.Up;
        public readonly Vector3 Toward = Vector3.Normalize(Vector3.Cross(camera.Right, camera.Up));
        public readonly float Radius = radius;
        public readonly float Pixel = pixel;
        public readonly Vector3 Colour = colour;
        public bool Backing;
    }

    private static void DrawIcon(DebugDraw output, SceneNode node, Light light, in IconPen pen, bool detailed)
    {
        if (!detailed)
        {
            // Hairlines here: the doubled stroke would fill a ring this small.
            float ring = pen.Radius * 0.8f;
            if (pen.Backing)
            {
                ThinRing(output, in pen, ring + pen.Pixel, Backing);
            }
            else
            {
                ThinRing(output, in pen, ring, pen.Colour);
                ThinRing(output, in pen, ring - pen.Pixel, pen.Colour);
            }

            return;
        }

        Basis(node, out Vector3 forward, out Vector3 right, out Vector3 up);

        switch (light.Kind)
        {
            // A sun: a disc with three arrows running the way the light travels.
            case LightKind.Directional:
            {
                IconRing(output, in pen, pen.At, pen.Right, pen.Up, pen.Radius * 0.5f);

                Vector3 side = AcrossScreen(in pen, forward) * (pen.Radius * 0.62f);
                Vector3 from = pen.At + (forward * pen.Radius * 0.85f);
                IconArrow(output, in pen, from, from + (forward * pen.Radius * 2.6f), forward);
                IconArrow(output, in pen, from + side, from + side + (forward * pen.Radius * 1.9f), forward);
                IconArrow(output, in pen, from - side, from - side + (forward * pen.Radius * 1.9f), forward);
                break;
            }

            // A bulb: a disc with rays in every direction.
            case LightKind.Point:
            {
                IconRing(output, in pen, pen.At, pen.Right, pen.Up, pen.Radius * 0.45f);

                for (int i = 0; i < 8; i++)
                {
                    float angle = i * (MathF.Tau / 8);
                    Vector3 ray = (pen.Right * MathF.Cos(angle)) + (pen.Up * MathF.Sin(angle));
                    Stroke(output, in pen, pen.At + (ray * pen.Radius * 0.68f), pen.At + (ray * pen.Radius));
                }

                break;
            }

            // A cone from the lamp, as wide as its outer angle within reason.
            case LightKind.Spot:
            {
                float half = Math.Clamp(light.OuterAngle, 12f, 42f) * (MathF.PI / 180f);
                float length = pen.Radius * 2.8f;
                Vector3 mouth = pen.At + (forward * length);
                float mouthRadius = length * MathF.Tan(half);

                IconRing(output, in pen, mouth, right, up, mouthRadius);
                for (int i = 0; i < 4; i++)
                {
                    float angle = (i * (MathF.Tau / 4)) + (MathF.PI / 4f);
                    Vector3 rim = mouth + (((right * MathF.Cos(angle)) + (up * MathF.Sin(angle))) * mouthRadius);
                    Stroke(output, in pen, pen.At, rim);
                }

                IconRing(output, in pen, pen.At, pen.Right, pen.Up, pen.Radius * 0.22f);
                break;
            }

            // The panel in its own plane, with an arrow out of the side that
            // lights. An area light is one-sided, so the arrow is the point.
            case LightKind.Rect:
            {
                float longest = MathF.Max(MathF.Max(light.Width, light.Height), 1e-4f);
                Vector3 halfWidth = right * pen.Radius * MathF.Max(light.Width / longest, 0.35f);
                Vector3 halfHeight = up * pen.Radius * MathF.Max(light.Height / longest, 0.35f);

                Vector3 a = pen.At - halfWidth - halfHeight;
                Vector3 b = pen.At + halfWidth - halfHeight;
                Vector3 c = pen.At + halfWidth + halfHeight;
                Vector3 d = pen.At - halfWidth + halfHeight;

                Stroke(output, in pen, a, b);
                Stroke(output, in pen, b, c);
                Stroke(output, in pen, c, d);
                Stroke(output, in pen, d, a);
                IconArrow(output, in pen, pen.At, pen.At + (forward * pen.Radius * 2.2f), forward);
                break;
            }

            case LightKind.Disc:
            {
                IconRing(output, in pen, pen.At, right, up, pen.Radius);
                IconArrow(output, in pen, pen.At, pen.At + (forward * pen.Radius * 2.2f), forward);
                break;
            }
        }
    }

    // A unit vector across the screen at right angles to a world direction, so
    // parallel arrows stay side by side whichever way the light points.
    private static Vector3 AcrossScreen(in IconPen pen, Vector3 direction)
    {
        Vector3 across = Vector3.Cross(direction, pen.Toward);
        return across.LengthSquared() > 1e-6f ? Vector3.Normalize(across) : pen.Right;
    }

    private static void IconRing(DebugDraw output, in IconPen pen, Vector3 centre, Vector3 u, Vector3 v, float radius)
    {
        Vector3 previous = centre + (u * radius);

        for (int i = 1; i <= IconRingSegments; i++)
        {
            float angle = i * (MathF.Tau / IconRingSegments);
            Vector3 current = centre + (u * radius * MathF.Cos(angle)) + (v * radius * MathF.Sin(angle));
            Stroke(output, in pen, previous, current);
            previous = current;
        }
    }

    private static void ThinRing(DebugDraw output, in IconPen pen, float radius, Vector3 colour)
    {
        Vector3 previous = pen.At + (pen.Right * radius);

        for (int i = 1; i <= IconRingSegments; i++)
        {
            float angle = i * (MathF.Tau / IconRingSegments);
            Vector3 current = pen.At + (pen.Right * radius * MathF.Cos(angle)) + (pen.Up * radius * MathF.Sin(angle));
            output.Line(previous, current, colour);
            previous = current;
        }
    }

    private static void IconArrow(DebugDraw output, in IconPen pen, Vector3 from, Vector3 tip, Vector3 direction)
    {
        Stroke(output, in pen, from, tip);

        Vector3 barb = AcrossScreen(in pen, direction) * (pen.Radius * 0.28f);
        Vector3 neck = tip - (direction * pen.Radius * 0.5f);
        Stroke(output, in pen, tip, neck + barb);
        Stroke(output, in pen, tip, neck - barb);
    }

    // One stroke of an icon. The debug lane draws hairlines, so the colour is
    // two lines a pixel apart, and the backing is one either side of those.
    private static void Stroke(DebugDraw output, in IconPen pen, Vector3 a, Vector3 b)
    {
        // At right angles to the stroke as it lies on screen.
        Vector3 along = b - a;
        float x = Vector3.Dot(along, pen.Right);
        float y = Vector3.Dot(along, pen.Up);
        float length = MathF.Sqrt((x * x) + (y * y));

        Vector3 across = length > 1e-6f
            ? ((pen.Right * -y) + (pen.Up * x)) * (pen.Pixel / length)
            : pen.Right * pen.Pixel;

        if (pen.Backing)
        {
            output.Line(a + (across * 1.5f), b + (across * 1.5f), Backing);
            output.Line(a - (across * 1.5f), b - (across * 1.5f), Backing);
        }
        else
        {
            output.Line(a + (across * 0.5f), b + (across * 0.5f), pen.Colour);
            output.Line(a - (across * 0.5f), b - (across * 0.5f), pen.Colour);
        }
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
                DrawRing(output, at, Vector3.UnitX, Vector3.UnitY, light.Range, colour);
                DrawRing(output, at, Vector3.UnitY, Vector3.UnitZ, light.Range, colour);
                DrawRing(output, at, Vector3.UnitZ, Vector3.UnitX, light.Range, colour);
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

        DrawRing(output, centre, right, up, radius, colour);

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
            DrawRing(output, at, right, up, light.Radius, colour);
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

    // Line by line: DebugDraw.Polyline takes a list, which would allocate per
    // ring per frame. EditingAllocationTests holds this path to zero.
    private static void DrawRing(DebugDraw output, Vector3 centre, Vector3 u, Vector3 v, float radius, Vector3 colour)
    {
        if (radius <= 0f)
            return;

        Vector3 previous = centre + (u * radius);

        for (int i = 1; i <= RingSegments; i++)
        {
            float angle = i * (MathF.Tau / RingSegments);
            Vector3 current = centre + (u * radius * MathF.Cos(angle)) + (v * radius * MathF.Sin(angle));
            output.Line(previous, current, colour);
            previous = current;
        }
    }
}

/// <summary>
/// Ray-picking for light nodes, which the scene's spatial index does not carry.
/// </summary>
public static class LightPicking
{
    /// <summary>
    /// The icon's world radius at <paramref name="at"/>. Zero when the point is
    /// outside the view.
    /// </summary>
    public static float WorldRadius(Camera camera, Vector2 viewportSize, Vector3 at) =>
        TryMeasure(camera, viewportSize, at, out float radius, out _) ? radius : 0f;

    /// <summary>
    /// The icon's radius on screen at <paramref name="at"/>, in pixels. Zero
    /// when the point is outside the view.
    /// </summary>
    public static float PixelRadius(Camera camera, Vector2 viewportSize, Vector3 at) =>
        TryMeasure(camera, viewportSize, at, out float radius, out float pixel) ? radius / pixel : 0f;

    /// <summary>
    /// The icon's world radius at <paramref name="at"/> and what one screen
    /// pixel measures there. False when the point is outside the view.
    /// </summary>
    // The one place the icon's size is decided. Drawing, picking and the
    // marquee all ask it, so what is clicked is what is seen.
    public static bool TryMeasure(
        Camera camera, Vector2 viewportSize, Vector3 at, out float worldRadius, out float worldPerPixel)
    {
        worldRadius = 0f;
        worldPerPixel = 0f;

        float depth = GizmoMath.ViewDepth(camera, at);
        if (depth <= 0f || viewportSize.Y <= 0f)
            return false;

        worldPerPixel = GizmoMath.WorldPerPixel(camera, viewportSize.Y, depth);
        if (worldPerPixel <= 0f)
            return false;

        worldRadius = Math.Clamp(
            LightOverlay.IconWorldRadius,
            LightOverlay.MinIconPixels * worldPerPixel,
            LightOverlay.IconPixels * worldPerPixel);
        return true;
    }

    /// <summary>The nearest light icon <paramref name="ray"/> passes through, if any.</summary>
    public static bool TryPick(
        Scene scene, Camera camera, in Ray3 ray, Vector2 viewportSize,
        out SceneNode? node, out float distance)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        node = null;
        distance = float.PositiveInfinity;

        if (viewportSize.Y <= 0f)
            return false;

        IReadOnlyList<SceneNode> nodes = scene.LightNodes;

        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode candidate = nodes[i];
            Vector3 at = candidate.WorldPosition;

            float radius = WorldRadius(camera, viewportSize, at);
            if (radius <= 0f)
                continue;

            if (!TryRaySphere(in ray, at, radius, out float hit) || hit >= distance)
                continue;

            node = candidate;
            distance = hit;
        }

        return node is not null;
    }

    // Assumes a normalised direction. Near root, clamped to zero so an origin
    // inside the sphere hits at the origin.
    private static bool TryRaySphere(in Ray3 ray, Vector3 centre, float radius, out float distance)
    {
        distance = 0f;

        Vector3 toCentre = ray.Origin - centre;
        float b = Vector3.Dot(toCentre, ray.Direction);
        float c = Vector3.Dot(toCentre, toCentre) - (radius * radius);

        // Outside and pointing away.
        if (c > 0f && b > 0f)
            return false;

        float discriminant = (b * b) - c;
        if (discriminant < 0f)
            return false;

        float near = -b - MathF.Sqrt(discriminant);
        distance = MathF.Max(near, 0f);
        return true;
    }
}
