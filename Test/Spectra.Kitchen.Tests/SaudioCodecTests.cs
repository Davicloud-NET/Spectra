using Spectra.Kitchen.Audio;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Audio;
using System;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// <see cref="SaudioWriter"/> against the engine's <see cref="SaudioReader"/>.
/// The byte layout itself is pinned by <c>SaudioFormatTests</c> in the engine suite.
/// </summary>
public class SaudioCodecTests
{
    [Fact]
    public void A_written_sound_reads_back_field_for_field()
    {
        short[] pcm = Ramp(frames: 32, channels: 2);
        var format = new AudioFormat(48_000, 2);

        byte[] file = SaudioWriter.Write(format, pcm, LoopRegion.None, positional: false);
        SaudioInfo info = SaudioReader.Read(file, "probe");

        info.Codec.ShouldBe(SaudioCodec.PcmS16);
        info.Format.ShouldBe(format);
        info.ChannelLayout.ShouldBe(SaudioChannelLayout.Stereo);
        info.FrameCount.ShouldBe(32);
        info.Loop.IsLooping.ShouldBeFalse();
        info.IsStreaming.ShouldBeFalse();
        info.IsPositional.ShouldBeFalse();
        info.SeekTable.ShouldBeEmpty();

        ReadOnlySpan<short> back = info.Pcm(file);
        back.Length.ShouldBe(pcm.Length);
        for (int i = 0; i < pcm.Length; i++) back[i].ShouldBe(pcm[i]);
    }

    [Fact]
    public void Loop_points_survive_the_container_as_sample_frames()
    {
        short[] pcm = Ramp(frames: 100, channels: 2);
        var loop = new LoopRegion(17, 83);

        byte[] file = SaudioWriter.Write(new AudioFormat(48_000, 2), pcm, loop, positional: false);

        SaudioReader.Read(file, "probe").Loop.ShouldBe(loop);
    }

    [Fact]
    public void A_streamed_sound_carries_a_seek_table_the_reader_walks()
    {
        short[] pcm = Ramp(frames: 100, channels: 1);

        byte[] file = SaudioWriter.Write(
            new AudioFormat(48_000, 1), pcm, LoopRegion.None, positional: true, framesPerSeekEntry: 32);

        SaudioInfo info = SaudioReader.Read(file, "probe");

        info.IsStreaming.ShouldBeTrue();
        info.FramesPerSeekEntry.ShouldBe(32);

        // 100 frames at 32 per entry: the fourth entry covers a partial block.
        info.SeekTable.Length.ShouldBe(4);
        info.SeekTable[0].ShouldBe(info.DataOffset);
        info.SeekTable[3].ShouldBe(info.DataOffset + 96 * 2);
    }

    [Fact]
    public void A_stereo_sound_is_never_written_as_positional()
    {
        // OpenAL never spatialises a stereo buffer.
        short[] pcm = Ramp(frames: 8, channels: 2);

        byte[] file = SaudioWriter.Write(new AudioFormat(48_000, 2), pcm, LoopRegion.None, positional: true);

        SaudioReader.Read(file, "probe").IsPositional.ShouldBeFalse();
    }

    [Fact]
    public void A_mono_sound_asked_for_positionally_says_so()
    {
        short[] pcm = Ramp(frames: 8, channels: 1);

        byte[] file = SaudioWriter.Write(new AudioFormat(48_000, 1), pcm, LoopRegion.None, positional: true);

        SaudioReader.Read(file, "probe").IsPositional.ShouldBeTrue();
    }

    [Fact]
    public void Writing_the_same_sound_twice_produces_the_same_bytes()
    {
        short[] pcm = Ramp(frames: 64, channels: 2);
        var format = new AudioFormat(44_100, 2);
        var loop = new LoopRegion(8, 40);

        byte[] first = SaudioWriter.Write(format, pcm, loop, positional: false, framesPerSeekEntry: 16);
        byte[] second = SaudioWriter.Write(format, pcm, loop, positional: false, framesPerSeekEntry: 16);

        first.ShouldBe(second);
    }

    [Fact]
    public void Every_reserved_byte_is_written_zero()
    {
        byte[] file = SaudioWriter.Write(
            new AudioFormat(48_000, 1), Ramp(frames: 4, channels: 1), LoopRegion.None, positional: true);

        file[SaudioFormat.ReservedOffset].ShouldBe((byte)0);
        file[SaudioFormat.ReservedOffset + 1].ShouldBe((byte)0);

        for (int at = SaudioFormat.HeaderPaddingOffset; at < SaudioFormat.HeaderSize; at++)
            file[at].ShouldBe((byte)0);
    }

    [Fact]
    public void Markers_survive_the_container_as_frames_and_names()
    {
        short[] pcm = Ramp(frames: 100, channels: 2);
        AudioMarker[] markers = [new(0, "start"), new(40, "Tür öffnet"), new(100, "end")];

        byte[] file = SaudioWriter.Write(
            new AudioFormat(48_000, 2), pcm, LoopRegion.None, positional: false, markers: markers);

        SaudioInfo info = SaudioReader.Read(file, "probe");
        info.Markers.ShouldBe(markers);
        info.SkippedSectionCount.ShouldBe(0);

        // The samples are still the ones written.
        ReadOnlySpan<short> back = info.Pcm(file);
        back.Length.ShouldBe(pcm.Length);
        back[^1].ShouldBe(pcm[^1]);
    }

    [Fact]
    public void Markers_are_written_by_frame_and_then_by_name_whatever_order_they_come_in()
    {
        short[] pcm = Ramp(frames: 100, channels: 1);
        var format = new AudioFormat(48_000, 1);
        AudioMarker[] given = [new(60, "b"), new(10, "z"), new(60, "B"), new(60, "a")];
        AudioMarker[] shuffled = [given[3], given[0], given[2], given[1]];

        byte[] first = SaudioWriter.Write(format, pcm, LoopRegion.None, positional: true, markers: given);
        byte[] second = SaudioWriter.Write(format, pcm, LoopRegion.None, positional: true, markers: shuffled);

        second.ShouldBe(first);

        // Ordinal: capitals sort before small letters.
        SaudioReader.Read(first, "probe").Markers.ShouldBe(
            [new AudioMarker(10, "z"), new AudioMarker(60, "B"), new AudioMarker(60, "a"), new AudioMarker(60, "b")]);
    }

    [Fact]
    public void A_sound_with_no_markers_is_written_with_no_section_table()
    {
        short[] pcm = Ramp(frames: 8, channels: 1);

        byte[] file = SaudioWriter.Write(new AudioFormat(48_000, 1), pcm, LoopRegion.None, positional: true);

        file.Length.ShouldBe(SaudioFormat.HeaderSize + 8 * 2);
        SaudioReader.Read(file, "probe").Markers.ShouldBeEmpty();
    }

    [Fact]
    public void Markers_and_a_seek_table_share_one_file()
    {
        short[] pcm = Ramp(frames: 100, channels: 1);

        byte[] file = SaudioWriter.Write(
            new AudioFormat(48_000, 1),
            pcm,
            new LoopRegion(10, 90),
            positional: true,
            framesPerSeekEntry: 32,
            markers: [new AudioMarker(50, "now")]);

        SaudioInfo info = SaudioReader.Read(file, "probe");

        info.SeekTable.Length.ShouldBe(4);
        info.SeekTable[0].ShouldBe(info.DataOffset);
        info.Loop.ShouldBe(new LoopRegion(10, 90));
        info.Markers.ShouldBe([new AudioMarker(50, "now")]);
        info.Pcm(file)[99].ShouldBe(pcm[99]);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    public void The_samples_start_on_an_even_byte_whatever_length_the_marker_names_are(string name)
    {
        // The payload is read in place as 16-bit samples.
        byte[] file = SaudioWriter.Write(
            new AudioFormat(48_000, 1),
            Ramp(frames: 8, channels: 1),
            LoopRegion.None,
            positional: true,
            markers: [new AudioMarker(4, name)]);

        (SaudioReader.Read(file, "probe").DataOffset % 2).ShouldBe(0);
    }

    [Fact]
    public void A_marker_past_the_end_of_the_sound_is_refused_at_write()
    {
        Should.Throw<ArgumentException>(() => SaudioWriter.Write(
            new AudioFormat(48_000, 1),
            Ramp(frames: 8, channels: 1),
            LoopRegion.None,
            positional: true,
            markers: [new AudioMarker(9, "late")]));
    }

    [Fact]
    public void A_marker_with_no_name_is_refused_at_write()
    {
        Should.Throw<ArgumentException>(() => SaudioWriter.Write(
            new AudioFormat(48_000, 1),
            Ramp(frames: 8, channels: 1),
            LoopRegion.None,
            positional: true,
            markers: [new AudioMarker(4, string.Empty)]));
    }

    [Fact]
    public void A_marker_name_too_long_for_its_record_is_refused_at_write()
    {
        Should.Throw<ArgumentException>(() => SaudioWriter.Write(
            new AudioFormat(48_000, 1),
            Ramp(frames: 8, channels: 1),
            LoopRegion.None,
            positional: true,
            markers: [new AudioMarker(4, new string('x', SaudioFormat.MaxMarkerNameBytes + 1))]));
    }

    [Fact]
    public void A_loop_past_the_end_of_the_sound_is_refused_at_write_rather_than_shipped()
    {
        // Thrown, not clamped: a clamped loop plays wrong and the cook log says fine.
        short[] pcm = Ramp(frames: 8, channels: 1);

        Should.Throw<ArgumentException>(() => SaudioWriter.Write(
            new AudioFormat(48_000, 1), pcm, new LoopRegion(2, 900), positional: true));
    }

    [Fact]
    public void Interleaved_samples_that_are_not_a_whole_number_of_frames_are_refused()
    {
        Should.Throw<ArgumentException>(() => SaudioWriter.Write(
            new AudioFormat(48_000, 2), new short[7], LoopRegion.None, positional: false));
    }

    // Per-channel offset, so a channel swap or an off-by-one shows as a mismatch.
    private static short[] Ramp(int frames, int channels)
    {
        var pcm = new short[frames * channels];
        for (int frame = 0; frame < frames; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
                pcm[frame * channels + channel] = (short)(frame * 53 + channel * 7 - 3000);
        }

        return pcm;
    }
}
