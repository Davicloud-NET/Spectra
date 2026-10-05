using Avalonia;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Draws a group's wires as cubics, once everything has its place.
internal sealed class LogicWireRouter
{
    // How far along a corner's two legs its control points sit, for a quarter circle.
    private const double Roundness = 0.5523;

    private readonly LogicLanes _lanes;
    private readonly Vector _origin;
    private List<LogicCubic> _path = [];

    // origin is where the group's top left corner is in the scene.
    public LogicWireRouter(LogicLanes lanes, Point origin)
    {
        _lanes = lanes;
        _origin = new Vector(origin.X, origin.Y);
    }

    public IReadOnlyList<LogicCubic> Route(LogicLayoutWire wire)
    {
        _path = [];
        var start = new Point(_lanes.CardLeft(wire.From) + LogicMetrics.CardWidth, wire.From.Top + wire.FromOffset);
        var end = new Point(_lanes.CardLeft(wire.To), wire.To.Top + wire.ToOffset);

        switch (wire.Route)
        {
            case LogicWireRoute.Loop:
                Loop(wire, start, end);
                break;
            case LogicWireRoute.Back:
                Back(wire, start, end);
                break;
            default:
                Forward(wire, start, end);
                break;
        }

        return _path;
    }

    private void Forward(LogicLayoutWire wire, Point start, Point end)
    {
        Point from = start;
        foreach (LogicLayoutNode slot in wire.Slots)
        {
            double y = slot.Top + slot.Height / 2;
            var entry = new Point(_lanes.ColumnLeft(slot.Column), y);
            var exit = new Point(_lanes.ColumnRight(slot.Column), y);

            Sweep(from, entry);
            Line(entry, exit);
            from = exit;
        }

        if (wire.LabelBounds is Rect label)
        {
            Sweep(from, label.Center);
            Sweep(label.Center, end);
        }
        else
        {
            Sweep(from, end);
        }
    }

    // Out of the right, under the card, back in on the left.
    private void Loop(LogicLayoutWire wire, Point start, Point end)
    {
        double reach = Math.Min(
            LogicMetrics.LoopReach + LogicMetrics.LoopReachStep * wire.Level,
            LogicMetrics.LoopReachLimit);
        var under = new Point((start.X + end.X) / 2, wire.RunY);

        Add(start, new Point(start.X + reach, start.Y), new Point(start.X + reach, under.Y), under);
        Add(under, new Point(end.X - reach, under.Y), new Point(end.X - reach, end.Y), end);
    }

    // Out of the right, down beside the column, along under the group, up
    // beside the receiver's column and in on the left.
    private void Back(LogicLayoutWire wire, Point start, Point end)
    {
        double down = _lanes.DownRunX(wire);
        double up = _lanes.UpRunX(wire);
        double y = wire.RunY;

        Point from = Turn(start, new Point(down, start.Y), new Point(down, y));
        from = Turn(from, new Point(down, y), new Point(up, y));

        if (wire.LabelBounds is Rect label)
        {
            double radius = LogicMetrics.CornerRadius;
            var through = new Point(Math.Clamp(label.Center.X, up + radius, down - radius), y);
            Line(from, through);
            from = through;
        }

        from = Turn(from, new Point(up, y), new Point(up, end.Y));
        from = Turn(from, new Point(up, end.Y), end);
        Line(from, end);
    }

    // Draws up to a corner and rounds it. Returns where the next leg starts.
    private Point Turn(Point from, Point corner, Point toward)
    {
        Point before = Short(corner, from);
        Point after = Short(corner, toward);

        Line(from, before);
        Add(before, Between(before, corner), Between(after, corner), after);
        return after;
    }

    // The point one corner radius from a corner along the leg to another point.
    private static Point Short(Point corner, Point other)
    {
        double x = other.X - corner.X;
        double y = other.Y - corner.Y;
        double length = Math.Sqrt(x * x + y * y);
        if (length == 0)
            return corner;

        double step = Math.Min(LogicMetrics.CornerRadius, length) / length;
        return new Point(corner.X + x * step, corner.Y + y * step);
    }

    private static Point Between(Point from, Point corner) =>
        new(from.X + (corner.X - from.X) * Roundness, from.Y + (corner.Y - from.Y) * Roundness);

    // An S through a lane: level at both ends.
    private void Sweep(Point from, Point to)
    {
        double half = (to.X - from.X) / 2;
        Add(from, new Point(from.X + half, from.Y), new Point(to.X - half, to.Y), to);
    }

    private void Line(Point from, Point to)
    {
        if (from == to)
            return;

        var third = new Vector((to.X - from.X) / 3, (to.Y - from.Y) / 3);
        Add(from, from + third, to - third, to);
    }

    private void Add(Point start, Point control1, Point control2, Point end) =>
        _path.Add(new LogicCubic(start + _origin, control1 + _origin, control2 + _origin, end + _origin));
}
