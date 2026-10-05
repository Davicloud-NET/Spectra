using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;
using SoundCornersSpike.Worlds;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Questions;

// Question 2: the time and memory of one flood with nothing to bound it but
// its radius.
internal static class Cost
{
    private static readonly Method[] Methods =
    [
        Accuracy.Methods[0],
        Accuracy.Methods[1],
        Accuracy.Methods[2],
        Accuracy.Methods[4],
        Accuracy.Methods[5],
        Accuracy.Methods[6],
    ];

    private static readonly float[] Radii = [20f, 40f, 60f];

    // Past this the window's arrays alone are over 40 MB and one flood takes seconds.
    private const long LargestWindow = 3_200_000;

    public static void Run()
    {
        World("two sealed rooms", HandCase.DoorwayRooms(out _).Build(), new Vector3(6f, 1.5f, 10f) + HandCase.Shift);
        World("the demo's course", DemoWorld.Build().World, new Vector3(133f, 1.6f, 0f));
        World("a hut in a field", OutdoorWorlds.HutInField(), new Vector3(15f, 1.6f, 6f) + HandCase.Shift);
    }

    private static void World(string name, CsgWorld world, Vector3 listener)
    {
        Report.Section($"One flood, bounded only by its radius: {name}");
        Report.Note("Warm: the world's answers are kept from the flood before. Cold: every cell is asked of the world first.");
        Report.Note("Each time is a median with the spread of three rounds.");

        var probe = new LiveProbe(world);
        var flood = new Flood();
        var rows = new List<string[]>();

        foreach (float cell in Accuracy.CellSizes)
        {
            foreach (float radius in Radii)
            {
                foreach (Method method in Methods)
                {
                    FloodOptions options = method.Options(radius);

                    long side = (2 * ((long)System.MathF.Ceiling(options.Radius / cell) + 2)) + 1;
                    if (side * side * side > LargestWindow)
                    {
                        rows.Add(
                        [
                            Report.F(cell, 1), Report.F(radius, 0), method.Name, "not run",
                            "", Report.F(side * side * side * 14 / 1048576.0, 0), "", "",
                        ]);
                        continue;
                    }

                    var field = new AirField(probe, cell, CellRule.CenterAndLinks);

                    FloodStats stats = default;
                    Timed cold = Timing.Measure(
                        () =>
                        {
                            field = new AirField(probe, cell, CellRule.CenterAndLinks);
                            stats = flood.Run(field, PartSet.Empty, listener, options);
                        },
                        runs: 5,
                        warmup: 1);

                    Timed warm = Timing.Measure(() => stats = flood.Run(field, PartSet.Empty, listener, options), runs: 11);

                    rows.Add(
                    [
                        Report.F(cell, 1),
                        Report.F(radius, 0),
                        method.Name,
                        stats.Settled.ToString(),
                        Report.F(warm.MedianMs * 1e6 / System.Math.Max(stats.Settled, 1), 0),
                        Report.F(flood.Window.MemoryBytes / 1048576.0, 1),
                        $"{warm.Ms}, {warm.Spread}",
                        $"{cold.Ms}, {cold.Spread}",
                    ]);
                }
            }
        }

        Report.Table(
            ["cell", "radius", "flood", "cells settled", "ns / cell, warm", "window, MB", "warm, ms", "cold, ms"],
            rows);
    }
}
