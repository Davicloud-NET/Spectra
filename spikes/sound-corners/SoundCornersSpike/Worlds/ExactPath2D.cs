using System;
using System.Collections.Generic;
using System.Numerics;

namespace SoundCornersSpike.Worlds;

// The true shortest path in a plane whose open space is a union of
// rectangles: a visibility graph over every corner and edge crossing, solved
// with Dijkstra. This is the hand solution the floods are measured against.
// Rectangles that only meet at a point would let a path squeeze through it,
// so the cases never build that.
internal static class ExactPath2D
{
    private const float Epsilon = 1e-5f;

    public static List<Vector2> Solve(IReadOnlyList<FreeRect> free, Vector2 from, Vector2 to)
    {
        var nodes = new List<Vector2> { from, to };
        var xs = new List<float>();
        var ys = new List<float>();

        foreach (FreeRect rect in free)
        {
            xs.Add(rect.X0);
            xs.Add(rect.X1);
            ys.Add(rect.Y0);
            ys.Add(rect.Y1);
        }

        foreach (float x in xs)
        {
            foreach (float y in ys)
            {
                var point = new Vector2(x, y);
                if (Inside(free, point) && !nodes.Contains(point)) nodes.Add(point);
            }
        }

        int count = nodes.Count;
        var distance = new float[count];
        var previous = new int[count];
        var done = new bool[count];
        Array.Fill(distance, float.PositiveInfinity);
        Array.Fill(previous, -1);
        distance[0] = 0f;

        for (int step = 0; step < count; step++)
        {
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                if (!done[i] && (best < 0 || distance[i] < distance[best])) best = i;
            }

            if (best < 0 || float.IsPositiveInfinity(distance[best])) break;
            done[best] = true;
            if (best == 1) break;

            for (int i = 0; i < count; i++)
            {
                if (done[i]) continue;

                float candidate = distance[best] + Vector2.Distance(nodes[best], nodes[i]);
                if (candidate >= distance[i] || !Visible(free, nodes[best], nodes[i])) continue;

                distance[i] = candidate;
                previous[i] = best;
            }
        }

        var path = new List<Vector2>();
        if (float.IsPositiveInfinity(distance[1])) return path;

        for (int i = 1; i >= 0; i = previous[i]) path.Add(nodes[i]);
        path.Reverse();
        return Simplify(path);
    }

    public static float Length(IReadOnlyList<Vector2> path)
    {
        float length = 0f;
        for (int i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
        return length;
    }

    private static bool Inside(IReadOnlyList<FreeRect> free, Vector2 point)
    {
        foreach (FreeRect rect in free)
        {
            if (point.X >= rect.X0 - Epsilon && point.X <= rect.X1 + Epsilon
                && point.Y >= rect.Y0 - Epsilon && point.Y <= rect.Y1 + Epsilon)
            {
                return true;
            }
        }

        return false;
    }

    // The segment stays in open space when the stretches the rectangles cover
    // join up from one end to the other.
    private static bool Visible(IReadOnlyList<FreeRect> free, Vector2 a, Vector2 b)
    {
        var spans = new List<(float Start, float End)>();
        foreach (FreeRect rect in free)
        {
            if (Clip(rect, a, b, out float start, out float end)) spans.Add((start, end));
        }

        spans.Sort((left, right) => left.Start.CompareTo(right.Start));

        float covered = 0f;
        foreach ((float start, float end) in spans)
        {
            if (start > covered + Epsilon) return false;
            covered = MathF.Max(covered, end);
        }

        return covered >= 1f - Epsilon;
    }

    private static bool Clip(FreeRect rect, Vector2 a, Vector2 b, out float start, out float end)
    {
        start = 0f;
        end = 1f;
        Vector2 delta = b - a;

        return ClipAxis(a.X, delta.X, rect.X0, rect.X1, ref start, ref end)
            && ClipAxis(a.Y, delta.Y, rect.Y0, rect.Y1, ref start, ref end);
    }

    private static bool ClipAxis(float origin, float delta, float min, float max, ref float start, ref float end)
    {
        if (MathF.Abs(delta) < 1e-9f) return origin >= min - Epsilon && origin <= max + Epsilon;

        float t0 = (min - origin) / delta;
        float t1 = (max - origin) / delta;
        if (t0 > t1) (t0, t1) = (t1, t0);

        start = MathF.Max(start, t0);
        end = MathF.Min(end, t1);
        return start <= end + Epsilon;
    }

    // Drops corners the path passes straight through.
    private static List<Vector2> Simplify(List<Vector2> path)
    {
        var kept = new List<Vector2> { path[0] };
        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector2 before = Vector2.Normalize(path[i] - kept[^1]);
            Vector2 after = Vector2.Normalize(path[i + 1] - path[i]);
            if (Vector2.Dot(before, after) < 1f - 1e-6f) kept.Add(path[i]);
        }

        kept.Add(path[^1]);
        return kept;
    }
}
