using System.IO;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// Bakes of one map are byte-identical across processes, worker counts and
/// cache state.
/// </summary>
// Runs the real scook in separate processes: .NET seeds string hashing per
// process, so an in-process comparison cannot see a hash-order leak.
[Trait("Suite", "Determinism")]
public class ScmapDeterminismTests
{
    // So an empty or truncated file cannot pass the identity tests.
    private const int CompiledMapFloor = 2048;

    [Fact]
    public void Two_clean_bakes_in_two_processes_are_byte_identical()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixture(project);

        byte[] first = Bake(project, "clean-a", "--no-cache");
        byte[] second = Bake(project, "clean-b", "--no-cache");

        first.Length.ShouldBeGreaterThan(CompiledMapFloor);
        second.ShouldBe(first);

        // The entity strings are what a hash-order leak would reorder.
        ScmapProbe.Read(first).Entities.Count.ShouldBe(4);
    }

    [Fact]
    public void One_worker_and_many_workers_bake_the_same_map()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixture(project);

        byte[] serial = Bake(project, "j1", "--no-cache", "-j", "1");
        byte[] parallel = Bake(project, "j8", "--no-cache", "-j", "8");

        parallel.ShouldBe(serial);
    }

    [Fact]
    public void A_cached_bake_and_a_clean_bake_are_byte_identical()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixture(project);

        // The first run fills .spectra-cook/.
        byte[] clean = Bake(project, "cold");
        byte[] cached = Bake(project, "warm");

        cached.ShouldBe(clean);
    }

    [Fact]
    public void Keeping_the_brush_source_re_bakes_rather_than_serving_the_cached_map()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixture(project);

        byte[] without = Bake(project, "plain");
        byte[] with = Bake(project, "kept", "--keep-brush-source");

        // The switch must reach the cache key, or the second bake is served stale.
        with.ShouldNotBe(without);
        with.Length.ShouldBeGreaterThan(without.Length);
    }

    // Two materials (submesh ordering), a flush doorway cut (coincident planes),
    // a part brush (brush-source section always present) and four entities with
    // keyvalues, wires and node flags.
    private static void WriteFixture(TempProject project)
    {
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteMaterials(project);
        fixture.WriteBundle(project, "Room.smap", withEntities: true);
    }

    // --loose, so the .scmap is a file to compare directly.
    private static byte[] Bake(TempProject project, string label, params string[] extra)
    {
        string output = Path.Combine(project.Root, label);

        ScookProcess.Result run = ScookProcess.Run(
            ["cook", project.Root, "--loose", "-o", output, .. extra]);

        run.ExitCode.ShouldBe(0, $"scook failed: {run.Stderr}");
        return File.ReadAllBytes(Path.Combine(output, "Maps", "Room.scmap"));
    }
}
