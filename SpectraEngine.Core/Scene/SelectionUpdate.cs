namespace SpectraEngine.Core.Scene;

/// <summary>
/// How a batch of nodes combines with the selection already in a
/// <see cref="SelectionSet"/>. The editing layer maps modifier keys to these.
/// </summary>
public enum SelectionUpdate
{
    /// <summary>The batch becomes the selection; whatever was selected is dropped.</summary>
    Replace,

    /// <summary>The batch is added to the selection; nothing is dropped.</summary>
    Add,

    /// <summary>
    /// Each node in the batch flips: selected ones are dropped, unselected ones
    /// are added. Nodes outside the batch are untouched.
    /// </summary>
    Toggle,
}
