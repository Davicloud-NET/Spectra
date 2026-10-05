using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Puts entity data on a node or takes it off. Null on either side means the
/// node carries none. The node keeps its id, name and geometry.
/// </summary>
// Entity data is mutable. Each side is kept as a private copy and installed
// as another copy, so a later keyvalue edit cannot change what undo restores.
public sealed class SetEntityCommand : IEditorCommand
{
    private readonly EntityData? _before;
    private readonly EntityData? _after;
    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after data. Both are copied.</summary>
    public SetEntityCommand(Guid nodeId, EntityData? before, EntityData? after)
    {
        NodeId = nodeId;
        _before = before?.Clone();
        _after = after?.Clone();
    }

    /// <summary>
    /// Captures the node's current entity data as the before state. Call
    /// before applying the edit.
    /// </summary>
    public static SetEntityCommand Capture(SceneNode node, EntityData? after)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new SetEntityCommand(node.Id, node.Entity, after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The class the node was before the edit, or null when it was no entity.</summary>
    public string? BeforeClassName => _before?.ClassName;

    /// <summary>The class the node is after the edit, or null when it is no entity.</summary>
    public string? AfterClassName => _after?.ClassName;

    /// <inheritdoc/>
    public string Name { get; init; } = "Entity";

    /// <inheritdoc/>
    public void Do(Scene scene) => Apply(scene, _after);

    /// <inheritdoc/>
    public void Undo(Scene scene) => Apply(scene, _before);

    /// <inheritdoc/>
    public void RollBack(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.TryFindById(NodeId, out SceneNode? node))
        {
            node.Entity = _before?.Clone();
            return;
        }

        if (_lastApplied is not null && _lastApplied.TryGetTarget(out SceneNode? detached))
            detached.Entity = _before?.Clone();
    }

    private void Apply(Scene scene, EntityData? data)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.TryFindById(NodeId, out SceneNode? node))
            return;

        if (_lastApplied is null)
            _lastApplied = new WeakReference<SceneNode>(node);
        else
            _lastApplied.SetTarget(node);

        node.Entity = data?.Clone();
    }
}
