using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Bsp;

internal static class WorkspaceProbe
{
    internal static void Run()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var box = Brush.CreateBox(new(-1), new(1));
        var placements = Enumerable.Range(0, 1000).Select(i => new BrushPlacement(box,
            Matrix4x4.CreateTranslation(i * 80 + 10, 10, 10))).ToArray();
        var original = CsgWorld.Build(placements);
        placements = (BrushPlacement[])placements.Clone();
        placements[500] = new(box, Matrix4x4.CreateTranslation(40010.25f, 10, 10));
        var dirty = ChunkGrid.ComputeFootprint(placements[500]);
        Console.WriteLine("Same immutable 1k world; isolated replacement. Holding an outer lease forces a fresh exclusive workspace in the control. 2 warmups + 9 x 100 compiles, alternating control/reuse.");
        var results = new Dictionary<bool, List<(double Ms, long Bytes)>> { [false] = [], [true] = [] };
        for (int rep = 0; rep < 11; rep++)
        foreach (bool reuse in rep % 2 == 0 ? new[] { false, true } : new[] { true, false })
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            double milliseconds = 0; long bytes = 0;
            for (int iteration = 0; iteration < 100; iteration++)
            {
                using var held = reuse ? null : CompileWorkspace.Rent();
                long before = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                var world = CsgWorld.Build(placements, dirty, original);
                milliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                if (world.PatchBaseId != original.Id) throw new Exception("Not incremental.");
            }
            if (rep >= 2) results[reuse].Add((milliseconds / 100, bytes / 100));
        }
        foreach (var (reuse, samples) in results)
        {
            var times = samples.Select(x => x.Ms).Order().ToArray();
            var sizes = samples.Select(x => x.Bytes).Order().ToArray();
            Console.WriteLine($"reuse={reuse} median={times[4]:F4} ms range=[{times[0]:F4},{times[^1]:F4}] allocation={sizes[4]} B/compile");
        }
    }
}
