namespace SpectraEngine.Editing.Selection;

/// <summary>
/// How much of a node the marquee has to cover before it counts as selected.
/// </summary>
public enum BoxSelectMode
{
    /// <summary>A node is selected when its projected bounds touch the rectangle.</summary>
    Intersect,

    /// <summary>A node is selected only when its projected bounds lie entirely inside the rectangle.</summary>
    Contain,
}
