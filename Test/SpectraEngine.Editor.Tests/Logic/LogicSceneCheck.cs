using Avalonia;
using SpectraEngine.Editor.Shell.Logic;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Tests.Logic;

// The rules a laid out scene has to keep, checked the way the design mock checked itself.
internal static class LogicSceneCheck
{
    private const int SamplesPerPiece = 12;

    // The first thing wrong with the scene's text: a card or label outside
    // the scene, or two of them closer than the gap. Null when nothing is.
    public static string? FirstTextProblem(LogicScene scene)
    {
        var boxes = new List<(string Name, Rect Box)>();
        foreach (LogicSceneCard card in scene.Cards)
            boxes.Add(($"card '{card.Card.Name}'", card.Bounds));

        foreach (LogicSceneEdge edge in scene.Edges)
        {
            if (edge.LabelBounds is Rect label)
                boxes.Add(($"label '{edge.Label.Text}' of {edge.Edge.From.Name} > {edge.Edge.To.Name}", label));
        }

        foreach ((string name, Rect box) in boxes)
        {
            if (box.X < 0 || box.Y < 0 || box.Right > scene.Size.Width || box.Bottom > scene.Size.Height)
                return $"{name} at {box} leaves the scene of {scene.Size}";
        }

        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                if (!Apart(boxes[i].Box, boxes[j].Box))
                    return $"{boxes[i].Name} at {boxes[i].Box} touches {boxes[j].Name} at {boxes[j].Box}";
            }
        }

        return null;
    }

    // The first wire that runs across a card it does not end on, or null.
    public static string? FirstCardCrossing(LogicScene scene)
    {
        foreach (LogicSceneEdge edge in scene.Edges)
        {
            foreach (LogicSceneCard card in scene.Cards)
            {
                bool isEnd = ReferenceEquals(card.Card, edge.Edge.From) || ReferenceEquals(card.Card, edge.Edge.To);
                if (!isEnd && Crosses(edge, card.Bounds))
                    return $"the wire {edge.Edge.From.Name} > {edge.Edge.To.Name} crosses card '{card.Card.Name}'";
            }
        }

        return null;
    }

    public static bool Crosses(LogicSceneEdge edge, Rect card)
    {
        if (!edge.Bounds.Intersects(card))
            return false;

        Rect inside = card.Deflate(1);
        foreach (LogicCubic piece in edge.Segments)
        {
            for (int i = 0; i <= SamplesPerPiece; i++)
            {
                if (inside.Contains(piece.At(i / (double)SamplesPerPiece)))
                    return true;
            }
        }

        return false;
    }

    private static bool Apart(Rect a, Rect b) =>
        a.Right + LogicMetrics.TextGap <= b.X
        || b.Right + LogicMetrics.TextGap <= a.X
        || a.Bottom + LogicMetrics.TextGap <= b.Y
        || b.Bottom + LogicMetrics.TextGap <= a.Y;
}
