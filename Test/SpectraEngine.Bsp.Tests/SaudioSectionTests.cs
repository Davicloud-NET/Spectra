using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Audio;
using System;
using System.Buffers.Binary;

namespace SpectraEngine.Bsp.Tests;

// The section table of a .saudio and the markers in it. Files come from
// HandBuiltSaudio, and each refusal test damages one field of a valid file.
public sealed class SaudioSectionTests
{
    private const string Source = "Sounds/probe.saudio";
    private const int Frames = 100;

    // "LIPS": a tag this build has no reader for.
    private const uint UnknownTag = 'L' | ('I' << 8) | ('P' << 16) | ((uint)'S' << 24);

    [Fact]
    public void A_sound_with_no_section_table_has_no_markers_and_still_reads()
    {
        SaudioInfo info = SaudioReader.Read(HandBuiltSaudio.Resident(Frames), Source);

        info.Markers.ShouldBeEmpty();
        info.SkippedSectionCount.ShouldBe(0);
        info.FrameCount.ShouldBe(Frames);
    }

    [Fact]
    public void Markers_are_read_back_as_frames_and_names()
    {
        // The first frame and the very end are both places a marker can be.
        byte[] file = WithMarkers((0, "start"), (40, "Tür öffnet"), (Frames, "end"));

        SaudioInfo info = SaudioReader.Read(file, Source);

        info.Markers.ShouldBe(
        [
            new AudioMarker(0, "start"),
            new AudioMarker(40, "Tür öffnet"),
            new AudioMarker(Frames, "end"),
        ]);

        // The samples are where they were.
        info.Pcm(file).Length.ShouldBe(Frames);
    }

    [Fact]
    public void Two_markers_may_share_a_frame()
    {
        byte[] file = WithMarkers((40, "a"), (40, "b"));

        SaudioReader.Read(file, Source).Markers.Count.ShouldBe(2);
    }

    [Fact]
    public void A_section_table_with_no_sections_is_a_sound_with_no_markers()
    {
        byte[] file = HandBuiltSaudio.WithSections(Frames);

        SaudioReader.Read(file, Source).Markers.ShouldBeEmpty();
    }

    [Fact]
    public void An_unknown_section_is_skipped_and_the_markers_beside_it_are_read()
    {
        byte[] file = HandBuiltSaudio.WithSections(
            Frames,
            (UnknownTag, [1, 2, 3, 4, 5]),
            (HandBuiltSaudio.MarkerTag, HandBuiltSaudio.MarkerBody((7, "now"))),
            (UnknownTag, []));

        SaudioInfo info = SaudioReader.Read(file, Source);

        info.Markers.ShouldBe([new AudioMarker(7, "now")]);
        info.SkippedSectionCount.ShouldBe(2);
    }

    [Fact]
    public void A_refusal_names_the_file()
    {
        byte[] file = WithMarkers((7, "now"));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(HandBuiltSaudio.SectionTableOffsetOffset), 9_999);

        Refusal(file).ShouldContain(Source);
    }

    [Fact]
    public void A_section_table_that_starts_outside_the_file_is_refused()
    {
        byte[] file = WithMarkers((7, "now"));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(HandBuiltSaudio.SectionTableOffsetOffset), 9_999);

        Refusal(file).ShouldContain("section table starts at byte 9999");
    }

    [Fact]
    public void A_section_table_that_starts_inside_the_header_is_refused()
    {
        byte[] file = WithMarkers((7, "now"));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(HandBuiltSaudio.SectionTableOffsetOffset), 8);

        Refusal(file).ShouldContain("section table starts at byte 8");
    }

    [Fact]
    public void A_section_table_that_runs_past_the_file_is_refused()
    {
        byte[] file = WithMarkers((7, "now"));
        int table = HandBuiltSaudio.SectionEntryOffset(file, 0) - HandBuiltSaudio.SectionTableHeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(table), 5_000);

        string message = Refusal(file);
        message.ShouldContain("declares 5000 sections");
        message.ShouldContain($"{file.Length}-byte file");
    }

    [Fact]
    public void A_section_that_runs_past_the_file_is_refused_even_when_its_tag_is_unknown()
    {
        // The skip rule must not let a malformed file through.
        byte[] file = HandBuiltSaudio.WithSections(Frames, (UnknownTag, [1, 2, 3, 4]));
        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(HandBuiltSaudio.SectionEntryOffset(file, 0) + 8), 4_000);

        string message = Refusal(file);
        message.ShouldContain("'LIPS' section claims 4000 bytes");
    }

    [Fact]
    public void A_section_placed_inside_the_header_is_refused()
    {
        byte[] file = WithMarkers((7, "now"));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(HandBuiltSaudio.SectionEntryOffset(file, 0) + 4), 16);

        Refusal(file).ShouldContain("'MARK' section claims");
    }

    [Fact]
    public void A_marker_section_carried_twice_is_refused()
    {
        byte[] body = HandBuiltSaudio.MarkerBody((7, "now"));
        byte[] file = HandBuiltSaudio.WithSections(
            Frames, (HandBuiltSaudio.MarkerTag, body), (HandBuiltSaudio.MarkerTag, body));

        Refusal(file).ShouldContain("more than once");
    }

    [Fact]
    public void A_marker_section_too_short_for_its_own_count_is_refused()
    {
        byte[] file = HandBuiltSaudio.WithSections(Frames, (HandBuiltSaudio.MarkerTag, [1, 0]));

        Refusal(file).ShouldContain("too short to hold its own count");
    }

    [Fact]
    public void A_marker_count_the_section_cannot_hold_is_refused_before_anything_is_allocated()
    {
        byte[] file = WithMarkers((7, "now"));
        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(HandBuiltSaudio.SectionOffset(file, 0)), uint.MaxValue);

        Refusal(file).ShouldContain($"declares {uint.MaxValue} markers");
    }

    [Fact]
    public void A_marker_name_that_runs_past_its_section_is_refused()
    {
        byte[] file = WithMarkers((7, "now"));
        BinaryPrimitives.WriteUInt16LittleEndian(
            file.AsSpan(HandBuiltSaudio.SectionOffset(file, 0) + 4 + 8), 200);

        Refusal(file).ShouldContain("200-byte name");
    }

    [Fact]
    public void A_marker_record_cut_short_is_refused()
    {
        // Two records declared, room for the first and most of the second.
        byte[] body = HandBuiltSaudio.MarkerBody((7, "a"), (9, "b"));
        Array.Resize(ref body, body.Length - 2);
        byte[] file = HandBuiltSaudio.WithSections(Frames, (HandBuiltSaudio.MarkerTag, body));

        Refusal(file).ShouldContain("marker 1 starts at byte");
    }

    [Fact]
    public void A_marker_past_the_end_of_the_sound_is_refused()
    {
        byte[] file = WithMarkers((Frames + 1, "late"));

        Refusal(file).ShouldContain($"marker 0 is at frame {Frames + 1} and the sound is {Frames} frames long");
    }

    [Fact]
    public void Markers_out_of_frame_order_are_refused()
    {
        byte[] file = WithMarkers((40, "second"), (10, "first"));

        Refusal(file).ShouldContain("marker 1 is at frame 10 and marker 0 is at 40");
    }

    [Fact]
    public void A_marker_with_no_name_is_refused()
    {
        byte[] file = WithMarkers((7, ""));

        Refusal(file).ShouldContain("marker 0 has no name");
    }

    [Fact]
    public void A_marker_name_that_is_not_text_is_refused()
    {
        byte[] file = WithMarkers((7, "now"));

        // 0xFF never appears in UTF-8.
        file[HandBuiltSaudio.SectionOffset(file, 0) + 4 + 10] = 0xFF;

        Refusal(file).ShouldContain("not UTF-8");
    }

    private static byte[] WithMarkers(params (long Frame, string Name)[] markers) =>
        HandBuiltSaudio.WithSections(Frames, (HandBuiltSaudio.MarkerTag, HandBuiltSaudio.MarkerBody(markers)));

    private static string Refusal(byte[] file) =>
        Should.Throw<SaudioFormatException>(() => SaudioReader.Read(file, Source)).Message;
}
