namespace SpectraEngine.Core.Inspection;

/// <summary>One tag of the selection.</summary>
/// <param name="Tag">The tag.</param>
/// <param name="Count">How many of the selected nodes have it.</param>
public readonly record struct TagPanelRow(string Tag, int Count);
