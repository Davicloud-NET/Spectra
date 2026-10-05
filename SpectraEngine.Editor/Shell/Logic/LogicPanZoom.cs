using Avalonia;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// Where the graph sits in its view: a scene point times <see cref="Zoom"/>
/// plus <see cref="Offset"/> is a view point. The offset is kept on whole
/// pixels, so text at 100 percent is drawn sharp.
/// </summary>
public readonly record struct LogicPanZoom(Vector Offset, double Zoom)
{
    /// <summary>The furthest out the wheel zooms.</summary>
    public const double MinimumZoom = 0.1;

    /// <summary>The furthest in the view zooms.</summary>
    public const double MaximumZoom = 2;

    /// <summary>The furthest out <see cref="Fit"/> goes, to show the whole of a large graph.</summary>
    public const double MinimumFitZoom = 0.02;

    /// <summary>The smallest zoom a graph is placed at without being asked: its text can still be read.</summary>
    public const double ReadableZoom = 0.75;

    /// <summary>The scene at its own size, its corner in the view's corner.</summary>
    public static LogicPanZoom Identity => new(default, 1);

    /// <summary>The same as a matrix, for drawing.</summary>
    public Matrix Matrix => new(Zoom, 0, 0, Zoom, Offset.X, Offset.Y);

    /// <summary>Where a scene point is in the view.</summary>
    public Point ToView(Point scene) => new(scene.X * Zoom + Offset.X, scene.Y * Zoom + Offset.Y);

    /// <summary>Which scene point is under a view point.</summary>
    public Point ToScene(Point view) => new((view.X - Offset.X) / Zoom, (view.Y - Offset.Y) / Zoom);

    /// <summary>The part of the scene a rectangle of the view shows.</summary>
    public Rect ToScene(Rect view) => new(ToScene(view.TopLeft), ToScene(view.BottomRight));

    /// <summary>The same zoom, moved by a distance in the view.</summary>
    public LogicPanZoom MovedBy(Vector distance) => this with { Offset = Whole(Offset + distance) };

    /// <summary>
    /// Another zoom, with the scene point under <paramref name="anchor"/>
    /// staying where it is. The zoom is kept between the two limits, or at
    /// the zoom it starts from when a fit took it further out.
    /// </summary>
    /// <param name="anchor">A point in the view, such as the pointer.</param>
    /// <param name="zoom">The zoom asked for.</param>
    public LogicPanZoom ZoomedAbout(Point anchor, double zoom)
    {
        zoom = double.IsFinite(zoom)
            ? Math.Clamp(zoom, Math.Min(MinimumZoom, Zoom), MaximumZoom)
            : Zoom;

        Point held = ToScene(anchor);
        return new LogicPanZoom(Whole(new Vector(anchor.X - held.X * zoom, anchor.Y - held.Y * zoom)), zoom);
    }

    /// <summary>The same zoom, with the middle of a scene rectangle in the middle of the view.</summary>
    public LogicPanZoom CenteredOn(Rect scene, Size view)
    {
        Point middle = scene.Center;
        return this with
        {
            Offset = Whole(new Vector(view.Width / 2 - middle.X * Zoom, view.Height / 2 - middle.Y * Zoom)),
        };
    }

    /// <summary>
    /// The same zoom, moved no further than it takes to have a scene
    /// rectangle in the view. One too large for the view keeps its top left
    /// corner in.
    /// </summary>
    /// <param name="scene">The part of the scene to show, such as a card.</param>
    /// <param name="view">How large the view is.</param>
    /// <param name="margin">The room to keep between the rectangle and the view's edges.</param>
    public LogicPanZoom Showing(Rect scene, Size view, double margin)
    {
        Point from = ToView(scene.TopLeft);
        Point to = ToView(scene.BottomRight);

        var shift = new Vector(
            Shift(from.X, to.X, view.Width, margin),
            Shift(from.Y, to.Y, view.Height, margin));

        return shift == default ? this : MovedBy(shift);
    }

    /// <summary>
    /// The whole scene in the middle of the view, as large as fits and never
    /// larger than its own size.
    /// </summary>
    public static LogicPanZoom Fit(Size scene, Size view)
    {
        if (scene.Width <= 0 || scene.Height <= 0 || view.Width <= 0 || view.Height <= 0)
            return Identity;

        double zoom = Math.Min(1, Math.Min(view.Width / scene.Width, view.Height / scene.Height));
        return new LogicPanZoom(default, Math.Max(zoom, MinimumFitZoom)).CenteredOn(new Rect(scene), view);
    }

    /// <summary>
    /// Where a graph goes when nobody asked for a place: fitted when that
    /// leaves its text readable, otherwise at <see cref="ReadableZoom"/> with
    /// <paramref name="focus"/> in the middle, or the scene's corner in the
    /// view's corner when there is nothing to focus on.
    /// </summary>
    /// <param name="focus">The part of the scene to keep in view, such as the selected cards.</param>
    public static LogicPanZoom Placed(Size scene, Size view, Rect? focus)
    {
        LogicPanZoom fitted = Fit(scene, view);
        if (fitted.Zoom >= ReadableZoom)
            return fitted;

        var readable = new LogicPanZoom(default, ReadableZoom);
        return focus is { } rect ? readable.CenteredOn(rect, view) : readable;
    }

    // How far a span has to move to lie between the margins of a room.
    private static double Shift(double start, double end, double room, double margin)
    {
        if (start < margin || end - start > room - (2 * margin))
            return margin - start;

        return end > room - margin ? room - margin - end : 0;
    }

    private static Vector Whole(Vector offset) => new(Math.Round(offset.X), Math.Round(offset.Y));
}
