using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio;
using System;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

// One sound cooked with no project around it, checked against the pack a
// project cook writes for the same file.
public class LooseSoundCookTests
{
    private const string SourcePath = "Sounds/guard_hey.wav";
    private const string CookedPath = "Sounds/guard_hey.saudio";
    private const string LabelPath = "Sounds/guard_hey.markers.txt";

    [Fact]
    public void A_loose_cook_gives_the_bytes_a_project_cook_puts_in_the_pack()
    {
        // Resampled, looped and cued, so every part of the rule has something to do.
        using var project = new TempProject();
        project.WriteAsset(SourcePath, CuedWav.Add(
            TempProject.Wav(frames: 44_100, sampleRate: 44_100, loopStart: 11_025, loopEnd: 22_049),
            new CuedWav.Cue(1, 11_025, "open"),
            new CuedWav.Cue(2, 30_000)));

        LooseSoundResult loose = LooseSoundCook.Run(project.Layout.AssetsPath, SourcePath);

        loose.Cooked.ShouldNotBeNull().ShouldBe(PackedBytes(project));
    }

    [Fact]
    public void A_loose_cook_reads_the_label_file_beside_the_sound()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 48_000));
        project.WriteAsset(LabelPath, "0.5\tnow\n");

        LooseSoundResult loose = LooseSoundCook.Run(project.Layout.AssetsPath, SourcePath);

        byte[] cooked = loose.Cooked.ShouldNotBeNull();
        cooked.ShouldBe(PackedBytes(project));
        SaudioReader.Read(cooked, CookedPath).Markers.ShouldBe([new AudioMarker(24_000, "now")]);
    }

    [Fact]
    public void A_loose_cook_says_what_the_project_cook_says()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 441, sampleRate: 44_100, channels: 2));

        LooseSoundResult loose = LooseSoundCook.Run(project.Layout.AssetsPath, SourcePath);
        CookResult packed = new CookSession(project.Layout, new CookSettings { UseCache = false })
            .Run(TestContext.Current.CancellationToken);

        loose.Diagnostics.Select(d => d.ToString()).ShouldBe(packed.Diagnostics.Select(d => d.ToString()));
        loose.Diagnostics.ShouldContain(d => d.Id.ToString() == "SC4003" && !d.IsError);
    }

    [Fact]
    public void The_key_holds_while_the_files_stand_and_moves_when_the_sound_changes()
    {
        using var project = new TempProject();
        string root = project.Layout.AssetsPath;
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 480));

        LooseSoundResult first = LooseSoundCook.Run(root, SourcePath);
        LooseSoundCook.CurrentKey(root, first.Dependencies).ShouldBe(first.Key);

        project.WriteAsset(SourcePath, TempProject.Wav(frames: 480, seed: 3));

        LooseSoundCook.CurrentKey(root, first.Dependencies).ShouldNotBe(first.Key);
        LooseSoundCook.Run(root, SourcePath).Key.ShouldNotBe(first.Key);
    }

    [Fact]
    public void A_label_file_that_appears_beside_the_sound_moves_the_key()
    {
        using var project = new TempProject();
        string root = project.Layout.AssetsPath;
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 48_000));

        LooseSoundResult bare = LooseSoundCook.Run(root, SourcePath);
        project.WriteAsset(LabelPath, "0.25\topen\n");

        LooseSoundCook.CurrentKey(root, bare.Dependencies).ShouldNotBe(bare.Key);

        LooseSoundResult labelled = LooseSoundCook.Run(root, SourcePath);
        project.WriteAsset(LabelPath, "0.5\tnow\n");

        LooseSoundCook.CurrentKey(root, labelled.Dependencies).ShouldNotBe(labelled.Key);
    }

    [Fact]
    public void The_key_moves_with_the_project_rate()
    {
        using var project = new TempProject();
        string root = project.Layout.AssetsPath;
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 480));

        LooseSoundResult cooked = LooseSoundCook.Run(root, SourcePath);
        var slower = new CookSettings { AudioSampleRate = 44_100 };

        LooseSoundCook.CurrentKey(root, cooked.Dependencies, slower).ShouldNotBe(cooked.Key);
        SaudioReader.Read(LooseSoundCook.Run(root, SourcePath, slower).Cooked.ShouldNotBeNull(), CookedPath)
            .Format.SampleRate.ShouldBe(44_100);
    }

    [Fact]
    public void A_sound_the_cook_refuses_comes_back_with_its_code_and_no_bytes()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Bytes(64));

        LooseSoundResult refused = LooseSoundCook.Run(project.Layout.AssetsPath, SourcePath);

        refused.Cooked.ShouldBeNull();
        refused.IsRepeatable.ShouldBeTrue();

        CookDiagnostic error = refused.Diagnostics.Single(d => d.IsError);
        error.Id.ToString().ShouldBe("SC4001");
        error.Message.ShouldContain(SourcePath);
    }

    [Fact]
    public void A_warning_is_an_error_when_the_settings_are_strict()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 64, channels: 2));

        LooseSoundResult strict = LooseSoundCook.Run(
            project.Layout.AssetsPath, SourcePath, new CookSettings { Strict = true });

        strict.Cooked.ShouldBeNull();
        strict.Diagnostics.Single(d => d.Id.ToString() == "SC4003").IsError.ShouldBeTrue();
    }

    [Fact]
    public void A_sound_that_is_not_there_is_a_failure_not_to_be_remembered()
    {
        using var project = new TempProject();

        LooseSoundResult missing = LooseSoundCook.Run(project.Layout.AssetsPath, SourcePath);

        missing.Cooked.ShouldBeNull();
        missing.IsRepeatable.ShouldBeFalse();
        missing.Diagnostics.Single().Id.ToString().ShouldBe("SC1002");
    }

    [Fact]
    public void The_source_of_a_cooked_path_is_the_wav_or_wave_beside_it()
    {
        using var project = new TempProject();
        string root = project.Layout.AssetsPath;
        project.WriteAsset(SourcePath, TempProject.Wav());
        project.WriteAsset("Sounds/lift_hum.wave", TempProject.Wav());
        project.WriteAsset("Sounds/notes.txt", "not a sound");

        LooseSoundCook.FindSource(root, CookedPath).ShouldBe(SourcePath);
        LooseSoundCook.FindSource(root, "Sounds/lift_hum.saudio").ShouldBe("Sounds/lift_hum.wave");

        LooseSoundCook.FindSource(root, "Sounds/notes.saudio").ShouldBeNull();
        LooseSoundCook.FindSource(root, SourcePath).ShouldBeNull();
        LooseSoundCook.FindSource(root, "../outside.saudio").ShouldBeNull();
    }

    // Read back out of the pack, not off a rule's emission: the pack is what ships.
    private static byte[] PackedBytes(TempProject project)
    {
        CookResult result = new CookSession(project.Layout, new CookSettings { UseCache = false }).Run();
        result.Succeeded.ShouldBeTrue(string.Join('\n', result.Diagnostics));

        using var pack = new PackSource(NullLogger.Instance, result.OutputPath.ShouldNotBeNull());

        pack.TryOpen(CookedPath, out ContentBlob? blob).ShouldBeTrue();
        using ContentBlob opened = blob.ShouldNotBeNull();

        return opened.Span.ToArray();
    }
}
