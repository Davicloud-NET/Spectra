using Avalonia;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>One cubic Bezier piece of a wire.</summary>
public readonly record struct LogicCubic(Point Start, Point Control1, Point Control2, Point End)
{
    private const int Steps = 16;

    /// <summary>A box the curve cannot leave.</summary>
    public Rect Bounds
    {
        get
        {
            double left = Math.Min(Math.Min(Start.X, Control1.X), Math.Min(Control2.X, End.X));
            double top = Math.Min(Math.Min(Start.Y, Control1.Y), Math.Min(Control2.Y, End.Y));
            double right = Math.Max(Math.Max(Start.X, Control1.X), Math.Max(Control2.X, End.X));
            double bottom = Math.Max(Math.Max(Start.Y, Control1.Y), Math.Max(Control2.Y, End.Y));
            return new Rect(left, top, right - left, bottom - top);
        }
    }

    /// <summary>The point at <paramref name="t"/>, from 0 at the start to 1 at the end.</summary>
    public Point At(double t)
    {
        double u = 1 - t;
        double a = u * u * u;
        double b = 3 * u * u * t;
        double c = 3 * u * t * t;
        double d = t * t * t;

        return new Point(
            a * Start.X + b * Control1.X + c * Control2.X + d * End.X,
            a * Start.Y + b * Control1.Y + c * Control2.Y + d * End.Y);
    }

    /// <summary>How far a point is from the curve, near enough for picking.</summary>
    public double DistanceTo(Point point)
    {
        double nearest = double.MaxValue;
        Point from = Start;

        for (int step = 1; step <= Steps; step++)
        {
            Point to = At(step / (double)Steps);
            nearest = Math.Min(nearest, DistanceToSegment(point, from, to));
            from = to;
        }

        return nearest;
    }

    private static double DistanceToSegment(Point point, Point from, Point to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double lengthSquared = dx * dx + dy * dy;

        double along = lengthSquared == 0
            ? 0
            : Math.Clamp(((point.X - from.X) * dx + (point.Y - from.Y) * dy) / lengthSquared, 0, 1);

        double x = from.X + along * dx - point.X;
        double y = from.Y + along * dy - point.Y;
        return Math.Sqrt(x * x + y * y);
    }
}
