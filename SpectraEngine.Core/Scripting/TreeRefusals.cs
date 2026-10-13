using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Core.Scripting;

// Why a change to a level's tree is refused, as a script's error says it.
// Each answers null for a change that may be made. `root` is the level's
// root, and `isRunning` whether the level still plays.
internal static class TreeRefusals
{
    // What the count of nodes scripts made says when no more fit.
    public static string TooMany =>
        $"scripts would have made more than {TreeLimits.MaxMadeNodes} nodes that are still there or still held, " +
        "and a level holds no more. Destroy the ones that are done with, and keep none of them in a variable";

    public static string? OfParent(SceneNode node, SceneNode? parent, SceneNode root, bool isRunning)
    {
        if (ReferenceEquals(node, root))
            return $"The Parent property of {node.Name} is locked: it is the root of the level";
        if (node.IsDestroyed)
            return $"The Parent property of {node.Name} is locked: {node.Name} was destroyed";
        if (ReferenceEquals(node.Parent, parent))
            return null;
        if (!isRunning)
            return $"{node.Name} cannot be changed: the level has stopped.";
        if (node.HoldsWorldBrush)
            return $"{node.Name} cannot get another parent: {OnlyPartsCan("move")}";

        return parent is null ? null : OfGoingUnder(node, parent);
    }

    public static string? OfClone(SceneNode source, SceneNode root, bool isRunning)
    {
        if (ReferenceEquals(source, root))
            return $"{source.Name} is the root of the level and cannot be cloned";
        if (!isRunning)
            return $"{source.Name} was not cloned: the level has stopped";

        return source.HoldsWorldBrush ? $"{source.Name} cannot be cloned: {OnlyPartsCan("be made")}" : null;
    }

    // Null also for a node that is destroyed already, which is left as it is.
    public static string? OfDestroy(SceneNode node, SceneNode root, bool isRunning)
    {
        if (ReferenceEquals(node, root))
            return $"{node.Name} is the root of the level and cannot be destroyed";
        if (node.IsDestroyed)
            return null;
        if (!isRunning)
            return $"{node.Name} cannot be changed: the level has stopped.";

        return node.HoldsWorldBrush ? $"{node.Name} cannot be destroyed: {OnlyPartsCan("be destroyed")}" : null;
    }

    private static string? OfGoingUnder(SceneNode node, SceneNode parent)
    {
        if (parent.IsDestroyed)
            return $"{node.Name} cannot get {parent.Name} as its parent: {parent.Name} was destroyed";

        // Roblox's words. A cycle would hang the next walk of the tree.
        for (SceneNode? above = parent; above is not null; above = above.Parent)
        {
            if (ReferenceEquals(above, node))
                return $"Attempt to set parent of {node.Name} to {parent.Name} would result in circular reference";
        }

        if (node.SubtreeBrushCount > 0 && IsScaled(parent))
        {
            return $"{node.Name} cannot get {parent.Name} as its parent: that node or one above it is scaled, " +
                "and a node cannot scale a brush at or below it. Put it under a node that is not scaled.";
        }

        int room = TreeLimits.MaxDepth - TreeLimits.LevelOf(parent);
        return TreeLimits.HeightUpTo(node, room) > room
            ? $"{node.Name} cannot get {parent.Name} as its parent: that would make the tree more than " +
              $"{TreeLimits.MaxDepth} nodes deep, which is as deep as a tree may be"
            : null;
    }

    // The words the other refusals for a world brush use.
    private static string OnlyPartsCan(string what) =>
        $"a world brush sits at or below it, and only parts can {what} while a level plays. Convert the brush to a part.";

    private static bool IsScaled(SceneNode node)
    {
        for (SceneNode? at = node; at is not null; at = at.Parent)
        {
            if (at.LocalScale != Vector3.One)
                return true;
        }

        return false;
    }
}
