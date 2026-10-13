using SpectraEngine.Core.Inspection;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>What the Logic view's status row says. Empty strings are left out.</summary>
/// <param name="Entities">How many entities have a card.</param>
/// <param name="Wires">How many wires are drawn.</param>
/// <param name="GoingNowhere">How many of them can never deliver.</param>
/// <param name="Unwired">Which entities have no wires and are not shown.</param>
/// <param name="Truncated">That the level has more entities than the view was given.</param>
public sealed record LogicStatus(
    string Entities,
    string Wires,
    string GoingNowhere,
    string Unwired,
    string Truncated)
{
    /// <summary>The status of a view with nothing to show.</summary>
    public static LogicStatus None { get; } = new("", "", "", "", "");

    /// <summary>
    /// The sender of the first wire on show that goes nowhere, or null. It is
    /// where <see cref="GoingNowhere"/> leads when it is pressed.
    /// </summary>
    public Guid? GoingNowhereSender { get; init; }

    /// <summary>The same as <see cref="Unwired"/> in fewer words, for a narrow row.</summary>
    public string UnwiredShort { get; init; } = "";

    /// <summary>
    /// What the last edit in the view did, or empty. While it has something
    /// to say it stands in the place of <see cref="Unwired"/> and <see cref="Truncated"/>.
    /// </summary>
    public string News { get; init; } = "";

    /// <summary>
    /// The hint at the right of the row while the level is edited. A narrow
    /// row shows <see cref="LogicViewText.EditingHintShort"/>, which every
    /// hint begins with.
    /// </summary>
    public string Hint { get; init; } = LogicViewText.EditingHint;

    /// <summary>The hint at the right of the row while the level runs.</summary>
    public string PlayingHint { get; init; } = LogicViewText.PlayingHint;

    /// <summary>
    /// Whether the level has scripts that run and the view says so. The view
    /// then explains its dashed wires.
    /// </summary>
    public bool HasScripts { get; init; }

    /// <summary>How many running scripts have a card, or empty when none has.</summary>
    public string Scripts { get; init; } = "";

    /// <summary>How many of the wires on show running scripts made, or empty when none did.</summary>
    public string ScriptWires { get; init; } = "";

    /// <summary>
    /// What the view says under its legend of solid and dashed wires. Empty
    /// when there is no legend to show: the level has no scripts, or the
    /// view shows none.
    /// </summary>
    /// <param name="isPlaying">Whether the level runs. Until it does, nothing is known of what its scripts connect.</param>
    public string ScriptsHint(bool isPlaying) => !HasScripts ? ""
        : isPlaying ? LogicViewText.ScriptsPlayingHint
        : LogicViewText.ScriptsEditingHint;

    /// <summary>Reads the status off what a scope shows.</summary>
    /// <param name="scoped">The cards and edges shown.</param>
    /// <param name="info">The snapshot they were built from, for how much of the level it lists.</param>
    /// <param name="mode">How much of the level the scope shows.</param>
    public static LogicStatus Of(LogicScopedGraph scoped, LogicGraphInfo info, LogicScopeMode mode)
    {
        ArgumentNullException.ThrowIfNull(scoped);
        ArgumentNullException.ThrowIfNull(info);

        // With no card on show the empty state does the talking.
        LogicCounts counts = scoped.Counts;
        if (scoped.Cards.Count == 0)
            return None with { Truncated = TruncatedText(info) };

        // Near a selection most of the level is left out, wired or not.
        int unwiredCount = mode == LogicScopeMode.WholeLevel ? counts.HiddenUnwiredEntities : 0;
        string unwired = LogicViewText.Unwired(scoped.FirstHiddenUnwiredName, unwiredCount);

        // In a level with scripts a count of wires says whose they are.
        string wires = scoped.Graph.ScriptCount > 0
            ? LogicViewText.WiresInLevel(counts.Wires)
            : LogicViewText.Wires(counts.Wires);

        return new LogicStatus(
            LogicViewText.Entities(counts.Entities),
            wires,
            LogicViewText.GoingNowhere(counts.WiresGoingNowhere),
            unwired,
            TruncatedText(info))
        {
            GoingNowhereSender = FirstGoingNowhere(scoped),
            UnwiredShort = LogicViewText.UnwiredShort(unwiredCount, counts.UnwiredCards),
            Hint = counts.UnwiredCards > 0 ? LogicViewText.UnwiredHint : LogicViewText.EditingHint,
            PlayingHint = counts.ScriptWires > 0 ? LogicViewText.PlayingScriptsHint : LogicViewText.PlayingHint,
            HasScripts = scoped.Graph.ScriptCount > 0,
            Scripts = LogicViewText.Scripts(counts.Scripts),
            ScriptWires = LogicViewText.MadeByScripts(counts.ScriptWires),
        };
    }

    private static Guid? FirstGoingNowhere(LogicScopedGraph scoped)
    {
        foreach (LogicEdge edge in scoped.Edges)
        {
            if (scoped.Graph.GoesNowhere(edge.Wire))
                return edge.From.NodeId;
        }

        return null;
    }

    private static string TruncatedText(LogicGraphInfo info) =>
        info.IsTruncated ? LogicViewText.Truncated(info.Entities.Count, info.TotalEntities) : "";
}
