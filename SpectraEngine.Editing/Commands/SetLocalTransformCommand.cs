using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Sets a node's whole local <see cref="Transform"/>, scale included, to an
/// absolute value. Do not write scale on a brush node: the static-world
/// compile needs brush placements rigid.
/// </summary>
public sealed class SetLocalTransformCommand : ICoalescingCommand
{
    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after transforms.</summary>
    public SetLocalTransformCommand(Guid nodeId, Transform before, Transform after)
    {
        NodeId = nodeId;
        Before = before;
        After = after;
    }

    /// <summary>
    /// Captures the node's current local transform as the before state. Call
    /// before applying the edit.
    /// </summary>
    public static SetLocalTransformCommand Capture(SceneNode node, Transform after)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new SetLocalTransformCommand(node.Id, node.LocalTransform, after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The node's local transform before the edit.</summary>
    public Transform Before { get; }

    /// <summary>The node's local transform after the edit.</summary>
    public Transform After { get; private set; }

    /// <inheritdoc/>
    public string Name { get; init; } = "Transform";

    /// <summary>
    /// Retargets the after state, keeping the captured before state. Lets a
    /// drag reuse one command per frame.
    /// </summary>
    public void SetAfter(Transform after) => After = after;

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
            node.LocalTransform = Before;
            return;
        }

        // Node left the scene mid-gesture: restore it anyway. A cancel
        // discards its commands, so nothing else will.
        if (_lastApplied is not null && _lastApplied.TryGetTarget(out SceneNode? detached))
            detached.LocalTransform = Before;
    }

    /// <inheritdoc/>
    public bool TryAbsorb(IEditorCommand newer)
    {
        if (newer is not SetLocalTransformCommand next || next.NodeId != NodeId)
            return false;

        SetAfter(next.After);
        return true;
    }

    private void Apply(Scene scene, Transform transform)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.TryFindById(NodeId, out SceneNode? node))
            return;

        if (_lastApplied is null)
            _lastApplied = new WeakReference<SceneNode>(node);
        else
            _lastApplied.SetTarget(node);

        node.LocalTransform = transform;
    }
}
