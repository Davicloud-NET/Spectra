using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Replaces the whole list of wires leaving a node's entity. Covers add,
/// remove and edit; order is kept as given.
/// </summary>
// Whole lists, not deltas: wires have no identity and two can be identical,
// so an index-based edit replayed after an undo hits the wrong one.
// Never sort or dedupe: the order round-trips through map.json.
public sealed class SetEntityConnectionsCommand : ICoalescingCommand
{
    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after lists.</summary>
    public SetEntityConnectionsCommand(
        Guid nodeId, IReadOnlyList<EntityConnection> before, IReadOnlyList<EntityConnection> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        NodeId = nodeId;
        Before = Copy(before);
        After = Copy(after);
    }

    /// <summary>
    /// Captures the node's current wiring as the before state. Call before
    /// applying the edit. Throws when the node carries no entity.
    /// </summary>
    public static SetEntityConnectionsCommand Capture(
        SceneNode node, IReadOnlyList<EntityConnection> after)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(after);

        if (node.Entity is not { } entity)
        {
            throw new InvalidOperationException(
                $"Node '{node.Name}' carries no entity to wire.");
        }

        return new SetEntityConnectionsCommand(node.Id, entity.Connections, after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The wires the node carried before the edit.</summary>
    public IReadOnlyList<EntityConnection> Before { get; }

    /// <summary>The wires it carries after the edit.</summary>
    public IReadOnlyList<EntityConnection> After { get; private set; }

    /// <summary>Retargets the after state, keeping the captured before state.</summary>
    public void SetAfter(IReadOnlyList<EntityConnection> after)
    {
        ArgumentNullException.ThrowIfNull(after);
        After = Copy(after);
    }

    /// <inheritdoc/>
    public bool TryAbsorb(IEditorCommand newer)
    {
        // Node id is enough: every edit carries the whole list.
        if (newer is not SetEntityConnectionsCommand next || next.NodeId != NodeId)
            return false;

        After = next.After;
        return true;
    }

    /// <inheritdoc/>
    public string Name { get; init; } = "Entity Wiring";

    /// <inheritdoc/>
    public void Do(Scene scene) => Apply(scene, After);

    /// <inheritdoc/>
    public void Undo(Scene scene) => Apply(scene, Before);

    /// <inheritdoc/>
    public void RollBack(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.TryFindById(NodeId, out SceneNode? node) && node.Entity is { } live)
        {
            Write(live, Before);
            return;
        }

        // Node left the scene mid-gesture: restore it anyway. A cancel
        // discards its commands, so nothing else will.
        if (_lastApplied is not null
            && _lastApplied.TryGetTarget(out SceneNode? detached)
            && detached.Entity is { } orphan)
        {
            Write(orphan, Before);
        }
    }

    private void Apply(Scene scene, IReadOnlyList<EntityConnection> value)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (!scene.TryFindById(NodeId, out SceneNode? node) || node.Entity is not { } entity)
            return;

        _lastApplied ??= new WeakReference<SceneNode>(node);
        Write(entity, value);
    }

    // Refill in place: EntityData hands this list out, so others hold it.
    private static void Write(EntityData entity, IReadOnlyList<EntityConnection> value)
    {
        entity.Connections.Clear();
        for (int i = 0; i < value.Count; i++)
            entity.Connections.Add(value[i]);
    }

    // Snapshot: the caller may pass the live list, which the edit then rewrites.
    private static EntityConnection[] Copy(IReadOnlyList<EntityConnection> from)
    {
        if (from.Count == 0)
            return [];

        var copy = new EntityConnection[from.Count];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = from[i];

        return copy;
    }

    /// <summary>Whether two wire lists are the same wires in the same order.</summary>
    public static bool SameWiring(
        IReadOnlyList<EntityConnection> a, IReadOnlyList<EntityConnection> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Count != b.Count)
            return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }
}
