using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

// For a segment that lies in brush faces. Such a segment is in solid only
// where there is solid on every side of it: in the seam of two flush brushes,
// and not along the face of a wall. The face planes cut the space round the
// segment into sectors. The composer runs once for each, and what every run
// found is kept.
internal sealed class SolidSpanSides
{
    // Normals closer to parallel than this are one plane.
    private const float SamePlane = 1e-6f;

    // The side toward this names the materials. Any fixed choice would do: it
    // keeps a live and a baked world, which find brushes in different orders,
    // to one answer.
    private static readonly Vector3 NamingSide = new(1f, 2f, 4f);

    private Vector3[] _normals = new Vector3[2];
    private int _normalCount;

    private float[] _angles = new float[4];
    private Vector3[] _sides = new Vector3[4];

    private SolidSpan[] _kept = new SolidSpan[16];
    private SolidSpan[] _next = new SolidSpan[16];
    private SolidSpan[] _found = new SolidSpan[16];
    private int _keptCount;

    public bool Any => _normalCount > 0;

    public void Clear() => _normalCount = 0;

    public void Add(LineFaces faces)
    {
        AddNormal(faces.First);
        AddNormal(faces.Second);
    }

    // direction is the segment's. Writes what Compose would, for the solid
    // that every side of the segment has.
    public int Compose(SolidSpanComposer composer, Vector3 direction, Span<SolidSpan> spans, out bool truncated)
    {
        int sides = FindSides(direction);

        for (int i = 0; i < sides; i++)
        {
            int found = ComposeSide(composer, _sides[i]);

            if (i == 0)
            {
                (_kept, _found) = (_found, _kept);
                _keptCount = found;
            }
            else
            {
                KeepWhatIsAlsoIn(_found.AsSpan(0, found));
            }
        }

        return SolidSpanComposer.Write(_kept.AsSpan(0, _keptCount), spans, out truncated);
    }

    private void AddNormal(Vector3 normal)
    {
        if (normal == Vector3.Zero)
            return;

        for (int i = 0; i < _normalCount; i++)
        {
            if (MathF.Abs(Vector3.Dot(_normals[i], normal)) > 1f - SamePlane)
                return;
        }

        if (_normalCount == _normals.Length)
            Array.Resize(ref _normals, _normals.Length * 2);
        _normals[_normalCount++] = normal;
    }

    // One direction into each sector, the naming side first.
    private int FindSides(Vector3 direction)
    {
        int count = _normalCount * 2;
        if (_sides.Length < count)
        {
            _sides = new Vector3[count];
            _angles = new float[count];
        }

        // A frame across the segment. Each plane crosses it in a line through
        // the middle, which is two sector edges half a turn apart.
        Vector3 right = _normals[0];
        Vector3 up = Vector3.Normalize(Vector3.Cross(direction, right));

        int edges = 0;
        for (int i = 0; i < _normalCount; i++)
        {
            Vector3 edge = Vector3.Cross(direction, _normals[i]);
            float angle = MathF.Atan2(Vector3.Dot(edge, up), Vector3.Dot(edge, right));
            InsertInOrder(_angles, edges++, angle);
            InsertInOrder(_angles, edges++, angle > 0f ? angle - MathF.PI : angle + MathF.PI);
        }

        int naming = 0;
        for (int i = 0; i < count; i++)
        {
            float edgeAfter = i + 1 < count ? _angles[i + 1] : _angles[0] + MathF.Tau;
            float middle = (_angles[i] + edgeAfter) * 0.5f;
            _sides[i] = MathF.Cos(middle) * right + MathF.Sin(middle) * up;

            if (Vector3.Dot(_sides[i], NamingSide) > Vector3.Dot(_sides[naming], NamingSide))
                naming = i;
        }

        (_sides[0], _sides[naming]) = (_sides[naming], _sides[0]);
        return count;
    }

    // Keeps the first count angles ascending. There are a handful, and nothing
    // here may allocate.
    private static void InsertInOrder(float[] angles, int count, float angle)
    {
        int at = count;
        for (; at > 0 && angles[at - 1] > angle; at--)
            angles[at] = angles[at - 1];
        angles[at] = angle;
    }

    private int ComposeSide(SolidSpanComposer composer, Vector3 side)
    {
        while (true)
        {
            int found = composer.Compose(side, _found, out bool truncated);
            if (!truncated)
                return found;

            _found = new SolidSpan[_found.Length * 2];
        }
    }

    // Cuts what is kept down to where other has solid too. Both are in order.
    private void KeepWhatIsAlsoIn(ReadOnlySpan<SolidSpan> other)
    {
        int count = 0;
        int from = 0;

        for (int i = 0; i < _keptCount; i++)
        {
            SolidSpan kept = _kept[i];
            while (from < other.Length && other[from].End <= kept.Start)
                from++;

            for (int j = from; j < other.Length && other[j].Start < kept.End; j++)
            {
                float start = MathF.Max(kept.Start, other[j].Start);
                float end = MathF.Min(kept.End, other[j].End);
                if (!(end > start))
                    continue;

                if (count == _next.Length)
                    Array.Resize(ref _next, _next.Length * 2);
                _next[count++] = kept with { Start = start, End = end };
            }
        }

        (_kept, _next) = (_next, _kept);
        _keptCount = count;
    }
}
