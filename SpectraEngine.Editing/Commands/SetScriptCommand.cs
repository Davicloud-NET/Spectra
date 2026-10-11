using SpectraEngine.Core.Scene;
using System;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// Swaps the <see cref="Script"/> on a node and remembers the previous one. A
/// script never changes once made, so every script edit goes through this.
/// Null on either side puts a script on the node or takes it off.
/// </summary>
public sealed class SetScriptCommand : IEditorCommand
{
    private WeakReference<SceneNode>? _lastApplied;

    /// <summary>Creates a command from explicit before/after scripts.</summary>
    public SetScriptCommand(Guid nodeId, Script? before, Script? after)
    {
        NodeId = nodeId;
        Before = before;
        After = after;
    }

    /// <summary>
    /// Captures <paramref name="node"/>'s current script as the before state.
    /// Call before applying the edit.
    /// </summary>
    public static SetScriptCommand Capture(SceneNode node, Script? after)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new SetScriptCommand(node.Id, node.Script, after);
    }

    /// <summary>The id of the node this command edits.</summary>
    public Guid NodeId { get; }

    /// <summary>The script the node carried before the edit, or null if it had none.</summary>
    public Script? Before { get; }

    /// <summary>The script the node carries after the edit, or null if it has none.</summary>
    public Script? After { get; }

    /// <inheritdoc/>
    public string Name { get; init; } = "Script";

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
            node.Script = Before;
            return;
        }

        if (_lastApplied is not null && _lastApplied.TryGetTarget(out SceneNode? detached))
            detached.Script = Before;
    }

    private void Apply(Scene scene, Script? script)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!scene.TryFindById(NodeId, out SceneNode? node))
            return;

        if (_lastApplied is null)
            _lastApplied = new WeakReference<SceneNode>(node);
        else
            _lastApplied.SetTarget(node);

        node.Script = script;
    }
}
