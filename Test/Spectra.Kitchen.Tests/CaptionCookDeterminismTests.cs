using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// A project with captions cooks to the same bytes in a second process, from
/// the cache and at any number of workers.
/// </summary>
// Runs the scook binary, as CookDeterminismTests does and for its reason.
[Trait("Suite", "Determinism")]
public class CaptionCookDeterminismTests
{
    [Fact]
    public void Two_clean_cooks_in_two_processes_are_byte_identical()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixture(project);

        CookRun first = Cook(project, "clean-a", "--no-cache");
        CookRun second = Cook(project, "clean-b", "--no-cache");

        second.Pack.ShouldBe(first.Pack);
        second.Manifest.ShouldBe(first.Manifest);
        second.Stdout.ShouldBe(first.Stdout.Replace("clean-a", "clean-b"));
    }

    [Fact]
    public void A_cached_cook_and_a_clean_cook_are_byte_identical_and_say_the_same()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixture(project);

        CookRun clean = Cook(project, "cold");
        CookRun cached = Cook(project, "warm");

        // The four sounds and the clean subtitle file. A caption file and a
        // file with a warning are cooked every time.
        cached.Stdout.ShouldContain("5 from cache");
        cached.Pack.ShouldBe(clean.Pack);
        // A sound that is gone, a missing translation, and two parts of
        // WebVTT that are not read.
        Warnings(cached).ShouldBe(Warnings(clean));
        Warnings(cached).Length.ShouldBe(4);
    }

    [Fact]
    public void One_worker_and_many_workers_produce_the_same_pack()
    {
        ScookProcess.Require();

        using var project = new TempProject();
        WriteFixture(project);

        CookRun serial = Cook(project, "j1", "--no-cache", "-j", "1");
        CookRun parallel = Cook(project, "j8", "--no-cache", "-j", "8");

        parallel.Stdout.ShouldContain("8 workers");
        parallel.Pack.ShouldBe(serial.Pack);
        parallel.Manifest.ShouldBe(serial.Manifest);
        Warnings(parallel).ShouldBe(Warnings(serial));
    }

    // Two languages, one of them incomplete, speech with subtitles in both,
    // and one subtitle file that uses what the engine does not read.
    private static void WriteFixture(TempProject project)
    {
        string[] sounds = ["Sounds/door_open.wav", "Sounds/lift_hum.wav", "Sounds/vo/guard_hey.wav", "Sounds/vo/radio.wav"];
        for (int i = 0; i < sounds.Length; i++)
            project.WriteAsset(sounds[i], TempProject.Wav(frames: 4_800, seed: i));

        project.WriteAsset(
            "Captions/en.txt",
            "// Captions/en.txt\nSounds/door_open.wav = Door opens\nSounds/lift_hum.wav = Lift hums\n");
        project.WriteAsset("Captions/de.txt", "Sounds/lift_hum.wav = Aufzug summt\nSounds/gone.wav = Weg\n");

        project.WriteAsset(
            "Sounds/vo/guard_hey.en.vtt", "WEBVTT\n\n00:00.000 --> 00:00.050\n<v Guard>Hey! You there!\n");
        project.WriteAsset(
            "Sounds/vo/radio.en.vtt", "WEBVTT\n\n00:00.000 --> 00:00.050 align:start\n<i>Static</i>\n");
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
        return new CookRun(run.Stdout + run.Stderr, File.ReadAllBytes(pack), File.ReadAllBytes(manifest));
    }

    private static string[] Warnings(CookRun run) =>
        [.. run.Stdout.Split('\n').Where(static line => line.Contains(" warning SC41", System.StringComparison.Ordinal))];

    private readonly record struct CookRun(string Stdout, byte[] Pack, byte[] Manifest);
}
