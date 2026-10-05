using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Writes a node's physics bits and whether it is drawn. The node keeps its
/// id, kind and geometry.
/// </summary>
public sealed class SetNodeFlagsCommand : IEditorCommand
{
    /// <summary>A node's authored flags, as a value.</summary>
    public readonly record struct NodeFlags(PhysicsFlags Physics, bool IsRendered)
    {
        /// <summary>Reads the current flags off a node.</summary>
        public static NodeFlags From(SceneNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            return new NodeFlags(node.PhysicsFlags & ~PhysicsFlags.HasBody, node.IsRendered);
        }

        /// <summary>These flags with one physics bit set or cleared.</summary>
        public NodeFlags With(PhysicsFlags bit, bool on) =>
            this with { Physics = on ? Physics | bit : Physics & ~bit };

        /// <summary>Writes these flags onto a node.</summary>
        // HasBody belongs to the physics layer, so the node keeps its own.
        public void ApplyTo(SceneNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            node.PhysicsFlags =
                (Physics & ~PhysicsFlags.HasBody) | (node.PhysicsFlags & PhysicsFlags.HasBody);
            node.IsRendered = IsRendered;
        }
    }

    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after flags.</summary>
    public SetNodeFlagsCommand(Guid nodeId, NodeFlags before, NodeFlags after)
    {
        NodeId = nodeId;
        Before = before;
        After = after;
    }

    /// <summary>
    /// Captures the node's current flags as the before state. Call before
    /// applying the edit.
    /// </summary>
    public static SetNodeFlagsCommand Capture(SceneNode node, NodeFlags after)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new SetNodeFlagsCommand(node.Id, NodeFlags.From(node), after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The flags the node carried before the edit.</summary>
    public NodeFlags Before { get; }

    /// <summary>The flags the node carries after the edit.</summary>
    public NodeFlags After { get; }

    /// <inheritdoc/>
    public string Name { get; init; } = "Behavior";

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
            Before.ApplyTo(node);
            return;
        }

        if (_lastApplied is not null && _lastApplied.TryGetTarget(out SceneNode? detached))
            Before.ApplyTo(detached);
    }

    private void Apply(Scene scene, NodeFlags flags)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.TryFindById(NodeId, out SceneNode? node))
            return;

        if (_lastApplied is null)
            _lastApplied = new WeakReference<SceneNode>(node);
        else
            _lastApplied.SetTarget(node);

        flags.ApplyTo(node);
    }
}
