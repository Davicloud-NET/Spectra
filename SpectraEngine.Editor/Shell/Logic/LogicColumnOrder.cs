using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Fills a group's columns: gives each long wire a slot in every column it
// skips, then orders each column so wires cross as little as a few sweeps
// can manage.
internal static class LogicColumnOrder
{
    // Fixed, so the same graph always gets the same picture.
    private const int Sweeps = 8;

    public static List<LogicLayoutNode>[] Build(
        IReadOnlyList<LogicLayoutNode> cards,
        IReadOnlyList<LogicLayoutWire> wires,
        int columnCount)
    {
        var columns = new List<LogicLayoutNode>[columnCount];
        for (int i = 0; i < columnCount; i++)
            columns[i] = [];

        foreach (LogicLayoutNode card in cards)
            columns[card.Column].Add(card);

        int rank = cards.Count;
        foreach (LogicLayoutWire wire in wires)
        {
            if (wire.Route != LogicWireRoute.Forward)
                continue;

            for (int column = wire.From.Column + 1; column < wire.To.Column; column++)
            {
                var slot = new LogicLayoutNode(null, LogicMetrics.SlotHeight) { Rank = rank++, Column = column };
                wire.Slots.Add(slot);
                columns[column].Add(slot);
            }

            Link(wire);
        }

        foreach (List<LogicLayoutNode> column in columns)
        {
            column.Sort((a, b) => a.Rank.CompareTo(b.Rank));
            Number(column);
        }

        for (int sweep = 0; sweep < Sweeps; sweep++)
        {
            if (sweep % 2 == 0)
            {
                for (int i = 1; i < columnCount; i++)
                    Reorder(columns[i], towardLeft: true);
            }
            else
            {
                for (int i = columnCount - 2; i >= 0; i--)
                    Reorder(columns[i], towardLeft: false);
            }
        }

        return columns;
    }

    private static void Link(LogicLayoutWire wire)
    {
        LogicLayoutNode left = wire.From;
        double leftOffset = wire.FromOffset;

        foreach (LogicLayoutNode slot in wire.Slots)
        {
            Join(left, leftOffset, slot, slot.Height / 2);
            left = slot;
            leftOffset = slot.Height / 2;
        }

        Join(left, leftOffset, wire.To, wire.ToOffset);
    }

    private static void Join(LogicLayoutNode left, double leftOffset, LogicLayoutNode right, double rightOffset)
    {
        left.Right.Add(new LogicLink(right, rightOffset, leftOffset));
        right.Left.Add(new LogicLink(left, leftOffset, rightOffset));
    }

    // Each node goes to the average place of what it is wired to in the
    // neighbouring column. A node wired to nothing there keeps its place.
    private static void Reorder(List<LogicLayoutNode> column, bool towardLeft)
    {
        foreach (LogicLayoutNode node in column)
        {
            List<LogicLink> links = towardLeft ? node.Left : node.Right;
            if (links.Count == 0)
            {
                node.SortKey = node.Order;
                continue;
            }

            double sum = 0;
            foreach (LogicLink link in links)
                sum += link.Other.Order + link.OtherOffset / (link.Other.Height + 1);

            node.SortKey = sum / links.Count;
        }

        column.Sort((a, b) =>
        {
            int byKey = a.SortKey.CompareTo(b.SortKey);
            return byKey != 0 ? byKey : a.Rank.CompareTo(b.Rank);
        });

        Number(column);
    }

    private static void Number(List<LogicLayoutNode> column)
    {
        for (int i = 0; i < column.Count; i++)
            column[i].Order = i;
    }
}
