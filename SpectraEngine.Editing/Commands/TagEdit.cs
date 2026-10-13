namespace SpectraEngine.Editing.Commands;

/// <summary>
/// One tag a panel is asking to put on the selection or take off it. It says
/// which, not "the other": a toggle against a stale snapshot flips the wrong way.
/// </summary>
/// <param name="Tag">The tag.</param>
/// <param name="IsOn">True to put it on every selected node, false to take it off every one.</param>
public readonly record struct TagEdit(string Tag, bool IsOn);
