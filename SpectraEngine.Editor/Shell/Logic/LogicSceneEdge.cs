using Avalonia;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>An edge with a path through the scene.</summary>
public sealed class LogicSceneEdge
{
    internal LogicSceneEdge(LogicEdge edge, IReadOnlyList<LogicCubic> segments, Rect? labelBounds)
    {
        Edge = edge;
        Segments = segments;
        LabelBounds = labelBounds;

        // Not Rect.Union: a straight piece has a box with no area.
        Rect first = segments[0].Bounds;
        double left = first.X, top = first.Y, right = first.Right, bottom = first.Bottom;

        for (int i = 1; i < segments.Count; i++)
        {
            Rect next = segments[i].Bounds;
            left = Math.Min(left, next.X);
            top = Math.Min(top, next.Y);
            right = Math.Max(right, next.Right);
            bottom = Math.Max(bottom, next.Bottom);
        }

        Bounds = new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>The edge this draws.</summary>
    public LogicEdge Edge { get; }

    /// <summary>The path, each piece starting where the one before ends.</summary>
    public IReadOnlyList<LogicCubic> Segments { get; }

    /// <summary>Where the path leaves the sender's output.</summary>
    public Point Start => Segments[0].Start;

    /// <summary>Where the path enters the receiver's input.</summary>
    public Point End => Segments[^1].End;

    /// <summary>The words on the edge. Empty for none.</summary>
    public LogicLabel Label => Edge.Label;

    /// <summary>The label's box, when the edge has a label. The path runs through its middle.</summary>
    public Rect? LabelBounds { get; }

    /// <summary>The first wire this edge draws.</summary>
    public LogicWireKey Wire => Edge.Wire;

    /// <summary>Every wire this edge draws, as indices into the sender's wire list.</summary>
    public IReadOnlyList<int> WireIndices => Edge.WireIndices;

    /// <summary>What the schemas say about the wire.</summary>
    public LogicVerdict Verdict => Edge.Verdict;

    /// <summary>A box the path cannot leave.</summary>
    public Rect Bounds { get; }

    /// <summary>How far a point is from the path, near enough for picking.</summary>
    public double DistanceTo(Point point)
    {
        double nearest = double.MaxValue;
        foreach (LogicCubic segment in Segments)
            nearest = Math.Min(nearest, segment.DistanceTo(point));

        return nearest;
    }

    // The same, looking only at pieces that can be within reach. A wire that
    // crosses a whole level has many pieces, and picking runs on every move.
    internal double DistanceWithin(Point point, double reach)
    {
        double nearest = double.MaxValue;
        foreach (LogicCubic segment in Segments)
        {
            if (segment.Bounds.Inflate(reach).Contains(point))
                nearest = Math.Min(nearest, segment.DistanceTo(point));
        }

        return nearest;
    }
}
