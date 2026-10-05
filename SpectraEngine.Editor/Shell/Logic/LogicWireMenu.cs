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
    private LogicWireMenu(string title, IReadOnlyList<LogicWireMenuItem> items)
    {
        Title = title;
        Items = items;
    }

    /// <summary>What the wire joins, as the menu's first line.</summary>
    public string Title { get; }

    /// <summary>The lines to pick from.</summary>
    public IReadOnlyList<LogicWireMenuItem> Items { get; }

    /// <summary>The menu for a wire between two cards.</summary>
    /// <param name="from">The card that sends.</param>
    /// <param name="output">The output the drag began on, or null when it began elsewhere on the card.</param>
    /// <param name="to">The card the wire was dropped on.</param>
    public static LogicWireMenu For(LogicCard from, string? output, LogicCard to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        string title = LogicWireText.Title(from, output, to);
        if (output is not null)
            return new LogicWireMenu(title, Inputs(output, to));

        List<string> outputs = Declared(from.Outputs);
        if (outputs.Count == 0)
            return new LogicWireMenu(title, [new LogicWireMenuItem(LogicWireText.NoOutput(from), "", "", Inputs("", to))]);

        var items = new LogicWireMenuItem[outputs.Count];
        for (int i = 0; i < items.Length; i++)
            items[i] = new LogicWireMenuItem(outputs[i], outputs[i], "", Inputs(outputs[i], to));

        return new LogicWireMenu(title, items);
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
