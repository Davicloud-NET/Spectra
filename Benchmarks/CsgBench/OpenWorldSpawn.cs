using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

// The openworld scenes as levels that play: a script's door makes a part
// and destroys one on every tick. A spawn must cost what it costs in a small
// world, and must never reach the static-world compile.
internal static class OpenWorldSpawn
{
    // Parts alive at a time. Each lives this many ticks.
    private const int LiveParts = 64;

    private const int WarmTicks = 1024;
    private const int TicksPerRep = 8192;

    public static void Run(int[] sizes, Func<int, List<BrushPlacement>> placementsOf, int timedReps)
    {
        Console.WriteLine();
        Console.WriteLine("  SPAWN (openworld) — a level that plays in each world: on every tick a part is made, placed and parented,");
        Console.WriteLine($"  the part made {LiveParts} ticks before is destroyed, the entity world ticks and one ray is cast, as a mover casts.");
        Console.WriteLine("  The world's brushes are world brushes, so a compile would show as a change of what a compile reads.");
        Console.WriteLine("  parts  | tick us | alloc B/tick | compiles | world unchanged");
        Console.WriteLine("  -------|---------|--------------|----------|----------------");

        var medians = new double[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
            medians[i] = RunSize(sizes[i], placementsOf(sizes[i]), timedReps);

        // A spawn, a destroy and the ray each walk the scene's tree of boxes
        // from the root, and that tree is about six levels taller in the
        // largest world than in the smallest. So the band is 2x here, where
        // the move gesture has 1.5x. A stage that walks the world would show
        // about 50x.
        double ratio = medians[^1] / medians[0];
        Console.WriteLine(
            $"  verdict (SPAWN and DESTROY): a tick at {sizes[^1] / 1000}k parts = {ratio:F2}x the tick at " +
            $"{sizes[0] / 1000}k parts -> " +
            (ratio <= 2.0
                ? "world-size independent (the scene's tree of boxes is a few levels taller)"
                : "NOT world-size independent — investigate before accepting this as a baseline"));
    }

    private static double RunSize(int parts, List<BrushPlacement> placements, int timedReps)
    {
        var scene = new Scene($"open {parts}");
        foreach (BrushPlacement placement in placements)
        {
            scene.Root.AddChild(new SceneNode("Brush")
            {
                Brush = placement.Brush,
                LocalPosition = placement.Transform.Translation,
            });
        }

        IReadOnlyList<BrushPlacement> before = scene.CaptureStaticWorldPlacements(out _) ?? [];
        var world = new EntityWorld(scene, NullLogger.Instance);
        world.Activate();
        var level = new Level(scene, world, placements);

        for (int tick = 0; tick < WarmTicks; tick++)
            level.Tick();

        var times = new double[timedReps];
        for (int rep = 0; rep < timedReps; rep++)
        {
            Collect();
            var watch = Stopwatch.StartNew();
            for (int tick = 0; tick < TicksPerRep; tick++)
                level.Tick();

            watch.Stop();
            times[rep] = watch.Elapsed.TotalMicroseconds / TicksPerRep;
        }

        Collect();
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        for (int tick = 0; tick < TicksPerRep; tick++)
            level.Tick();

        long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        // What a compile would read: the same brushes at the same places, in
        // the same order. And the scene was never asked to compile.
        IReadOnlyList<BrushPlacement> after = scene.CaptureStaticWorldPlacements(out _) ?? [];
        bool unchanged = after.Count == before.Count;
        for (int i = 0; unchanged && i < after.Count; i++)
            unchanged = ReferenceEquals(after[i].Brush, before[i].Brush) && after[i].Transform == before[i].Transform;

        Array.Sort(times);
        double median = times[times.Length / 2];
        Console.WriteLine(
            $"  {parts,6} | {median,7:F2} | {allocated / (double)TicksPerRep,12:F0} | {scene.StaticWorldCompileCount,8} | " +
            $"{(unchanged ? "yes" : "NO")}");
        if (!unchanged || scene.StaticWorldCompileCount != 0)
            Console.WriteLine("  WARNING: a spawn or a destroy changed what the static-world compile reads!");

        world.Deactivate();
        if (scene.NodeCount != placements.Count + 1)
            Console.WriteLine("  WARNING: stopping the level left nodes a spawn made in the scene!");

        return median;
    }

    private static void Collect()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    // One level that plays, and what a tick of it does.
    private sealed class Level(Scene scene, EntityWorld world, List<BrushPlacement> placements)
    {
        private readonly ScriptWrites _writes = new(world);
        private readonly Queue<SceneNode> _alive = new();
        private readonly List<SceneNode> _destroyed = [];
        private int _tick;

        public void Tick()
        {
            // Above one of the world's own parts, another on every tick, so
            // spawns land all over the world and not in one corner of it.
            int site = (int)((uint)(_tick++ * 7919) % (uint)placements.Count);
            Vector3 at = placements[site].Transform.Translation + new Vector3(0f, 6f, 0f);

            TreeWrites tree = _writes.Tree;
            if (tree.TryMake("Part", out SceneNode? part, out _))
            {
                MemberValue place = MemberValue.Of(at);
                _writes.TryWrite(part, SettableMemberId.Position, in place, out _);
                tree.TrySetParent(part, scene.Root, out _);
                _alive.Enqueue(part);
            }

            if (_alive.Count > LiveParts)
                tree.TryDestroy(_alive.Dequeue(), _destroyed, out _);

            world.Tick(PhysicsDefaults.FixedDeltaTime);
            scene.Raycast(new Ray3(at + Vector3.UnitY, -Vector3.UnitY), out _);
        }
    }
}
