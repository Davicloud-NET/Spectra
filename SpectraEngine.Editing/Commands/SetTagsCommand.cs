using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Swaps the tags a node was built with and remembers the previous ones. A
/// <see cref="TagSet"/> never changes once made, so every tag edit goes
/// through this.
/// </summary>
public sealed class SetTagsCommand : IEditorCommand
{
    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after tags.</summary>
    public SetTagsCommand(Guid nodeId, TagSet before, TagSet after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        NodeId = nodeId;
        Before = before;
        After = after;
    }

    /// <summary>
    /// Captures the node's current tags as the before state. Call before
    /// applying the edit.
    /// </summary>
    public static SetTagsCommand Capture(SceneNode node, TagSet after)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new SetTagsCommand(node.Id, node.Tags, after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The tags the node had before the edit.</summary>
    public TagSet Before { get; }

    /// <summary>The tags the node has after the edit.</summary>
    public TagSet After { get; }

    /// <inheritdoc/>
    public string Name { get; init; } = "Tags";

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
            node.Tags = Before;
            return;
        }

        if (_lastApplied is not null && _lastApplied.TryGetTarget(out SceneNode? detached))
            detached.Tags = Before;
    }

    private void Apply(Scene scene, TagSet tags)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.TryFindById(NodeId, out SceneNode? node))
            return;

        if (_lastApplied is null)
            _lastApplied = new WeakReference<SceneNode>(node);
        else
            _lastApplied.SetTarget(node);

        node.Tags = tags;
    }
}
