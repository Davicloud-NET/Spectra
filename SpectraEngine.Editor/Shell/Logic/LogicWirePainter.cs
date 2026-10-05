using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Draws the wires: the path, the dot at each end, the label and the dot that
// travels along a wire that waits.
internal sealed class LogicWirePainter
{
    // How finely a path is walked to place the travelling dot.
    private const int StepsPerPiece = 12;

    private readonly LogicPalette _palette;
    private readonly LogicTextCache _texts;
    private LogicScene? _scene;
    private Geometry?[] _paths = [];

    public LogicWirePainter(LogicPalette palette, LogicTextCache texts)
    {
        _palette = palette;
        _texts = texts;
    }

    // The scene whose wires are drawn next. Its paths are made as they are
    // first drawn and kept for as long as the scene is.
    public void Use(LogicScene scene)
    {
        if (ReferenceEquals(scene, _scene))
            return;

        _scene = scene;
        _paths = new Geometry?[scene.Edges.Count];
    }

    public void DrawPath(DrawingContext context, int index, LogicWireFace face, bool isHovered, bool isSelected)
    {
        Geometry path = _paths[index] ??= PathOf(face.Edge);
        LogicWireLook look = face.State.Look;

        if (isHovered)
            context.DrawGeometry(null, _palette.Wires.Glow(look), path);

        context.DrawGeometry(null, isSelected ? _palette.Wires.Selected : _palette.Wires.Pen(look), path);
    }

    public void DrawEnds(DrawingContext context, LogicWireFace face, bool isSelected)
    {
        IBrush brush = _palette.Wires.Brush(isSelected ? LogicWireLook.Focus : face.State.Look);
        double radius = _palette.DotRadius;

        context.DrawEllipse(brush, null, face.Edge.Start, radius, radius);
        context.DrawEllipse(brush, null, face.Edge.End, radius, radius);
    }

    public void DrawLabel(DrawingContext context, LogicWireFace face, bool isHovered, bool isSelected)
    {
        string text = face.Text;
        if (text.Length == 0 || face.Edge.LabelBounds is not Rect room)
            return;

        LogicWireState state = face.State;
        LogicInk ink = state.Look switch
        {
            LogicWireLook.Broken => LogicInk.BrokenLabel,
            LogicWireLook.Firing or LogicWireLook.Waiting => LogicInk.LitLabel,
            _ => face.IsMono ? LogicInk.MonoLabel : LogicInk.Label,
        };

        double textRoom = room.Width - 2 * LogicMetrics.LabelPadding;
        FormattedText words = state.HasText
            ? _texts.GetRunning(text, ink, textRoom)
            : _texts.Get(text, ink, textRoom);

        if (face.Pill(words.Width) is not Rect pill)
            return;

        double radius = pill.Height / 2;

        IPen edge = ink switch
        {
            _ when isSelected => _palette.LitLabelEdge,
            LogicInk.BrokenLabel => _palette.BrokenLabelEdge,
            LogicInk.LitLabel => _palette.LitLabelEdge,
            _ => isHovered ? _palette.HoveredEdge : _palette.LabelEdge,
        };

        context.DrawRectangle(_palette.LabelFill, edge, pill.Deflate(0.5), radius, radius);
        context.DrawText(words, new Point(
            pill.X + (pill.Width - words.Width) / 2,
            pill.Center.Y - words.Height / 2));
    }

    public void DrawTravel(DrawingContext context, LogicWireFace face)
    {
        if (face.State.Travel is not double travel)
            return;

        double radius = _palette.TravelDotRadius;
        context.DrawEllipse(_palette.TravelFill, _palette.TravelEdge, PointAlong(face.Edge.Segments, travel), radius, radius);
    }

    private static StreamGeometry PathOf(LogicSceneEdge edge)
    {
        var path = new StreamGeometry();
        using StreamGeometryContext pen = path.Open();

        pen.BeginFigure(edge.Start, isFilled: false);
        for (int i = 0; i < edge.Segments.Count; i++)
        {
            LogicCubic piece = edge.Segments[i];
            pen.CubicBezierTo(piece.Control1, piece.Control2, piece.End);
        }

        pen.EndFigure(isClosed: false);
        return path;
    }

    // The point a share of the way along the path, by length and not by
    // piece: a wire's pieces differ a lot in length.
    private static Point PointAlong(IReadOnlyList<LogicCubic> pieces, double share)
    {
        double target = Length(pieces) * Math.Clamp(share, 0, 1);
        double walked = 0;

        for (int i = 0; i < pieces.Count; i++)
        {
            Point from = pieces[i].Start;
            for (int step = 1; step <= StepsPerPiece; step++)
            {
                Point to = pieces[i].At(step / (double)StepsPerPiece);
                double stride = Distance(from, to);

                if (walked + stride >= target && stride > 0)
                {
                    double along = (target - walked) / stride;
                    return new Point(from.X + (to.X - from.X) * along, from.Y + (to.Y - from.Y) * along);
                }

                walked += stride;
                from = to;
            }
        }

        return pieces[^1].End;
    }

    private static double Length(IReadOnlyList<LogicCubic> pieces)
    {
        double length = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            Point from = pieces[i].Start;
            for (int step = 1; step <= StepsPerPiece; step++)
            {
                Point to = pieces[i].At(step / (double)StepsPerPiece);
                length += Distance(from, to);
                from = to;
            }
        }

        return length;
    }

    private static double Distance(Point a, Point b)
    {
        double x = a.X - b.X;
        double y = a.Y - b.Y;
        return Math.Sqrt(x * x + y * y);
    }
}
