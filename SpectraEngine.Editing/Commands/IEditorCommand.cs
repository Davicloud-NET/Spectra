using SpectraEngine.Core.Scene;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// One reversible editor operation. Tools change a scene only through commands,
/// so the <see cref="Undo.UndoStack"/> can replay every edit in both directions.
/// </summary>
// Implementations store node ids, never references: undoing a delete recreates
// the node under the same id. They store absolute before/after values, never
// deltas, so replaying one is idempotent. Render thread only.
public interface IEditorCommand
{
    /// <summary>Label for the "Undo &lt;name&gt;" menu item.</summary>
    string Name { get; }

    /// <summary>Applies the after state. Idempotent; a missing node is a no-op.</summary>
    void Do(Scene scene);

    /// <summary>Restores the before state. Same rules as <see cref="Do"/>.</summary>
    void Undo(Scene scene);

    /// <summary>Restores the before state when a gesture is cancelled.</summary>
    // Unlike Undo, this should also restore a node that left the scene mid-gesture
    // when the command can still reach it. A cancelled transaction is discarded,
    // so nothing else will put the value back.
    void RollBack(Scene scene) => Undo(scene);
}
