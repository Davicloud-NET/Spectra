using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Gives every node of a group its height on the page: columns keep their
// order, nothing touches, and wires run as level as a few passes can make
// them.
internal static class LogicRows
{
    // Fixed, so the same graph always gets the same picture.
    private const int Rounds = 4;
    private const int FinalPasses = 2;

    // A node with no wire on the side being looked at moves when pushed.
    private const double LooseWeight = 0.001;

    private enum Side
    {
        Left,
        Right,
        Both,
    }

    public static void Place(List<LogicLayoutNode>[] columns)
    {
        foreach (List<LogicLayoutNode> column in columns)
        {
            double top = 0;
            for (int i = 0; i < column.Count; i++)
            {
                column[i].Top = top;
                top += Step(column, i);
            }
        }

        for (int round = 0; round < Rounds; round++)
        {
            for (int i = 1; i < columns.Length; i++)
                Align(columns[i], Side.Left);

            for (int i = columns.Length - 2; i >= 0; i--)
                Align(columns[i], Side.Right);
        }

        for (int pass = 0; pass < FinalPasses; pass++)
        {
            foreach (List<LogicLayoutNode> column in columns)
                Align(column, Side.Both);
        }

        MoveToTop(columns);
    }

    // Moves each node toward where its wires would be level, then pushes the
    // column apart again.
    private static void Align(List<LogicLayoutNode> column, Side side)
    {
        var wanted = new double[column.Count];
        var weights = new double[column.Count];
        var steps = new double[column.Count];

        for (int i = 0; i < column.Count; i++)
        {
            LogicLayoutNode node = column[i];
            double pull = 0;
            int wires = 0;

            if (side != Side.Right)
                Pull(node, node.Left, ref pull, ref wires);

            if (side != Side.Left)
                Pull(node, node.Right, ref pull, ref wires);

            wanted[i] = wires == 0 ? node.Top : node.Top + pull / wires;
            weights[i] = wires == 0 ? LooseWeight : 1;
            steps[i] = Step(column, i);
        }

        double[] tops = LogicSpacing.Separate(wanted, weights, steps);
        for (int i = 0; i < column.Count; i++)
            column[i].Top = tops[i];
    }

    private static void Pull(LogicLayoutNode node, List<LogicLink> links, ref double pull, ref int wires)
    {
        foreach (LogicLink link in links)
        {
            pull += link.Other.Top + link.OtherOffset - (node.Top + link.OwnOffset);
            wires++;
        }
    }

    // From a node's top to the least top of the node under it.
    private static double Step(List<LogicLayoutNode> column, int index)
    {
        LogicLayoutNode node = column[index];
        if (index == column.Count - 1)
            return node.Height;

        LogicLayoutNode next = column[index + 1];
        double gap = node.IsCard && next.IsCard ? LogicMetrics.CardGap
            : node.IsCard || next.IsCard ? LogicMetrics.CardSlotGap
            : LogicMetrics.SlotGap;

        return node.Height + gap;
    }

    private static void MoveToTop(List<LogicLayoutNode>[] columns)
    {
        double top = double.MaxValue;
        foreach (List<LogicLayoutNode> column in columns)
        {
            if (column.Count > 0)
                top = Math.Min(top, column[0].Top);
        }

        foreach (List<LogicLayoutNode> column in columns)
        {
            foreach (LogicLayoutNode node in column)
                node.Top -= top;
        }
    }
}
