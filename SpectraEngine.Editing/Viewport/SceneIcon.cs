using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Viewport;

/// <summary>The icon of one light or point entity, as one view shows it.</summary>
public struct SceneIcon
{
    /// <summary>
    /// The node's place in <see cref="Scene.LightNodes"/> for a light, in
    /// <see cref="Scene.EntityNodes"/> for a point entity.
    /// </summary>
    // A place in the list and not the node, so nothing here keeps a level alive.
    public int Node { get; internal set; }

    /// <summary>Where the node is.</summary>
    public Vector3 Anchor { get; internal set; }

    /// <summary>
    /// Where the icon is drawn and clicked. Not <see cref="Anchor"/> when
    /// other icons would have covered it.
    /// </summary>
    public Vector3 At { get; internal set; }

    /// <summary>The icon's radius in the world.</summary>
    public float Radius { get; internal set; }

    /// <summary>What one screen pixel measures at the icon.</summary>
    public float Pixel { get; internal set; }

    /// <summary>Whether the icon is within the budget of its kind, and so drawn.</summary>
    public bool IsDrawn { get; internal set; }

    /// <summary>
    /// Whether the icon is drawn as its file's glyph. Otherwise it is a small
    /// mark, and stays on its node.
    /// </summary>
    public bool IsGlyph { get; internal set; }

    /// <summary>Whether the icon is drawn somewhere other than on its node.</summary>
    public readonly bool IsMoved => At != Anchor;
}
