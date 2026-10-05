using Avalonia;
using System;

namespace SpectraEngine.Editor.Shell.Logic;

/// <summary>
/// Where the graph sits in its view: a scene point times <see cref="Zoom"/>
/// plus <see cref="Offset"/> is a view point.
/// </summary>
public readonly record struct LogicPanZoom(Vector Offset, double Zoom)
{
    /// <summary>The furthest out the view zooms.</summary>
    public const double MinimumZoom = 0.1;

    /// <summary>The furthest in the view zooms.</summary>
    public const double MaximumZoom = 2;

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
    public LogicPanZoom MovedBy(Vector distance) => this with { Offset = Offset + distance };

    /// <summary>
    /// Another zoom, kept between the two limits, with the scene point under
    /// <paramref name="anchor"/> staying where it is.
    /// </summary>
    /// <param name="anchor">A point in the view, such as the pointer.</param>
    /// <param name="zoom">The zoom asked for.</param>
    public LogicPanZoom ZoomedAbout(Point anchor, double zoom)
    {
        zoom = ClampZoom(zoom);
        Point held = ToScene(anchor);
        return new LogicPanZoom(new Vector(anchor.X - held.X * zoom, anchor.Y - held.Y * zoom), zoom);
    }

    /// <summary>The same zoom, with the middle of a scene rectangle in the middle of the view.</summary>
    public LogicPanZoom CenteredOn(Rect scene, Size view)
    {
        Point middle = scene.Center;
        return this with
        {
            Offset = new Vector(
                Math.Round(view.Width / 2 - middle.X * Zoom),
                Math.Round(view.Height / 2 - middle.Y * Zoom)),
        };
    }

    /// <summary>
    /// The whole scene in the middle of the view, as large as fits and never
    /// larger than its own size.
    /// </summary>
    public static LogicPanZoom Fit(Size scene, Size view)
    {
        if (scene.Width <= 0 || scene.Height <= 0 || view.Width <= 0 || view.Height <= 0)
            return Identity;

        double zoom = ClampZoom(Math.Min(1, Math.Min(view.Width / scene.Width, view.Height / scene.Height)));
        return new LogicPanZoom(default, zoom).CenteredOn(new Rect(scene), view);
    }

    /// <summary>A zoom brought between the two limits.</summary>
    public static double ClampZoom(double zoom) =>
        double.IsFinite(zoom) ? Math.Clamp(zoom, MinimumZoom, MaximumZoom) : 1;
}
