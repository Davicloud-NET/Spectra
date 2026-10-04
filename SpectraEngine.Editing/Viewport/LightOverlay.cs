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
// collidable), so the icon is the only thing to see or click.
public sealed class LightOverlay
{
    /// <summary>
    /// The icon's radius in screen pixels. <see cref="LightPicking"/> picks at
    /// the same radius.
    /// </summary>
    public const float IconPixels = 9f;

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
    private const float DisabledDim = 0.28f;

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
            float radius = LightPicking.WorldRadius(camera, viewportSize, at);
            if (radius <= 0f)
                continue;

            // The light's own colour; grey when switched off.
            Vector3 colour = light.Enabled
                ? Vector3.Max(light.Color, new Vector3(0.25f))
                : new Vector3(DisabledDim);

            DrawIcon(output, at, radius, colour, camera);
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

    // An octagon with spokes, billboarded to the camera.
    private static void DrawIcon(DebugDraw output, Vector3 at, float radius, Vector3 colour, Camera camera)
    {
        Vector3 right = camera.Right * radius;
        Vector3 up = camera.Up * radius;

        const int Points = 8;
        Vector3 previous = at + right;

        for (int i = 1; i <= Points; i++)
        {
            float angle = i * (MathF.Tau / Points);
            Vector3 current = at + (right * MathF.Cos(angle)) + (up * MathF.Sin(angle));
            output.Line(previous, current, colour);
            previous = current;
        }

        for (int i = 0; i < 4; i++)
        {
            float angle = (i * (MathF.Tau / 4)) + (MathF.PI / 4f);
            Vector3 direction = (right * MathF.Cos(angle)) + (up * MathF.Sin(angle));
            output.Line(at + (direction * 0.9f), at + (direction * 1.7f), colour);
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
    public static float WorldRadius(Camera camera, Vector2 viewportSize, Vector3 at)
    {
        float depth = GizmoMath.ViewDepth(camera, at);
        if (depth <= 0f)
            return 0f;

        return LightOverlay.IconPixels * GizmoMath.WorldPerPixel(camera, viewportSize.Y, depth);
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
