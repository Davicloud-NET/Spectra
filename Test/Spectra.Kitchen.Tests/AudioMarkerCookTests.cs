using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio;
using System.Buffers.Binary;
using System.Linq;

namespace Spectra.Kitchen.Tests;

// Markers through a whole cook: from a WAV's cue points, or the label file
// beside it, into the cooked sound in the pack.
public class AudioMarkerCookTests
{
    private const string SourcePath = "Sounds/guard_hey.wav";
    private const string CookedPath = "Sounds/guard_hey.saudio";
    private const string LabelPath = "Sounds/guard_hey.markers.txt";

    [Fact]
    public void Labelled_cue_points_land_on_the_right_frames_at_the_project_rate()
    {
        // One second at 44.1 kHz with cue points at a quarter and a half second.
        using var project = new TempProject();
        project.WriteAsset(SourcePath, CuedWav.Add(
            TempProject.Wav(frames: 44_100, sampleRate: 44_100),
            new CuedWav.Cue(1, 11_025, "open"),
            new CuedWav.Cue(2, 22_050, "now")));

        SaudioInfo info = CookedSound(Cook(project));

        info.Format.SampleRate.ShouldBe(48_000);
        info.Markers.ShouldBe([new AudioMarker(12_000, "open"), new AudioMarker(24_000, "now")]);
    }

    [Fact]
    public void A_cue_point_with_no_label_is_numbered_by_its_place_in_time()
    {
        // Listed out of time order, and the ids are not what numbers them.
        using var project = new TempProject();
        project.WriteAsset(SourcePath, CuedWav.Add(
            TempProject.Wav(frames: 48_000),
            new CuedWav.Cue(40, 30_000),
            new CuedWav.Cue(90, 100, "first"),
            new CuedWav.Cue(10, 20_000)));

        CookResult result = Cook(project);

        CookedSound(result).Markers.ShouldBe(
        [
            new AudioMarker(100, "first"),
            new AudioMarker(20_000, "marker2"),
            new AudioMarker(30_000, "marker3"),
        ]);

        result.Diagnostics.ShouldNotContain(d => d.Id.ToString() == "SC4007");
    }

    [Fact]
    public void A_marker_past_the_end_warns_and_is_dropped()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, CuedWav.Add(
            TempProject.Wav(frames: 48_000),
            new CuedWav.Cue(1, 24_000, "inside"),
            new CuedWav.Cue(2, 96_000, "late")));

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        CookedSound(result).Markers.ShouldBe([new AudioMarker(24_000, "inside")]);

        CookDiagnostic warning = result.Diagnostics.Single(d => d.Id.ToString() == "SC4007");
        warning.Severity.ShouldBe(CookDiagnosticSeverity.Warning);
        warning.File.ShouldBe(SourcePath);
        warning.Message.ShouldContain("'late'");
        warning.Message.ShouldContain("at 2 s");
        warning.Message.ShouldContain("ends at 1 s");
    }

    [Fact]
    public void A_marker_past_the_end_fails_a_strict_cook()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, CuedWav.Add(
            TempProject.Wav(frames: 48_000), new CuedWav.Cue(1, 96_000, "late")));

        CookResult result = Cook(project, strict: true);

        result.Succeeded.ShouldBeFalse();
        result.Diagnostics.Single(d => d.Id.ToString() == "SC4007").IsError.ShouldBeTrue();
    }

    [Fact]
    public void A_marker_at_the_very_end_of_the_sound_is_kept()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, CuedWav.Add(
            TempProject.Wav(frames: 44_100, sampleRate: 44_100), new CuedWav.Cue(1, 44_100, "end")));

        CookResult result = Cook(project);

        CookedSound(result).Markers.ShouldBe([new AudioMarker(48_000, "end")]);
        result.Diagnostics.ShouldNotContain(d => d.Id.ToString() == "SC4007");
    }

    [Fact]
    public void Cooking_a_sound_with_markers_twice_gives_the_same_bytes()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, CuedWav.Add(
            TempProject.Wav(frames: 4_410, sampleRate: 44_100),
            new CuedWav.Cue(1, 2_000, "b"),
            new CuedWav.Cue(2, 1_000),
            new CuedWav.Cue(3, 2_000, "a")));

        byte[] first = CookedBytes(Cook(project));
        byte[] second = CookedBytes(Cook(project));

        second.ShouldBe(first);
    }

    [Fact]
    public void The_cooked_bytes_do_not_depend_on_the_order_the_wav_lists_its_chunks_in()
    {
        CuedWav.Cue[] cues = [new(1, 2_000, "b"), new(2, 3_000, "c"), new(3, 2_000, "a")];
        CuedWav.Cue[] shuffled = [cues[1], cues[2], cues[0]];

        using var listed = new TempProject();
        listed.WriteAsset(SourcePath, CuedWav.Add(TempProject.Wav(frames: 4_800), cues));

        using var other = new TempProject();
        other.WriteAsset(SourcePath, CuedWav.AddLabelsFirst(TempProject.Wav(frames: 4_800), shuffled));

        byte[] first = CookedBytes(Cook(listed));
        byte[] second = CookedBytes(Cook(other));

        second.ShouldBe(first);
        SaudioReader.Read(first, CookedPath).Markers.Select(m => m.Name).ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void A_sound_with_no_cue_points_has_no_markers_and_no_section_table()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 480));

        byte[] cooked = CookedBytes(Cook(project));

        SaudioReader.Read(cooked, CookedPath).Markers.ShouldBeEmpty();
        BinaryPrimitives.ReadUInt32LittleEndian(cooked.AsSpan(SaudioFormat.SectionTableOffsetOffset)).ShouldBe(0u);
        cooked.Length.ShouldBe(SaudioFormat.HeaderSize + 480 * 2);
    }

    [Fact]
    public void A_label_file_beside_a_sound_with_no_cue_points_gives_it_markers()
    {
        // The label times are seconds, so they do not move with the WAV's rate.
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 44_100, sampleRate: 44_100));
        project.WriteAsset(LabelPath, "0.250000\t0.250000\topen\n0.500000\t0.750000\n");

        CookResult result = Cook(project);

        CookedSound(result).Markers.ShouldBe(
            [new AudioMarker(12_000, "open"), new AudioMarker(24_000, "marker2")]);
    }

    [Fact]
    public void The_wavs_own_cue_points_win_over_a_label_file()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, CuedWav.Add(TempProject.Wav(frames: 48_000), new CuedWav.Cue(1, 100, "cue")));
        project.WriteAsset(LabelPath, "0.5\t0.5\tlabel\n");

        CookedSound(Cook(project)).Markers.ShouldBe([new AudioMarker(100, "cue")]);
    }

    [Fact]
    public void A_label_line_the_cook_cannot_read_warns_with_its_file_and_line()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 48_000));
        project.WriteAsset(LabelPath, "0.25\t0.25\topen\nhalf a second in\n");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        CookedSound(result).Markers.ShouldBe([new AudioMarker(12_000, "open")]);

        CookDiagnostic warning = result.Diagnostics.Single(d => d.Id.ToString() == "SC4008");
        warning.Severity.ShouldBe(CookDiagnosticSeverity.Warning);
        warning.File.ShouldBe(LabelPath);
        warning.Line.ShouldBe(2);
    }

    [Fact]
    public void A_label_past_the_end_of_the_sound_warns_like_a_cue_point_does()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 48_000));
        project.WriteAsset(LabelPath, "5.0\t5.0\tlate\n");

        CookResult result = Cook(project);

        CookedSound(result).Markers.ShouldBeEmpty();
        result.Diagnostics.Single(d => d.Id.ToString() == "SC4007").Message.ShouldContain("'late'");
    }

    [Fact]
    public void Adding_or_editing_the_label_file_recooks_the_sound_out_of_the_cache()
    {
        using var project = new TempProject();
        project.WriteAsset(SourcePath, TempProject.Wav(frames: 48_000));

        CookResult bare = CookCached(project);
        CookedSound(bare).Markers.ShouldBeEmpty();
        CookCached(project).CacheHits.ShouldBe(1);

        project.WriteAsset(LabelPath, "0.25\t0.25\topen\n");
        CookResult added = CookCached(project);
        added.Assets.Single(a => a.SourcePath == SourcePath).FromCache.ShouldBeFalse();
        CookedSound(added).Markers.ShouldBe([new AudioMarker(12_000, "open")]);

        project.WriteAsset(LabelPath, "0.5\t0.5\tnow\n");
        CookResult edited = CookCached(project);
        edited.Assets.Single(a => a.SourcePath == SourcePath).FromCache.ShouldBeFalse();
        CookedSound(edited).Markers.ShouldBe([new AudioMarker(24_000, "now")]);

        CookResult again = CookCached(project);
        again.Assets.Single(a => a.SourcePath == SourcePath).FromCache.ShouldBeTrue();
        CookedSound(again).Markers.ShouldBe([new AudioMarker(24_000, "now")]);
    }

    private static CookResult Cook(TempProject project, bool strict = false) =>
        new CookSession(project.Layout, new CookSettings { UseCache = false, Strict = strict }).Run();

    private static CookResult CookCached(TempProject project) =>
        new CookSession(project.Layout, new CookSettings()).Run();

    private static SaudioInfo CookedSound(CookResult result) =>
        SaudioReader.Read(CookedBytes(result), CookedPath);

    // Read back out of the pack, not off the rule's emission: the pack is what ships.
    private static byte[] CookedBytes(CookResult result)
    {
        result.Succeeded.ShouldBeTrue(Describe(result));

        using var pack = new PackSource(NullLogger.Instance, result.OutputPath!);

        pack.TryOpen(CookedPath, out ContentBlob? blob).ShouldBeTrue();
        using (blob) return blob!.Span.ToArray();
    }

    private static string Describe(CookResult result) => string.Join('\n', result.Diagnostics);
}
