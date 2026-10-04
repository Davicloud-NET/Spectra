using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// Turns a live <see cref="BspTree"/> into the flat node array the compiled map
/// format bakes.
/// </summary>
public static class BspFlattener
{
    // An index into the node array, not a child code like FlatBspNode.EmptyLeaf.
    private const int NoParent = -1;

    /// <summary>
    /// Flattens <paramref name="tree"/> pre-order, front child first, so every
    /// child index is greater than its parent's. <paramref name="rootIndex"/>
    /// is 0, or a leaf code when the tree is one bare leaf.
    /// </summary>
    public static FlatBspNode[] Flatten(BspTree tree, out int rootIndex)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var nodes = new List<FlatBspNode>();

        // Explicit stack: tree depth is not bounded here.
        var pending = new Stack<PendingChild>();
        pending.Push(new PendingChild(tree.Root, NoParent, IsFront: false));

        rootIndex = FlatBspNode.EmptyLeaf;

        while (pending.Count > 0)
        {
            PendingChild slot = pending.Pop();
            BspNode live = slot.Node;

            int child;
            if (live.IsLeaf)
            {
                child = live.IsSolid ? FlatBspNode.SolidLeaf : FlatBspNode.EmptyLeaf;
            }
            else
            {
                // Children patch this slot when they are emitted.
                child = nodes.Count;
                nodes.Add(new FlatBspNode(live.Plane, FlatBspNode.EmptyLeaf, FlatBspNode.EmptyLeaf));

                // Back first so Front pops first.
                pending.Push(new PendingChild(live.Back!, child, IsFront: false));
                pending.Push(new PendingChild(live.Front!, child, IsFront: true));
            }

            if (slot.Parent == NoParent)
            {
                rootIndex = child;
                continue;
            }

            FlatBspNode parent = nodes[slot.Parent];
            nodes[slot.Parent] = slot.IsFront
                ? new FlatBspNode(parent.Plane, child, parent.Back)
                : new FlatBspNode(parent.Plane, parent.Front, child);
        }

        return [.. nodes];
    }

    private readonly record struct PendingChild(BspNode Node, int Parent, bool IsFront);
}
