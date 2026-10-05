using System;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>The sentences the Logic view shows round its graph, in one place.</summary>
public static class LogicViewText
{
    /// <summary>The hint at the right of the status row while editing.</summary>
    public const string EditingHint = EditingHintShort + " Double-click a card to frame it in the viewport.";

    /// <summary>The same where the row has no room for all of it.</summary>
    public const string EditingHintShort = "Drag from a card or an output onto a card to wire it.";

    /// <summary>
    /// The hint while a card on show has no wires. Its entity has a card
    /// only because it is selected, and so will the one it is wired to.
    /// </summary>
    public const string UnwiredHint = EditingHintShort + " Ctrl-click another entity to give it a card too.";

    /// <summary>The hint at the right of the status row while a level runs.</summary>
    public const string PlayingHint = "Wires light up as they fire. Stop to edit.";

    /// <summary>The key that shows what is near the selection.</summary>
    public const string AroundSelection = "Around the selection";

    /// <summary>The same key where the toolbar is narrow.</summary>
    public const string AroundSelectionShort = "Selection";

    /// <summary>The key that shows every card.</summary>
    public const string WholeLevel = "Whole level";

    /// <summary>The same key where the toolbar is narrow.</summary>
    public const string WholeLevelShort = "Level";

    /// <summary>What the empty filter box says.</summary>
    public const string FilterPlaceholder = "Filter by name or class";

    /// <summary>The same where the box is too narrow for it.</summary>
    public const string FilterPlaceholderShort = "Filter";

    /// <summary>What the event strip says while it has no line to show.</summary>
    public const string NoEvents = "Wires that fire are listed here.";

    /// <summary>How many entities have a card.</summary>
    public static string Entities(int count) => count == 1 ? "1 entity" : $"{Number(count)} entities";

    /// <summary>How many wires are drawn.</summary>
    public static string Wires(int count) => count == 1 ? "1 wire" : $"{Number(count)} wires";

    /// <summary>How many wires can never deliver, or empty when none.</summary>
    public static string GoingNowhere(int count) => count switch
    {
        <= 0 => "",
        1 => "1 wire goes nowhere",
        _ => $"{Number(count)} wires go nowhere",
    };

    /// <summary>The entities that have no wires and are not shown, or empty when there is none.</summary>
    /// <param name="first">The name of the first of them. May be empty.</param>
    /// <param name="count">How many there are.</param>
    public static string Unwired(string first, int count)
    {
        if (count <= 0)
            return "";

        if (string.IsNullOrEmpty(first))
        {
            return count == 1
                ? "1 entity has no wires and is not shown."
                : $"{Number(count)} entities have no wires and are not shown.";
        }

        return count switch
        {
            1 => $"{first} has no wires and is not shown.",
            2 => $"{first} and 1 more entity have no wires and are not shown.",
            _ => $"{first} and {Number(count - 1)} more entities have no wires and are not shown.",
        };
    }

    /// <summary>The same in fewer words, for a row too narrow for the name.</summary>
    /// <param name="count">How many entities have no wires and are not shown.</param>
    /// <param name="shown">How many more have no wires and are shown.</param>
    public static string UnwiredShort(int count, int shown)
    {
        string more = shown > 0 ? "more " : "";
        return count switch
        {
            <= 0 => "",
            1 => $"1 {more}entity has no wires.",
            _ => $"{Number(count)} {more}entities have no wires.",
        };
    }

    /// <summary>What to say when the level has more entities than the view was given.</summary>
    public static string Truncated(int listed, int total) =>
        $"Showing the first {Number(listed)} of {Number(total)} entities.";

    /// <summary>What the view says in place of a graph, or empty when it has one.</summary>
    public static string Empty(LogicEmptyReason reason) => reason switch
    {
        LogicEmptyReason.NoEntitySelected =>
            "Select an entity to see what it is wired to, or show the whole level.",
        LogicEmptyReason.LevelHasNoWires =>
            "Nothing in this level is wired yet. Select two entities, then drag from one card onto the other.",
        _ => "",
    };

    /// <summary>One wire in words, for a tooltip.</summary>
    /// <param name="edge">The wire, with the label the level was authored with.</param>
    public static string Sentence(LogicEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        string target = edge.To.Stub switch
        {
            LogicStubKind.Activator => "the activator",
            LogicStubKind.NoTarget => "nothing",
            _ => edge.To.Name,
        };

        string sentence = $"{edge.From.Name}.{edge.Output} sends {edge.Input} to {target}";
        if (!edge.Label.IsEmpty)
            sentence += ", " + edge.Label.Text;

        string problem = Problem(edge);
        return problem.Length == 0 ? sentence + "." : $"{sentence}. {problem}";
    }

    /// <summary>One card in words, for a tooltip. A card cuts a long name short.</summary>
    public static string Sentence(LogicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (!card.IsStub)
            return $"{card.Name}, {card.DisplayName}";

        return card.Name.Length == 0 ? card.DisplayName + "." : $"{card.Name}. {card.DisplayName}.";
    }

    private static string Problem(LogicEdge edge) => edge.Verdict switch
    {
        LogicVerdict.TargetMissing => edge.To.Stub switch
        {
            LogicStubKind.NoTarget => "It has no target.",
            LogicStubKind.MissingPrefix => $"Nothing matches {edge.To.Name}.",
            _ => $"Nothing is named {edge.To.Name}.",
        },
        LogicVerdict.NoSuchInput => $"{edge.To.Name} has no input {edge.Input}.",
        LogicVerdict.NoSuchOutput => $"{edge.From.Name} has no output {edge.Output}.",
        _ => "",
    };

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
