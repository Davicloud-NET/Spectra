using System.IO;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// Bakes of a level with tags and attributes are byte-identical across
/// processes, worker counts and cache state.
/// </summary>
// Runs the real scook in separate processes, as ScmapDeterminismTests does:
// the tags and the attributes' names are strings, and a set of them walked
// in hash order would differ from one process to the next.
[Trait("Suite", "Determinism")]
public class TaggedMapDeterminismTests
{
    [Fact]
    public void Two_clean_bakes_of_a_tagged_level_in_two_processes_are_byte_identical()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixture(project);

        byte[] first = Bake(project, "clean-a", "--no-cache");
        byte[] second = Bake(project, "clean-b", "--no-cache");

        second.ShouldBe(first);

        // What was compared holds the tags and the attributes.
        ScmapDocument document = ScmapReader.Read(first, "Maps/Room.scmap");
        document.NodeTags.Length.ShouldBe(8);
        document.NodeAttributes.Length.ShouldBe(9);
    }

    [Fact]
    public void One_worker_and_many_workers_bake_the_same_tagged_level()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixture(project);

        byte[] serial = Bake(project, "j1", "--no-cache", "-j", "1");
        byte[] parallel = Bake(project, "j8", "--no-cache", "-j", "8");

        parallel.ShouldBe(serial);
    }

    [Fact]
    public void A_cached_bake_and_a_clean_bake_of_a_tagged_level_are_byte_identical()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixture(project);

        // The first run fills .spectra-cook/.
        byte[] clean = Bake(project, "cold");
        byte[] cached = Bake(project, "warm");

        cached.ShouldBe(clean);
    }

    private static void WriteFixture(TempProject project)
    {
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteMaterials(project);
        MapFixture.WriteBundle(project, "Room.smap", TaggedRoom.Build(fixture));
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
