using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// What the menu of a dropped wire offers: the receiver's inputs, under each
/// of the sender's outputs when the drag did not begin on one. A class that
/// lists none gets one line that leaves the part empty.
/// </summary>
public sealed class LogicWireMenu
{
    private LogicWireMenu(string title, string note, IReadOnlyList<LogicWireMenuItem> items)
    {
        Title = title;
        Note = note;
        Items = items;
    }

    /// <summary>What the wire joins, as the menu's first line.</summary>
    public string Title { get; }

    /// <summary>
    /// What the menu says under its title when the wire would reach more
    /// than the card it was dropped on. Empty when it reaches only that.
    /// </summary>
    public string Note { get; }

    /// <summary>The lines to pick from.</summary>
    public IReadOnlyList<LogicWireMenuItem> Items { get; }

    /// <summary>The menu for a wire between two cards.</summary>
    /// <param name="from">The card that sends.</param>
    /// <param name="output">The output the drag began on, or null when it began elsewhere on the card.</param>
    /// <param name="to">The card the wire was dropped on.</param>
    /// <param name="reached">How many entities the wire would reach: more than one when they share a name.</param>
    public static LogicWireMenu For(LogicCard from, string? output, LogicCard to, int reached = 1)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        return new LogicWireMenu(
            LogicWireText.Title(from, output, to),
            LogicWireText.SharedName(to.Name, reached),
            output is null ? Outputs(from, to) : Inputs(output, to));
    }

    private static LogicWireMenuItem[] Outputs(LogicCard from, LogicCard to)
    {
        List<string> outputs = Declared(from.Outputs);
        if (outputs.Count == 0)
            return [new LogicWireMenuItem(LogicWireText.NoOutput(from), "", "", Inputs("", to))];

        var items = new LogicWireMenuItem[outputs.Count];
        for (int i = 0; i < items.Length; i++)
            items[i] = new LogicWireMenuItem(outputs[i], outputs[i], "", Inputs(outputs[i], to));

        return items;
    }

    private static LogicWireMenuItem[] Inputs(string output, LogicCard to)
    {
        List<string> inputs = Declared(to.Inputs);
        if (inputs.Count == 0)
            return [new LogicWireMenuItem(LogicWireText.NoInput(to), output, "", [])];

        var items = new LogicWireMenuItem[inputs.Count];
        for (int i = 0; i < items.Length; i++)
            items[i] = new LogicWireMenuItem(inputs[i], output, inputs[i], []);

        return items;
    }

    // A name only a wire spells is not one the class lists.
    private static List<string> Declared(IReadOnlyList<LogicPort> ports)
    {
        var names = new List<string>(ports.Count);
        foreach (LogicPort port in ports)
        {
            if (port.IsDeclared)
                names.Add(port.Name);
        }

        return names;
    }
}
