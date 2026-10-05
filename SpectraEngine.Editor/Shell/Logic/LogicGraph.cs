using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// A level's wiring as cards and edges: every entity, every wire resolved to
/// the entities its target names, and what the schemas say is wrong with each.
/// </summary>
public sealed class LogicGraph
{
    // Null for a card no edge touches.
    private readonly List<LogicEdge>?[] _arriving;
    private readonly List<LogicEdge>?[] _leaving;
    private readonly Dictionary<Guid, LogicCard> _byNode = [];
    private readonly HashSet<LogicWireKey> _goingNowhere;

    internal LogicGraph(
        IReadOnlyList<LogicCard> cards,
        IReadOnlyList<LogicEdge> edges,
        HashSet<LogicWireKey> goingNowhere)
    {
        Cards = cards;
        Edges = edges;
        _goingNowhere = goingNowhere;
        _arriving = new List<LogicEdge>?[cards.Count];
        _leaving = new List<LogicEdge>?[cards.Count];

        foreach (LogicCard card in cards)
        {
            if (!card.IsStub)
                _byNode.TryAdd(card.NodeId, card);
        }

        foreach (LogicEdge edge in edges)
        {
            (_leaving[edge.From.Index] ??= []).Add(edge);
            (_arriving[edge.To.Index] ??= []).Add(edge);
        }
    }

    /// <summary>
    /// Reads a level's wiring. Without a catalogue every class is unknown, and
    /// nothing is said about inputs and outputs.
    /// </summary>
    public static LogicGraph Build(LogicGraphInfo info, EntitySchemaCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new LogicGraphBuilder(info, catalog).Build();
    }

    /// <summary>
    /// The wired entities in scene order, then the stubs by kind and name,
    /// then the entities with no wires in scene order.
    /// </summary>
    public IReadOnlyList<LogicCard> Cards { get; }

    /// <summary>The edges, by sender in scene order and then by wire.</summary>
    public IReadOnlyList<LogicEdge> Edges { get; }

    /// <summary>How many wires the level has.</summary>
    public int WireCount { get; internal init; }

    /// <summary>How many entities have no wire leaving or arriving.</summary>
    public int UnwiredCount { get; internal init; }

    /// <summary>
    /// How many wires can never deliver: the target is missing, or no entity
    /// it reaches has the input.
    /// </summary>
    public int WiresGoingNowhere => _goingNowhere.Count;

    /// <summary>An edge of the first such wire in scene order, or null.</summary>
    public LogicEdge? FirstGoingNowhere { get; internal init; }

    /// <summary>Whether the level has more entities than the snapshot listed.</summary>
    public bool IsTruncated { get; internal init; }

    /// <summary>The card of an entity, or null when the node is not an entity the level lists.</summary>
    public LogicCard? CardOf(Guid nodeId) => _byNode.GetValueOrDefault(nodeId);

    /// <summary>The edges that end at a card: who sends what to which of its inputs.</summary>
    public IReadOnlyList<LogicEdge> Arriving(LogicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return _arriving[card.Index] ?? (IReadOnlyList<LogicEdge>)[];
    }

    /// <summary>The edges that start at a card.</summary>
    public IReadOnlyList<LogicEdge> Leaving(LogicCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return _leaving[card.Index] ?? (IReadOnlyList<LogicEdge>)[];
    }

    /// <summary>Whether a wire can never deliver.</summary>
    public bool GoesNowhere(LogicWireKey wire) => _goingNowhere.Contains(wire);
}
