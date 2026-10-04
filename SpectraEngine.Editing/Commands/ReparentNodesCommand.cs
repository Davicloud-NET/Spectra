using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// One node's move: the parent and index it came from, and the ones it goes to.
/// </summary>
public readonly record struct NodeReparent(
    Guid NodeId, Guid FromParentId, int FromIndex, Guid ToParentId, int ToIndex);

/// <summary>
/// Moves nodes to a different parent or sibling position, and puts them back
/// on undo. Does not preserve world transforms; compose with
/// <see cref="SetLocalTransformCommand"/> for that.
/// </summary>
// Render thread only.
public sealed class ReparentNodesCommand : IEditorCommand
{
    private readonly NodeReparent[] _forward;
    private readonly NodeReparent[] _backward;

    /// <summary>Creates a command from explicit before/after placements. The list is copied.</summary>
    public ReparentNodesCommand(IReadOnlyList<NodeReparent> moves)
    {
        ArgumentNullException.ThrowIfNull(moves);

        // Each direction is sorted ascending by the index it inserts at.
        _forward = [.. moves.OrderBy(m => m.ToIndex)];
        _backward = [.. moves.OrderBy(m => m.FromIndex)];
    }

    /// <summary>
    /// Captures each node's current placement as the before state. Call before
    /// moving anything. Nodes with no parent are skipped.
    /// </summary>
    /// <param name="firstIndex">Where the first node lands; the rest follow in order.</param>
    public static ReparentNodesCommand Capture(
        IReadOnlyList<SceneNode> nodes, SceneNode newParent, int firstIndex, string name = "Reparent")
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(newParent);

        var moves = new List<NodeReparent>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode node = nodes[i];
            if (node.Parent is { } parent)
                moves.Add(new NodeReparent(node.Id, parent.Id, node.IndexInParent, newParent.Id, firstIndex + moves.Count));
        }

        return new ReparentNodesCommand(moves) { Name = name };
    }

    /// <inheritdoc/>
    public string Name { get; init; } = "Reparent";

    /// <summary>The moves, in the order Do applies them.</summary>
    public IReadOnlyList<NodeReparent> Moves => _forward;

    /// <inheritdoc/>
    public void Do(Scene scene) => Apply(scene, _forward, forward: true);

    /// <inheritdoc/>
    public void Undo(Scene scene) => Apply(scene, _backward, forward: false);

    // Two passes: park every mover at the end of its destination parent, then
    // place them. The stored indices are positions in the finished list, with
    // all movers vacated. Applying them one at a time misplaces nodes when two
    // move within one parent, and undo then is not an inverse.
    private static void Apply(Scene scene, NodeReparent[] moves, bool forward)
    {
        ArgumentNullException.ThrowIfNull(scene);

        // A missing node or parent is skipped: history behind an undone delete
        // can name nodes that are not in the scene.
        var nodes = new SceneNode?[moves.Length];
        var parents = new SceneNode?[moves.Length];
        bool anyToDo = false;

        for (int i = 0; i < moves.Length; i++)
        {
            NodeReparent move = moves[i];
            Guid parentId = forward ? move.ToParentId : move.FromParentId;

            if (!scene.TryFindById(move.NodeId, out SceneNode? node) ||
                !scene.TryFindById(parentId, out SceneNode? parent))
            {
                continue;
            }

            nodes[i] = node;
            parents[i] = parent;

            int index = forward ? move.ToIndex : move.FromIndex;
            if (!ReferenceEquals(node.Parent, parent) || node.IndexInParent != index)
                anyToDo = true;
        }

        // Checked over the whole set, not per node: a node parked in pass one
        // is never already in place in pass two.
        if (!anyToDo)
            return;

        // Park. InsertChild detaches from the old parent itself.
        for (int i = 0; i < moves.Length; i++)
        {
            if (nodes[i] is { } node && parents[i] is { } parent)
                parent.InsertChild(parent.Children.Count, node);
        }

        // Place, ascending by destination index.
        for (int i = 0; i < moves.Length; i++)
        {
            if (nodes[i] is not { } node || parents[i] is not { } parent)
                continue;

            parent.InsertChild(forward ? moves[i].ToIndex : moves[i].FromIndex, node);
        }
    }
}
