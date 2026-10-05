using Avalonia;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>One port row of a placed card.</summary>
/// <param name="Port">The input or output the row shows.</param>
/// <param name="Row">The row, as wide as the card.</param>
/// <param name="Anchor">
/// Where a wire meets the row: on the card's left edge for an input, on its
/// right edge for an output.
/// </param>
public readonly record struct LogicScenePort(LogicPort Port, Rect Row, Point Anchor)
{
    /// <summary>The port's name.</summary>
    public string Name => Port.Name;

    /// <summary>Whether the port sends.</summary>
    public bool IsOutput => Port.IsOutput;
}
