using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

internal static class SceneMaintenanceProbe
{
    internal static void Run()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.WriteLine("2 warmups + 9 samples; medians; same source runs against original and final commits. No GPU driver. Boxes centered inside distinct cells.");
        var replace = typeof(Scene).GetMethod("ReplaceStaticWorld", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<Action<Scene, Renderer, CsgWorld>>();
        foreach (int count in new[] { 1000, 10000, 50000 })
        {
            var renderer = new FakeRenderer();
            var parts = new Scene();
            var brush = Brush.CreateBox(new(-1), new(1));
            for (int i = 0; i < count; i++) parts.Root.AddChild(new SceneNode
                { Brush = brush, BrushKind = BrushKind.Part, LocalPosition = new((i % 100) * 3, 10, (i / 100) * 3) });
            parts.ProcessPartBrushMeshes(renderer);
            var partTimes = new List<double>();
            for (int rep = 0; rep < 11; rep++)
            {
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < 100; i++) parts.ProcessPartBrushMeshes(renderer);
                if (rep >= 2) partTimes.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds / 100);
            }
            parts.ReleasePartBrushMeshes(renderer);
            var scene = new Scene();
            var placements = Enumerable.Range(0, count).Select(i => new BrushPlacement(brush,
                Matrix4x4.CreateTranslation((i % 100) * 80 + 10, 10, (i / 100) * 80 + 10))).ToArray();
            var world = CsgWorld.Build(placements);
            replace(scene, renderer, world);
            var collision = new BrushPlaneCollisionSource(scene, new CharacterTuning());
            var region = new Aabb(new(9), new(11));
            collision.BeginTick(region, default);
            var swaps = new List<double>(); var selections = new List<double>();
            for (int rep = 0; rep < 11; rep++)
            {
                placements = (BrushPlacement[])placements.Clone();
                int last = count - 1;
                var matrix = placements[last].Transform; matrix.M41 += rep % 2 == 0 ? .25f : -.25f;
                placements[last] = new(brush, matrix);
                var dirty = ChunkGrid.ComputeFootprint(placements[last]);
                world = CsgWorld.Build(placements, dirty, world);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long start = Stopwatch.GetTimestamp(); replace(scene, renderer, world);
                double swap = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                start = Stopwatch.GetTimestamp(); collision.BeginTick(region, default);
                double select = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                if (rep >= 2) { swaps.Add(swap); selections.Add(select); }
            }
            partTimes.Sort(); swaps.Sort(); selections.Sort();
            Console.WriteLine($"{count}: unchanged parts={partTimes[4]:F3} us; replacement-only world swap={swaps[4]:F3} us [{swaps[0]:F3},{swaps[^1]:F3}]; remote collision publication={selections[4]:F3} us [{selections[0]:F3},{selections[^1]:F3}]");
        }
    }
}
