using System.Globalization;

namespace SpectraEngine.Editor.Shell;

/// <summary>One tag of the selection, as a chip in the Properties panel.</summary>
public sealed class TagChipModel(string tag, int count, int selectionCount)
{
    /// <summary>The tag.</summary>
    public string Tag { get; } = tag;

    /// <summary>How many of the selected nodes have it.</summary>
    public int Count { get; } = count;

    /// <summary>How many nodes are selected.</summary>
    public int SelectionCount { get; } = selectionCount;

    /// <summary>Whether only some of the selected nodes have the tag.</summary>
    public bool IsPartial => Count < SelectionCount;

    /// <summary>"2 of 5" for a tag only some have, otherwise empty.</summary>
    public string PartialLabel => IsPartial
        ? string.Format(CultureInfo.InvariantCulture, "{0} of {1}", Count, SelectionCount)
        : string.Empty;

    /// <summary>What the chip says when the pointer rests on it.</summary>
    public string Help => IsPartial
        ? $"On {Count} of the {SelectionCount} selected. Add it again to put it on all of them."
        : SelectionCount > 1 ? $"On all {SelectionCount} selected." : "A script finds the node by this tag.";

    /// <summary>What the chip's remove button says.</summary>
    public string RemoveHelp => SelectionCount > 1
        ? "Take this tag off every selected node."
        : "Take this tag off.";
}
