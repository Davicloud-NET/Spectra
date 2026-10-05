using System;
using System.Diagnostics;
using System.Numerics;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// What one span trace costs on a level the size of the demo's, authored and
/// cooked. The numbers are printed. The test fails only on a trace far slower
/// than a frame could afford.
/// </summary>
// Opt-in: it reads the clock, which says little on a busy machine. To run it,
// in Release:
//   PowerShell:  $env:SPECTRA_SPAN_COST = "1"
//   bash:        export SPECTRA_SPAN_COST=1
//   dotnet run -c Release --project Test/Spectra.Kitchen.Tests -- -trait "Suite=SpanCost" -showLiveOutput
[Trait("Suite", "SpanCost")]
public class SolidSpanCostTests
{
    private const string Switch = "SPECTRA_SPAN_COST";

    // A hundred traces a frame at this cost would take 25 ms. The measured
    // cost is a few hundred times below it.
    private const double CeilingNanoseconds = 250_000;

    // Long enough for the JIT to finish its optimised code. Counting calls is
    // not: the first numbers then come from code still being tuned.
    private static readonly TimeSpan WarmUp = TimeSpan.FromMilliseconds(600);

    private const int Batches = 201;
    private const int CallsPerBatch = 200;

    // From the start room's sound, out through its shut door.
    private static readonly Vector3 ShortFrom = new(126f, 1.25f, 0f);
    private static readonly Vector3 ShortTo = new(133f, 1.25f, 0f);

    // From the start room's far corner across the whole course and out the
    // other side: the course's west wall, the terrace, a pillar and its east wall.
    private static readonly Vector3 LongFrom = new(123f, 1.25f, -3f);
    private static readonly Vector3 LongTo = new(171f, 1.25f, 10.538f);

    [Fact]
    public void A_trace_on_a_level_the_size_of_the_demo_costs_microseconds_authored_and_cooked()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(Switch) == "1",
            $"Opt-in: set {Switch}=1 to time the span trace.");

        using CookedLevel level = CookedLevel.Bake(DemoSizedLevel());

        var scratch = new SolidSpan[32];
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < WarmUp)
        {
            level.Authored.TraceSolidSpans(ShortFrom, ShortTo, scratch, out _);
            level.Scene.TraceSolidSpans(ShortFrom, ShortTo, scratch, out _);
            level.Authored.TraceSolidSpans(LongFrom, LongTo, scratch, out _);
            level.Scene.TraceSolidSpans(LongFrom, LongTo, scratch, out _);
        }

        Report("short, authored", level.Authored, ShortFrom, ShortTo, expectAtLeast: 1);
        Report("short, cooked", level.Scene, ShortFrom, ShortTo, expectAtLeast: 1);
        Report("long, authored", level.Authored, LongFrom, LongTo, expectAtLeast: 4);
        Report("long, cooked", level.Scene, LongFrom, LongTo, expectAtLeast: 4);
    }

    private static void Report(string what, Scene scene, Vector3 from, Vector3 to, int expectAtLeast)
    {
        var spans = new SolidSpan[32];
        int count = scene.TraceSolidSpans(from, to, spans, out _);
        count.ShouldBeGreaterThanOrEqualTo(expectAtLeast, what);

        var perCall = new double[Batches];
        for (int batch = 0; batch < Batches; batch++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < CallsPerBatch; i++)
                scene.TraceSolidSpans(from, to, spans, out _);

            perCall[batch] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / CallsPerBatch;
        }

        Array.Sort(perCall);
        double median = perCall[Batches / 2];

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{what}: {Vector3.Distance(from, to):0.0} units, {count} span(s), " +
            $"median {median:0} ns per trace over {Batches * CallsPerBatch} calls " +
            $"(fastest batch {perCall[0]:0} ns, slowest {perCall[^1]:0} ns)");

        median.ShouldBeLessThan(CeilingNanoseconds, what);
    }

    // The demo's world as brushes: its play area with the start room, its small
    // room, and a field of 192 scattered boxes laid out like the demo's. The
    // real demo scene needs a renderer and loaded assets to build.
    private static Scene DemoSizedLevel()
    {
        var scene = new Scene("DemoSized");

        MaterialRef structure = MaterialRegistry.Intern("Materials/span_cost_structure.spectramat");
        MaterialRef wall = MaterialRegistry.Intern("Materials/span_cost_wall.spectramat");
        MaterialRef accent = MaterialRegistry.Intern("Materials/span_cost_accent.spectramat");

        Box(scene, "Floor", new Vector3(0f, -1.1f, 0f), new Vector3(3f, 0.1f, 3f), structure);
        Box(scene, "WallNorth", new Vector3(0f, -0.1f, -3.1f), new Vector3(3.1f, 1f, 0.1f), wall);
        Box(scene, "WallWest", new Vector3(-3.1f, -0.1f, 0.05f), new Vector3(0.1f, 1f, 3.05f), wall);
        Box(scene, "PillarA", new Vector3(-2f, 0.1f, -2f), new Vector3(0.3f, 1.1f, 0.3f), accent);
        Box(scene, "PillarB", new Vector3(2f, 0.1f, 2f), new Vector3(0.3f, 1.1f, 0.3f), accent);

        var doorway = new Vector3(0.5f, 0.65f, 0.15f);
        Place(
            scene, "DoorwayCut", new Vector3(0f, -0.45f, -3.1f),
            Brush.CreateBox(-doorway, doorway, accent).WithOperation(BrushOperation.Subtractive));

        DemoPlayArea.Build(scene, structure, wall, accent);
        Scatter(scene);

        return scene;
    }

    // Fourteen by fourteen sites over 200 units, the four at the centre left
    // empty for the room.
    private static void Scatter(Scene scene)
    {
        const int sites = 14;
        const float spacing = 200f / sites;
        var random = new Random(192);

        for (int gx = 0; gx < sites; gx++)
        {
            for (int gz = 0; gz < sites; gz++)
            {
                float minX = -100f + gx * spacing;
                float minZ = -100f + gz * spacing;
                if (minX < 5f && minX + spacing > -5f && minZ < 5f && minZ + spacing > -5f)
                    continue;

                var half = new Vector3(
                    0.4f + random.NextSingle() * 1.2f,
                    0.4f + random.NextSingle() * 1.2f,
                    0.4f + random.NextSingle() * 1.2f);

                var center = new Vector3(
                    minX + 2f + random.NextSingle() * (spacing - 4f),
                    -1f + random.NextSingle() * 0.5f + half.Y,
                    minZ + 2f + random.NextSingle() * (spacing - 4f));

                Box(scene, $"Part{gx}_{gz}", center, half, default);
            }
        }
    }

    private static void Box(Scene scene, string name, Vector3 center, Vector3 half, MaterialRef material) =>
        Place(scene, name, center, Brush.CreateBox(-half, half, material));

    private static void Place(Scene scene, string name, Vector3 center, Brush brush)
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.LocalPosition = center;
        node.Brush = brush;
    }
}
