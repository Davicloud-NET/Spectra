namespace SpectraEngine.Editing.Commands;

/// <summary>
/// A command that can absorb a later command of the same kind on the same
/// node, keeping its own before state and taking the newer after state. This
/// makes a per-frame drag one undo entry.
/// </summary>
// Only attempted inside an open transaction; committed history is never collapsed.
public interface ICoalescingCommand : IEditorCommand
{
    /// <summary>
    /// Absorbs <paramref name="newer"/> if it is the same kind of edit on the
    /// same node. On true the caller drops <paramref name="newer"/>.
    /// </summary>
    bool TryAbsorb(IEditorCommand newer);
}
