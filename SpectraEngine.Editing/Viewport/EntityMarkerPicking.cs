using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Ray-picking for the entity nodes <see cref="EntityMarkerOverlay"/> marks,
/// which the scene's spatial index does not carry.
/// </summary>
public static class EntityMarkerPicking
{
    /// <summary>
    /// Whether a node is shown by a marker: an entity with no brush, mesh or
    /// light of its own to show it.
    /// </summary>
    public static bool HasMarker(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Entity is not null &&
               node.Brush is null &&
               node.MeshRenderer is null &&
               node.Light is null;
    }

    /// <summary>The nearest marker <paramref name="ray"/> passes through, if any.</summary>
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

        IReadOnlyList<SceneNode> nodes = scene.EntityNodes;

        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode candidate = nodes[i];
            if (!HasMarker(candidate))
                continue;

            Vector3 at = candidate.WorldPosition;

            // The radius the overlay draws at, so what is clicked is what is seen.
            float radius = LightPicking.WorldRadius(camera, viewportSize, at);
            if (radius <= 0f)
                continue;

            if (!LightPicking.TryRaySphere(in ray, at, radius, out float hit) || hit >= distance)
                continue;

            node = candidate;
            distance = hit;
        }

        return node is not null;
    }
}
