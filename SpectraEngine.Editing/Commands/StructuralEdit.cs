using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Commands;

// Attach and detach shared by AddNodesCommand and RemoveNodesCommand, so the
// two directions cannot disagree about index restoration.
internal static class StructuralEdit
{
    // Placements must already be in ascending index order.
    public static void Attach(Scene scene, IReadOnlyList<NodePlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(scene);

        for (int i = 0; i < placements.Count; i++)
        {
            NodePlacement placement = placements[i];
            if (!scene.TryFindById(placement.ParentId, out SceneNode? parent))
                continue;

            // Already in place: skip. A re-insert bumps the graph structure
            // version and forces a full compile for no change.
            if (ReferenceEquals(placement.Node.Parent, parent) &&
                placement.Node.IndexInParent == placement.Index)
            {
                continue;
            }

            parent.InsertChild(placement.Index, placement.Node);
        }
    }

    public static void Detach(Scene scene, IReadOnlyList<NodePlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(scene);

        for (int i = placements.Count - 1; i >= 0; i--)
        {
            SceneNode node = placements[i].Node;
            node.Parent?.RemoveChild(node);
        }
    }

    // Nodes with no parent are skipped: there is no placement to restore.
    public static NodePlacement[] CapturePlacements(IReadOnlyList<SceneNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var captured = new List<NodePlacement>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            SceneNode node = nodes[i];
            if (node.Parent is { } parent)
                captured.Add(new NodePlacement(node, parent.Id, node.IndexInParent));
        }

        return [.. captured];
    }
}
