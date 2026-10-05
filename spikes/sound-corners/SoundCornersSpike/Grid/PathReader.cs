using System;
using System.Collections.Generic;
using System.Numerics;

namespace SoundCornersSpike.Grid;

// Reads one sound's answer out of a finished flood.
// Long for one class: the pulling, the refining and the tightening each
// read the same list of corners, and the report compares them side by side.
internal sealed class PathReader(Flood flood)
{
    private readonly List<int> _cells = [];
    private readonly List<Vector3> _points = [];

    // The path's corners from the listener to the source, both included, as
    // the last Read left them.
    public IReadOnlyList<Vector3> Points => _points;

    // How a breadth-first, Dijkstra or marching flood's chain of parents is
    // pulled straight: along the grid, or with rays against the real world.
    // A Theta* flood needs no pulling.
    public Sight PullSight { get; set; } = Sight.Ray;

    public long GridLosChecks { get; private set; }

    public long ExactRays { get; private set; }

    /// <param name="ownPart">The part the sound sits in, which must not block it.</param>
    /// <param name="bends">Also work out how much the whole path turns.</param>
    /// <param name="refine">Find the first leg's direction with rays against the real world.</param>
    /// <param name="tighten">
    /// As <paramref name="refine"/>, and first swing the first corner about the line to the corner after
    /// it, to where the way over is shortest.
    /// </param>
    public PathAnswer Read(
        Vector3 source, int ownPart = -1, bool bends = true, bool refine = false, bool tighten = false)
    {
        refine |= tighten;

        FloodWindow w = flood.Window;
        var answer = default(PathAnswer);

        int end = EndCell(source, ownPart, out bool own);
        if (end == FloodWindow.None) return answer;

        bool theta = flood.Algorithm is FloodAlgorithm.Theta6 or FloodAlgorithm.Theta26;

        _points.Clear();
        _points.Add(w.Listener);

        if (theta)
        {
            // The parents are the corners already. FirstHop gives the first
            // one with no walk when the rest is not asked for.
            if (bends || refine)
            {
                _cells.Clear();
                for (int c = own ? w.Parent[end] : end; c != FloodWindow.Root; c = w.Parent[c]) _cells.Add(c);
                for (int i = _cells.Count - 1; i >= 0; i--) _points.Add(w.CenterWorld(_cells[i]));
            }
            else
            {
                int last = own ? w.Parent[end] : end;
                if (last != FloodWindow.Root) _points.Add(w.CenterWorld(w.FirstHop[last]));
            }

            int before = own ? w.Parent[end] : end;
            answer.Length = before == FloodWindow.Root
                ? Vector3.Distance(w.Listener, source)
                : w.G[before] + Vector3.Distance(w.CenterWorld(before), source);
        }
        else
        {
            bool whole = PullSight != Sight.Grid || bends || refine;
            Pull(end, firstLegOnly: !whole);

            // The last corner is the end cell's centre. The source is in it.
            if (own) _points.RemoveAt(_points.Count - 1);
            answer.Length = w.G[end] + (own ? 0f : Vector3.Distance(w.CenterWorld(end), source));
        }

        _points.Add(source);

        // Pulled straight against the real world, the legs are a path of
        // their own, and a better length than the flood's count of steps.
        if (!theta && PullSight != Sight.Grid) answer.Length = Length(_points);

        answer.Found = true;
        answer.Legs = _points.Count - 1;
        answer.Direct = _points.Count == 2;
        answer.BendPoint = _points[1];
        answer.EndsBendDegrees = float.NaN;

        if (refine && !answer.Direct)
        {
            if (!tighten || !TryTighten(ownPart, out answer.BendPoint))
                answer.BendPoint = Refine(eye: 0, step: 1, ownPart);

            Vector3 lastCorner = Refine(eye: _points.Count - 1, step: -1, ownPart);

            // How far out of line the sound leaves the source and arrives
            // at the listener. For a path that wraps one way round, the two
            // add up to its whole bend.
            answer.EndsBendDegrees =
                Angle(answer.BendPoint - w.Listener, source - w.Listener)
                + Angle(lastCorner - source, w.Listener - source);
        }

        Vector3 leg = answer.BendPoint - w.Listener;
        answer.FirstLeg = leg.LengthSquared() > 1e-12f ? Vector3.Normalize(leg) : Vector3.UnitX;
        answer.BendDegrees = bends ? TotalBend(_points) : float.NaN;
        return answer;
    }

    /// <summary>
    /// The last path read, aimed again from where the listener is now, with
    /// no new flood: its corners stay and only the first leg is found afresh.
    /// Not found when the listener no longer sees the first corner.
    /// </summary>
    public PathAnswer Reaim(Vector3 listener, int ownPart = -1)
    {
        var answer = default(PathAnswer);
        if (_points.Count < 2) return answer;

        _points[0] = listener;
        if (!Sees(listener, _points[1], ownPart)) return answer;

        answer.Found = true;
        answer.Legs = _points.Count - 1;
        answer.Direct = _points.Count == 2;
        answer.Length = Length(_points);
        answer.BendPoint = _points[1];
        answer.BendDegrees = float.NaN;
        answer.EndsBendDegrees = float.NaN;

        if (!answer.Direct && !TryTighten(ownPart, out answer.BendPoint))
            answer.BendPoint = Refine(eye: 0, step: 1, ownPart);

        Vector3 leg = answer.BendPoint - listener;
        answer.FirstLeg = leg.LengthSquared() > 1e-12f ? Vector3.Normalize(leg) : Vector3.UnitX;
        return answer;
    }

    private static float Angle(Vector3 a, Vector3 b) =>
        a.LengthSquared() < 1e-12f || b.LengthSquared() < 1e-12f
            ? 0f
            : AngleDegrees(Vector3.Normalize(a), Vector3.Normalize(b));

    public static float TotalBend(IReadOnlyList<Vector3> points)
    {
        float total = 0f;
        Vector3 previous = default;
        bool hasPrevious = false;

        for (int i = 1; i < points.Count; i++)
        {
            Vector3 leg = points[i] - points[i - 1];
            if (leg.LengthSquared() < 1e-8f) continue;

            leg = Vector3.Normalize(leg);
            if (hasPrevious) total += AngleDegrees(previous, leg);

            previous = leg;
            hasPrevious = true;
        }

        return total;
    }

    public static float Length(IReadOnlyList<Vector3> points)
    {
        float length = 0f;
        for (int i = 1; i < points.Count; i++) length += Vector3.Distance(points[i - 1], points[i]);
        return length;
    }

    // By the cross product, not the arc cosine: that one reads two equal
    // unit vectors as 0.03 degrees apart.
    public static float AngleDegrees(Vector3 a, Vector3 b) =>
        MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b)) * (180f / MathF.PI);

    // The closed cell the path ends in: the source's own, or else the best
    // neighbour that sees the source. A sound inside its own part, or one
    // mounted on a wall, has no cell of its own.
    private int EndCell(Vector3 source, int ownPart, out bool own)
    {
        FloodWindow w = flood.Window;
        own = false;
        if (!w.TryLocalCell(source, out int sx, out int sy, out int sz)) return FloodWindow.None;

        int center = w.Index(sx, sy, sz);
        if ((w.Mark[center] & FloodWindow.Closed) != 0)
        {
            own = true;
            return center;
        }

        int best = FloodWindow.None;
        float bestLength = float.PositiveInfinity;

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = sx + dx, y = sy + dy, z = sz + dz;
                    if (!w.InRange(x, y, z)) continue;

                    int cell = w.Index(x, y, z);
                    if ((w.Mark[cell] & FloodWindow.Closed) == 0) continue;

                    Vector3 from = w.CenterWorld(x, y, z);
                    float length = w.G[cell] + Vector3.Distance(from, source);
                    if (length >= bestLength) continue;

                    ExactRays++;
                    if (w.Field.Probe.SegmentBlocked(from, source) || w.Parts.SegmentBlocked(from, source, ownPart))
                        continue;

                    best = cell;
                    bestLength = length;
                }
            }
        }

        return best;
    }

    // Turns the staircase of parents into straight legs: from each corner,
    // the furthest cell along the path that it can see on the grid.
    private void Pull(int end, bool firstLegOnly)
    {
        FloodWindow w = flood.Window;

        _cells.Clear();
        for (int c = end; c != FloodWindow.Root; c = w.Parent[c]) _cells.Add(c);
        _cells.Reverse();

        int from = FloodWindow.Root;
        int reached = -1;

        while (reached < _cells.Count - 1)
        {
            int next = PullSight == Sight.Grid ? FurthestOnGrid(from, reached) : FurthestByRay(from, reached);

            reached = next;
            from = _cells[reached];
            _points.Add(w.CenterWorld(from));

            if (firstLegOnly && reached < _cells.Count - 1)
            {
                _points.Add(w.CenterWorld(end));
                return;
            }
        }
    }

    // The furthest cell of the chain that the corner sees along the grid,
    // looked for from the far end.
    private int FurthestOnGrid(int from, int reached)
    {
        FloodWindow w = flood.Window;

        for (int j = _cells.Count - 1; j > reached + 1; j--)
        {
            w.Decode(_cells[j], out int x, out int y, out int z);
            GridLosChecks++;
            if (w.GridLos(from, x, y, z)) return j;
        }

        return reached + 1;
    }

    // The same with rays, by halving. That takes sight along a shortest path
    // to end once and not come back, which holds nearly always. When it does
    // not, the cell found is still one the corner sees.
    private int FurthestByRay(int from, int reached)
    {
        FloodWindow w = flood.Window;
        int seen = reached + 1;
        int hidden = _cells.Count;

        while (hidden - seen > 1)
        {
            // The far end first: a straight run needs one ray.
            int middle = hidden == _cells.Count ? _cells.Count - 1 : (seen + hidden) / 2;
            w.Decode(_cells[middle], out int x, out int y, out int z);
            ExactRays++;

            if (w.ExactLos(from, x, y, z)) seen = middle;
            else hidden = middle;

            if (seen == _cells.Count - 1) break;
        }

        return seen;
    }

    // The point where the leg from one end of the path really ends: the last
    // point along the path that end sees, found by halving between a corner
    // it sees and the next one it does not. eye is the listener's index in
    // the points or the source's, and step walks away from it.
    private Vector3 Refine(int eye, int step, int ownPart)
    {
        Vector3 from = _points[eye];
        int far = step > 0 ? _points.Count - 1 : 0;
        int k = eye + step;
        if (!Sees(from, _points[k], ownPart)) return _points[k];

        while (k != far && Sees(from, _points[k + step], ownPart)) k += step;
        if (k == far) return _points[k];

        Vector3 seen = _points[k];
        Vector3 hidden = _points[k + step];
        float low = 0f, high = 1f;

        for (int i = 0; i < 12; i++)
        {
            float middle = (low + high) * 0.5f;
            if (Sees(from, Vector3.Lerp(seen, hidden, middle), ownPart)) low = middle;
            else high = middle;
        }

        return Vector3.Lerp(seen, hidden, low);
    }

    // The grid puts the first corner at a cell centre, which along a wall's
    // top edge can be a cell to one side of where the shortest way crosses.
    // This swings the corner about the line from the listener to the corner
    // after it and keeps the angle at which the two legs, drawn tight
    // against the world, are shortest.
    private bool TryTighten(int ownPart, out Vector3 corner)
    {
        // Against the furthest point of the path that one corner can reach:
        // the source itself when the path goes over a single obstacle. The
        // corners in between are cell centres, and no better placed.
        for (int k = _points.Count - 1; k >= 2; k--)
        {
            if (TryTighten(_points[k], ownPart, out corner)) return true;
        }

        corner = default;
        return false;
    }

    private bool TryTighten(Vector3 next, int ownPart, out Vector3 corner)
    {
        corner = default;
        float cell = flood.Window.Cell;
        Vector3 eye = _points[0];

        Vector3 chord = next - eye;
        float chordLength = chord.Length();
        if (chordLength < 1e-3f) return false;

        Vector3 axis = chord / chordLength;
        float along = Vector3.Dot(_points[1] - eye, axis);
        Vector3 off = _points[1] - eye - (axis * along);
        float height = off.Length();
        if (height < 0.05f * cell) return false;

        var arch = new Arch(eye, next, eye + (axis * along), off / height, Vector3.Cross(axis, off / height), height);

        // As far to either side as a cell and a half at the corner's
        // distance from the line.
        float low = -MathF.Min(1f, MathF.Atan2(1.5f * cell, height));
        float high = -low;
        const float Golden = 0.618034f;

        float a = high - (Golden * (high - low));
        float b = low + (Golden * (high - low));
        float lengthA = Span(in arch, a, ownPart, out Vector3 cornerA);
        float lengthB = Span(in arch, b, ownPart, out Vector3 cornerB);

        for (int i = 0; i < 7; i++)
        {
            if (lengthA < lengthB)
            {
                high = b;
                b = a;
                lengthB = lengthA;
                cornerB = cornerA;
                a = high - (Golden * (high - low));
                lengthA = Span(in arch, a, ownPart, out cornerA);
            }
            else
            {
                low = a;
                a = b;
                lengthA = lengthB;
                cornerA = cornerB;
                b = low + (Golden * (high - low));
                lengthB = Span(in arch, b, ownPart, out cornerB);
            }
        }

        corner = lengthA < lengthB ? cornerA : cornerB;
        return float.IsFinite(MathF.Min(lengthA, lengthB));
    }

    private readonly record struct Arch(Vector3 Eye, Vector3 Next, Vector3 Foot, Vector3 Out, Vector3 Side, float Height);

    // The length of the two legs over the obstacle in the half plane at one
    // angle about the line, each drawn tight from its end, and where they meet.
    private float Span(in Arch arch, float angle, int ownPart, out Vector3 corner)
    {
        Vector3 direction = (arch.Out * MathF.Cos(angle)) + (arch.Side * MathF.Sin(angle));
        Vector3 apex = arch.Foot + (direction * arch.Height);
        corner = apex;

        // The apex has to see both ends. A little higher often does.
        int raises = 0;
        while (!Sees(arch.Eye, apex, ownPart) || !Sees(arch.Next, apex, ownPart))
        {
            if (++raises > 3) return float.PositiveInfinity;
            apex += direction * (0.5f * flood.Window.Cell);
        }

        Vector3 fromEye = LastSeen(arch.Eye, apex, arch.Next, ownPart);
        Vector3 fromNext = LastSeen(arch.Next, apex, arch.Eye, ownPart);
        corner = Meet(arch.Eye, fromEye - arch.Eye, arch.Next, fromNext - arch.Next, apex);
        return Vector3.Distance(arch.Eye, corner) + Vector3.Distance(corner, arch.Next);
    }

    // The last point on the way from seen to hidden that the eye still sees.
    private Vector3 LastSeen(Vector3 eye, Vector3 seen, Vector3 hidden, int ownPart)
    {
        float low = 0f, high = 1f;
        for (int i = 0; i < 10; i++)
        {
            float middle = (low + high) * 0.5f;
            if (Sees(eye, Vector3.Lerp(seen, hidden, middle), ownPart)) low = middle;
            else high = middle;
        }

        return Vector3.Lerp(seen, hidden, low);
    }

    // Where two lines in one plane cross: the middle of their closest points.
    private static Vector3 Meet(Vector3 p, Vector3 d, Vector3 q, Vector3 e, Vector3 fallback)
    {
        Vector3 r = p - q;
        float dd = Vector3.Dot(d, d), de = Vector3.Dot(d, e), ee = Vector3.Dot(e, e);
        float dr = Vector3.Dot(d, r), er = Vector3.Dot(e, r);
        float denominator = (dd * ee) - (de * de);
        if (denominator < 1e-9f) return fallback;

        float s = ((de * er) - (ee * dr)) / denominator;
        float t = ((dd * er) - (de * dr)) / denominator;
        if (!(s > 0f) || !(t > 0f)) return fallback;

        return ((p + (d * s)) + (q + (e * t))) * 0.5f;
    }

    private bool Sees(Vector3 from, Vector3 to, int ownPart)
    {
        FloodWindow w = flood.Window;
        ExactRays++;
        return !w.Field.Probe.SegmentBlocked(from, to) && !w.Parts.SegmentBlocked(from, to, ownPart);
    }
}
