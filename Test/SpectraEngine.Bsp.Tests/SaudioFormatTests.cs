using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Audio;
using System;
using System.Buffers.Binary;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What <see cref="SaudioReader"/> accepts and refuses, and that each refusal names the bad field.
/// </summary>
// Files come from HandBuiltSaudio, which writes bytes from the spec without engine types.
// Each refusal test damages one field of a valid file.
public sealed class SaudioFormatTests
{
    private const string Source = "Sounds/probe.saudio";

    [Fact]
    public void The_header_geometry_is_exactly_what_the_format_declares()
    {
        SaudioFormat.HeaderSize.ShouldBe(HandBuiltSaudio.HeaderSize);
        SaudioFormat.Magic.ShouldBe(HandBuiltSaudio.Magic);
        SaudioFormat.SeekTableHeaderSize.ShouldBe(HandBuiltSaudio.SeekTableHeaderSize);
        SaudioFormat.SeekTableEntrySize.ShouldBe(HandBuiltSaudio.SeekTableEntrySize);
        SaudioFormat.SectionTableHeaderSize.ShouldBe(HandBuiltSaudio.SectionTableHeaderSize);
        SaudioFormat.SectionEntrySize.ShouldBe(HandBuiltSaudio.SectionEntrySize);
        SaudioFormat.MarkerSection.ShouldBe(HandBuiltSaudio.MarkerTag);

        SaudioFormat.MagicOffset.ShouldBe(HandBuiltSaudio.MagicOffset);
        SaudioFormat.VersionOffset.ShouldBe(HandBuiltSaudio.VersionOffset);
        SaudioFormat.CodecOffset.ShouldBe(HandBuiltSaudio.CodecOffset);
        SaudioFormat.FlagsOffset.ShouldBe(HandBuiltSaudio.FlagsOffset);
        SaudioFormat.SampleRateOffset.ShouldBe(HandBuiltSaudio.SampleRateOffset);
        SaudioFormat.ChannelsOffset.ShouldBe(HandBuiltSaudio.ChannelsOffset);
        SaudioFormat.ChannelLayoutOffset.ShouldBe(HandBuiltSaudio.ChannelLayoutOffset);
        SaudioFormat.FrameCountOffset.ShouldBe(HandBuiltSaudio.FrameCountOffset);
        SaudioFormat.LoopStartOffset.ShouldBe(HandBuiltSaudio.LoopStartOffset);
        SaudioFormat.LoopEndOffset.ShouldBe(HandBuiltSaudio.LoopEndOffset);
        SaudioFormat.SeekTableOffsetOffset.ShouldBe(HandBuiltSaudio.SeekTableOffsetOffset);
        SaudioFormat.DataOffsetOffset.ShouldBe(HandBuiltSaudio.DataOffsetOffset);
        SaudioFormat.SectionTableOffsetOffset.ShouldBe(HandBuiltSaudio.SectionTableOffsetOffset);

        // Reads "SAUD" in a hex dump.
        Span<byte> dump = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(dump, SaudioFormat.Magic);
        dump[0].ShouldBe((byte)'S');
        dump[1].ShouldBe((byte)'A');
        dump[2].ShouldBe((byte)'U');
        dump[3].ShouldBe((byte)'D');
    }

    [Fact]
    public void A_hand_built_sound_round_trips_through_the_reader()
    {
        byte[] file = HandBuiltSaudio.Resident(frames: 16, channels: 2, sampleRate: 44_100);

        SaudioInfo info = SaudioReader.Read(file, Source);

        info.FormatVersion.ShouldBe(EngineInfo.AudioFormatVersion);
        info.Codec.ShouldBe(SaudioCodec.PcmS16);
        info.Flags.ShouldBe(SaudioFlags.None);
        info.Format.SampleRate.ShouldBe(44_100);
        info.Format.Channels.ShouldBe(2);
        info.ChannelLayout.ShouldBe(SaudioChannelLayout.Stereo);
        info.FrameCount.ShouldBe(16);
        info.Loop.IsLooping.ShouldBeFalse();
        info.DataOffset.ShouldBe(SaudioFormat.HeaderSize);
        info.DataLength.ShouldBe(16 * 2 * 2);
        info.SeekTable.ShouldBeEmpty();
        info.IsStreaming.ShouldBeFalse();

        // The last sample checks the length: one frame short still gets the first one right.
        ReadOnlySpan<short> pcm = info.Pcm(file);
        pcm.Length.ShouldBe(32);
        pcm[0].ShouldBe((short)(-4000));
        pcm[31].ShouldBe((short)(31 * 37 - 4000));
    }

    [Fact]
    public void Loop_points_are_read_as_sample_frames_rather_than_bytes_or_seconds()
    {
        // Read as bytes, 4 and 12 would be frames 1 and 3 of this stereo file:
        // a valid loop in the wrong place.
        byte[] file = HandBuiltSaudio.Resident(frames: 16, channels: 2, loopStart: 4, loopEnd: 12);

        LoopRegion loop = SaudioReader.Read(file, Source).Loop;

        loop.IsLooping.ShouldBeTrue();
        loop.StartFrame.ShouldBe(4);
        loop.EndFrame.ShouldBe(12);
        loop.LengthFrames.ShouldBe(8);
    }

    [Fact]
    public void A_streaming_sound_carries_a_seek_table_that_walks_its_own_payload()
    {
        byte[] file = HandBuiltSaudio.Streaming(frames: 64, channels: 2, framesPerEntry: 16);

        SaudioInfo info = SaudioReader.Read(file, Source);

        info.IsStreaming.ShouldBeTrue();
        info.FramesPerSeekEntry.ShouldBe(16);
        info.SeekTable.Length.ShouldBe(4);

        for (int i = 0; i < info.SeekTable.Length; i++)
            info.SeekTable[i].ShouldBe(info.DataOffset + i * 16 * 2 * 2);
    }

    [Fact]
    public void The_payload_may_be_shorter_than_the_file_but_never_longer()
    {
        // Trailing bytes are fine: a container may pad, or hand over a larger buffer.
        byte[] file = HandBuiltSaudio.Resident(frames: 8);
        Array.Resize(ref file, file.Length + 32);

        SaudioReader.Read(file, Source).DataLength.ShouldBe(8 * 2);
    }

    [Fact]
    public void A_file_that_is_not_a_saudio_at_all_is_told_so_rather_than_told_it_is_short()
    {
        byte[] file = HandBuiltSaudio.Resident();
        file[0] = (byte)'X';

        Refusal(file).ShouldContain("'SAUD' magic");
    }

    [Fact]
    public void A_file_shorter_than_the_header_is_refused_by_length()
    {
        byte[] file = HandBuiltSaudio.Resident();
        Array.Resize(ref file, 20);

        Refusal(file).ShouldContain("shorter than the 56-byte header");
    }

    [Fact]
    public void A_file_cooked_for_another_format_version_is_refused_and_told_to_recook()
    {
        byte[] file = HandBuiltSaudio.Resident();
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(HandBuiltSaudio.VersionOffset), 99);

        string message = Refusal(file);
        message.ShouldContain("version 99");
        message.ShouldContain("recook");
    }

    [Fact]
    public void A_sound_from_the_first_format_version_is_told_to_recook_and_not_that_it_is_short()
    {
        // A version 1 header is 48 bytes, so a very short version 1 sound is under 56.
        byte[] file = HandBuiltSaudio.Resident(frames: 1);
        Array.Resize(ref file, 50);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(HandBuiltSaudio.VersionOffset), 1);

        string message = Refusal(file);
        message.ShouldContain("version 1 ");
        message.ShouldContain("recook");
    }

    [Fact]
    public void A_codec_the_format_reserves_is_named_rather_than_lumped_in_with_a_bad_byte()
    {
        byte[] file = HandBuiltSaudio.Resident();
        file[HandBuiltSaudio.CodecOffset] = (byte)SaudioCodec.Opus;

        string message = Refusal(file);
        message.ShouldContain("Opus");
        message.ShouldContain("PcmS16");
    }

    [Fact]
    public void A_codec_byte_the_format_does_not_define_is_refused()
    {
        byte[] file = HandBuiltSaudio.Resident();
        file[HandBuiltSaudio.CodecOffset] = 200;

        Refusal(file).ShouldContain("codec byte is 200");
    }

    [Fact]
    public void A_flag_bit_this_build_does_not_define_is_refused_rather_than_masked_off()
    {
        byte[] file = HandBuiltSaudio.Resident();
        file[HandBuiltSaudio.FlagsOffset] = 0x80;

        Refusal(file).ShouldContain("flag byte is 0x80");
    }

    [Fact]
    public void A_sample_rate_of_zero_is_refused()
    {
        byte[] file = HandBuiltSaudio.Resident();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(HandBuiltSaudio.SampleRateOffset), 0);

        Refusal(file).ShouldContain("sample rate is 0");
    }

    [Fact]
    public void A_sample_rate_nothing_could_have_written_is_refused()
    {
        byte[] file = HandBuiltSaudio.Resident();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(HandBuiltSaudio.SampleRateOffset), 5_000_000);

        Refusal(file).ShouldContain("sample rate is 5000000");
    }

    [Fact]
    public void A_channel_count_OpenAL_has_no_PCM16_buffer_for_is_refused()
    {
        byte[] file = HandBuiltSaudio.Resident();
        file[HandBuiltSaudio.ChannelsOffset] = 3;

        Refusal(file).ShouldContain("declares 3 channels");
    }

    [Fact]
    public void A_channel_layout_that_disagrees_with_the_channel_count_is_refused()
    {
        byte[] file = HandBuiltSaudio.Resident(channels: 2);
        file[HandBuiltSaudio.ChannelLayoutOffset] = 0;

        Refusal(file).ShouldContain("do not describe the same frame");
    }

    [Fact]
    public void A_sound_with_no_frames_in_it_is_refused()
    {
        byte[] file = HandBuiltSaudio.Resident();
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(HandBuiltSaudio.FrameCountOffset), 0);

        Refusal(file).ShouldContain("declares 0 sample frames");
    }

    [Fact]
    public void A_frame_count_whose_byte_length_would_not_fit_an_int_is_refused()
    {
        // Wrapped, this becomes a small plausible length and the read runs off
        // the end of a mapped view.
        byte[] file = HandBuiltSaudio.Resident();
        BinaryPrimitives.WriteUInt64LittleEndian(
            file.AsSpan(HandBuiltSaudio.FrameCountOffset), (ulong)SaudioFormat.MaxFrameCount + 1);

        Refusal(file).ShouldContain("sample frames");
    }

    [Fact]
    public void A_u64_field_above_what_a_long_holds_is_refused_rather_than_cast_negative()
    {
        // A negative frame count would pass every upper bound.
        byte[] file = HandBuiltSaudio.Resident();
        BinaryPrimitives.WriteUInt64LittleEndian(
            file.AsSpan(HandBuiltSaudio.FrameCountOffset), ulong.MaxValue);

        Refusal(file).ShouldContain("FrameCount");
    }

    [Fact]
    public void A_payload_that_runs_off_the_end_of_the_file_is_refused()
    {
        byte[] file = HandBuiltSaudio.Resident(frames: 16);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(HandBuiltSaudio.FrameCountOffset), 4096);

        Refusal(file).ShouldContain("and the file is");
    }

    [Fact]
    public void A_payload_starting_before_the_header_ends_is_refused()
    {
        byte[] file = HandBuiltSaudio.Resident();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(HandBuiltSaudio.DataOffsetOffset), 8);

        Refusal(file).ShouldContain("at offset 8");
    }

    [Fact]
    public void A_payload_at_an_odd_offset_is_refused_rather_than_read_across_sample_boundaries()
    {
        byte[] file = HandBuiltSaudio.Resident(frames: 8);
        Array.Resize(ref file, file.Length + 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(HandBuiltSaudio.DataOffsetOffset), SaudioFormat.HeaderSize + 1);

        Refusal(file).ShouldContain("not a multiple of the 2-byte PCM16 sample");
    }

    [Fact]
    public void A_loop_start_with_no_loop_end_is_refused()
    {
        byte[] file = HandBuiltSaudio.Resident(frames: 16);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(HandBuiltSaudio.LoopStartOffset), 4);

        Refusal(file).ShouldContain("no loop end");
    }

    [Fact]
    public void An_empty_loop_region_is_refused_rather_than_hung_on()
    {
        // End equal to start would hang the fill loop.
        byte[] file = HandBuiltSaudio.Resident(frames: 16, loopStart: 4, loopEnd: 8);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(HandBuiltSaudio.LoopEndOffset), 4);

        Refusal(file).ShouldContain("contains no frames");
    }

    [Fact]
    public void A_loop_that_ends_past_the_sound_is_refused()
    {
        byte[] file = HandBuiltSaudio.Resident(frames: 16, loopStart: 4, loopEnd: 12);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(HandBuiltSaudio.LoopEndOffset), 64);

        Refusal(file).ShouldContain("loop ends at frame 64");
    }

    [Fact]
    public void A_streaming_flag_with_no_seek_table_is_refused()
    {
        byte[] file = HandBuiltSaudio.Resident(flags: 1);

        Refusal(file).ShouldContain("flagged streaming and carries no seek table");
    }

    [Fact]
    public void A_seek_table_on_a_file_that_is_not_flagged_streaming_is_refused()
    {
        byte[] file = HandBuiltSaudio.Streaming();
        file[HandBuiltSaudio.FlagsOffset] = 0;

        Refusal(file).ShouldContain("is not flagged streaming");
    }

    [Fact]
    public void A_seek_stride_of_zero_frames_is_refused()
    {
        byte[] file = HandBuiltSaudio.Streaming();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(SaudioFormat.HeaderSize + 4), 0);

        Refusal(file).ShouldContain("0 frames per entry");
    }

    [Fact]
    public void A_seek_table_that_does_not_cover_the_whole_sound_is_refused()
    {
        byte[] file = HandBuiltSaudio.Streaming(frames: 64, framesPerEntry: 16);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(SaudioFormat.HeaderSize), 3);

        Refusal(file).ShouldContain("needs 4");
    }

    [Fact]
    public void A_seek_table_and_a_payload_that_overlap_are_refused()
    {
        // Payload pulled back into the table. Every length and entry is still
        // consistent on its own.
        byte[] file = HandBuiltSaudio.Streaming(frames: 64, channels: 1, framesPerEntry: 16);
        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(HandBuiltSaudio.DataOffsetOffset), SaudioFormat.HeaderSize + 8);

        Refusal(file).ShouldContain("the two overlap");
    }

    [Fact]
    public void A_seek_entry_outside_the_payload_is_refused()
    {
        byte[] file = HandBuiltSaudio.Streaming(frames: 64, framesPerEntry: 16);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(HandBuiltSaudio.SeekEntryOffset(2)), 4);

        Refusal(file).ShouldContain("outside the payload");
    }

    [Fact]
    public void A_seek_table_that_does_not_ascend_is_refused()
    {
        byte[] file = HandBuiltSaudio.Streaming(frames: 64, channels: 1, framesPerEntry: 16);

        // Entry 2 gets entry 1's offset: still in the payload and frame-aligned.
        BinaryPrimitives.WriteUInt64LittleEndian(
            file.AsSpan(HandBuiltSaudio.SeekEntryOffset(2)),
            BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(HandBuiltSaudio.SeekEntryOffset(1))));

        Refusal(file).ShouldContain("must ascend");
    }

    [Fact]
    public void A_seek_entry_landing_mid_frame_is_refused()
    {
        // Landing between a frame's channels swaps left and right for the rest of the stream.
        byte[] file = HandBuiltSaudio.Streaming(frames: 64, channels: 2, framesPerEntry: 16);
        BinaryPrimitives.WriteUInt64LittleEndian(
            file.AsSpan(HandBuiltSaudio.SeekEntryOffset(1)),
            BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(HandBuiltSaudio.SeekEntryOffset(1))) + 2);

        Refusal(file).ShouldContain("whole number of 2-channel frames");
    }

    [Fact]
    public void An_authored_sound_maps_to_the_cooked_path_beside_it()
    {
        AudioContentPath.CookedPathFor("Sounds/door_open.wav").ShouldBe("Sounds/door_open.saudio");
        AudioContentPath.IsCooked("Sounds/door_open.saudio").ShouldBeTrue();
        AudioContentPath.IsCooked("Sounds/door_open.wav").ShouldBeFalse();

        AudioContentPath.CookedPathFor("Sounds/door_open.saudio").ShouldBe("Sounds/door_open.saudio");
    }

    private static string Refusal(byte[] file) =>
        Should.Throw<SaudioFormatException>(() => SaudioReader.Read(file, Source)).Message;
}
