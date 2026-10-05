using Avalonia;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>A wiring graph laid out: where every card, wire and label goes.</summary>
public sealed class LogicScene
{
    private readonly Dictionary<Guid, LogicSceneCard> _byNode = [];

    internal LogicScene(IReadOnlyList<LogicSceneCard> cards, IReadOnlyList<LogicSceneEdge> edges, Size size)
    {
        Cards = cards;
        Edges = edges;
        Size = size;

        foreach (LogicSceneCard card in cards)
        {
            if (!card.Card.IsStub)
                _byNode.TryAdd(card.Card.NodeId, card);
        }
    }

    /// <summary>The cards, group by group from the top, by name inside a group.</summary>
    public IReadOnlyList<LogicSceneCard> Cards { get; }

    /// <summary>The edges, in the order of the cards they leave.</summary>
    public IReadOnlyList<LogicSceneEdge> Edges { get; }

    /// <summary>How much room the scene takes. Everything with text in it lies inside.</summary>
    public Size Size { get; }

    /// <summary>The card of an entity, or null when the scene does not show it.</summary>
    public LogicSceneCard? CardOf(Guid nodeId) => _byNode.GetValueOrDefault(nodeId);

    /// <summary>
    /// What is under a point. A port wins over its card, a card over a label,
    /// a label over a wire. Among several of one kind the nearest wins.
    /// </summary>
    /// <param name="point">A point in the scene.</param>
    /// <param name="tolerance">How far from a wire or a port's dot still counts.</param>
    public LogicHit HitTest(Point point, double tolerance)
    {
        LogicHit port = HitPort(point, tolerance);
        if (port.Kind != LogicHitKind.None)
            return port;

        foreach (LogicSceneCard card in Cards)
        {
            if (card.Bounds.Contains(point))
                return new LogicHit(LogicHitKind.Card, card, null, null);
        }

        foreach (LogicSceneEdge edge in Edges)
        {
            if (edge.LabelBounds is Rect label && label.Contains(point))
                return new LogicHit(LogicHitKind.Label, null, null, edge);
        }

        return HitEdge(point, tolerance);
    }

    private LogicHit HitPort(Point point, double tolerance)
    {
        LogicHit nearest = LogicHit.None;
        double nearestDistance = double.MaxValue;

        foreach (LogicSceneCard card in Cards)
        {
            if (!card.Bounds.Inflate(tolerance).Contains(point))
                continue;

            foreach (LogicScenePort port in card.Ports)
            {
                double distance = Distance(port.Anchor, point);
                if (distance <= tolerance && distance < nearestDistance)
                {
                    nearest = new LogicHit(LogicHitKind.Port, card, port, null);
                    nearestDistance = distance;
                }
            }
        }

        return nearest.Kind == LogicHitKind.None ? HitPortRow(point) : nearest;
    }

    private LogicHit HitPortRow(Point point)
    {
        foreach (LogicSceneCard card in Cards)
        {
            if (!card.Bounds.Contains(point))
                continue;

            foreach (LogicScenePort port in card.Ports)
            {
                if (port.Row.Contains(point))
                    return new LogicHit(LogicHitKind.Port, card, port, null);
            }
        }

        return LogicHit.None;
    }

    private LogicHit HitEdge(Point point, double tolerance)
    {
        LogicSceneEdge? nearest = null;
        double nearestDistance = double.MaxValue;

        foreach (LogicSceneEdge edge in Edges)
        {
            if (!edge.Bounds.Inflate(tolerance).Contains(point))
                continue;

            double distance = edge.DistanceWithin(point, tolerance);
            if (distance <= tolerance && distance < nearestDistance)
            {
                nearest = edge;
                nearestDistance = distance;
            }
        }

        return nearest is null ? LogicHit.None : new LogicHit(LogicHitKind.Edge, null, null, nearest);
    }

    private static double Distance(Point a, Point b)
    {
        double x = a.X - b.X;
        double y = a.Y - b.Y;
        return Math.Sqrt(x * x + y * y);
    }
}
