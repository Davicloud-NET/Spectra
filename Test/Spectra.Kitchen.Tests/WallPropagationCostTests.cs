using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// What walls cost the sound a frame on a level the size of the demo's, and
/// what the first look at a material's file costs. The numbers are printed.
/// The test fails only on a frame far slower than the budget should allow.
/// </summary>
// Opt-in: it reads the clock, which says little on a busy machine. To run it,
// in Release:
//   PowerShell:  $env:SPECTRA_WALL_COST = "1"
//   bash:        export SPECTRA_WALL_COST=1
//   dotnet run -c Release --project Test/Spectra.Kitchen.Tests -- -trait "Suite=WallCost" -showLiveOutput
[Trait("Suite", "WallCost")]
public class WallPropagationCostTests
{
    private const string Switch = "SPECTRA_WALL_COST";

    // The budget's lines at ten times what a trace was measured to cost.
    private const double CeilingMicroseconds = 2_000;

    private const int WarmUpFrames = 4_000;
    private const int Frames = 6_000;

    [Theory]
    [InlineData(8, false)]
    [InlineData(32, false)]
    [InlineData(200, false)]
    [InlineData(32, true)]
    public void A_frame_of_walls_on_a_level_the_size_of_the_demo_costs_microseconds(int sounds, bool cooked)
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(Switch) == "1",
            $"Opt-in: set {Switch}=1 to time the walls.");

        using CookedLevel level = CookedLevel.Bake(SolidSpanCostTests.DemoSizedLevel());
        Scene scene = cooked ? level.Scene : level.Authored;

        Report($"{sounds} sounds, listener walking", scene, sounds, speed: 4.5f);
        Report($"{sounds} sounds, listener standing", scene, sounds, speed: 0f);
    }

    [Fact]
    public void The_first_look_at_a_materials_file_costs_a_fraction_of_a_millisecond()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(Switch) == "1",
            $"Opt-in: set {Switch}=1 to time the walls.");

        const int files = 200;
        string root = Path.Combine(Path.GetTempPath(), "SpectraWallCost", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Materials"));

        try
        {
            var materials = new MaterialRef[files];
            for (int i = 0; i < files; i++)
            {
                string path = $"Materials/wall_cost_{i}.spectramat";
                File.WriteAllText(
                    Path.Combine(root, path),
                    "shader = lit\nacoustic = brick\n\ntexture uDiffuse = Textures/wall.png, linearmipmap, repeat\n" +
                    "color uBaseColor = #B4A08C\nfloat uRoughness = 0.8\n");
                materials[i] = MaterialRegistry.Intern(path);
            }

            var stack = new ContentSourceStack();
            stack.Mount(new LooseFileSource(NullLogger.Instance, root));
            var acoustics = new MaterialAcoustics(NullLogger.Instance, stack);

            var each = new double[files];
            for (int i = 0; i < files; i++)
            {
                long start = Stopwatch.GetTimestamp();
                acoustics.Resolve(materials[i]).ShouldBeSameAs(AcousticPresets.Brick);
                each[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            long again = Stopwatch.GetTimestamp();
            for (int i = 0; i < files; i++)
                acoustics.Resolve(materials[i]);
            double known = Stopwatch.GetElapsedTime(again).TotalNanoseconds / files;

            Array.Sort(each);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"first look at a loose material file: median {each[files / 2]:0} us, " +
                $"slowest {each[^1]:0} us over {files} files. A material already known: {known:0} ns.");

            each[files / 2].ShouldBeLessThan(5_000);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Report(string what, Scene scene, int sounds, float speed)
    {
        var rig = new WallRig(new SceneSoundObstacles(() => scene), new FakeAcousticMaterials());

        // Over the course, in rows, at head height.
        for (int i = 0; i < sounds; i++)
            rig.Add(new Vector3(133f + (i % 20 * 1.8f), 1.25f, -18f + (i / 20 * 3.7f)), maxDistance: 60f);

        int frame = 0;
        void Step()
        {
            // Round the course's middle, past the door wall and the pillars.
            float angle = frame++ * speed * WallRig.FrameSeconds / 12f;
            rig.Listener = new Vector3(150f + (12f * MathF.Cos(angle)), 1.62f, 12f * MathF.Sin(angle));
            rig.Frame();
        }

        for (int i = 0; i < WarmUpFrames; i++)
            Step();

        var each = new double[Frames];
        long traces = 0;
        long waiting = 0;
        for (int i = 0; i < Frames; i++)
        {
            long start = Stopwatch.GetTimestamp();
            Step();
            each[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;

            traces += rig.Walls.Stats.Traces;
            waiting += rig.Walls.Stats.Waiting;
        }

        Array.Sort(each);
        double mean = 0;
        foreach (double one in each) mean += one;
        mean /= Frames;

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{what}: mean {mean:0.0} us a frame, median {each[Frames / 2]:0.0}, " +
            $"99th in 100 {each[Frames * 99 / 100]:0.0}, slowest {each[^1]:0.0}; " +
            $"{(double)traces / Frames:0.0} lines and {(double)waiting / Frames:0.0} sounds waiting a frame, " +
            $"{rig.Walls.Stats.Sounds} in earshot");

        each[Frames * 99 / 100].ShouldBeLessThan(CeilingMicroseconds, what);
    }
}
