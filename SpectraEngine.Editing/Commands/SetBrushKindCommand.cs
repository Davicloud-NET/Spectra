using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Converts a brush between world geometry and a standalone part. The node
/// keeps its id, brush and face materials.
/// </summary>
public sealed class SetBrushKindCommand : IEditorCommand
{
    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after kinds.</summary>
    public SetBrushKindCommand(Guid nodeId, BrushKind before, BrushKind after)
    {
        NodeId = nodeId;
        Before = before;
        After = after;
    }

    /// <summary>
    /// Captures the node's current kind as the before state. Call before
    /// applying the edit.
    /// </summary>
    public static SetBrushKindCommand Capture(SceneNode node, BrushKind after)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new SetBrushKindCommand(node.Id, node.BrushKind, after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The kind the node carried before the edit.</summary>
    public BrushKind Before { get; }

    /// <summary>The kind the node carries after the edit.</summary>
    public BrushKind After { get; }

    /// <inheritdoc/>
    public string Name { get; init; } = "Convert Brush";

    /// <inheritdoc/>
    public void Do(Scene scene) => Apply(scene, After);

    /// <inheritdoc/>
    public void Undo(Scene scene) => Apply(scene, Before);

    /// <inheritdoc/>
    public void RollBack(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.TryFindById(NodeId, out SceneNode? node))
        {
            node.BrushKind = Before;
            return;
        }

        if (_lastApplied is not null && _lastApplied.TryGetTarget(out SceneNode? detached))
            detached.BrushKind = Before;
    }

    private void Apply(Scene scene, BrushKind kind)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.TryFindById(NodeId, out SceneNode? node))
            return;

        if (_lastApplied is null)
            _lastApplied = new WeakReference<SceneNode>(node);
        else
            _lastApplied.SetTarget(node);

        node.BrushKind = kind;
    }
}
