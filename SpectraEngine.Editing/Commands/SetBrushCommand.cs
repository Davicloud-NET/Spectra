using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Swaps the <see cref="Brush"/> on a node and remembers the previous one.
/// Brushes are immutable, so every brush edit (retexture, resize) goes through
/// this. Null on either side attaches or detaches a brush.
/// </summary>
// A new Brush instance is what invalidates the cached carve: the cache keys on
// reference identity.
public sealed class SetBrushCommand : ICoalescingCommand
{
    // Node last written to, for RollBack. Same as on SetTransformCommand.
    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after brushes.</summary>
    public SetBrushCommand(Guid nodeId, Brush? before, Brush? after)
    {
        NodeId = nodeId;
        Before = before;
        After = after;
    }

    /// <summary>
    /// Captures <paramref name="node"/>'s current brush as the before state.
    /// Call before applying the edit.
    /// </summary>
    public static SetBrushCommand Capture(SceneNode node, Brush? after)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new SetBrushCommand(node.Id, node.Brush, after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The brush the node carried before the edit, or null if it had none.</summary>
    public Brush? Before { get; }

    /// <summary>The brush the node carries after the edit.</summary>
    public Brush? After { get; private set; }

    /// <inheritdoc/>
    public string Name { get; init; } = "Brush";

    /// <summary>Replaces the after state, keeping the before state.</summary>
    public void SetAfter(Brush? after) => After = after;

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
            node.Brush = Before;
            return;
        }

        // Node left the scene mid-gesture: restore through the kept reference.
        if (_lastApplied is not null && _lastApplied.TryGetTarget(out SceneNode? detached))
            detached.Brush = Before;
    }

    /// <inheritdoc/>
    public bool TryAbsorb(IEditorCommand newer)
    {
        if (newer is not SetBrushCommand next || next.NodeId != NodeId)
            return false;

        SetAfter(next.After);
        return true;
    }

    private void Apply(Scene scene, Brush? brush)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.TryFindById(NodeId, out SceneNode? node))
            return;

        if (_lastApplied is null)
            _lastApplied = new WeakReference<SceneNode>(node);
        else
            _lastApplied.SetTarget(node);

        node.Brush = brush;
    }
}
