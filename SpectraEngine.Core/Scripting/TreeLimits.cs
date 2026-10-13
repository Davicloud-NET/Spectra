using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// How much scripts can add to a level's tree while it plays. A script in a
/// loop makes nodes without end, and these are what stops it.
/// </summary>
public static class TreeLimits
{
    /// <summary>
    /// The most nodes scripts have made that are still there at one time. A
    /// node that was destroyed stops counting once no script holds it: what
    /// a script holds of one keeps the whole node.
    /// </summary>
    public const int MaxMadeNodes = 16384;

    /// <summary>
    /// The most nodes from the top of a tree down to its deepest node, both
    /// counted. The engine walks a tree by calling itself for each level.
    /// </summary>
    public const int MaxDepth = 256;

    /// <summary>How many nodes there are from the top of the node's tree down to the node, both counted.</summary>
    public static int LevelOf(SceneNode node)
    {
        int level = 1;
        for (SceneNode? above = node.Parent; above is not null; above = above.Parent)
            level++;

        return level;
    }

    /// <summary>
    /// How many levels the node and what is below it take, or one more than
    /// <paramref name="most"/> as soon as it is known to be more.
    /// </summary>
    // It goes no deeper than it is asked about, so it is safe on any tree.
    public static int HeightUpTo(SceneNode node, int most)
    {
        if (most < 1)
            return 1;

        int below = 0;
        for (int i = 0; i < node.Children.Count && below < most; i++)
            below = Math.Max(below, HeightUpTo(node.Children[i], most - 1));

        return 1 + below;
    }
}
