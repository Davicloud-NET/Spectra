using System;
using System.Numerics;

namespace SpectraEngine.Editing.Icons;

// An SVG arc in its centre form: the ellipse it runs on, the angle it starts
// at and how far it turns. A path gives the two ends and two flags instead.
internal readonly struct SvgArc
{
    private readonly double _centreX;
    private readonly double _centreY;
    private readonly double _cos;
    private readonly double _sin;

    private SvgArc(
        double centreX, double centreY, double radiusX, double radiusY, double cos, double sin,
        double start, double sweep)
    {
        _centreX = centreX;
        _centreY = centreY;
        RadiusX = radiusX;
        RadiusY = radiusY;
        _cos = cos;
        _sin = sin;
        Start = start;
        Sweep = sweep;
    }

    // After the scale-up a too small pair of radii gets.
    public double RadiusX { get; }

    public double RadiusY { get; }

    public double Start { get; }

    // Signed: positive turns the way the sweep flag asks for.
    public double Sweep { get; }

    // The conversion in the SVG implementation notes, "endpoint to centre".
    public static SvgArc Between(
        Vector2 from, Vector2 to, double radiusX, double radiusY, double turn, bool large, bool sweep)
    {
        double cos = Math.Cos(turn);
        double sin = Math.Sin(turn);

        // Half the chord, in the ellipse's own axes.
        double halfX = (from.X - to.X) / 2.0;
        double halfY = (from.Y - to.Y) / 2.0;
        double x = (cos * halfX) + (sin * halfY);
        double y = (-sin * halfX) + (cos * halfY);

        // Radii too small to span the chord grow until they do.
        double span = ((x * x) / (radiusX * radiusX)) + ((y * y) / (radiusY * radiusY));
        if (span > 1.0)
        {
            double grow = Math.Sqrt(span);
            radiusX *= grow;
            radiusY *= grow;
        }

        double across = (radiusX * radiusX * y * y) + (radiusY * radiusY * x * x);
        double room = (radiusX * radiusX * radiusY * radiusY) - across;
        double reach = Math.Sqrt(Math.Max(0.0, room / across)) * (large != sweep ? 1.0 : -1.0);

        double localX = reach * radiusX * y / radiusY;
        double localY = reach * -radiusY * x / radiusX;
        double centreX = (cos * localX) - (sin * localY) + ((from.X + to.X) / 2.0);
        double centreY = (sin * localX) + (cos * localY) + ((from.Y + to.Y) / 2.0);

        double start = Math.Atan2((y - localY) / radiusY, (x - localX) / radiusX);
        double end = Math.Atan2((-y - localY) / radiusY, (-x - localX) / radiusX);
        double turned = end - start;

        if (sweep && turned < 0.0)
            turned += Math.Tau;
        else if (!sweep && turned > 0.0)
            turned -= Math.Tau;

        return new SvgArc(centreX, centreY, radiusX, radiusY, cos, sin, start, turned);
    }

    public Vector2 At(double angle)
    {
        double x = RadiusX * Math.Cos(angle);
        double y = RadiusY * Math.Sin(angle);

        return new Vector2(
            (float)(_centreX + (_cos * x) - (_sin * y)),
            (float)(_centreY + (_sin * x) + (_cos * y)));
    }
}
