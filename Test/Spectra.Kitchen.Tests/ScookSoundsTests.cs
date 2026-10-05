using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.CLI;
using Spectra.Kitchen.Cooking;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio;
using System;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

// scook sounds: what a build calls to cook the sounds of a game that runs
// from loose files. Driven in process through Program.Run.
public sealed class ScookSoundsTests : IDisposable
{
    private const string Sound = "Sounds/door_open.wav";
    private const string Cooked = "Sounds/door_open.saudio";
    private const string Labels = "Sounds/door_open.markers.txt";

    private const int ExitSuccess = 0;
    private const int ExitCookError = 1;
    private const int ExitUsageError = 2;

    private readonly TempProject _project = new();

    public void Dispose() => _project.Dispose();

    private string Source => _project.Layout.AssetsPath;

    // Inside the project folder so it is deleted with it, outside its content.
    private string Output => Path.Combine(_project.Root, "loose");

    [Fact]
    public void It_writes_the_bytes_a_project_cook_puts_in_the_pack()
    {
        _project.WriteAsset(Sound, CuedWav.Add(
            TempProject.Wav(frames: 4_410, sampleRate: 44_100, loopStart: 100, loopEnd: 2_000),
            new CuedWav.Cue(1, 1_000, "open")));
        _project.WriteAsset("Sounds/Lift/hum_2d.wave", TempProject.Wav(frames: 96, channels: 2));

        Run run = Invoke("sounds", Source, "-o", Output);

        run.ExitCode.ShouldBe(ExitSuccess);
        run.Stdout.ShouldContain("cooked 2 sound(s)");

        File.ReadAllBytes(CookedFile(Cooked)).ShouldBe(PackedBytes(Cooked));
        File.ReadAllBytes(CookedFile("Sounds/Lift/hum_2d.saudio")).ShouldBe(PackedBytes("Sounds/Lift/hum_2d.saudio"));
    }

    [Fact]
    public void A_sound_whose_cooked_file_is_newer_is_left_alone()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 480));
        Invoke("sounds", Source, "-o", Output).ExitCode.ShouldBe(ExitSuccess);

        // Not a sound any more: a second cook would put the real bytes back.
        byte[] planted = TempProject.Bytes(32);
        File.WriteAllBytes(CookedFile(Cooked), planted);

        Run again = Invoke("sounds", Source, "-o", Output);

        again.ExitCode.ShouldBe(ExitSuccess);
        again.Stdout.ShouldContain("cooked 0 sound(s)");
        again.Stdout.ShouldContain("1 up to date");
        File.ReadAllBytes(CookedFile(Cooked)).ShouldBe(planted);
    }

    [Fact]
    public void A_wav_newer_than_its_cooked_file_is_cooked_again()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 480));
        Invoke("sounds", Source, "-o", Output).ExitCode.ShouldBe(ExitSuccess);
        byte[] before = File.ReadAllBytes(CookedFile(Cooked));

        _project.WriteAsset(Sound, TempProject.Wav(frames: 480, seed: 5));
        MakeNewerThanCooked(Sound);

        Run again = Invoke("sounds", Source, "-o", Output);

        again.Stdout.ShouldContain("cooked 1 sound(s)");
        byte[] after = File.ReadAllBytes(CookedFile(Cooked));
        after.ShouldNotBe(before);
        after.ShouldBe(LooseSoundCook.Run(Source, Sound).Cooked);
    }

    [Fact]
    public void A_label_file_newer_than_the_cooked_file_cooks_the_sound_again()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 48_000));
        Invoke("sounds", Source, "-o", Output).ExitCode.ShouldBe(ExitSuccess);
        SaudioReader.Read(File.ReadAllBytes(CookedFile(Cooked)), Cooked).Markers.ShouldBeEmpty();

        _project.WriteAsset(Labels, "0.5\tnow\n");
        MakeNewerThanCooked(Labels);

        Invoke("sounds", Source, "-o", Output).ExitCode.ShouldBe(ExitSuccess);

        SaudioReader.Read(File.ReadAllBytes(CookedFile(Cooked)), Cooked).Markers
            .ShouldBe([new AudioMarker(24_000, "now")]);
    }

    [Fact]
    public void A_refused_wav_is_reported_with_its_code_and_fails_the_run()
    {
        _project.WriteAsset(Sound, TempProject.Bytes(64));
        _project.WriteAsset("Sounds/lift_hum.wav", TempProject.Wav(frames: 96));

        Run run = Invoke("sounds", Source, "-o", Output, "-q");

        run.ExitCode.ShouldBe(ExitCookError);

        string line = run.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).ShouldHaveSingleItem();
        line.ShouldStartWith($"{Sound}: error SC4001: ");

        File.Exists(CookedFile(Cooked)).ShouldBeFalse();
        File.Exists(CookedFile("Sounds/lift_hum.saudio")).ShouldBeTrue();

        // It fails again until the sound is repaired: nothing marks it done.
        Invoke("sounds", Source, "-o", Output, "-q").ExitCode.ShouldBe(ExitCookError);
    }

    [Fact]
    public void A_warning_is_printed_and_fails_the_run_only_when_strict()
    {
        // Stereo and not named _2d.
        _project.WriteAsset(Sound, TempProject.Wav(frames: 64, channels: 2));

        Run strict = Invoke("sounds", Source, "-o", Output, "--strict", "-q");
        strict.ExitCode.ShouldBe(ExitCookError);
        strict.Stderr.ShouldStartWith($"{Sound}: error SC4003: ");
        File.Exists(CookedFile(Cooked)).ShouldBeFalse();

        Run plain = Invoke("sounds", Source, "-o", Output, "-q");
        plain.ExitCode.ShouldBe(ExitSuccess);
        plain.Stdout.ShouldBeEmpty();
        plain.Stderr.ShouldStartWith($"{Sound}: warning SC4003: ");
        File.Exists(CookedFile(Cooked)).ShouldBeTrue();
    }

    [Fact]
    public void A_folder_with_no_sounds_succeeds_quietly_and_writes_nothing()
    {
        _project.WriteAsset("Textures/wall.png", TempProject.Png());

        Run run = Invoke("sounds", Source, "-o", Output, "-q");

        run.ExitCode.ShouldBe(ExitSuccess);
        run.Stdout.ShouldBeEmpty();
        run.Stderr.ShouldBeEmpty();
        Directory.Exists(Output).ShouldBeFalse();
    }

    [Fact]
    public void A_wav_and_a_wave_of_one_name_cannot_both_be_cooked()
    {
        _project.WriteAsset(Sound, TempProject.Wav(frames: 64));
        _project.WriteAsset("Sounds/door_open.wave", TempProject.Wav(frames: 96));

        Run run = Invoke("sounds", Source, "-o", Output, "-q");

        run.ExitCode.ShouldBe(ExitCookError);
        run.Stderr.ShouldContain("error SC9002: ");
        run.Stderr.ShouldContain("'Sounds/door_open.wave'");
    }

    [Fact]
    public void The_verb_needs_an_output_folder_and_a_content_folder_that_is_there()
    {
        Run noOutput = Invoke("sounds", Source);
        noOutput.ExitCode.ShouldBe(ExitUsageError);
        noOutput.Stderr.ShouldContain("'sounds' requires -o");

        Run noFolder = Invoke("sounds", Path.Combine(_project.Root, "absent"), "-o", Output);
        noFolder.ExitCode.ShouldBe(ExitCookError);
        noFolder.Stderr.ShouldContain("error SC1001: ");
    }

    private string CookedFile(string cookedPath) =>
        Path.Combine(Output, cookedPath.Replace('/', Path.DirectorySeparatorChar));

    // File times have a coarse clock, so a file written right after the cook
    // can carry the cooked file's own time. Set it apart instead of waiting.
    private void MakeNewerThanCooked(string contentPath)
    {
        string full = Path.Combine(Source, contentPath.Replace('/', Path.DirectorySeparatorChar));
        File.SetLastWriteTimeUtc(full, File.GetLastWriteTimeUtc(CookedFile(Cooked)).AddSeconds(2));
    }

    // What scook cook writes for the project as it stands, read back out of the pack.
    private byte[] PackedBytes(string cookedPath)
    {
        CookResult result = new CookSession(_project.Layout, new CookSettings { UseCache = false }).Run();
        result.Succeeded.ShouldBeTrue(string.Join('\n', result.Diagnostics));

        using var pack = new PackSource(NullLogger.Instance, result.OutputPath.ShouldNotBeNull());

        pack.TryOpen(cookedPath, out ContentBlob? blob).ShouldBeTrue();
        using ContentBlob opened = blob.ShouldNotBeNull();

        return opened.Span.ToArray();
    }

    private static Run Invoke(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // Prepended: appended, a trailing option swallows it as its argument.
        int exit = Program.Run(["--no-color", .. args], stdout, stderr);

        return new Run(exit, stdout.ToString(), stderr.ToString());
    }

    private readonly record struct Run(int ExitCode, string Stdout, string Stderr);
}
