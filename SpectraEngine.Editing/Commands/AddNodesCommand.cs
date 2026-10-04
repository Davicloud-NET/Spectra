using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Where a node sits in the graph: its parent's id and its index among that
/// parent's children.
/// </summary>
// The index matters: child order is placement order, which breaks ties in the
// carve. A node restored at another index compiles to different geometry.
// The node is held by reference because a detached node has no id to look up.
public readonly record struct NodePlacement(SceneNode Node, Guid ParentId, int Index);

/// <summary>
/// Attaches detached nodes to the scene at recorded positions, and detaches
/// them again on undo.
/// </summary>
// Owns its nodes while undone, so history keeps whole subtrees (and their GPU
// meshes) alive until the entry leaves the ring. Render thread only.
public sealed class AddNodesCommand : IEditorCommand
{
    private readonly NodePlacement[] _placements;

    /// <summary>Creates a command that attaches each node at its placement. The list is copied.</summary>
    public AddNodesCommand(IReadOnlyList<NodePlacement> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        // Ascending index: inserting a higher index first would clamp and reorder siblings.
        _placements = [.. placements.OrderBy(p => p.Index)];
    }

    /// <inheritdoc/>
    public string Name { get; init; } = "Add";

    /// <summary>The placements, in the order they are applied.</summary>
    public IReadOnlyList<NodePlacement> Placements => _placements;

    /// <inheritdoc/>
    public void Do(Scene scene) => StructuralEdit.Attach(scene, _placements);

    /// <inheritdoc/>
    public void Undo(Scene scene) => StructuralEdit.Detach(scene, _placements);
}
