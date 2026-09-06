using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using SpectraEngine.Bsp.Tests;
using Microsoft.Extensions.Logging.Abstractions;

internal static class StructuralProbe
{
    internal static void Run()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.WriteLine("Live Scene journal, isolated boxes spaced 80 units; 2 warmups, 9 samples, full GC before each sample.");
        Console.WriteLine("Includes journal capture, worker dispatch, CPU compile, publication and headless GPU mesh replacement; no polling sleep.");
        foreach (int count in new[] { 1000, 10000, 50000 })
        {
            var scene = new Scene();
            var renderer = new FakeRenderer();
            var brush = Brush.CreateBox(new(-1), new(1));
            for (int i = 0; i < count; i++)
            {
                var node = scene.Root.CreateChild("box");
                node.Brush = brush; node.LocalPosition = new((i % 100) * 80 + 10, 10, (i / 100) * 80 + 10);
            }
            scene.RebuildStaticWorld(renderer);
            var added = new SceneNode("added") { Brush = brush, LocalPosition = new(-70,10,10) };
            var add = new List<double>(); var remove = new List<double>(); var bytes = new List<long>();
            for (int rep = 0; rep < 11; rep++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long before = GC.GetTotalAllocatedBytes(true);
                double a = Change(() => scene.Root.AddChild(added));
                double r = Change(() => scene.Root.RemoveChild(added));
                long allocated = GC.GetTotalAllocatedBytes(true) - before;
                if (rep >= 2) { add.Add(a); remove.Add(r); bytes.Add(allocated / 2); }
            }
            add.Sort(); remove.Sort(); bytes.Sort();
            Console.WriteLine($"{count}: add {add[4]:F3} ms [{add[0]:F3},{add[^1]:F3}], remove {remove[4]:F3} ms [{remove[0]:F3},{remove[^1]:F3}], median {bytes[4]} B/edit");
            scene.ReleasePartBrushMeshes(renderer);
            double Change(Action action)
            {
                var previous = scene.StaticWorld!;
                long start = Stopwatch.GetTimestamp(); action();
                while (scene.StaticWorld == previous)
                {
                    scene.ProcessStaticWorldCompilation(renderer, NullLogger.Instance);
                    if (Stopwatch.GetElapsedTime(start).TotalSeconds > 30) throw new TimeoutException();
                    Thread.Yield();
                }
                if (scene.StaticWorld!.PatchBaseId != previous.Id) throw new Exception("Unexpected full compile.");
                return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
        }
    }
}

