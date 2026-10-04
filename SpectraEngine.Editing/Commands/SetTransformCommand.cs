using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Sets a node's local position and rotation to absolute values. Scale is left
/// alone; use <see cref="SetLocalTransformCommand"/> to edit it.
/// </summary>
public sealed class SetTransformCommand : ICoalescingCommand
{
    // Lets RollBack reach a node that left the scene mid-gesture. Weak, so
    // history never keeps a removed subtree alive.
    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>
    /// Creates a command from explicit before/after values. Prefer
    /// <see cref="Capture"/>, <see cref="Move"/> or <see cref="Rotate"/>.
    /// </summary>
    public SetTransformCommand(
        Guid nodeId,
        Vector3 beforePosition,
        Quaternion beforeRotation,
        Vector3 afterPosition,
        Quaternion afterRotation)
    {
        NodeId = nodeId;
        BeforePosition = beforePosition;
        BeforeRotation = beforeRotation;
        AfterPosition = afterPosition;
        AfterRotation = afterRotation;
    }

    /// <summary>
    /// Captures the node's current local position and rotation as the before
    /// state. Call before applying the edit.
    /// </summary>
    public static SetTransformCommand Capture(SceneNode node, Vector3 afterPosition, Quaternion afterRotation)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new SetTransformCommand(
            node.Id, node.LocalPosition, node.LocalRotation, afterPosition, afterRotation);
    }

    /// <summary>A position-only edit.</summary>
    public static SetTransformCommand Move(SceneNode node, Vector3 afterPosition)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Capture(node, afterPosition, node.LocalRotation);
    }

    /// <summary>A rotation-only edit.</summary>
    public static SetTransformCommand Rotate(SceneNode node, Quaternion afterRotation)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Capture(node, node.LocalPosition, afterRotation);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The node's local position before the edit.</summary>
    public Vector3 BeforePosition { get; }

    /// <summary>The node's local rotation before the edit.</summary>
    public Quaternion BeforeRotation { get; }

    /// <summary>The node's local position after the edit.</summary>
    public Vector3 AfterPosition { get; private set; }

    /// <summary>The node's local rotation after the edit.</summary>
    public Quaternion AfterRotation { get; private set; }

    /// <inheritdoc/>
    public string Name { get; init; } = "Transform";

    /// <summary>
    /// Retargets the after state, keeping the captured before state. Lets a
    /// drag reuse one command per frame.
    /// </summary>
    public void SetAfter(Vector3 position, Quaternion rotation)
    {
        AfterPosition = position;
        AfterRotation = rotation;
    }

    /// <inheritdoc/>
    public void Do(Scene scene) => Apply(scene, AfterPosition, AfterRotation);

    /// <inheritdoc/>
    public void Undo(Scene scene) => Apply(scene, BeforePosition, BeforeRotation);

    /// <inheritdoc/>
    public void RollBack(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (scene.TryFindById(NodeId, out SceneNode? node))
        {
            Write(node, BeforePosition, BeforeRotation);
            return;
        }

        // Node left the scene mid-gesture: restore it anyway. A cancel
        // discards its commands, so nothing else will.
        if (_lastApplied is not null && _lastApplied.TryGetTarget(out SceneNode? detached))
            Write(detached, BeforePosition, BeforeRotation);
    }

    /// <inheritdoc/>
    public bool TryAbsorb(IEditorCommand newer)
    {
        if (newer is not SetTransformCommand next || next.NodeId != NodeId)
            return false;

        SetAfter(next.AfterPosition, next.AfterRotation);
        return true;
    }

    private void Apply(Scene scene, Vector3 position, Quaternion rotation)
    {
        ArgumentNullException.ThrowIfNull(scene);
        // Missing node is fine: history behind an undone delete names nodes
        // that are not in the scene.
        if (!scene.TryFindById(NodeId, out SceneNode? node))
            return;

        Remember(node);
        Write(node, position, rotation);
    }

    private static void Write(SceneNode node, Vector3 position, Quaternion rotation)
    {
        node.LocalPosition = position;
        node.LocalRotation = rotation;
    }

    private void Remember(SceneNode node)
    {
        if (_lastApplied is null)
            _lastApplied = new WeakReference<SceneNode>(node);
        else
            _lastApplied.SetTarget(node);
    }
}
