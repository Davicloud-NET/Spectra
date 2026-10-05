using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Gives each card of one group a column, left to right by cause, and marks
// the wires that close a cycle as running back.
internal sealed class LogicColumns
{
    private const byte Unseen = 0;
    private const byte OnPath = 1;
    private const byte Done = 2;

    private readonly IReadOnlyList<LogicLayoutNode> _cards;
    private readonly List<LogicLayoutWire>[] _leaving;
    private readonly int[] _arrivals;
    private readonly byte[] _state;
    private readonly List<LogicLayoutNode> _finished;
    private readonly Stack<(LogicLayoutNode Card, int Next)> _path = new();

    // Takes the group's cards by name, each with its place as its rank, and
    // its wires in their fixed order.
    public LogicColumns(IReadOnlyList<LogicLayoutNode> cards, IReadOnlyList<LogicLayoutWire> wires)
    {
        _cards = cards;
        _leaving = new List<LogicLayoutWire>[cards.Count];
        _arrivals = new int[cards.Count];
        _state = new byte[cards.Count];
        _finished = new List<LogicLayoutNode>(cards.Count);

        for (int i = 0; i < cards.Count; i++)
            _leaving[i] = [];

        foreach (LogicLayoutWire wire in wires)
        {
            if (wire.Route == LogicWireRoute.Loop)
                continue;

            _leaving[wire.From.Rank].Add(wire);
            _arrivals[wire.To.Rank]++;
        }
    }

    // Sets every card's column and returns how many columns there are.
    public int Assign()
    {
        MarkBackWires();

        // Cards finish after everything they send to, so walking the list
        // backwards meets each sender before its receivers.
        for (int i = _finished.Count - 1; i >= 0; i--)
        {
            LogicLayoutNode sender = _finished[i];
            foreach (LogicLayoutWire wire in _leaving[sender.Rank])
            {
                if (wire.Route == LogicWireRoute.Forward)
                    wire.To.Column = Math.Max(wire.To.Column, sender.Column + 1);
            }
        }

        PullSourcesRight();

        int last = 0;
        foreach (LogicLayoutNode card in _cards)
            last = Math.Max(last, card.Column);

        return last + 1;
    }

    private void MarkBackWires()
    {
        // Cards nothing sends to go first, so a cycle is cut where it is
        // entered. Within each pass the cards come by name.
        foreach (LogicLayoutNode card in _cards)
        {
            if (_arrivals[card.Rank] == 0)
                Walk(card);
        }

        foreach (LogicLayoutNode card in _cards)
            Walk(card);
    }

    // Depth first without recursion: a level can chain thousands of entities.
    private void Walk(LogicLayoutNode start)
    {
        if (_state[start.Rank] != Unseen)
            return;

        _state[start.Rank] = OnPath;
        _path.Push((start, 0));

        while (_path.Count > 0)
        {
            (LogicLayoutNode card, int next) = _path.Pop();
            List<LogicLayoutWire> wires = _leaving[card.Rank];

            if (next == wires.Count)
            {
                _state[card.Rank] = Done;
                _finished.Add(card);
                continue;
            }

            _path.Push((card, next + 1));
            LogicLayoutNode receiver = wires[next].To;

            if (_state[receiver.Rank] == OnPath)
            {
                wires[next].Route = LogicWireRoute.Back;
            }
            else if (_state[receiver.Rank] == Unseen)
            {
                _state[receiver.Rank] = OnPath;
                _path.Push((receiver, 0));
            }
        }
    }

    // A card nothing sends to sits one column before its nearest receiver, so
    // its wire does not cross the whole group to get there.
    private void PullSourcesRight()
    {
        var hasSender = new bool[_cards.Count];
        foreach (List<LogicLayoutWire> wires in _leaving)
        {
            foreach (LogicLayoutWire wire in wires)
                hasSender[wire.To.Rank] |= wire.Route == LogicWireRoute.Forward;
        }

        foreach (LogicLayoutNode card in _cards)
        {
            if (hasSender[card.Rank])
                continue;

            int nearest = int.MaxValue;
            foreach (LogicLayoutWire wire in _leaving[card.Rank])
            {
                if (wire.Route == LogicWireRoute.Forward)
                    nearest = Math.Min(nearest, wire.To.Column);
            }

            if (nearest != int.MaxValue)
                card.Column = nearest - 1;
        }
    }
}
