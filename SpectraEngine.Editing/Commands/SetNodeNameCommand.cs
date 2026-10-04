using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Editing.Commands;

/// <summary>Renames a node.</summary>
public sealed class SetNodeNameCommand : IEditorCommand
{
    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after names.</summary>
    public SetNodeNameCommand(Guid nodeId, string before, string after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        NodeId = nodeId;
        Before = before;
        After = after;
    }

    /// <summary>
    /// Captures the node's current name as the before state. Call before
    /// applying the edit.
    /// </summary>
    public static SetNodeNameCommand Capture(SceneNode node, string after)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new SetNodeNameCommand(node.Id, node.Name, after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The name the node carried before the edit.</summary>
    public string Before { get; }

    /// <summary>The name the node carries after the edit.</summary>
    public string After { get; }

    /// <inheritdoc/>
    public string Name { get; init; } = "Rename";

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
            node.Name = Before;
            return;
        }

        // Node left the scene mid-gesture: restore it anyway. A cancel
        // discards its commands, so nothing else will.
        if (_lastApplied is not null && _lastApplied.TryGetTarget(out SceneNode? detached))
            detached.Name = Before;
    }

    private void Apply(Scene scene, string name)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (!scene.TryFindById(NodeId, out SceneNode? node))
            return;

        _lastApplied ??= new WeakReference<SceneNode>(node);
        node.Name = name;
    }
}
