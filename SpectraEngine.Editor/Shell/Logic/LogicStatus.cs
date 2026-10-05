using SpectraEngine.Core.Inspection;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>What the Logic view's status row says. Empty strings are left out.</summary>
/// <param name="Entities">How many entities have a card.</param>
/// <param name="Wires">How many wires are drawn.</param>
/// <param name="GoingNowhere">How many of them can never deliver.</param>
/// <param name="Unwired">Which entities have no wires and so no card.</param>
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
        string unwired = mode == LogicScopeMode.WholeLevel
            ? LogicViewText.Unwired(scoped.Graph.FirstUnwiredName, counts.UnwiredEntities)
            : "";

        return new LogicStatus(
            LogicViewText.Entities(counts.Entities),
            LogicViewText.Wires(counts.Wires),
            LogicViewText.GoingNowhere(counts.WiresGoingNowhere),
            unwired,
            TruncatedText(info))
        {
            GoingNowhereSender = FirstGoingNowhere(scoped),
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
