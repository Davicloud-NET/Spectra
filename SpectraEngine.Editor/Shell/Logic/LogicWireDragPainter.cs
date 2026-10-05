using Avalonia;
using Avalonia.Media;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

// Draws a wire on its way to a card: the card it would land on, lit, and the
// wire from its sender to the pointer.
internal sealed class LogicWireDragPainter
{
    // The wire is drawn as short lines. A path would be made anew for every
    // move of the pointer.
    private const int Pieces = 24;

    private readonly LogicPalette _palette;

    public LogicWireDragPainter(LogicPalette palette) => _palette = palette;

    // Drawn in the scene, like the cards. The pointer is a point of the view.
    public void Draw(DrawingContext context, LogicWireGesture gesture, LogicPanZoom view)
    {
        if (!gesture.ShowsWire)
            return;

        if (gesture.Target is { } target)
        {
            double corner = _palette.CardRadius;
            context.DrawRectangle(_palette.TargetWash, _palette.SelectedEdge, target.Bounds, corner, corner);
        }

        Point start = gesture.Start;
        Point end = view.ToScene(gesture.Pointer);

        // Level at both ends, as a laid out wire is. It leaves to the right
        // even when the pointer is behind its card.
        double reach = Math.Max(Math.Abs(end.X - start.X) / 2, LogicMetrics.LaneWidth / 2);
        var curve = new LogicCubic(start, new Point(start.X + reach, start.Y), new Point(end.X - reach, end.Y), end);

        IPen pen = _palette.Wires.Pen(LogicWireLook.Focus);
        Point from = start;

        for (int piece = 1; piece <= Pieces; piece++)
        {
            Point to = curve.At(piece / (double)Pieces);
            context.DrawLine(pen, from, to);
            from = to;
        }

        IBrush brush = _palette.Wires.Brush(LogicWireLook.Focus);
        double dot = _palette.DotRadius;

        context.DrawEllipse(brush, null, start, dot, dot);
        context.DrawEllipse(brush, null, end, dot, dot);
    }
}
