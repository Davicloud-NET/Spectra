using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Selection;

/// <summary>
/// Which of a scene's nodes a screen-space rectangle covers. Stateless; render
/// thread only.
/// </summary>
// The BVH answers a sub-frustum first. Frustum.Intersects is conservative, so
// each hit is then re-tested by projecting its AABB and comparing rectangles.
// A box with a corner behind the eye has no projection: Intersect takes it,
// Contain does not.
public static class BoxSelectQuery
{
    // Below this the perspective divide is unusable.
    private const float MinClipW = 1e-4f;

    /// <summary>
    /// Fills <paramref name="results"/> with every node the rectangle covers.
    /// The list is cleared first.
    /// </summary>
    /// <param name="rect">The marquee, in viewport pixels (top-left origin, y down).</param>
    public static void Query(
        Scene scene,
        in ScreenRect rect,
        Vector2 viewportSize,
        BoxSelectMode mode,
        List<SceneNode> results)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(results);

        results.Clear();
        if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
            return;

        Camera camera = scene.Camera;
        Frustum frustum = camera.ScreenRectToFrustum(rect.Min, rect.Max, viewportSize);
        scene.QueryFrustum(in frustum, results);

        int write = 0;
        for (int read = 0; read < results.Count; read++)
        {
            SceneNode node = results[read];
            if (!Covers(scene, camera, node, in rect, viewportSize, mode))
                continue;

            results[write++] = node;
        }

        results.RemoveRange(write, results.Count - write);

        AppendCoveredIcons(scene, camera, in rect, viewportSize, mode, results);
    }

    // Lights and point entities are not in the spatial index (that would make
    // them collidable), so they get their own pass. Tests the icon at the
    // radius the overlay draws and the click picks.
    private static void AppendCoveredIcons(
        Scene scene,
        Camera camera,
        in ScreenRect rect,
        Vector2 viewportSize,
        BoxSelectMode mode,
        List<SceneNode> results)
    {
        IReadOnlyList<SceneNode> lights = scene.LightNodes;

        for (int i = 0; i < lights.Count; i++)
        {
            SceneNode node = lights[i];

            // Already judged through the index.
            if (node.Brush is not null || node.MeshRenderer is not null)
                continue;

            if (CoversIcon(camera, node, in rect, viewportSize, mode))
                results.Add(node);
        }

        IReadOnlyList<SceneNode> entities = scene.EntityNodes;

        for (int i = 0; i < entities.Count; i++)
        {
            SceneNode node = entities[i];

            // An entity with a light was judged by the loop above.
            if (EntityMarkerPicking.HasMarker(node) && CoversIcon(camera, node, in rect, viewportSize, mode))
                results.Add(node);
        }
    }

    /// <summary>
    /// Whether the rectangle covers the icon drawn at this node: a light's,
    /// or an entity's marker.
    /// </summary>
    public static bool CoversIcon(
        Camera camera,
        SceneNode node,
        in ScreenRect rect,
        Vector2 viewportSize,
        BoxSelectMode mode)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(node);

        Vector3 at = node.WorldPosition;

        // Behind the eye plane there is no projection.
        float depth = Vector3.Dot(at - camera.Position, camera.Forward);
        if (depth <= MinClipW)
            return false;

        Vector2 screen = Project(camera, at, viewportSize);
        float radius = LightPicking.PixelRadius(camera, viewportSize, at);

        var icon = new ScreenRect(
            new Vector2(screen.X - radius, screen.Y - radius),
            new Vector2(screen.X + radius, screen.Y + radius));

        return mode == BoxSelectMode.Contain
            ? rect.Contains(in icon)
            : rect.Intersects(in icon);
    }

    private static Vector2 Project(Camera camera, Vector3 world, Vector2 viewportSize)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), camera.GetViewProjection());
        float inverseW = 1f / MathF.Max(clip.W, MinClipW);

        return new Vector2(
            (clip.X * inverseW * 0.5f + 0.5f) * viewportSize.X,
            (0.5f - clip.Y * inverseW * 0.5f) * viewportSize.Y);
    }

    /// <summary>
    /// Whether the rectangle covers this one node under <paramref name="mode"/>,
    /// by its projected bounds.
    /// </summary>
    public static bool Covers(
        Scene scene,
        Camera camera,
        SceneNode node,
        in ScreenRect rect,
        Vector2 viewportSize,
        BoxSelectMode mode)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(node);

        if (!scene.TryGetWorldBounds(node, out Aabb bounds))
            return false;

        if (!TryProjectBounds(camera, in bounds, viewportSize, out ScreenRect projected))
            return mode == BoxSelectMode.Intersect; // Straddles the eye plane.

        return mode == BoxSelectMode.Contain
            ? rect.Contains(in projected)
            : rect.Intersects(in projected);
    }

    /// <summary>
    /// Projects a world AABB to the screen rectangle bounding its corners.
    /// False when any corner is at or behind the eye plane.
    /// </summary>
    public static bool TryProjectBounds(
        Camera camera, in Aabb bounds, Vector2 viewportSize, out ScreenRect rect)
    {
        ArgumentNullException.ThrowIfNull(camera);

        rect = default;
        if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
            return false;

        Matrix4x4 viewProjection = camera.GetViewProjection();
        Vector3 min = bounds.Min;
        Vector3 max = bounds.Max;

        var screenMin = new Vector2(float.MaxValue);
        var screenMax = new Vector2(float.MinValue);

        for (int corner = 0; corner < 8; corner++)
        {
            var world = new Vector3(
                (corner & 1) == 0 ? min.X : max.X,
                (corner & 2) == 0 ? min.Y : max.Y,
                (corner & 4) == 0 ? min.Z : max.Z);

            Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
            if (clip.W < MinClipW)
                return false;

            // Inverse of Camera.ScreenPointToRay's mapping, y flip included.
            float inverseW = 1f / clip.W;
            var pixel = new Vector2(
                (clip.X * inverseW + 1f) * 0.5f * viewportSize.X,
                (1f - clip.Y * inverseW) * 0.5f * viewportSize.Y);

            screenMin = Vector2.Min(screenMin, pixel);
            screenMax = Vector2.Max(screenMax, pixel);
        }

        rect = new ScreenRect(screenMin, screenMax);
        return true;
    }
}
