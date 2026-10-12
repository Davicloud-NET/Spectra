using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// Byte identity of two clean cooks, a cached cook against a clean one, and
/// <c>-j1</c> against <c>-jN</c>.
/// </summary>
// Runs the scook binary, not CookSession: the string hash seed is per process,
// so an in-process comparison cannot see a hash-order leak.
// The manifest is compared too where possible. The pack sorts by asset id and
// would hide a scheduling leak; the manifest is in walk order.
[Trait("Suite", "Determinism")]
public class CookDeterminismTests
{
    private const int AssetCount = 36;

    // Counts of what WriteFixtureWithImages adds.
    private const int ImageCount = 3;
    private const int MaterialCount = 1;
    private const int ModelCount = 1;

    // Two sounds and the label file beside one of them, which is copied raw.
    private const int SoundFileCount = 3;

    private static readonly string[] Folders = ["Textures", "Models", "Materials", "Audio"];

    // Six sizes against four folders, so completion order is neither walk
    // order nor size order.
    private static readonly int[] Sizes = [24, 262_144, 1_024, 65_536, 96, 8_192];

    [Fact]
    public void Two_clean_cooks_in_two_processes_are_byte_identical()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixtureWithImages(project);

        CookRun first = Cook(project, "clean-a", "--no-cache");
        CookRun second = Cook(project, "clean-b", "--no-cache");

        second.Pack.ShouldBe(first.Pack);
        second.Manifest.ShouldBe(first.Manifest);
    }

    [Fact]
    public void A_cached_cook_and_a_clean_cook_are_byte_identical()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixtureWithImages(project);

        // The first run fills .spectra-cook/.
        CookRun clean = Cook(project, "cold");
        CookRun cached = Cook(project, "warm");

        // Without this, a cache that never hits still passes.
        cached.Stdout.ShouldContain(
            $"{AssetCount + ImageCount + MaterialCount + ModelCount + SoundFileCount} from cache");

        cached.Pack.ShouldBe(clean.Pack);
    }

    [Fact]
    public void One_worker_and_many_workers_produce_the_same_pack()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixtureWithImages(project);

        CookRun serial = Cook(project, "j1", "--no-cache", "-j", "1");
        CookRun parallel = Cook(project, "j8", "--no-cache", "-j", "8");

        // A run clamped to one worker would agree with -j1 trivially.
        parallel.Stdout.ShouldContain("8 workers");
        serial.Stdout.ShouldNotContain("workers");

        parallel.Pack.ShouldBe(serial.Pack);
        parallel.Manifest.ShouldBe(serial.Manifest);
    }

    [Fact]
    public void A_parallel_cook_pairs_every_result_with_the_asset_it_came_from()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixture(project);

        CookRun parallel = Cook(project, "paired", "--no-cache", "-j", "8");

        // For a raw copy the source, input and output paths are one string, so a
        // record naming two is a result paired with the wrong asset.
        string[] records = ManifestRecords(parallel.Manifest);
        records.Length.ShouldBe(AssetCount);

        var sources = new List<string>(records.Length);
        foreach (string record in records)
        {
            string[] paths = PathsIn(record);

            paths.Length.ShouldBe(3, $"expected a source, an input and an output in: {record}");
            paths.Distinct(StringComparer.Ordinal).Count().ShouldBe(1, $"mis-paired result: {record}");
            sources.Add(paths[0]);
        }

        // Walk order is ordinal ascending by content path.
        sources.ShouldBe([.. sources.Order(StringComparer.Ordinal)]);
    }

    private static void WriteFixture(TempProject project)
    {
        for (int i = 0; i < AssetCount; i++)
        {
            project.WriteAsset(
                $"{Folders[i % Folders.Length]}/asset_{i:D2}.bin",
                TempProject.Bytes(Sizes[i % Sizes.Length], seed: (byte)i));
        }
    }

    // Adds images, a material, a model and sounds with markers for the
    // byte-identity oracles. Not used by the pairing test: only a raw copy has
    // one path for all three.
    private static void WriteFixtureWithImages(TempProject project)
    {
        WriteFixture(project);

        for (int i = 0; i < ImageCount; i++)
            project.WriteAsset($"Textures/tile_{i}.png", TempProject.Png(16, 16, seed: (byte)(i * 40)));

        // A rule that reads a second asset.
        project.WriteAsset(
            "Materials/tile.spectramat",
            "shader = lit\ntexture uDiffuse = Textures/tile_0.png, linearmipmap, repeat\n");

        // Uses the material above so it reports nothing: a model with a
        // diagnostic is never cached, which would break the cached count.
        project.WriteAsset("Models/prop.gltf", GltfFixture.Json(materialName: "tile"));

        // Markers from cue points, and from a label file the rule reads as a
        // second input. Mono at the project rate, so neither reports anything.
        project.WriteAsset("Sounds/cued.wav", CuedWav.Add(
            TempProject.Wav(frames: 4_800),
            new CuedWav.Cue(1, 2_400, "b"),
            new CuedWav.Cue(2, 1_200),
            new CuedWav.Cue(3, 2_400, "a")));

        project.WriteAsset("Sounds/labelled.wav", TempProject.Wav(frames: 4_800, seed: 3));
        project.WriteAsset("Sounds/labelled.markers.txt", "0.05\t0.05\tsecond\n0.025\t0.025\tfirst\n");
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

    // The manifest writes one asset per line.
    private static string[] ManifestRecords(byte[] manifest) =>
        [.. Encoding.UTF8.GetString(manifest)
            .Split('\n')
            .Where(static line => line.Contains("\"rule\":\"", StringComparison.Ordinal))];

    // The asset's own path first, then inputs, then outputs.
    private static string[] PathsIn(string record)
    {
        const string Opening = "\"path\":\"";

        return [.. record
            .Split(Opening)
            .Skip(1)
            .Select(static part => part[..part.IndexOf('"')])];
    }

    private readonly record struct CookRun(string Stdout, byte[] Pack, byte[] Manifest);
}
