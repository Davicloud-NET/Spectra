using System;
using System.Numerics;

namespace SpectraEngine.Core.Audio.Propagation;

// Where the lines from one sound end: the first at the listener, the others on
// a ring round the listener's head, square to that first line. The ring does
// not turn with the head, so looking round changes nothing.
internal sealed class HeadLines
{
    // A line shorter than this has no direction to be square to.
    private const float NoDirection = 1e-6f;

    // Cosine and sine of each ring point's angle.
    private readonly Vector2[] _ring;
    private readonly float _radius;

    public HeadLines(int count, float radius)
    {
        _ring = new Vector2[count - 1];
        _radius = radius;

        // A quarter step off the axes. Seen from the side or from above, no
        // two points then line up, so an upright or a level edge crosses the
        // lines one at a time.
        for (int i = 0; i < _ring.Length; i++)
        {
            float angle = (i + 0.25f) * MathF.Tau / _ring.Length;
            _ring[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }
    }

    public int Count => _ring.Length + 1;

    public Ends From(Vector3 sound, Vector3 listener)
    {
        Vector3 along = listener - sound;
        if (!(along.LengthSquared() > NoDirection * NoDirection))
            return new Ends(_ring, listener, Vector3.UnitX * _radius, Vector3.UnitY * _radius);

        along = Vector3.Normalize(along);

        // Straight up or down has no side. Any will do.
        Vector3 side = Vector3.Cross(along, Vector3.UnitY);
        if (side.LengthSquared() < NoDirection)
            side = Vector3.Cross(along, Vector3.UnitX);

        side = Vector3.Normalize(side);
        return new Ends(_ring, listener, side * _radius, Vector3.Cross(side, along) * _radius);
    }

    // The ends of one sound's lines. Line 0 ends at the listener.
    public readonly struct Ends(Vector2[] ring, Vector3 listener, Vector3 side, Vector3 up)
    {
        public Vector3 this[int line] =>
            line == 0 ? listener : listener + (side * ring[line - 1].X) + (up * ring[line - 1].Y);
    }
}
