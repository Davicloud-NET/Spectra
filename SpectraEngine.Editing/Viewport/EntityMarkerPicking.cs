using System;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

/// <summary>
/// Which entity nodes <see cref="EntityMarkerOverlay"/> marks. The scene's
/// spatial index does not carry them, so <see cref="SceneIcons"/> picks them.
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
}
