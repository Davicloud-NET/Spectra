using SpectraEngine.Core.Entities;
using System;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>What the Logic view says about the wires it adds and removes.</summary>
public static class LogicWireText
{
    /// <summary>What the status row says when a drop asks for a wire the sender has.</summary>
    public const string AlreadyThere = "That wire is already there.";

    /// <summary>The line of a wire's menu that removes it.</summary>
    public const string Remove = "Remove wire";

    /// <summary>What the status row says once a wire is made.</summary>
    /// <param name="sender">The name of the entity that sends it.</param>
    /// <param name="wire">The wire.</param>
    public static string Wired(string sender, EntityConnection wire)
    {
        string missing = (string.IsNullOrEmpty(wire.Output), string.IsNullOrEmpty(wire.Input)) switch
        {
            (true, true) => "output and input",
            (true, false) => "output",
            (false, true) => "input",
            _ => "",
        };

        string sentence = $"Wired {Route(sender, wire)}.";
        return missing.Length == 0 ? sentence : $"{sentence} Set its {missing} under Sends in Properties.";
    }

    /// <summary>What the status row says once a wire is removed.</summary>
    /// <param name="sender">The name of the entity that sent it.</param>
    /// <param name="wire">The wire.</param>
    /// <param name="alike">How many wires its edge drew, itself among them.</param>
    public static string Removed(string sender, EntityConnection wire, int alike) => alike > 1
        ? $"Removed 1 of {alike.ToString("N0", CultureInfo.InvariantCulture)} wires from {Route(sender, wire)}."
        : $"Removed {Route(sender, wire)}.";

    /// <summary>The first line of a dropped wire's menu: what the wire joins.</summary>
    /// <param name="from">The card that sends.</param>
    /// <param name="output">The output the drag began on, or null.</param>
    /// <param name="to">The card it was dropped on.</param>
    public static string Title(LogicCard from, string? output, LogicCard to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        string sender = string.IsNullOrEmpty(output) ? Called(from) : $"{Called(from)}.{output}";
        return $"Wire {sender} to {Called(to)}";
    }

    /// <summary>The menu line that makes a wire with no input, for a class that lists none.</summary>
    public static string NoInput(LogicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return $"No input: {Kind(card)} lists none";
    }

    /// <summary>The menu line that makes a wire with no output, for a class that lists none.</summary>
    public static string NoOutput(LogicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return $"No output: {Kind(card)} lists none";
    }

    /// <summary>What an entity is called in a sentence: its name, or its class when it has none.</summary>
    public static string Called(string name, string className) =>
        name.Length > 0 ? name : className.Length > 0 ? className : "an entity";

    // Who fires what at whom.
    private static string Route(string sender, EntityConnection wire)
    {
        string from = string.IsNullOrEmpty(wire.Output) ? sender : $"{sender}.{wire.Output}";
        string target = string.IsNullOrEmpty(wire.TargetName) ? "nothing" : wire.TargetName;
        string to = string.IsNullOrEmpty(wire.Input) ? target : $"{target}.{wire.Input}";

        return $"{from} to {to}";
    }

    private static string Called(LogicCard card) => card.Name.Length > 0 ? card.Name : Kind(card);

    private static string Kind(LogicCard card) => card.DisplayName.Length > 0 ? card.DisplayName : "its class";
}
