using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>One line of the menu a dropped wire opens.</summary>
/// <param name="Text">What the line says.</param>
/// <param name="Output">The output of the wire it stands for. Empty for none.</param>
/// <param name="Input">The input of the wire it makes. Empty for none, and for a line that opens others.</param>
/// <param name="Items">The lines it opens. None when picking it makes the wire.</param>
public sealed record LogicWireMenuItem(
    string Text,
    string Output,
    string Input,
    IReadOnlyList<LogicWireMenuItem> Items)
{
    /// <summary>Whether picking the line makes the wire.</summary>
    public bool MakesWire => Items.Count == 0;
}
