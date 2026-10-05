using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Splits what a scope shows into groups of cards that wires join, and puts
// the groups in the order they stack: the ones holding a selected card, then
// the larger, then by name. A card with no wires is a group of its own. Those
// come after every other, selected or not, so one that comes or goes moves
// no wired card.
internal sealed class LogicGroups
{
    private readonly LogicCard[] _cards;
    private readonly LogicEdge[] _edges;
    private readonly LogicLayoutNode[] _nodes;

    // A card's place in _cards, by its place in the whole graph.
    private readonly int[] _rank;
    private readonly int[] _parent;

    private sealed class Group
    {
        public List<LogicLayoutNode> Cards { get; } = [];

        public List<LogicLayoutWire> Wires { get; } = [];

        public bool HasSelection { get; set; }

        public bool IsWired { get; set; }

        public int Place { get; init; }
    }

    private LogicGroups(LogicScopedGraph graph, LogicLayoutOptions options)
    {
        _cards = [.. graph.Cards];
        Array.Sort(_cards, LogicCardOrder.Compare);

        _rank = new int[graph.Graph.Cards.Count];
        for (int i = 0; i < _cards.Length; i++)
            _rank[_cards[i].Index] = i;

        // A wire's place in its sender's list is what tells two of its edges apart.
        _edges = [.. graph.Edges];
        Array.Sort(_edges, (a, b) =>
        {
            int byFrom = _rank[a.From.Index].CompareTo(_rank[b.From.Index]);
            if (byFrom != 0)
                return byFrom;

            int byWire = a.WireIndices[0].CompareTo(b.WireIndices[0]);
            return byWire != 0 ? byWire : _rank[a.To.Index].CompareTo(_rank[b.To.Index]);
        });

        var loops = new int[_cards.Length];
        _parent = new int[_cards.Length];

        for (int i = 0; i < _parent.Length; i++)
            _parent[i] = i;

        foreach (LogicEdge edge in _edges)
        {
            if (edge.IsLoop)
                loops[_rank[edge.From.Index]]++;
            else
                _parent[Root(_rank[edge.From.Index])] = Root(_rank[edge.To.Index]);
        }

        _nodes = new LogicLayoutNode[_cards.Length];
        for (int i = 0; i < _nodes.Length; i++)
        {
            var face = new LogicCardFace(_cards[i], options);
            _nodes[i] = new LogicLayoutNode(face, face.Height + LoopRoom(loops[i]));
        }
    }

    public static List<LogicGroupLayout> Split(
        LogicScopedGraph graph,
        LogicSceneBuilder scene,
        LogicLayoutOptions options,
        ILogicTextMeasure measure)
    {
        var split = new LogicGroups(graph, options);
        List<Group> groups = split.Collect(scene, measure);

        groups.Sort((a, b) =>
        {
            int byWires = b.IsWired.CompareTo(a.IsWired);
            if (byWires != 0)
                return byWires;

            int bySelection = b.HasSelection.CompareTo(a.HasSelection);
            if (bySelection != 0)
                return bySelection;

            int bySize = b.Cards.Count.CompareTo(a.Cards.Count);
            return bySize != 0 ? bySize : a.Place.CompareTo(b.Place);
        });

        var layouts = new List<LogicGroupLayout>(groups.Count);
        foreach (Group group in groups)
            layouts.Add(new LogicGroupLayout(group.Cards, group.Wires, options.MinimumLaneWidth));

        return layouts;
    }

    // Groups come out by the name of their first card, since the cards are walked by name.
    private List<Group> Collect(LogicSceneBuilder scene, ILogicTextMeasure measure)
    {
        var groups = new List<Group>();
        var groupOf = new Group?[_cards.Length];

        for (int i = 0; i < _cards.Length; i++)
        {
            int root = Root(i);
            Group group = groupOf[root] ??= NewGroup(groups);
            groupOf[i] = group;

            _nodes[i].Rank = group.Cards.Count;
            group.Cards.Add(_nodes[i]);
            group.HasSelection |= scene.IsSelected(_cards[i]);
            group.IsWired |= _cards[i].IsWired;
        }

        var loopsSeen = new int[_cards.Length];
        foreach (LogicEdge edge in _edges)
        {
            int from = _rank[edge.From.Index];
            Group? group = groupOf[from];
            if (group is null)
                continue;

            var wire = new LogicLayoutWire(edge, _nodes[from], _nodes[_rank[edge.To.Index]], LabelWidth(edge, measure))
            {
                Index = group.Wires.Count,
            };

            if (edge.IsLoop)
                wire.Level = loopsSeen[from]++;

            group.Wires.Add(wire);
        }

        return groups;
    }

    private static Group NewGroup(List<Group> groups)
    {
        var group = new Group { Place = groups.Count };
        groups.Add(group);
        return group;
    }

    private int Root(int card)
    {
        while (_parent[card] != card)
        {
            _parent[card] = _parent[_parent[card]];
            card = _parent[card];
        }

        return card;
    }

    // The room under a card for its wires to itself, down to the last one's label.
    private static double LoopRoom(int loops) => loops == 0
        ? 0
        : LogicMetrics.LoopDrop + LogicMetrics.RunStep * (loops - 1) + LogicMetrics.LabelHeight / 2;

    private static double LabelWidth(LogicEdge edge, ILogicTextMeasure measure)
    {
        if (edge.Label.IsEmpty)
            return 0;

        LogicTextStyle style = edge.Label.IsMono ? LogicTextStyle.MonoLabel : LogicTextStyle.Label;
        double width = measure.Width(edge.Label.Text, style) + 2 * LogicMetrics.LabelPadding;

        // A whole, even number of pixels, so the box's middle is a whole pixel too.
        return Math.Ceiling(width / 2) * 2;
    }
}
