using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;
using SoundCornersSpike.Worlds;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Questions;

// Question 5, the second half: floods on worker threads against a world that
// the main thread keeps compiling successors of.
internal static class Threads
{
    private const int Workers = 8;
    private const double Seconds = 4.0;

    private sealed record Published(CsgWorld World, int Shape);

    public static void Run()
    {
        Report.Section("Floods on worker threads while the world is recompiled");

        // The timings before this ran on one core. This needs several.
        Pinning.Release();

        // Two sealed rooms with a doorway, and a crate that jumps between two
        // places in front of it. Shape 0 and shape 1.
        // The same brushes in both, as after a move in the editor: only the
        // crate's transform differs.
        List<BrushPlacement> still = Placements(crateAt: 15f);
        var moved = new List<BrushPlacement>(still);
        moved[^1] = moved[^1] with { Transform = moved[^1].Transform * Matrix4x4.CreateTranslation(2f, 0f, 0f) };
        List<BrushPlacement>[] shapes = [still, moved];

        Vector3[] listeners = new Vector3[12];
        for (int i = 0; i < listeners.Length; i++)
            listeners[i] = new Vector3(3f + (i % 4 * 2f), 1.5f, 3f + (i / 4 * 6f)) + HandCase.Shift;

        Vector3[] sources =
        [
            new Vector3(20f, 1.5f, 4f) + HandCase.Shift,
            new Vector3(22f, 1.5f, 10f) + HandCase.Shift,
            new Vector3(16f, 1.5f, 17f) + HandCase.Shift,
        ];

        // What every flood must answer, from worlds built with no caches on
        // this thread.
        var expected = new float[2][];
        for (int shape = 0; shape < 2; shape++)
            expected[shape] = Answers(CsgWorld.Build(shapes[shape]), listeners, sources, new Flood());

        CsgWorld first = CsgWorld.Build(shapes[0], previousCache: null);
        var published = new Published(first, 0);

        long floods = 0, mismatches = 0, failures = 0;
        bool stop = false;
        var threads = new Thread[Workers];

        for (int t = 0; t < Workers; t++)
        {
            threads[t] = new Thread(() =>
            {
                var flood = new Flood();
                while (!Volatile.Read(ref stop))
                {
                    Published now = Volatile.Read(ref published);
                    try
                    {
                        float[] got = Answers(now.World, listeners, sources, flood);
                        Interlocked.Add(ref floods, listeners.Length);

                        for (int i = 0; i < got.Length; i++)
                        {
                            if (got[i] != expected[now.Shape][i]) Interlocked.Increment(ref mismatches);
                        }
                    }
                    catch (Exception)
                    {
                        // Counted, not handled: any exception here is the finding.
                        Interlocked.Increment(ref failures);
                    }
                }
            });
            threads[t].Start();
        }

        // The compile, as the engine runs it: the next world is derived from
        // the published one while the workers are still reading that one.
        int compiles = 0;
        long start = Stopwatch.GetTimestamp();
        CsgWorld current = first;
        int crate = shapes[0].Count - 1;

        while (Stopwatch.GetElapsedTime(start).TotalSeconds < Seconds)
        {
            int next = (compiles + 1) % 2;
            BrushPlacement before = shapes[1 - next][crate];
            BrushPlacement after = shapes[next][crate];

            var dirty = new SortedSet<ChunkCoord>(ChunkGrid.ComputeFootprint(in before));
            dirty.UnionWith(ChunkGrid.ComputeFootprint(in after));

            current = CsgWorld.Build(shapes[next], [.. dirty], current);
            Volatile.Write(ref published, new Published(current, next));
            compiles++;
        }

        Volatile.Write(ref stop, true);
        foreach (Thread thread in threads) thread.Join();

        Report.Table(
            ["worker threads", "seconds", "floods on workers", "worlds compiled meanwhile", "wrong answers", "exceptions"],
            [[Workers.ToString(), Report.F(Seconds, 0), floods.ToString(), compiles.ToString(), mismatches.ToString(), failures.ToString()]]);

        Pinning.Apply();
    }

    private static List<BrushPlacement> Placements(float crateAt)
    {
        var placements = new List<BrushPlacement>();
        Vector3 shift = HandCase.Shift;

        Add(placements, new Vector3(-4f, -2f, -4f) + shift, new Vector3(29f, 5f, 24f) + shift, cut: false);
        Add(placements, new Vector3(0f, 0f, 0f) + shift, new Vector3(12f, 3f, 20f) + shift, cut: true);
        Add(placements, new Vector3(13f, 0f, 0f) + shift, new Vector3(25f, 3f, 20f) + shift, cut: true);
        Add(placements, new Vector3(12f, 0f, 9.3f) + shift, new Vector3(13f, 2.2f, 10.7f) + shift, cut: true);
        Add(placements, new Vector3(crateAt, 0f, 8f) + shift, new Vector3(crateAt + 1.5f, 2.5f, 12f) + shift, cut: false);
        return placements;
    }

    private static void Add(List<BrushPlacement> placements, Vector3 min, Vector3 max, bool cut)
    {
        Vector3 half = (max - min) * 0.5f;
        Brush brush = Brush.CreateBox(-half, half);
        if (cut) brush = brush.WithOperation(BrushOperation.Subtractive);
        placements.Add(new BrushPlacement(brush, Matrix4x4.CreateTranslation((min + max) * 0.5f)));
    }

    private static float[] Answers(CsgWorld world, Vector3[] listeners, Vector3[] sources, Flood flood)
    {
        var field = new AirField(new LiveProbe(world), 1f, CellRule.CenterAndLinks);
        var reader = new PathReader(flood);
        var answers = new float[listeners.Length * sources.Length];

        for (int i = 0; i < listeners.Length; i++)
        {
            flood.Run(field, PartSet.Empty, listeners[i], Accuracy.Chosen.Options(radius: 40f));
            for (int k = 0; k < sources.Length; k++)
            {
                PathAnswer answer = reader.Read(sources[k], bends: false);
                answers[(i * sources.Length) + k] = answer.Found ? answer.Length : -1f;
            }
        }

        return answers;
    }
}
