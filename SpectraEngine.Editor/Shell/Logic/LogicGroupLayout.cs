using Avalonia;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Lays out one group: cards that wires join, directly or through others.
// Everything is measured from the group's own top left corner until Emit
// puts it in the scene.
internal sealed class LogicGroupLayout
{
    private readonly IReadOnlyList<LogicLayoutNode> _cards;
    private readonly IReadOnlyList<LogicLayoutWire> _wires;
    private readonly List<LogicLayoutNode>[] _columns;
    private readonly LogicLanes _lanes;

    // Takes the group's cards by name, each with its place as its rank, and
    // its wires in their fixed order, each with its place as its index.
    public LogicGroupLayout(
        IReadOnlyList<LogicLayoutNode> cards,
        IReadOnlyList<LogicLayoutWire> wires,
        double minimumLaneWidth)
    {
        _cards = cards;
        _wires = wires;

        int columnCount = new LogicColumns(cards, wires).Assign();
        _columns = LogicColumnOrder.Build(cards, wires, columnCount);
        LogicRows.Place(_columns);
        _lanes = new LogicLanes(columnCount, wires, minimumLaneWidth);

        PlaceLaneLabels();
        double bottom = SettleTop();
        PlaceLoops();
        Size = PlaceBackRuns(bottom);
    }

    // The room the group takes, labels and back wires included.
    public Size Size { get; }

    public void Emit(Point origin, LogicSceneBuilder scene)
    {
        foreach (LogicLayoutNode card in _cards)
        {
            if (card.Face is LogicCardFace face)
                scene.Add(face, new Point(origin.X + _lanes.CardLeft(card), origin.Y + card.Top));
        }

        var router = new LogicWireRouter(_lanes, origin);
        var offset = new Vector(origin.X, origin.Y);

        foreach (LogicLayoutWire wire in _wires)
            scene.Add(wire.Edge, router.Route(wire), wire.LabelBounds?.Translate(offset));
    }

    // A forward wire's label goes in the lane before its receiver.
    private void PlaceLaneLabels()
    {
        var byLane = new List<LogicLayoutWire>?[_columns.Length];
        foreach (LogicLayoutWire wire in _wires)
        {
            if (wire.Route == LogicWireRoute.Forward && wire.HasLabel)
                (byLane[wire.To.Column - 1] ??= []).Add(wire);
        }

        for (int lane = 0; lane < byLane.Length; lane++)
        {
            if (byLane[lane] is List<LogicLayoutWire> labelled)
                StackLabels(labelled, _lanes.LaneCenter(lane));
        }
    }

    // Each label wants to be level with the middle of its wire's last hop.
    // Where two would touch, both give way.
    private static void StackLabels(List<LogicLayoutWire> wires, double centerX)
    {
        var stack = new List<(double Top, LogicLayoutWire Wire)>(wires.Count);
        foreach (LogicLayoutWire wire in wires)
        {
            LogicLayoutNode? slot = wire.Slots.Count > 0 ? wire.Slots[^1] : null;
            double left = slot is null ? wire.From.Top + wire.FromOffset : slot.Top + slot.Height / 2;
            double right = wire.To.Top + wire.ToOffset;
            stack.Add(((left + right - LogicMetrics.LabelHeight) / 2, wire));
        }

        stack.Sort((a, b) =>
        {
            int byTop = a.Top.CompareTo(b.Top);
            return byTop != 0 ? byTop : a.Wire.Index.CompareTo(b.Wire.Index);
        });

        var wanted = new double[stack.Count];
        var weights = new double[stack.Count];
        var steps = new double[stack.Count];

        for (int i = 0; i < stack.Count; i++)
        {
            wanted[i] = stack[i].Top;
            weights[i] = 1;
            steps[i] = LogicMetrics.RunStep;
        }

        double[] tops = LogicSpacing.Separate(wanted, weights, steps);
        for (int i = 0; i < stack.Count; i++)
        {
            LogicLayoutWire wire = stack[i].Wire;
            wire.LabelBounds = new Rect(
                Math.Round(centerX - wire.LabelWidth / 2), tops[i], wire.LabelWidth, LogicMetrics.LabelHeight);
        }
    }

    // A label may have been pushed above the first card. Moves everything
    // down until nothing is above the group's top, and returns the lowest
    // bottom.
    private double SettleTop()
    {
        double top = 0;
        foreach (LogicLayoutWire wire in _wires)
        {
            if (wire.LabelBounds is Rect label)
                top = Math.Min(top, label.Y);
        }

        double bottom = 0;
        foreach (List<LogicLayoutNode> column in _columns)
        {
            foreach (LogicLayoutNode node in column)
            {
                node.Top -= top;
                bottom = Math.Max(bottom, node.Top + node.Height);
            }
        }

        foreach (LogicLayoutWire wire in _wires)
        {
            if (wire.LabelBounds is Rect label)
            {
                Rect moved = label.Translate(new Vector(0, -top));
                wire.LabelBounds = moved;
                bottom = Math.Max(bottom, moved.Bottom);
            }
        }

        return bottom;
    }

    // A wire to its own card runs under the card, with its label in the middle.
    private void PlaceLoops()
    {
        foreach (LogicLayoutWire wire in _wires)
        {
            if (wire.Route != LogicWireRoute.Loop)
                continue;

            LogicLayoutNode card = wire.From;
            wire.RunY = card.Top + (card.Face?.Height ?? 0)
                + LogicMetrics.LoopDrop + LogicMetrics.RunStep * wire.Level;

            if (wire.HasLabel)
                wire.LabelBounds = LabelAt(_lanes.CardLeft(card) + LogicMetrics.CardWidth / 2, wire);
        }
    }

    // A wire back to an earlier column runs under everything else in the
    // group, each on a level of its own. Returns the group's size.
    private Size PlaceBackRuns(double bottom)
    {
        double right = _lanes.Right;
        double height = bottom;

        foreach (LogicLayoutWire wire in _wires)
        {
            if (wire.Route != LogicWireRoute.Back)
                continue;

            wire.RunY = bottom + LogicMetrics.BackRunDrop + LogicMetrics.RunStep * wire.Level;
            height = Math.Max(height, wire.RunY + LogicMetrics.LabelHeight / 2);

            if (!wire.HasLabel)
                continue;

            // In the middle of the run, unless that would put it off the group's left.
            Rect label = LabelAt((_lanes.DownRunX(wire) + _lanes.UpRunX(wire)) / 2, wire);
            if (label.X < 0)
                label = label.WithX(0);

            wire.LabelBounds = label;
            right = Math.Max(right, label.Right);
        }

        return new Size(right, height);
    }

    private static Rect LabelAt(double centerX, LogicLayoutWire wire) => new(
        Math.Round(centerX - wire.LabelWidth / 2),
        wire.RunY - LogicMetrics.LabelHeight / 2,
        wire.LabelWidth,
        LogicMetrics.LabelHeight);
}
