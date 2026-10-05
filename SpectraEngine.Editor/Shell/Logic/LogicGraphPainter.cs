using Avalonia;
using Avalonia.Media;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Logic;

// Draws one frame of the Logic view's graph: the ground, then what the
// filter dims, then the rest. Only what the view can see is drawn.
internal sealed class LogicGraphPainter
{
    private readonly LogicPalette _palette = new();
    private readonly LogicCardPainter _cards;
    private readonly LogicWirePainter _wires;

    // What the frame being drawn is of. Set by Draw, read by its passes.
    private LogicViewModel? _model;
    private LogicSceneCard? _hoveredCard;
    private LogicSceneEdge? _hoveredEdge;
    private LogicDetail _detail;
    private Rect _seen;
    private double _reach;

    public LogicGraphPainter()
    {
        var texts = new LogicTextCache(_palette);
        _cards = new LogicCardPainter(_palette, texts);
        _wires = new LogicWirePainter(_palette, texts);
    }

    public void Draw(
        DrawingContext context,
        Size size,
        LogicViewModel? model,
        LogicSceneCard? hoveredCard,
        LogicSceneEdge? hoveredEdge)
    {
        var view = new Rect(size);
        context.FillRectangle(_palette.Ground, view);

        if (model is null)
            return;

        LogicPanZoom at = model.View;
        DrawGrid(context, view, at);

        if (model.Scene is not { Cards.Count: > 0 } scene)
            return;

        _model = model;
        _hoveredCard = hoveredCard;
        _hoveredEdge = hoveredEdge;
        _detail = LogicDrawMetrics.DetailAt(at.Zoom);
        _seen = at.ToScene(view);

        // How far past its box a wire draws: its dots, the glow under it, and
        // its own width, which grows as the view moves out.
        _reach = 2 * _palette.TravelDotRadius + LogicDrawMetrics.LeastWireOnScreen / at.Zoom;

        _wires.Use(scene);
        _palette.Wires.SetZoom(at.Zoom);

        if (!string.IsNullOrWhiteSpace(model.Filter))
        {
            using (context.PushTransform(at.Matrix))
                DrawPass(context, model, scene, dimmed: true);

            context.FillRectangle(_palette.DimWash, view);
        }

        using (context.PushTransform(at.Matrix))
            DrawPass(context, model, scene, dimmed: false);

        _model = null;
    }

    // Rows of dots that move and grow with the graph. Far out every other
    // row and column is left out, so the ground never turns grey.
    private void DrawGrid(DrawingContext context, Rect view, LogicPanZoom at)
    {
        double step = _palette.GridStep * at.Zoom;
        if (step <= 0)
            return;

        while (step < LogicDrawMetrics.LeastGridStepOnScreen)
            step *= 2;

        IPen pen = _palette.GridPen(step);
        double left = First(at.Offset.X, step);

        for (double y = First(at.Offset.Y, step); y < view.Height + _palette.GridDot; y += step)
            context.DrawLine(pen, new Point(left, y), new Point(view.Width + step, y));
    }

    // The first multiple of the step, counted from the offset, at or past zero.
    private static double First(double offset, double step)
    {
        double first = offset % step;
        return first < 0 ? first + step : first;
    }

    private void DrawPass(DrawingContext context, LogicViewModel model, LogicScene scene, bool dimmed)
    {
        IReadOnlyList<LogicWireFace> faces = model.Wires;

        for (int i = 0; i < faces.Count; i++)
        {
            if (Draws(faces[i], dimmed))
                _wires.DrawPath(context, i, faces[i], ReferenceEquals(faces[i].Edge, _hoveredEdge));
        }

        for (int i = 0; i < scene.Cards.Count; i++)
        {
            LogicSceneCard card = scene.Cards[i];
            if (model.IsDimmed(card.Card) == dimmed && _seen.Intersects(card.Bounds.Inflate(_reach)))
                _cards.Draw(context, card, model, _detail, ReferenceEquals(card, _hoveredCard));
        }

        if (_detail == LogicDetail.Far)
            return;

        for (int i = 0; i < faces.Count; i++)
        {
            if (Draws(faces[i], dimmed))
                _wires.DrawEnds(context, faces[i]);
        }

        // Under the labels: a dot on its way passes behind the words, not over them.
        for (int i = 0; i < faces.Count; i++)
        {
            if (faces[i].State.Travel is not null && Draws(faces[i], dimmed))
                _wires.DrawTravel(context, faces[i]);
        }

        if (_detail != LogicDetail.Full)
            return;

        for (int i = 0; i < faces.Count; i++)
        {
            if (Draws(faces[i], dimmed))
                _wires.DrawLabel(context, faces[i], ReferenceEquals(faces[i].Edge, _hoveredEdge));
        }
    }

    private bool Draws(LogicWireFace face, bool dimmed)
    {
        LogicSceneEdge edge = face.Edge;
        if (_model is null || _model.IsDimmed(edge.Edge) != dimmed)
            return false;

        return _seen.Intersects(edge.Bounds.Inflate(_reach))
            || (edge.LabelBounds is Rect label && _seen.Intersects(label));
    }
}
