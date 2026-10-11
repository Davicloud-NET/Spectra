using System;
using System.Numerics;

namespace SpectraEngine.Editing.Icons;

// An SVG arc in its center form: the ellipse it runs on, the angle it starts
// at and how far it turns. A path gives the two ends and two flags instead.
internal readonly struct SvgArc
{
    private double CenterX { get; init; }

    private double CenterY { get; init; }

    // The ellipse's turn, as the cosine and sine of its angle.
    private double Cos { get; init; }

    private double Sin { get; init; }

    // After the scale-up a too small pair of radii gets.
    public double RadiusX { get; private init; }

    public double RadiusY { get; private init; }

    public double Start { get; private init; }

    // Signed: positive turns the way the sweep flag asks for.
    public double Sweep { get; private init; }

    // The conversion in the SVG implementation notes, "endpoint to center".
    // The flags are the path's own two: the large arc, and the sweep.
    public static SvgArc Between(Vector2 from, Vector2 to, Vector2 radii, float turn, (bool Large, bool Sweep) flags)
    {
        double cos = Math.Cos(turn);
        double sin = Math.Sin(turn);
        double radiusX = radii.X;
        double radiusY = radii.Y;

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
        double reach = Math.Sqrt(Math.Max(0.0, room / across)) * (flags.Large != flags.Sweep ? 1.0 : -1.0);

        double localX = reach * radiusX * y / radiusY;
        double localY = reach * -radiusY * x / radiusX;

        double start = Math.Atan2((y - localY) / radiusY, (x - localX) / radiusX);
        double end = Math.Atan2((-y - localY) / radiusY, (-x - localX) / radiusX);
        double turned = end - start;

        if (flags.Sweep && turned < 0.0)
            turned += Math.Tau;
        else if (!flags.Sweep && turned > 0.0)
            turned -= Math.Tau;

        return new SvgArc
        {
            CenterX = (cos * localX) - (sin * localY) + ((from.X + to.X) / 2.0),
            CenterY = (sin * localX) + (cos * localY) + ((from.Y + to.Y) / 2.0),
            Cos = cos,
            Sin = sin,
            RadiusX = radiusX,
            RadiusY = radiusY,
            Start = start,
            Sweep = turned,
        };
    }

    public Vector2 At(double angle)
    {
        double x = RadiusX * Math.Cos(angle);
        double y = RadiusY * Math.Sin(angle);

        return new Vector2(
            (float)(CenterX + (Cos * x) - (Sin * y)),
            (float)(CenterY + (Sin * x) + (Cos * y)));
    }
}
