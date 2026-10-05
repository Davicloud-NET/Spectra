using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>Which cards of a wiring graph a view shows, and which of them a filter dims.</summary>
public sealed class LogicScope
{
    /// <summary>The whole level, or only what is near the selection.</summary>
    public LogicScopeMode Mode { get; init; } = LogicScopeMode.WholeLevel;

    /// <summary>
    /// How many wires away from a selected entity a card may be and still
    /// show. Zero shows the selected entities alone.
    /// </summary>
    public int Steps { get; init; } = 2;

    /// <summary>
    /// Text to look for in a card's name, class name or display name, in any
    /// case. Cards without it stay and are dimmed. Empty dims nothing.
    /// </summary>
    public string Filter { get; init; } = "";

    /// <summary>
    /// Picks the cards and edges to show. An entity with no wires shows only
    /// while it is selected, in either mode.
    /// </summary>
    /// <param name="graph">The level's wiring.</param>
    /// <param name="selection">The selected nodes. The ones that are not cards are ignored.</param>
    public LogicScopedGraph Apply(LogicGraph graph, IReadOnlySet<Guid> selection)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(selection);

        bool[] shown = Mode == LogicScopeMode.WholeLevel
            ? Everything(graph, selection)
            : Near(graph, selection);

        var cards = new List<LogicCard>();
        foreach (LogicCard card in graph.Cards)
        {
            if (shown[card.Index])
                cards.Add(card);
        }

        var edges = new List<LogicEdge>();
        foreach (LogicEdge edge in graph.Edges)
        {
            if (shown[edge.From.Index] && shown[edge.To.Index])
                edges.Add(edge);
        }

        return new LogicScopedGraph(graph, cards, edges, Dimmed(graph));
    }

    private static bool[] Everything(LogicGraph graph, IReadOnlySet<Guid> selection)
    {
        var shown = new bool[graph.Cards.Count];
        foreach (LogicCard card in graph.Cards)
            shown[card.Index] = card.IsWired || selection.Contains(card.NodeId);

        return shown;
    }

    private bool[] Near(LogicGraph graph, IReadOnlySet<Guid> selection)
    {
        var shown = new bool[graph.Cards.Count];
        var steps = new int[graph.Cards.Count];
        var reached = new Queue<LogicCard>();

        foreach (LogicCard card in graph.Cards)
        {
            if (!card.IsStub && selection.Contains(card.NodeId))
            {
                shown[card.Index] = true;
                reached.Enqueue(card);
            }
        }

        while (reached.Count > 0)
        {
            LogicCard card = reached.Dequeue();
            if (steps[card.Index] >= Steps)
                continue;

            foreach (LogicEdge edge in graph.Leaving(card))
                Reach(edge.To, steps[card.Index] + 1);

            foreach (LogicEdge edge in graph.Arriving(card))
                Reach(edge.From, steps[card.Index] + 1);
        }

        return shown;

        void Reach(LogicCard card, int distance)
        {
            if (shown[card.Index])
                return;

            shown[card.Index] = true;
            steps[card.Index] = distance;
            reached.Enqueue(card);
        }
    }

    private bool[] Dimmed(LogicGraph graph)
    {
        var dimmed = new bool[graph.Cards.Count];
        string filter = (Filter ?? "").Trim();
        if (filter.Length == 0)
            return dimmed;

        foreach (LogicCard card in graph.Cards)
        {
            dimmed[card.Index] =
                !card.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !card.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !card.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        return dimmed;
    }
}
