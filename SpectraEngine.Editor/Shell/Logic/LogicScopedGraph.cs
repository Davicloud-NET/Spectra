using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>The part of a wiring graph a view shows: what <see cref="LogicScope.Apply"/> picked.</summary>
public sealed class LogicScopedGraph
{
    private readonly bool[] _dimmed;

    internal LogicScopedGraph(
        LogicGraph graph,
        IReadOnlyList<LogicCard> cards,
        IReadOnlyList<LogicEdge> edges,
        bool[] dimmed,
        LogicEmptyReason emptyReason)
    {
        Graph = graph;
        Cards = cards;
        Edges = edges;
        EmptyReason = emptyReason;
        _dimmed = dimmed;
        Counts = Count();
    }

    /// <summary>The whole level's wiring.</summary>
    public LogicGraph Graph { get; }

    /// <summary>The cards shown, in the graph's order.</summary>
    public IReadOnlyList<LogicCard> Cards { get; }

    /// <summary>The edges shown: the ones with both ends among <see cref="Cards"/>.</summary>
    public IReadOnlyList<LogicEdge> Edges { get; }

    /// <summary>Why nothing is shown, or <see cref="LogicEmptyReason.None"/>.</summary>
    public LogicEmptyReason EmptyReason { get; }

    /// <summary>The numbers for a status line.</summary>
    public LogicCounts Counts { get; }

    /// <summary>Whether the filter leaves a card out.</summary>
    public bool IsDimmed(LogicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return _dimmed[card.Index];
    }

    /// <summary>Whether the filter leaves out both ends of an edge.</summary>
    public bool IsDimmed(LogicEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return _dimmed[edge.From.Index] && _dimmed[edge.To.Index];
    }

    /// <summary>
    /// The same cards and edges with other words on the edges. A view of a
    /// running level uses it to show what each wire is doing.
    /// </summary>
    public LogicScopedGraph WithLabels(Func<LogicEdge, LogicLabel> label)
    {
        ArgumentNullException.ThrowIfNull(label);

        var edges = new LogicEdge[Edges.Count];
        for (int i = 0; i < edges.Length; i++)
            edges[i] = Edges[i].WithLabel(label(Edges[i]));

        return new LogicScopedGraph(Graph, Cards, edges, _dimmed, EmptyReason);
    }

    private LogicCounts Count()
    {
        int entities = 0;
        foreach (LogicCard card in Cards)
            entities += card.IsStub ? 0 : 1;

        // A wire drawn as several edges still counts once.
        var wires = new HashSet<LogicWireKey>();
        int goingNowhere = 0;

        foreach (LogicEdge edge in Edges)
        {
            foreach (int index in edge.WireIndices)
            {
                var wire = new LogicWireKey(edge.From.NodeId, index);
                if (wires.Add(wire) && Graph.GoesNowhere(wire))
                    goingNowhere++;
            }
        }

        return new LogicCounts(
            Cards.Count, entities, wires.Count, goingNowhere, Graph.UnwiredCount, Graph.IsTruncated);
    }
}
