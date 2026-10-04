using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Writes one keyvalue on a node's entity payload. A null value means the key
/// is absent, so undoing an edit that added a key removes it again.
/// </summary>
// Null is not "": restoring an absent key as "" would add a member to map.json.
public sealed class SetEntityKeyvalueCommand : ICoalescingCommand
{
    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after values.</summary>
    /// <param name="before">The stored value, or null when the key was absent.</param>
    /// <param name="after">The value to store, or null to remove the key.</param>
    public SetEntityKeyvalueCommand(Guid nodeId, string key, string? before, string? after)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        NodeId = nodeId;
        Key = key;
        Before = before;
        After = after;
    }

    /// <summary>
    /// Captures the node's current value for the key as the before state. Call
    /// before applying the edit. Throws when the node carries no entity.
    /// </summary>
    public static SetEntityKeyvalueCommand Capture(SceneNode node, string key, string? after)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (node.Entity is not { } entity)
        {
            throw new InvalidOperationException(
                $"Node '{node.Name}' carries no entity to edit.");
        }

        string? before = entity.TryGetValue(key, out string stored) ? stored : null;
        return new SetEntityKeyvalueCommand(node.Id, key, before, after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The keyvalue's wire name.</summary>
    public string Key { get; }

    /// <summary>What the key held before the edit, or null when it was absent.</summary>
    public string? Before { get; }

    /// <summary>What the key holds after the edit, or null when it is removed.</summary>
    public string? After { get; private set; }

    /// <summary>Retargets the after state, keeping the captured before state.</summary>
    public void SetAfter(string? after) => After = after;

    /// <inheritdoc/>
    public bool TryAbsorb(IEditorCommand newer)
    {
        // Match node and key, or a drag on one field swallows an edit to another.
        if (newer is not SetEntityKeyvalueCommand next
            || next.NodeId != NodeId
            || !string.Equals(next.Key, Key, StringComparison.Ordinal))
        {
            return false;
        }

        SetAfter(next.After);
        return true;
    }

    /// <inheritdoc/>
    public string Name { get; init; } = "Entity Property";

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

    private void Apply(Scene scene, string? value)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (!scene.TryFindById(NodeId, out SceneNode? node) || node.Entity is not { } entity)
            return;

        _lastApplied ??= new WeakReference<SceneNode>(node);
        Write(entity, value);
    }

    private void Write(EntityData entity, string? value)
    {
        if (value is not null)
        {
            entity.SetValue(Key, value);
            return;
        }

        // First match only, like TryGetValue and SetValue: a hand-written
        // file may hold a duplicate key and the first is the one that binds.
        List<KeyValuePair<string, string>> keyvalues = entity.Keyvalues;
        for (int i = 0; i < keyvalues.Count; i++)
        {
            if (string.Equals(keyvalues[i].Key, Key, StringComparison.Ordinal))
            {
                keyvalues.RemoveAt(i);
                return;
            }
        }
    }
}
