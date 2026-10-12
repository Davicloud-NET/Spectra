using SpectraEngine.Core.Scene;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// A project with scripts and a level that names them cooks to the same
/// bytes in a second process, from the cache and at any number of workers.
/// </summary>
// Runs the scook binary, as CookDeterminismTests does and for its reason.
// On their own, so the oracles of every other kind of asset run where Luau
// is not built.
[Trait("Suite", "Determinism")]
[Trait("Suite", RealScriptCheck.Suite)]
public class ScriptCookDeterminismTests
{
    // Two scripts, and the level with its two materials.
    private const int FileCount = 5;

    [Fact]
    public void Two_clean_cooks_in_two_processes_are_byte_identical()
    {
        ScookProcess.Require();
        RealScriptCheck.Require();

        using var project = new TempProject();
        WriteFixture(project);

        CookRun first = Cook(project, "clean-a", "--no-cache");
        CookRun second = Cook(project, "clean-b", "--no-cache");

        second.Pack.ShouldBe(first.Pack);
        second.Manifest.ShouldBe(first.Manifest);
    }

    [Fact]
    public void A_cached_cook_and_a_clean_cook_are_byte_identical()
    {
        ScookProcess.Require();
        RealScriptCheck.Require();

        using var project = new TempProject();
        WriteFixture(project);

        CookRun clean = Cook(project, "cold");
        CookRun cached = Cook(project, "warm");

        // Nothing about a script or the level is cooked again.
        cached.Stdout.ShouldContain($"{FileCount} from cache");
        cached.Pack.ShouldBe(clean.Pack);
    }

    [Fact]
    public void One_worker_and_many_workers_produce_the_same_pack()
    {
        ScookProcess.Require();
        RealScriptCheck.Require();

        using var project = new TempProject();
        WriteFixture(project);

        CookRun serial = Cook(project, "j1", "--no-cache", "-j", "1");
        CookRun parallel = Cook(project, "j8", "--no-cache", "-j", "8");

        // Five files clamp -j8 to five workers.
        parallel.Stdout.ShouldContain($"{FileCount} workers");
        parallel.Pack.ShouldBe(serial.Pack);
        parallel.Manifest.ShouldBe(serial.Manifest);
    }

    // Two scripts, which scook checks with Luau, and a level whose nodes name
    // them: one twice, one switched off, one as a module.
    private static void WriteFixture(TempProject project)
    {
        project.WriteAsset("Scripts/spin.luau", "local part = script.Parent\nprint(part.Name)\n");
        project.WriteAsset("Scripts/Lib/rules.luau", "return { speed = 2 }\n");

        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteMaterials(project);

        Scene scene = fixture.BuildScene(withEntities: true, withLights: true);
        scene.Root.CreateChild("Spin").Script = new Script("Scripts/spin.luau");
        scene.Root.CreateChild("Later").Script = new Script("Scripts/spin.luau") { IsEnabled = false };
        scene.Root.CreateChild("Rules").Script = new Script("Scripts/Lib/rules.luau") { IsModule = true };
        MapFixture.WriteBundle(project, "Scripted.smap", scene);
    }

    private static CookRun Cook(TempProject project, string label, params string[] extra)
    {
        // Outside Assets/, or the next cook would walk this run's output.
        string output = Path.Combine(project.Root, label);
        string manifest = Path.Combine(project.Root, label + "-manifest.json");

        ScookProcess.Result run = ScookProcess.Run(
            ["cook", project.Root, "-o", output, "--manifest", manifest, .. extra]);

        run.ExitCode.ShouldBe(0, $"scook failed: {run.Stderr}");

        string pack = Directory.GetFiles(output, "*.spack").Single();
        return new CookRun(run.Stdout, File.ReadAllBytes(pack), File.ReadAllBytes(manifest));
    }

    private readonly record struct CookRun(string Stdout, byte[] Pack, byte[] Manifest);
}
