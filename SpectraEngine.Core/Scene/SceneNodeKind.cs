using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// What a node is, for a UI that only holds ids and names. Derived from the
/// node's payloads, never stored.
/// </summary>
public enum SceneNodeKind
{
    /// <summary>Carries no payload and no children: a transform and a name.</summary>
    Empty,

    /// <summary>Has children and no payload of its own.</summary>
    Group,

    /// <summary>Draws a mesh.</summary>
    Mesh,

    /// <summary>An additive brush fused into the compiled static world.</summary>
    BrushWorld,

    /// <summary>
    /// An additive brush that stays out of the carve, drawing from its own mesh
    /// and costing no recompile when it moves.
    /// </summary>
    BrushPart,

    /// <summary>A brush that removes solid. Renders nothing, so the tree is where it shows.</summary>
    BrushSubtractive,

    /// <summary>Carries a light.</summary>
    Light,

    /// <summary>Carries entity data: a class name, keyvalues and output wiring.</summary>
    Entity,

    /// <summary>Carries a script and no other payload.</summary>
    // Append only: the values cross the thread boundary as numbers.
    Script,
}

/// <summary>
/// Answers <see cref="SceneNodeKind"/> for a node.
/// </summary>
public static class SceneNodeClassifier
{
    /// <summary>
    /// Classifies <paramref name="node"/> by the payloads it carries.
    /// Render thread only: it reads a live node.
    /// </summary>
    // Brush first: a brush node can carry a mesh renderer too.
    public static SceneNodeKind Classify(SceneNode node)
    {
        if (node.Brush is { } brush)
        {
            if (brush.Operation == BrushOperation.Subtractive)
                return SceneNodeKind.BrushSubtractive;

            return node.BrushKind == BrushKind.Part
                ? SceneNodeKind.BrushPart
                : SceneNodeKind.BrushWorld;
        }

        // A brush that also carries entity data still reads as its brush kind.
        if (node.Entity is not null)
            return SceneNodeKind.Entity;

        if (node.Light is not null)
            return SceneNodeKind.Light;

        if (node.MeshRenderer is not null)
            return SceneNodeKind.Mesh;

        // Last of the payloads: a part that carries a script is still a part.
        if (node.Script is not null)
            return SceneNodeKind.Script;

        return node.Children.Count > 0 ? SceneNodeKind.Group : SceneNodeKind.Empty;
    }

    /// <summary>
    /// Whether where the node stands matters to anything. False for a script
    /// with no children: it is a file's name under its parent and nothing
    /// reads its position.
    /// </summary>
    public static bool HasPlace(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Children.Count > 0 || Classify(node) != SceneNodeKind.Script;
    }
}
