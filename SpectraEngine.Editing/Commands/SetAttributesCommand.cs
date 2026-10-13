using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Swaps the attributes a node was built with and remembers the previous
/// ones. An <see cref="AttributeSet"/> never changes once made, so every
/// attribute edit goes through this.
/// </summary>
// Coalescing, so a colour dragged in the picker is one entry.
public sealed class SetAttributesCommand : ICoalescingCommand
{
    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after attributes.</summary>
    public SetAttributesCommand(Guid nodeId, AttributeSet before, AttributeSet after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        NodeId = nodeId;
        Before = before;
        After = after;
    }

    /// <summary>
    /// Captures the node's current attributes as the before state. Call
    /// before applying the edit.
    /// </summary>
    public static SetAttributesCommand Capture(SceneNode node, AttributeSet after)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new SetAttributesCommand(node.Id, node.Attributes, after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The attributes the node had before the edit.</summary>
    public AttributeSet Before { get; }

    /// <summary>The attributes the node has after the edit.</summary>
    public AttributeSet After { get; private set; }

    /// <inheritdoc/>
    public string Name { get; init; } = "Attributes";

    /// <inheritdoc/>
    public bool TryAbsorb(IEditorCommand newer)
    {
        if (newer is not SetAttributesCommand next || next.NodeId != NodeId)
            return false;

        After = next.After;
        return true;
    }

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
            node.Attributes = Before;
            return;
        }

        if (_lastApplied is not null && _lastApplied.TryGetTarget(out SceneNode? detached))
            detached.Attributes = Before;
    }

    private void Apply(Scene scene, AttributeSet attributes)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.TryFindById(NodeId, out SceneNode? node))
            return;

        if (_lastApplied is null)
            _lastApplied = new WeakReference<SceneNode>(node);
        else
            _lastApplied.SetTarget(node);

        node.Attributes = attributes;
    }
}
