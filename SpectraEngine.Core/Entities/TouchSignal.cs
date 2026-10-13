using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// The two things a part or a mesh fires for scripts when the player touches
/// it. A script's listener sits in <see cref="EntityWorld.Listeners"/> under
/// the node and one of these names, and the touch pass looks there.
/// </summary>
public static class TouchSignal
{
    /// <summary>Fired once when the player starts to touch the node.</summary>
    public const string Touched = "Touched";

    /// <summary>Fired once when the player stops touching the node.</summary>
    public const string TouchEnded = "TouchEnded";

    /// <summary>Whether a name is one of the two.</summary>
    public static bool IsOne(string name) => name is Touched or TouchEnded;

    /// <summary>
    /// Why no touch of the node can be told, as a script's error says it.
    /// Null when one can.
    /// </summary>
    // The fused world is hulls that name no node, in an authored level and
    // in a baked one, so a touch of a world brush has nobody to tell.
    public static string? WhyNone(SceneNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        bool isWorldBrush = node.IsStaticWorldBrush
            || node.BakedKind is SceneNodeKind.BrushWorld or SceneNodeKind.BrushSubtractive;

        return isWorldBrush
            ? $"{node.Name} cannot tell what touches it: it is a world brush, fused into the level, " +
              "and only parts notice a touch while a level plays. Convert the brush to a part."
            : null;
    }
}
