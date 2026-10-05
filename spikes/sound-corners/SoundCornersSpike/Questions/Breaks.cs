using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;
using SoundCornersSpike.Worlds;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Questions;

// Question 9: the cases that were expected to go wrong.
internal static class Breaks
{
    public static void Run()
    {
        SlopedTunnel();
        ListenerInSolid();
        SnapGrid();
        FlushCut();
        BeyondTheRadius();
        ManyBrushes();
    }

    // Two rooms at different heights joined by a sloping passage with the
    // demo doorway's cross-section, 1.4 by 2.2.
    private static void SlopedTunnel()
    {
        Report.Section("Where it breaks: a sloping passage 1.4 wide and 2.2 high");
        Report.Note("Share of 8 positions against the grid at which the flood gets from the lower room to the upper one.");

        var flood = new Flood();
        var reader = new PathReader(flood);
        var rows = new List<string[]>();

        foreach (float degrees in new[] { 20f, 35f, 45f })
        {
            float rise = 10f * MathF.Tan(degrees * MathF.PI / 180f);

            foreach (float cell in Accuracy.CellSizes)
            {
                var row = new List<string> { Report.F(degrees, 0), Report.F(cell, 1) };

                foreach (FloodAlgorithm algorithm in new[] { FloodAlgorithm.Bfs6, FloodAlgorithm.Dijkstra26, FloodAlgorithm.Theta6 })
                {
                    int through = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        var b = new WorldBuilder { Offset = HandCase.Shift + new Vector3(0f, k * 0.25f, k * 0.125f) };
                        b.Box(new Vector3(-4f, -2f, -4f), new Vector3(30f, rise + 7f, 10f));
                        b.Cut(new Vector3(0f, 0f, 0f), new Vector3(8f, 3f, 6f));
                        b.Cut(new Vector3(18f, rise, 0f), new Vector3(26f, rise + 3f, 6f));

                        // From the lower room's wall to the upper room's,
                        // reaching a little into both.
                        var middle = new Vector3(13f, (rise * 0.5f) + 1.1f, 3f);
                        float length = MathF.Sqrt(100f + (rise * rise)) + 2f;
                        Quaternion tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, degrees * MathF.PI / 180f);
                        b.RotatedBox(middle, new Vector3(length * 0.5f, 1.1f, 0.7f), tilt, cut: true);

                        var field = new AirField(new LiveProbe(b.Build()), cell, CellRule.CenterAndLinks);
                        Vector3 listener = new Vector3(4f, 1.5f, 3f) + b.Offset;
                        Vector3 target = new Vector3(22f, rise + 1.5f, 3f) + b.Offset;

                        flood.Run(field, PartSet.Empty, listener, new FloodOptions { Algorithm = algorithm, Radius = 60f, Sight = Sight.RayFromListener });
                        if (reader.Read(target, bends: false).Found) through++;
                    }

                    row.Add(Report.Percent(through / 8.0, 0));
                }

                rows.Add(row.ToArray());
            }
        }

        Report.Table(["slope, deg", "cell", "breadth first, 6", "Dijkstra, 26", "Theta*, 6"], rows);
    }

    // A listener close to a wall stands in a cell whose centre is in the wall.
    private static void ListenerInSolid()
    {
        Report.Section("Where it breaks: a listener in a cell the test calls solid");

        DemoWorld demo = DemoWorld.Build();
        var probe = new LiveProbe(demo.World);
        var flood = new Flood();
        var random = new Random(9);

        // Head positions a walking player can have: in air, in the start
        // room and the west end of the course, a body's radius off any wall
        // at the least.
        var heads = new List<Vector3>();
        while (heads.Count < 3000)
        {
            var at = new Vector3(122f + ((float)random.NextDouble() * 22f), 1.6f, -19f + ((float)random.NextDouble() * 38f));
            if (probe.IsSolid(at)) continue;

            bool clear = true;
            for (int k = 0; k < 8 && clear; k++)
            {
                float angle = k * MathF.PI / 4f;
                clear = !probe.SegmentBlocked(at, at + (new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * 0.35f));
            }

            if (clear) heads.Add(at);
        }

        var rows = new List<string[]>();
        foreach (float cell in Accuracy.CellSizes)
        {
            foreach (bool moved in new[] { false, true })
            {
                var field = new AirField(probe, cell, CellRule.CenterAndLinks, moved ? HandCase.Shift * cell : Vector3.Zero);
                int inSolid = 0, noSeed = 0, noSeedInPart = 0, stuck = 0;

                foreach (Vector3 head in heads)
                {
                    field.CellOf(head, out int x, out int y, out int z);
                    if (!field.IsAir(x, y, z)) inSolid++;

                    FloodStats stats = flood.Run(field, demo.Parts, head, Accuracy.Chosen.Options(radius: 6f));
                    if (stats.Seeds == 0)
                    {
                        noSeed++;
                        foreach (PartHull hull in demo.Parts.Hulls)
                        {
                            if (!hull.Contains(head)) continue;

                            noSeedInPart++;
                            break;
                        }
                    }
                    else if (stats.Settled < 20)
                    {
                        stuck++;
                    }
                }

                rows.Add(
                [
                    Report.F(cell, 1) + (moved ? ", moved off the snap grid" : ""),
                    heads.Count.ToString(),
                    Report.Percent((double)inSolid / heads.Count),
                    noSeed.ToString(),
                    noSeedInPart.ToString(),
                    stuck.ToString(),
                ]);
            }
        }

        Report.Note("Head positions at 1.6 in the demo's start room and the west of its course, 0.35 clear of any wall.");
        Report.Note("The walls are the world's. A position can still be inside a part, such as the shut door.");
        Report.Table(
            [
                "cell", "positions", "own cell called solid", "floods with no start", "of those, inside a part",
                "floods that reach under 20 cells",
            ],
            rows);
    }

    // Levels are built on a snap grid, and so are cell centres.
    private static void SnapGrid()
    {
        Report.Section("Where it breaks: cell centres that lie on a surface");
        Report.Note("A cell is touchy when moving its centre 0.01 along any axis changes the answer. The demo's course.");

        DemoWorld demo = DemoWorld.Build();
        var probe = new LiveProbe(demo.World);
        var rows = new List<string[]>();

        foreach (float cell in Accuracy.CellSizes)
        {
            foreach (bool moved in new[] { false, true })
            {
                var field = new AirField(probe, cell, CellRule.Center, moved ? HandCase.Shift * cell : Vector3.Zero);
                field.CellOf(new Vector3(118f, -2f, -22f), out int x0, out int y0, out int z0);
                field.CellOf(new Vector3(172f, 6f, 22f), out int x1, out int y1, out int z1);

                int cells = 0, touchy = 0;
                for (int z = z0; z <= z1; z++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            Vector3 center = field.Center(x, y, z);
                            bool solid = probe.IsSolid(center);
                            bool differs = false;

                            for (int k = 0; k < 6 && !differs; k++)
                            {
                                var nudge = new Vector3(k / 2 == 0 ? 0.01f : 0f, k / 2 == 1 ? 0.01f : 0f, k / 2 == 2 ? 0.01f : 0f);
                                differs = probe.IsSolid(center + (k % 2 == 0 ? nudge : -nudge)) != solid;
                            }

                            cells++;
                            if (differs) touchy++;
                        }
                    }
                }

                rows.Add(
                [
                    Report.F(cell, 1),
                    moved ? "moved by (0.3125, 0.1875, 0.4375) of a cell" : "on the snap grid",
                    cells.ToString(),
                    touchy.ToString(),
                    Report.Percent((double)touchy / cells),
                ]);
            }
        }

        Report.Table(["cell", "air grid", "cells", "touchy cells", "share"], rows);
    }

    // Found by accident: the spike's first worlds were built at an offset a
    // float cannot hold, and a ray through an open doorway hit something.
    private static void FlushCut()
    {
        Report.Section("Where it breaks: a doorway cut flush with its wall, a rounding step apart");
        Report.Note("Two rooms cut from a block, and a doorway cut through the wall between them, its faces flush with the rooms'.");
        Report.Note("The ray runs through the middle of the doorway from one room to the other.");

        var rows = new List<string[]>();
        foreach (Vector3 offset in new[] { Vector3.Zero, HandCase.Shift, new Vector3(0.31f, 0.17f, 0.43f) })
        {
            var b = new WorldBuilder { Offset = offset };
            b.Box(new Vector3(-4f, -2f, -6f), new Vector3(29f, 5f, 26f));
            b.Cut(new Vector3(0f, 0f, -2f), new Vector3(12f, 3f, 20f));
            b.Cut(new Vector3(13f, 0f, -2f), new Vector3(25f, 3f, 20f));
            b.Cut(new Vector3(12f, 0f, 9.3f), new Vector3(13f, 2.2f, 10.7f));

            CsgWorld world = b.Build();
            Vector3 from = new Vector3(6f, 1.5f, 10f) + offset;
            Vector3 to = new Vector3(19f, 1.5f, 10f) + offset;

            bool hit = world.Raycast(from, to - from, Vector3.Distance(from, to), out BspRaycastHit found);

            int solid = 0;
            for (int i = 0; i <= 100000; i++)
            {
                if (world.ContainsPoint(Vector3.Lerp(from, to, i / 100000f))) solid++;
            }

            // The two faces that should be one: the room's and the doorway's.
            float roomFace = (6f + offset.X) + 6f;
            float doorFace = (12.5f + offset.X) - 0.5f;

            rows.Add(
            [
                $"({offset.X}, {offset.Y}, {offset.Z})",
                (doorFace - roomFace).ToString("E2"),
                hit ? $"blocked at x = {found.Point.X:F4}" : "clear",
                solid.ToString(),
            ]);
        }

        Report.Table(
            ["world built at an offset of", "doorway face minus room face", "ray", "of 100,001 points along it, in solid"],
            rows);
    }

    private static void BeyondTheRadius()
    {
        Report.Section("Where it breaks: a path longer than the radius");

        HandCase corridor = HandCase.UCorridor();
        var field = new AirField(new LiveProbe(corridor.World), 1f, CellRule.CenterAndLinks);
        var flood = new Flood();
        var reader = new PathReader(flood);
        var rows = new List<string[]>();

        foreach (float radius in new[] { 10f, 20f, 28f, 30f, 40f })
        {
            FloodStats stats = flood.Run(field, PartSet.Empty, corridor.Listener, Accuracy.Chosen.Options(radius));
            PathAnswer answer = reader.Read(corridor.Source);
            rows.Add(
            [
                Report.F(radius, 0), stats.Settled.ToString(),
                answer.Found ? Report.F(answer.Length) : "no path: the wall path is all there is",
            ]);
        }

        Report.Note($"The U corridor. The straight line is {Report.F(corridor.Straight)} and the true path {Report.F(corridor.ExactLength)}.");
        Report.Table(["radius", "cells settled", "answer"], rows);
    }

    private static void ManyBrushes()
    {
        Report.Section("Where it breaks: thousands of brushes");

        var rows = new List<string[]>();
        foreach (int brushes in new[] { 500, 2000, 5000 })
        {
            long start = Stopwatch.GetTimestamp();
            CsgWorld town = OutdoorWorlds.Town(brushes, out float side);
            double buildMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            var probe = new LiveProbe(town);
            Vector3 listener = new Vector3(0f, 1.6f, 0f) + HandCase.Shift;
            var flood = new Flood();

            FloodOptions options = Accuracy.Chosen.Options(40f);
            WorldHeights.Bound(options, town, listener, [], 40f, 1f);

            var field = new AirField(probe, 1f, CellRule.CenterAndLinks);
            FloodStats stats = default;
            Timed cold = Timing.Measure(
                () =>
                {
                    field = new AirField(probe, 1f, CellRule.CenterAndLinks);
                    stats = flood.Run(field, PartSet.Empty, listener, options);
                },
                runs: 5,
                warmup: 1);
            Timed warm = Timing.Measure(() => stats = flood.Run(field, PartSet.Empty, listener, options), runs: 11);

            int most = 0;
            foreach (WorldChunk chunk in town.Chunks.OrderedChunks) most = Math.Max(most, chunk.ResidentBrushIndices.Count);

            rows.Add(
            [
                brushes.ToString(),
                Report.F(side, 0),
                town.Chunks.Count.ToString(),
                most.ToString(),
                Report.F(buildMs, 0),
                stats.Settled.ToString(),
                Report.F((double)field.PointTests / Math.Max(field.CellsClassified, 1), 2),
                Report.F((double)field.SegmentTests / Math.Max(field.CellsClassified, 1), 2),
                $"{warm.Ms}, {warm.Spread}",
                $"{cold.Ms}, {cold.Spread}",
                Report.F(field.MemoryBytes / 1048576.0, 2),
            ]);
        }

        Report.Note("A town at one brush to every 64 square units, a quarter of them walls 0.3 thick at any angle.");
        Report.Note("Cell 1, radius 40, floor and ceiling from the brushes, no sounds given.");
        Report.Table(
            [
                "brushes", "town side", "chunks", "most brushes in a chunk", "one-off CSG build, ms", "cells settled",
                "points / cell", "rays / cell", "warm, ms", "cold, ms", "kept answers, MB",
            ],
            rows);
    }
}
