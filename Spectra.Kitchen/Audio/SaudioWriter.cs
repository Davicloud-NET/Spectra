using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Audio;
using System;
using System.Buffers.Binary;
using System.Text;

namespace Spectra.Kitchen.Audio;

/// <summary>
/// Writes a <c>.saudio</c>: the <see cref="SaudioFormat"/> header, an optional
/// seek table, an optional section table with its sections, then interleaved
/// PCM16.
/// </summary>
// Offsets come from SaudioFormat only. Reserved bytes must stay zero or two
// cooks stop being byte-identical.
public static class SaudioWriter
{
    /// <summary>
    /// Writes one cooked sound. Throws <see cref="ArgumentException"/> for input
    /// the format cannot hold.
    /// </summary>
    /// <param name="format">Rate and channel count of <paramref name="pcm"/>.</param>
    /// <param name="pcm">Interleaved PCM16. Length must be a whole number of frames.</param>
    /// <param name="loop">The region to repeat, or <see cref="LoopRegion.None"/>.</param>
    /// <param name="positional">Whether the sound is meant to be placed in the world.</param>
    /// <param name="framesPerSeekEntry">
    /// Frames between seek points. Zero writes no seek table and no streaming flag.
    /// </param>
    /// <param name="markers">
    /// Named moments in the sound, in any order. None writes no section table.
    /// </param>
    public static byte[] Write(
        AudioFormat format,
        ReadOnlySpan<short> pcm,
        LoopRegion loop,
        bool positional,
        int framesPerSeekEntry = 0,
        ReadOnlySpan<AudioMarker> markers = default)
    {
        long frames = RequireWritable(format, pcm, loop, framesPerSeekEntry);
        byte[] markerSection = EncodeMarkers(markers, frames);

        bool streaming = framesPerSeekEntry > 0;
        long entryCount = streaming ? (frames + framesPerSeekEntry - 1) / framesPerSeekEntry : 0;
        int seekTableBytes = streaming
            ? SaudioFormat.SeekTableHeaderSize + checked((int)entryCount) * SaudioFormat.SeekTableEntrySize
            : 0;

        bool sections = markerSection.Length > 0;
        int sectionTableOffset = SaudioFormat.HeaderSize + seekTableBytes;
        int sectionTableBytes = sections ? SaudioFormat.SectionTableHeaderSize + SaudioFormat.SectionEntrySize : 0;
        int markerOffset = sectionTableOffset + sectionTableBytes;

        // The payload is read in place as shorts, so it starts on an even byte.
        // A marker name can leave the sections an odd length.
        int dataOffset = checked(markerOffset + markerSection.Length);
        dataOffset += dataOffset & 1;

        int payloadBytes = checked((int)SaudioFormat.PcmByteLength(frames, format.Channels));
        var file = new byte[checked(dataOffset + payloadBytes)];

        SaudioFlags flags = SaudioFlags.None;
        if (streaming) flags |= SaudioFlags.Streaming;

        // Mono only: OpenAL never spatialises a stereo buffer.
        if (positional && format.Channels == 1) flags |= SaudioFlags.PositionalIntent;

        WriteShape(file, format, flags, frames, loop);

        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(SaudioFormat.SeekTableOffsetOffset),
            streaming ? SaudioFormat.HeaderSize : 0u);

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(SaudioFormat.DataOffsetOffset), (uint)dataOffset);

        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(SaudioFormat.SectionTableOffsetOffset),
            sections ? (uint)sectionTableOffset : 0u);

        if (streaming) WriteSeekTable(file, format, dataOffset, entryCount, framesPerSeekEntry);

        if (sections)
        {
            Span<byte> table = file.AsSpan(sectionTableOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(table[4..], SaudioFormat.MarkerSection);
            BinaryPrimitives.WriteUInt32LittleEndian(table[8..], (uint)markerOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(table[12..], (uint)markerSection.Length);
            markerSection.CopyTo(file.AsSpan(markerOffset));
        }

        // Per sample, not a span cast, so the file is little-endian on any host.
        Span<byte> payload = file.AsSpan(dataOffset, payloadBytes);
        for (int i = 0; i < pcm.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(payload[(i * 2)..], pcm[i]);

        return file;
    }

    // Returns the length in frames.
    private static long RequireWritable(
        AudioFormat format, ReadOnlySpan<short> pcm, LoopRegion loop, int framesPerSeekEntry)
    {
        if (pcm.Length % format.Channels != 0)
        {
            throw new ArgumentException(
                $"{pcm.Length} interleaved samples is not a whole number of {format.Channels}-channel frames.",
                nameof(pcm));
        }

        long frames = pcm.Length / format.Channels;
        if (frames <= 0 || frames > SaudioFormat.MaxFrameCount)
        {
            throw new ArgumentException(
                $"A .saudio holds between 1 and {SaudioFormat.MaxFrameCount} sample frames, not {frames}.",
                nameof(pcm));
        }

        if (loop.IsLooping && loop.EndFrame > frames)
        {
            throw new ArgumentException(
                $"The loop ends at frame {loop.EndFrame} and the sound is {frames} frames long.",
                nameof(loop));
        }

        if (framesPerSeekEntry < 0)
        {
            throw new ArgumentException(
                "A seek stride is a positive number of frames, or zero for no seek table.",
                nameof(framesPerSeekEntry));
        }

        return frames;
    }

    // The header fields that describe the sound. The offsets are the caller's.
    private static void WriteShape(
        Span<byte> file, AudioFormat format, SaudioFlags flags, long frames, LoopRegion loop)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(file[SaudioFormat.MagicOffset..], SaudioFormat.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(
            file[SaudioFormat.VersionOffset..], (ushort)EngineInfo.AudioFormatVersion);

        file[SaudioFormat.CodecOffset] = (byte)SaudioCodec.PcmS16;
        file[SaudioFormat.FlagsOffset] = (byte)flags;

        BinaryPrimitives.WriteUInt32LittleEndian(file[SaudioFormat.SampleRateOffset..], (uint)format.SampleRate);

        file[SaudioFormat.ChannelsOffset] = (byte)format.Channels;
        file[SaudioFormat.ChannelLayoutOffset] = (byte)SaudioFormat.LayoutFor(format.Channels);

        // ReservedOffset and HeaderPaddingOffset stay zero.

        BinaryPrimitives.WriteUInt64LittleEndian(file[SaudioFormat.FrameCountOffset..], (ulong)frames);
        BinaryPrimitives.WriteUInt64LittleEndian(
            file[SaudioFormat.LoopStartOffset..], (ulong)(loop.IsLooping ? loop.StartFrame : 0));
        BinaryPrimitives.WriteUInt64LittleEndian(
            file[SaudioFormat.LoopEndOffset..], (ulong)(loop.IsLooping ? loop.EndFrame : 0));
    }

    private static void WriteSeekTable(
        Span<byte> file, AudioFormat format, int dataOffset, long entryCount, int framesPerSeekEntry)
    {
        int at = SaudioFormat.HeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(file[at..], (uint)entryCount);
        BinaryPrimitives.WriteUInt32LittleEndian(file[(at + 4)..], (uint)framesPerSeekEntry);

        int frameBytes = format.Channels * SaudioFormat.PcmBytesPerSample;
        for (long entry = 0; entry < entryCount; entry++)
        {
            int entryAt = at + SaudioFormat.SeekTableHeaderSize +
                checked((int)(entry * SaudioFormat.SeekTableEntrySize));

            long offset = dataOffset + (entry * framesPerSeekEntry * frameBytes);
            BinaryPrimitives.WriteUInt64LittleEndian(file[entryAt..], (ulong)offset);
        }
    }

    // The body of the MARK section, or empty for no markers. Sorted by frame
    // and then by name, so the bytes do not depend on the order the source
    // listed them in.
    private static byte[] EncodeMarkers(ReadOnlySpan<AudioMarker> markers, long frames)
    {
        if (markers.IsEmpty) return [];

        AudioMarker[] sorted = markers.ToArray();
        Array.Sort(sorted, static (a, b) =>
        {
            int byFrame = a.Frame.CompareTo(b.Frame);
            return byFrame != 0 ? byFrame : string.CompareOrdinal(a.Name, b.Name);
        });

        int size = SaudioFormat.MarkerCountSize;
        foreach (AudioMarker marker in sorted)
            size = checked(size + SaudioFormat.MarkerRecordHeaderSize + RequireNameBytes(marker, frames));

        var section = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(section, (uint)sorted.Length);

        int at = SaudioFormat.MarkerCountSize;
        foreach (AudioMarker marker in sorted)
        {
            int nameAt = at + SaudioFormat.MarkerRecordHeaderSize;
            int nameBytes = Encoding.UTF8.GetBytes(marker.Name, section.AsSpan(nameAt));

            BinaryPrimitives.WriteUInt64LittleEndian(section.AsSpan(at), (ulong)marker.Frame);
            BinaryPrimitives.WriteUInt16LittleEndian(section.AsSpan(at + 8), (ushort)nameBytes);
            at = nameAt + nameBytes;
        }

        return section;
    }

    // Returns the name's length in UTF-8 bytes.
    private static int RequireNameBytes(AudioMarker marker, long frames)
    {
        if (marker.Frame < 0 || marker.Frame > frames)
        {
            throw new ArgumentException(
                $"The marker '{marker.Name}' is at frame {marker.Frame} and the sound is {frames} frames long.",
                "markers");
        }

        if (string.IsNullOrEmpty(marker.Name))
            throw new ArgumentException($"The marker at frame {marker.Frame} has no name.", "markers");

        int bytes = Encoding.UTF8.GetByteCount(marker.Name);
        if (bytes > SaudioFormat.MaxMarkerNameBytes)
        {
            throw new ArgumentException(
                $"The marker at frame {marker.Frame} has a {bytes}-byte name and a marker name holds " +
                $"{SaudioFormat.MaxMarkerNameBytes}.",
                "markers");
        }

        return bytes;
    }
}
