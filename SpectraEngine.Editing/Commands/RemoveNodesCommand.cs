using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Detaches nodes from the scene, and on undo puts them back under the same
/// parent at the same sibling index.
/// </summary>
// Holds the deleted subtrees (and their GPU meshes) until the entry leaves
// history. Render thread only.
public sealed class RemoveNodesCommand : IEditorCommand
{
    private readonly NodePlacement[] _placements;

    private RemoveNodesCommand(NodePlacement[] placements) => _placements = placements;

    /// <summary>
    /// Captures where <paramref name="nodes"/> sit so they can be put back.
    /// Call before removing them. Nodes with no parent are skipped.
    /// </summary>
    public static RemoveNodesCommand Capture(IReadOnlyList<SceneNode> nodes, string name = "Delete")
    {
        NodePlacement[] captured = StructuralEdit.CapturePlacements(nodes);
        return new RemoveNodesCommand([.. captured.OrderBy(p => p.Index)]) { Name = name };
    }

    /// <inheritdoc/>
    public string Name { get; init; } = "Delete";

    /// <summary>The nodes this command removes, and where they came from.</summary>
    public IReadOnlyList<NodePlacement> Placements => _placements;

    /// <inheritdoc/>
    public void Do(Scene scene) => StructuralEdit.Detach(scene, _placements);

    /// <inheritdoc/>
    public void Undo(Scene scene) => StructuralEdit.Attach(scene, _placements);
}
