using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Audio;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Packs;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio;
using System.IO;
using System.Linq;
using System.Text;

namespace Spectra.Kitchen.Tests;

// Asserts on header fields and bytes. Playback is a manual gate: CI has no sound card.
public class AudioRuleTests
{
    private const string SourcePath = "Sounds/door_open.wav";
    private const string CookedPath = "Sounds/door_open.saudio";

    [Fact]
    public void A_wav_is_cooked_to_a_saudio_rather_than_copied()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 128, sampleRate: 48_000, channels: 1));

        CookResult result = Cook(project);
        result.Succeeded.ShouldBeTrue(Describe(result));

        CookedAsset asset = result.Assets.Single();
        asset.Rule.ShouldBe(RuleKind.Audio);

        // Single: the authored .wav must not ship beside the cooked file.
        CookedOutput output = asset.Outputs.Single();
        output.Path.ShouldBe(CookedPath);

        var pack = project.Track(new PackSource(NullLogger.Instance, result.OutputPath!));
        pack.Exists(CookedPath).ShouldBeTrue();
        pack.Exists(SourcePath).ShouldBeFalse();

        pack.TryOpen(CookedPath, out ContentBlob? blob).ShouldBeTrue();
        using (blob)
        {
            SaudioInfo info = SaudioReader.Read(blob.Span, CookedPath);
            info.Codec.ShouldBe(SaudioCodec.PcmS16);
            info.Format.SampleRate.ShouldBe(48_000);
            info.Format.Channels.ShouldBe(1);
            info.FrameCount.ShouldBe(128);

            info.IsPositional.ShouldBeTrue();
            info.IsStreaming.ShouldBeFalse();
        }
    }

    [Fact]
    public void Two_cooks_of_one_sound_produce_the_same_bytes()
    {
        // Byte identity: the cook cache is content-addressed.
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 441, sampleRate: 44_100, channels: 2, seed: 5));

        byte[] first = CookedBytes(project);
        byte[] second = CookedBytes(project);

        first.ShouldBe(second);
    }

    [Fact]
    public void Loop_points_survive_a_resampled_cook_in_sample_frames()
    {
        // One second at 44.1 kHz looping from 0.25 s to 0.5 s must land on 12000
        // and 24000 at 48 kHz. The smpl chunk's end is inclusive, so 22049 below
        // is the last frame played.
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(
            frames: 44_100, sampleRate: 44_100, channels: 1, loopStart: 11_025, loopEnd: 22_049));

        SaudioInfo info = SaudioReader.Read(CookedBytes(project), CookedPath);

        info.Format.SampleRate.ShouldBe(48_000);
        info.FrameCount.ShouldBe(48_000);
        info.Loop.StartFrame.ShouldBe(12_000);
        info.Loop.EndFrame.ShouldBe(24_000);

        info.Format.FramesToSeconds(info.Loop.StartFrame).ShouldBe(0.25, 1e-9);
        info.Format.FramesToSeconds(info.Loop.EndFrame).ShouldBe(0.5, 1e-9);
    }

    [Fact]
    public void A_resampled_cook_says_so_rather_than_changing_the_content_silently()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 512, sampleRate: 22_050, channels: 1));

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));

        CookDiagnostic note = result.Diagnostics.Single(d => d.Id.ToString() == "SC4004");
        note.Severity.ShouldBe(CookDiagnosticSeverity.Info);
        note.Message.ShouldContain("22050");
        note.Message.ShouldContain("48000");
    }

    [Fact]
    public void A_sound_already_at_the_project_rate_is_not_resampled_at_all()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 64, sampleRate: 48_000, channels: 1));

        Cook(project).Diagnostics.ShouldNotContain(d => d.Id.ToString() == "SC4004");
    }

    [Fact]
    public void The_project_rate_is_a_setting_and_the_cook_resamples_to_whatever_it_says()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 480, sampleRate: 48_000, channels: 1));

        var settings = new CookSettings { UseCache = false, AudioSampleRate = 44_100 };
        CookResult result = new CookSession(project.Layout, settings).Run();
        result.Succeeded.ShouldBeTrue(Describe(result));

        SaudioInfo info = SaudioReader.Read(Payload(project, result), CookedPath);
        info.Format.SampleRate.ShouldBe(44_100);
        info.FrameCount.ShouldBe(AudioResampler.ConvertFrames(480, 48_000, 44_100));
    }

    [Fact]
    public void A_stereo_sound_nothing_declares_flat_warns_that_it_will_not_be_positional()
    {
        // OpenAL does not spatialise a stereo buffer and reports nothing.
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 64, sampleRate: 48_000, channels: 2));

        CookResult result = Cook(project);

        // Only a warning: stereo is legitimate to ship.
        result.Succeeded.ShouldBeTrue(Describe(result));

        CookDiagnostic warning = result.Diagnostics.Single(d => d.Id.ToString() == "SC4003");
        warning.Severity.ShouldBe(CookDiagnosticSeverity.Warning);
        warning.Message.ShouldContain("stereo");
        warning.Message.ShouldContain("_2d");
    }

    [Fact]
    public void A_stereo_sound_that_declares_itself_flat_is_silent_about_it()
    {
        using var project = new TempProject();
        project.WriteAsset("Sounds/theme_2d.wav", TempProject.Wav(frames: 64, sampleRate: 48_000, channels: 2));

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        result.Diagnostics.ShouldNotContain(d => d.Id.ToString() == "SC4003");
        SaudioReader.Read(Payload(project, result), "theme").IsPositional.ShouldBeFalse();
    }

    [Fact]
    public void A_mono_sound_that_declares_itself_flat_is_not_written_as_positional()
    {
        using var project = new TempProject();
        project.WriteAsset("Sounds/click_2d.wav", TempProject.Wav(frames: 32, sampleRate: 48_000, channels: 1));

        CookResult result = Cook(project);

        SaudioReader.Read(Payload(project, result), "click").IsPositional.ShouldBeFalse();
    }

    [Fact]
    public void A_long_sound_is_flagged_streaming_and_carries_a_seek_table()
    {
        using var project = new TempProject();
        project.WriteAsset(
            "Sounds/room_tone.wav",
            TempProject.Wav(frames: 48_000 * 11, sampleRate: 48_000, channels: 1));

        CookResult result = Cook(project);
        result.Succeeded.ShouldBeTrue(Describe(result));

        SaudioInfo info = SaudioReader.Read(Payload(project, result), "room_tone");
        info.IsStreaming.ShouldBeTrue();
        info.FramesPerSeekEntry.ShouldBe(48_000);
        info.SeekTable.Length.ShouldBe(11);
    }

    [Fact]
    public void A_file_named_wav_that_is_not_one_is_an_error_rather_than_a_raw_copy()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Bytes(64));

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeFalse();
        result.Diagnostics.ShouldContain(d => d.IsError && d.Id.ToString() == "SC4001");
        result.Assets.Single(a => a.SourcePath == SourcePath).Outputs.ShouldBeEmpty();
    }

    [Fact]
    public void A_loop_that_ends_past_the_data_is_dropped_and_said_out_loud()
    {
        // A DAW can write this after an edit.
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(
            frames: 64, sampleRate: 48_000, channels: 1, loopStart: 8, loopEnd: 4_000));

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        result.Diagnostics.ShouldContain(d => d.Id.ToString() == "SC4005");
        SaudioReader.Read(Payload(project, result), CookedPath).Loop.IsLooping.ShouldBeFalse();
    }

    [Fact]
    public void An_alternating_loop_is_dropped_rather_than_played_forward()
    {
        // AudioLoopCursor only plays forward.
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(
            frames: 64, sampleRate: 48_000, channels: 1, loopStart: 8, loopEnd: 40, loopType: 1));

        CookResult result = Cook(project);

        result.Diagnostics.ShouldContain(d => d.Id.ToString() == "SC4005");
        SaudioReader.Read(Payload(project, result), CookedPath).Loop.IsLooping.ShouldBeFalse();
    }

    [Fact]
    public void A_cooked_sound_verifies_clean_in_the_pack_it_was_written_to()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 96, sampleRate: 48_000, channels: 1));

        CookResult result = Cook(project);
        PackVerifyResult verified = PackVerifier.Verify(result.OutputPath!);

        verified.Succeeded.ShouldBeTrue(string.Join('\n', verified.Diagnostics));
        verified.EntriesChecked.ShouldBe(1);
    }

    [Fact]
    public void A_saudio_entry_the_reader_refuses_fails_a_verify()
    {
        // Pack written by hand: the cook refuses a broken sound before a pack exists.
        using var project = new TempProject();
        string pack = Path.Combine(project.Root, "mute.spack");

        var writer = new PackWriter();
        writer.Add("Sounds/broken.saudio", PackEntryKind.Audio, Encoding.ASCII.GetBytes("not a sound at all"));
        writer.WriteToFile(pack);

        PackVerifyResult result = PackVerifier.Verify(pack);

        result.Succeeded.ShouldBeFalse();
        result.Diagnostics.Single(d => d.IsError).Id.ToString().ShouldBe("SC4006");
    }

    [Fact]
    public void A_frame_count_converts_by_rounding_rather_than_by_truncating()
    {
        AudioResampler.ConvertFrames(11_025, 44_100, 48_000).ShouldBe(12_000);
        AudioResampler.ConvertFrames(1, 44_100, 48_000).ShouldBe(1);
        AudioResampler.ConvertFrames(1, 48_000, 44_100).ShouldBe(1);

        // Halfway rounds up.
        AudioResampler.ConvertFrames(1, 2, 3).ShouldBe(2);

        // Identity when the rates match.
        AudioResampler.ConvertFrames(123_456, 48_000, 48_000).ShouldBe(123_456);
    }

    private static CookResult Cook(TempProject project) =>
        new CookSession(project.Layout, new CookSettings { UseCache = false }).Run();

    private static byte[] CookedBytes(TempProject project) => Payload(project, Cook(project));

    // Read back out of the pack, not off the rule's emission: the pack is what ships.
    private static byte[] Payload(TempProject project, CookResult result)
    {
        result.Succeeded.ShouldBeTrue(Describe(result));

        string path = result.Assets.SelectMany(a => a.Outputs).Single().Path;
        using var pack = new PackSource(NullLogger.Instance, result.OutputPath!);

        pack.TryOpen(path, out ContentBlob? blob).ShouldBeTrue();
        using (blob) return blob!.Span.ToArray();
    }

    private static string Describe(CookResult result) => string.Join('\n', result.Diagnostics);
}
