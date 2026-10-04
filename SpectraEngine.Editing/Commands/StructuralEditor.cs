using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Undo;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Duplicate, delete, group, ungroup and reparent over a selection. Each is one
/// undo entry and works on the selection's roots. Selection changes are not
/// part of the undo entry.
/// </summary>
public static class StructuralEditor
{
    /// <summary>The undo entry name for a duplicate.</summary>
    public const string DuplicateTransactionName = "Duplicate";

    /// <summary>
    /// Copies each selected root to the end of its parent's children and
    /// selects the copies. Returns false when there is nothing to duplicate.
    /// </summary>
    public static bool TryDuplicate(Scene scene, UndoStack undo, IReadOnlyList<SceneNode> selection)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);

        List<SceneNode> roots = SelectionRoots(scene, selection);
        if (roots.Count == 0)
            return false;

        var placements = new List<NodePlacement>(roots.Count);
        var clones = new List<SceneNode>(roots.Count);

        // Consecutive indices per parent, so siblings duplicated together
        // keep their order.
        var nextIndex = new Dictionary<Guid, int>();
        foreach (SceneNode root in roots)
        {
            SceneNode parent = root.Parent!;
            if (!nextIndex.TryGetValue(parent.Id, out int index))
                index = parent.Children.Count;

            SceneNode clone = root.Clone();
            clones.Add(clone);
            placements.Add(new NodePlacement(clone, parent.Id, index));
            nextIndex[parent.Id] = index + 1;
        }

        undo.BeginTransaction(DuplicateTransactionName);
        Run(scene, undo, new AddNodesCommand(placements) { Name = DuplicateTransactionName });
        undo.CommitTransaction();

        scene.Selection.SetRange(clones);
        return true;
    }

    /// <summary>
    /// Removes each selected root from the scene and clears the selection.
    /// Returns false when there is nothing to delete.
    /// </summary>
    public static bool TryDelete(Scene scene, UndoStack undo, IReadOnlyList<SceneNode> selection)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);

        List<SceneNode> roots = SelectionRoots(scene, selection);
        if (roots.Count == 0)
            return false;

        undo.BeginTransaction("Delete");
        Run(scene, undo, RemoveNodesCommand.Capture(roots));
        undo.CommitTransaction();

        scene.Selection.Clear();
        return true;
    }

    /// <summary>
    /// Puts the selected roots under one new node, pivoted at the centre of
    /// their bounds, and selects it. Children keep their world transforms.
    /// Returns false when there is nothing to group or a transform cannot be
    /// preserved.
    /// </summary>
    // The group gets a translation only, so brush placements under it stay rigid.
    public static bool TryGroup(
        Scene scene, UndoStack undo, IReadOnlyList<SceneNode> selection, string groupName = "Group")
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);

        List<SceneNode> roots = SelectionRoots(scene, selection);
        if (roots.Count == 0)
            return false;

        SceneNode parent = roots[0].Parent!;
        if (!Matrix4x4.Invert(parent.WorldMatrix, out Matrix4x4 parentInverse))
            return false;

        if (!GizmoSelectionBounds.TryMeasure(
                roots, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, out Vector3 min, out Vector3 max))
        {
            return false;
        }

        var group = new SceneNode(groupName)
        {
            LocalPosition = Vector3.Transform((min + max) * 0.5f, parentInverse),
        };

        // The group takes the first selected sibling's slot.
        int groupIndex = parent.Children.Count;
        foreach (SceneNode root in roots)
        {
            if (ReferenceEquals(root.Parent, parent))
                groupIndex = Math.Min(groupIndex, root.IndexInParent);
        }

        undo.BeginTransaction("Group");
        Run(scene, undo, new AddNodesCommand([new NodePlacement(group, parent.Id, groupIndex)]) { Name = "Group" });

        if (!TryReparentPreservingWorld(scene, undo, roots, group, 0, "Group"))
        {
            undo.CancelTransaction();
            return false;
        }

        undo.CommitTransaction();
        scene.Selection.Select(group);
        return true;
    }

    /// <summary>
    /// Dissolves each selected node that has children: the children move up to
    /// its parent, keeping world transforms and order, and the emptied node is
    /// removed. Returns false when nothing selected has children.
    /// </summary>
    public static bool TryUngroup(Scene scene, UndoStack undo, IReadOnlyList<SceneNode> selection)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);

        List<SceneNode> roots = SelectionRoots(scene, selection);
        var groups = new List<SceneNode>(roots.Count);
        foreach (SceneNode root in roots)
        {
            if (root.Children.Count > 0)
                groups.Add(root);
        }

        if (groups.Count == 0)
            return false;

        var freed = new List<SceneNode>();
        undo.BeginTransaction("Ungroup");

        foreach (SceneNode group in groups)
        {
            SceneNode parent = group.Parent!;
            var children = new List<SceneNode>(group.Children);

            // Children take the group's slot.
            if (!TryReparentPreservingWorld(scene, undo, children, parent, group.IndexInParent, "Ungroup"))
            {
                undo.CancelTransaction();
                return false;
            }

            Run(scene, undo, RemoveNodesCommand.Capture([group], "Ungroup"));
            freed.AddRange(children);
        }

        undo.CommitTransaction();
        scene.Selection.SetRange(freed);
        return true;
    }

    /// <summary>
    /// Moves the selected roots under <paramref name="newParent"/> at
    /// <paramref name="insertIndex"/> (-1 appends), keeping world transforms.
    /// Roots that would form a cycle are left out. Returns false when nothing
    /// can move.
    /// </summary>
    public static bool TryReparent(
        Scene scene, UndoStack undo, IReadOnlyList<SceneNode> selection, SceneNode newParent, int insertIndex)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(undo);
        ArgumentNullException.ThrowIfNull(newParent);

        // The parent may have been deleted between gesture and apply.
        if (!scene.TryFindById(newParent.Id, out SceneNode? liveParent) ||
            !ReferenceEquals(liveParent, newParent))
        {
            return false;
        }

        List<SceneNode> roots = SelectionRoots(scene, selection);

        // Drop roots on the target's ancestor chain: InsertChild throws on a
        // cycle, which would leave the transaction open.
        for (SceneNode? ancestor = newParent; ancestor is not null; ancestor = ancestor.Parent)
        {
            for (int i = roots.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(roots[i], ancestor))
                    roots.RemoveAt(i);
            }
        }

        if (roots.Count == 0)
            return false;

        // Keep tree order, not click order: sibling order is authored data.
        // Only defined when the roots share a parent.
        bool oneParent = true;
        for (int i = 1; i < roots.Count && oneParent; i++)
            oneParent = ReferenceEquals(roots[i].Parent, roots[0].Parent);

        if (oneParent)
            roots.Sort(static (a, b) => a.IndexInParent.CompareTo(b.IndexInParent));

        int target = insertIndex < 0 ? newParent.Children.Count : insertIndex;

        // Same-parent movers leave their slots first, shifting the destination
        // down. Count against the original target, not a running total, or
        // the result depends on root order.
        int vacated = 0;
        foreach (SceneNode root in roots)
        {
            if (ReferenceEquals(root.Parent, newParent) && root.IndexInParent < target)
                vacated++;
        }

        int firstIndex = Math.Max(0, target - vacated);

        // No-op drop: record nothing, or Ctrl+Z gets an entry that undoes nothing.
        bool alreadyPlaced = true;
        for (int i = 0; i < roots.Count && alreadyPlaced; i++)
            alreadyPlaced = ReferenceEquals(roots[i].Parent, newParent) && roots[i].IndexInParent == firstIndex + i;

        if (alreadyPlaced)
            return false;

        undo.BeginTransaction("Reparent");
        if (!TryReparentPreservingWorld(scene, undo, roots, newParent, firstIndex, "Reparent"))
        {
            undo.CancelTransaction();
            return false;
        }

        undo.CommitTransaction();
        scene.Selection.SetRange(roots);
        return true;
    }

    /// <summary>
    /// The selected nodes with no selected ancestor. Excludes the scene root
    /// and nodes that are not in this scene.
    /// </summary>
    public static List<SceneNode> SelectionRoots(Scene scene, IReadOnlyList<SceneNode> selection)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(selection);

        var roots = new List<SceneNode>(selection.Count);
        for (int i = 0; i < selection.Count; i++)
        {
            SceneNode node = selection[i];

            // A selection can outlive its scene. SceneNode.Owner is internal
            // to Core, so ask the id index.
            if (node.Parent is null ||
                !scene.TryFindById(node.Id, out SceneNode? live) ||
                !ReferenceEquals(live, node))
            {
                continue;
            }

            bool carried = false;
            for (SceneNode? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (Contains(selection, ancestor))
                {
                    carried = true;
                    break;
                }
            }

            if (!carried)
                roots.Add(node);
        }

        return roots;
    }

    // Reparents and rewrites local transforms so nothing moves in world space.
    private static bool TryReparentPreservingWorld(
        Scene scene, UndoStack undo, IReadOnlyList<SceneNode> nodes, SceneNode newParent, int firstIndex, string name)
    {
        if (!Matrix4x4.Invert(newParent.WorldMatrix, out Matrix4x4 inverse))
            return false;

        // Solve before anything moves, while the world matrices are still the old ones.
        var locals = new Transform[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!TryLocalUnder(nodes[i], inverse, out locals[i]))
                return false;
        }

        var befores = new Transform[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
            befores[i] = nodes[i].LocalTransform;

        Run(scene, undo, ReparentNodesCommand.Capture(nodes, newParent, firstIndex, name));

        for (int i = 0; i < nodes.Count; i++)
            Run(scene, undo, new SetLocalTransformCommand(nodes[i].Id, befores[i], locals[i]) { Name = name });

        return true;
    }

    // world = local * parent.World, so local = world * inverse(parent.World).
    private static bool TryLocalUnder(SceneNode node, Matrix4x4 newParentInverse, out Transform local)
    {
        local = Transform.Identity;

        if (!Matrix4x4.Decompose(
                node.WorldMatrix * newParentInverse,
                out Vector3 scale,
                out Quaternion rotation,
                out Vector3 position))
        {
            return false;
        }

        local.Position = position;
        local.Rotation = rotation;
        local.Scale = scale;
        return true;
    }

    private static void Run(Scene scene, UndoStack undo, IEditorCommand command)
    {
        undo.Record(command);
        command.Do(scene);
    }

    private static bool Contains(IReadOnlyList<SceneNode> nodes, SceneNode node)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i], node))
                return true;
        }

        return false;
    }
}
