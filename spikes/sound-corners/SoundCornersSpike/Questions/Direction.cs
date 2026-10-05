using System;
using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;

namespace SoundCornersSpike.Questions;

// Question 3: how steady the first leg's direction is while the listener
// walks, and what it takes to steady it.
internal static class Direction
{
    // The floods whose directions are also tried smoothed.
    private static readonly Method[] Smoothed =
    [
        Accuracy.Methods[5],
        Accuracy.Methods[3],
        Accuracy.Methods[2],
        Accuracy.Methods[8],
    ];

    public static void Run()
    {
        foreach (Walk walk in new[] { Walk.PastDoorway(), Walk.AlongWall() })
        {
            Report.Section($"Direction of arrival, walking {walk.Name}");
            Report.Note($"{walk.Frames} frames at 60 a second and 5 units a second, a flood every frame, between floor and ceiling and stopped at the sound.");
            Report.Note("The true direction moves by at most "
                + Report.F(LargestTrueJump(walk), 2) + " deg a frame. Errors are against the true first leg.");

            var probe = new LiveProbe(walk.World);
            var flood = new Flood();
            var rows = new List<string[]>();
            var kept = new List<(Method Method, float Cell, string Leg, Vector3[] Legs, float[] Lengths)>();

            foreach (float cell in Accuracy.CellSizes)
            {
                var field = new AirField(probe, cell, CellRule.CenterAndLinks);

                foreach (Method method in Accuracy.Methods)
                {
                    var plain = new Vector3[walk.Frames];
                    var refined = new Vector3[walk.Frames];
                    var tight = new Vector3[walk.Frames];
                    var lengths = new float[walk.Frames];
                    int missing = 0;
                    PathReader reader = method.Reader(flood);

                    for (int i = 0; i < walk.Frames; i++)
                    {
                        flood.Run(field, PartSet.Empty, walk.Listeners[i], walk.Options(method, cell));
                        PathAnswer answer = reader.Read(walk.Source, bends: false);
                        PathAnswer exact = reader.Read(walk.Source, bends: false, refine: true);
                        PathAnswer swung = reader.Read(walk.Source, bends: false, tighten: true);

                        if (!answer.Found)
                        {
                            missing++;
                            plain[i] = i > 0 ? plain[i - 1] : Vector3.UnitX;
                            refined[i] = i > 0 ? refined[i - 1] : Vector3.UnitX;
                            tight[i] = i > 0 ? tight[i - 1] : Vector3.UnitX;
                            lengths[i] = i > 0 ? lengths[i - 1] : 0f;
                            continue;
                        }

                        plain[i] = answer.FirstLeg;
                        refined[i] = exact.FirstLeg;
                        tight[i] = swung.FirstLeg;
                        lengths[i] = answer.Length;
                    }

                    rows.Add([Report.F(cell, 1), method.Name, "cell centre", missing.ToString(), .. WalkStats.Of(walk, plain, lengths).Cells()]);
                    rows.Add([Report.F(cell, 1), method.Name, "refined with rays", missing.ToString(), .. WalkStats.Of(walk, refined, lengths).Cells()]);
                    rows.Add([Report.F(cell, 1), method.Name, "refined and tightened", missing.ToString(), .. WalkStats.Of(walk, tight, lengths).Cells()]);

                    if (cell < 2f && Array.IndexOf(Smoothed, method) >= 0)
                    {
                        kept.Add((method, cell, "refined", refined, lengths));
                        kept.Add((method, cell, "refined and tightened", tight, lengths));
                    }
                }
            }

            Report.Table(["cell", "flood", "first leg", "frames with no path", .. WalkStats.Header], rows);

            Smoothing(walk, kept);
            FromTheSource(walk, probe);
        }
    }

    private static float LargestTrueJump(Walk walk)
    {
        float largest = 0f;
        for (int i = 1; i < walk.Frames; i++)
            largest = MathF.Max(largest, PathReader.AngleDegrees(walk.TrueLegs[i - 1], walk.TrueLegs[i]));
        return largest;
    }

    // The refined directions again, smoothed over time the way the engine
    // smooths loudness.
    private static void Smoothing(
        Walk walk, List<(Method Method, float Cell, string Leg, Vector3[] Legs, float[] Lengths)> kept)
    {
        Report.Note("### The first leg found with rays, then smoothed over time");

        var rows = new List<string[]>();
        foreach ((Method method, float cell, string leg, Vector3[] legs, float[] lengths) in kept)
        {
            rows.Add([Report.F(cell, 1), method.Name, leg, "none", .. WalkStats.Of(walk, legs, lengths).Cells()]);

            foreach (float seconds in new[] { 0.05f, 0.1f, 0.2f })
            {
                Vector3[] eased = Ease(legs, seconds);
                rows.Add([Report.F(cell, 1), method.Name, leg, $"eased, {seconds:F2} s", .. WalkStats.Of(walk, eased, lengths).Cells()]);
            }

            foreach (float rate in new[] { 90f, 180f })
            {
                Vector3[] limited = Limit(legs, rate);
                rows.Add(
                [
                    Report.F(cell, 1), method.Name, leg, $"at most {rate:F0} deg a second",
                    .. WalkStats.Of(walk, limited, lengths).Cells(),
                ]);
            }
        }

        Report.Table(["cell", "flood", "first leg", "smoothing", .. WalkStats.Header], rows);
    }

    // Exponential approach with a time constant, as SoundPathSmoother does
    // for gain, on the unit vector.
    internal static Vector3[] Ease(Vector3[] legs, float seconds)
    {
        float blend = 1f - MathF.Exp(-Walk.FrameSeconds / seconds);
        var eased = new Vector3[legs.Length];
        eased[0] = legs[0];

        for (int i = 1; i < legs.Length; i++)
            eased[i] = Vector3.Normalize(Vector3.Lerp(eased[i - 1], legs[i], blend));

        return eased;
    }

    private static Vector3[] Limit(Vector3[] legs, float degreesPerSecond)
    {
        float most = degreesPerSecond * Walk.FrameSeconds;
        var limited = new Vector3[legs.Length];
        limited[0] = legs[0];

        for (int i = 1; i < legs.Length; i++)
        {
            float angle = PathReader.AngleDegrees(limited[i - 1], legs[i]);
            limited[i] = angle <= most
                ? legs[i]
                : Vector3.Normalize(Vector3.Lerp(limited[i - 1], legs[i], most / angle));
        }

        return limited;
    }

    // The other way round: one flood from the sound, never redone, and the
    // listener reads the slope of the path length where it stands.
    private static void FromTheSource(Walk walk, LiveProbe probe)
    {
        Report.Note("### The other way round: one flood from the sound, and the listener reads its slope");
        Report.Note("The flood is run once for the whole walk. The direction is the downhill slope of path length at the listener.");

        var flood = new Flood();
        var rows = new List<string[]>();

        Method[] methods =
        [
            new(FloodAlgorithm.Marching, Sight.Grid, "fast marching"),
            new(FloodAlgorithm.Theta6, Sight.Ray, "Theta*, 6, rays"),
        ];

        foreach (float cell in Accuracy.CellSizes)
        {
            var field = new AirField(probe, cell, CellRule.CenterAndLinks);

            foreach (Method method in methods)
            {
                flood.Run(field, PartSet.Empty, walk.Source, FromSource(walk, method, cell));

                var legs = new Vector3[walk.Frames];
                var lengths = new float[walk.Frames];
                int missing = 0;

                for (int i = 0; i < walk.Frames; i++)
                {
                    if (!Slope(flood.Window, probe, walk.Listeners[i], out legs[i], out lengths[i]))
                    {
                        missing++;
                        legs[i] = i > 0 ? legs[i - 1] : Vector3.UnitX;
                        lengths[i] = i > 0 ? lengths[i - 1] : 0f;
                    }
                }

                rows.Add([Report.F(cell, 1), method.Name, "none", missing.ToString(), .. WalkStats.Of(walk, legs, lengths).Cells()]);
                rows.Add(
                [
                    Report.F(cell, 1), method.Name, "eased, 0.10 s", missing.ToString(),
                    .. WalkStats.Of(walk, Ease(legs, 0.1f), lengths).Cells(),
                ]);
            }
        }

        Report.Table(["cell", "flood from the sound", "smoothing", "frames with no answer", .. WalkStats.Header], rows);
    }

    // One flood from the sound that covers the whole walk, under the ceiling.
    private static FloodOptions FromSource(Walk walk, Method method, float cell)
    {
        FloodOptions options = method.Options(radius: 40f);
        WorldHeights.Bound(options, walk.World, walk.Listeners[0], [walk.Source], 60f, cell);
        return options;
    }

    // A plane fitted through the path lengths of the cells round a point that
    // the point can see. Its downhill direction is where the sound comes from.
    public static bool Slope(FloodWindow w, SolidProbe probe, Vector3 point, out Vector3 leg, out float length)
    {
        leg = default;
        length = 0f;
        if (!w.TryLocalCell(point, out int cx, out int cy, out int cz)) return false;

        // Normal equations for g = a + b . d, in doubles: 4 unknowns.
        Span<double> m = stackalloc double[20];
        Span<double> row = stackalloc double[5];
        m.Clear();
        int used = 0;

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx, y = cy + dy, z = cz + dz;
                    if (!w.InRange(x, y, z)) continue;

                    int cell = w.Index(x, y, z);
                    if ((w.Mark[cell] & FloodWindow.Closed) == 0) continue;

                    Vector3 center = w.CenterWorld(x, y, z);
                    if (probe.SegmentBlocked(point, center)) continue;

                    Vector3 d = (center - point) / w.Cell;
                    row[0] = 1.0;
                    row[1] = d.X;
                    row[2] = d.Y;
                    row[3] = d.Z;
                    row[4] = w.G[cell];

                    for (int r = 0; r < 4; r++)
                    {
                        for (int c = 0; c < 5; c++) m[(r * 5) + c] += row[r] * row[c];
                    }

                    used++;
                }
            }
        }

        if (used < 4 || !Solve(m)) return false;

        var slope = new Vector3((float)m[9], (float)m[14], (float)m[19]);
        if (slope.LengthSquared() < 1e-10f) return false;

        leg = -Vector3.Normalize(slope);
        length = (float)m[4];
        return true;
    }

    // Gauss-Jordan on a 4 by 5 system. The answers end up in the last column.
    private static bool Solve(Span<double> m)
    {
        for (int column = 0; column < 4; column++)
        {
            int pivot = column;
            for (int r = column + 1; r < 4; r++)
            {
                if (Math.Abs(m[(r * 5) + column]) > Math.Abs(m[(pivot * 5) + column])) pivot = r;
            }

            if (Math.Abs(m[(pivot * 5) + column]) < 1e-9) return false;

            for (int c = 0; c < 5; c++)
                (m[(column * 5) + c], m[(pivot * 5) + c]) = (m[(pivot * 5) + c], m[(column * 5) + c]);

            double scale = 1.0 / m[(column * 5) + column];
            for (int c = 0; c < 5; c++) m[(column * 5) + c] *= scale;

            for (int r = 0; r < 4; r++)
            {
                if (r == column) continue;

                double factor = m[(r * 5) + column];
                for (int c = 0; c < 5; c++) m[(r * 5) + c] -= factor * m[(column * 5) + c];
            }
        }

        return true;
    }
}
