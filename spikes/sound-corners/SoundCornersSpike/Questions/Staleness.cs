using System;
using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;

namespace SoundCornersSpike.Questions;

// Question 5, the first half: how often the flood has to be redone while the
// listener walks.
internal static class Staleness
{
    private enum Between
    {
        // Keep the direction and the length of the last flood.
        Hold,

        // Keep the last flood's first corner and aim at it from where the
        // listener is now.
        Aim,

        // Keep the last flood's corners and find the first leg again with
        // rays every frame. Flood again at once when the listener loses
        // sight of the first corner.
        Reaim,
    }

    public static void Run()
    {
        foreach (Walk walk in new[] { Walk.PastDoorway(), Walk.AlongWall() })
        {
            Report.Section($"How often to flood again, walking {walk.Name}");
            Report.Note($"The flood is '{Accuracy.Chosen.Name}', read with the first corner refined and tightened.");
            Report.Note("Errors are against a flood every frame with the same settings, not against the true path.");
            Report.Note("A length error of 1 unit at 10 units is 0.8 dB of loudness.");

            var probe = new LiveProbe(walk.World);
            var rows = new List<string[]>();

            foreach (float cell in new[] { 0.5f, 1f })
            {
                var field = new AirField(probe, cell, CellRule.CenterAndLinks);
                PathAnswer[] fresh = EveryFrame(walk, field);

                (string Name, int Every, bool OnCell)[] rhythms =
                [
                    ("on leaving a cell", 0, true),
                    ("10 a second", 6, false),
                    ("5 a second", 12, false),
                    ("2 a second", 30, false),
                    ("1 a second", 60, false),
                ];

                foreach (var rhythm in rhythms)
                {
                    foreach (Between between in new[] { Between.Hold, Between.Aim, Between.Reaim })
                    {
                        (int floods, float worstLeg, float meanLeg, float worstLength, int framesOver5) =
                            Replay(walk, field, fresh, rhythm.Every, rhythm.OnCell, between);

                        rows.Add(
                        [
                            Report.F(cell, 1),
                            rhythm.Name,
                            between switch
                            {
                                Between.Hold => "hold the answer",
                                Between.Aim => "aim at the kept corner",
                                _ => "find the first leg again with rays, flood when the corner is lost",
                            },
                            floods.ToString(),
                            Report.F(floods / (walk.Frames * Walk.FrameSeconds), 1),
                            Report.F(worstLeg, 1),
                            Report.F(meanLeg, 2),
                            framesOver5.ToString(),
                            Report.F(worstLength),
                        ]);
                    }
                }
            }

            Report.Table(
                [
                    "cell", "flood", "between floods", "floods", "floods a second", "largest direction error, deg",
                    "mean direction error, deg", "frames over 5 deg", "largest length error, units",
                ],
                rows);
        }
    }

    private static PathAnswer[] EveryFrame(Walk walk, AirField field)
    {
        var flood = new Flood();
        PathReader reader = Accuracy.Chosen.Reader(flood);
        var answers = new PathAnswer[walk.Frames];

        for (int i = 0; i < walk.Frames; i++)
        {
            flood.Run(field, PartSet.Empty, walk.Listeners[i], walk.Options(Accuracy.Chosen, field.Cell));
            answers[i] = reader.Read(walk.Source, bends: false, tighten: true);
        }

        return answers;
    }

    private static (int Floods, float WorstLeg, float MeanLeg, float WorstLength, int FramesOver5) Replay(
        Walk walk, AirField field, PathAnswer[] fresh, int every, bool onCell, Between between)
    {
        var flood = new Flood();
        PathReader reader = Accuracy.Chosen.Reader(flood);

        PathAnswer kept = default;
        Vector3 keptAt = default;
        int keptCellX = int.MinValue, keptCellY = 0, keptCellZ = 0;
        int floods = 0, over5 = 0;
        float worstLeg = 0f, totalLeg = 0f, worstLength = 0f;

        for (int i = 0; i < walk.Frames; i++)
        {
            Vector3 listener = walk.Listeners[i];
            field.CellOf(listener, out int cx, out int cy, out int cz);

            bool due = i == 0
                || (onCell ? cx != keptCellX || cy != keptCellY || cz != keptCellZ : i % every == 0);

            PathAnswer now = kept;
            if (!due && between == Between.Reaim && kept.Found)
            {
                now = reader.Reaim(listener);
                due = !now.Found;
            }

            if (due)
            {
                flood.Run(field, PartSet.Empty, listener, walk.Options(Accuracy.Chosen, field.Cell));
                kept = reader.Read(walk.Source, bends: false, tighten: true);
                now = kept;
                keptAt = listener;
                keptCellX = cx;
                keptCellY = cy;
                keptCellZ = cz;
                floods++;
            }

            if (!now.Found || !fresh[i].Found) continue;

            Vector3 leg = now.FirstLeg;
            float length = now.Length;

            if (between == Between.Aim)
            {
                // The rest of the path, from the corner on, has not changed.
                float rest = kept.Length - Vector3.Distance(keptAt, kept.BendPoint);
                Vector3 toCorner = kept.BendPoint - listener;
                leg = Vector3.Normalize(toCorner);
                length = toCorner.Length() + rest;
            }

            float error = PathReader.AngleDegrees(leg, fresh[i].FirstLeg);
            worstLeg = MathF.Max(worstLeg, error);
            totalLeg += error;
            if (error > 5f) over5++;
            worstLength = MathF.Max(worstLength, MathF.Abs(length - fresh[i].Length));
        }

        return (floods, worstLeg, totalLeg / walk.Frames, worstLength, over5);
    }
}
