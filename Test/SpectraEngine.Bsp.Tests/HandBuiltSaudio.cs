using System;
using System.Buffers.Binary;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

// Builds valid .saudio bytes from the format spec. Offsets are literals, not
// SaudioFormat constants, so the reader is not checked against itself.
// Tests patch one field each to make an invalid file.
internal static class HandBuiltSaudio
{
    public const int HeaderSize = 56;
    public const int SeekTableHeaderSize = 8;
    public const int SeekTableEntrySize = 8;
    public const int SectionTableHeaderSize = 4;
    public const int SectionEntrySize = 12;

    public const int MagicOffset = 0x00;
    public const int VersionOffset = 0x04;
    public const int CodecOffset = 0x06;
    public const int FlagsOffset = 0x07;
    public const int SampleRateOffset = 0x08;
    public const int ChannelsOffset = 0x0C;
    public const int ChannelLayoutOffset = 0x0D;
    public const int FrameCountOffset = 0x10;
    public const int LoopStartOffset = 0x18;
    public const int LoopEndOffset = 0x20;
    public const int SeekTableOffsetOffset = 0x28;
    public const int DataOffsetOffset = 0x2C;
    public const int SectionTableOffsetOffset = 0x30;

    // "SAUD" little-endian.
    public const uint Magic = 'S' | ('A' << 8) | ('U' << 16) | ((uint)'D' << 24);

    // "MARK" little-endian.
    public const uint MarkerTag = 'M' | ('A' << 8) | ('R' << 16) | ((uint)'K' << 24);

    // No streaming flag, no seek table, no sections.
    public static byte[] Resident(
        int frames = 16,
        int channels = 1,
        int sampleRate = 48_000,
        long loopStart = 0,
        long loopEnd = 0,
        byte flags = 0)
    {
        int payload = frames * channels * 2;
        var file = new byte[HeaderSize + payload];

        WriteHeader(file, frames, channels, sampleRate, loopStart, loopEnd, flags, 0, HeaderSize);
        FillPayload(file, HeaderSize, payload);
        return file;
    }

    // Streaming flag plus a matching seek table.
    public static byte[] Streaming(
        int frames = 64,
        int channels = 1,
        int sampleRate = 48_000,
        int framesPerEntry = 16)
    {
        int entries = (frames + framesPerEntry - 1) / framesPerEntry;
        int tableBytes = SeekTableHeaderSize + entries * SeekTableEntrySize;
        int dataOffset = HeaderSize + tableBytes;
        int payload = frames * channels * 2;

        var file = new byte[dataOffset + payload];
        WriteHeader(file, frames, channels, sampleRate, 0, 0, flags: 1, HeaderSize, dataOffset);

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(HeaderSize), (uint)entries);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(HeaderSize + 4), (uint)framesPerEntry);

        int frameBytes = channels * 2;
        for (int entry = 0; entry < entries; entry++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                file.AsSpan(HeaderSize + SeekTableHeaderSize + entry * SeekTableEntrySize),
                (ulong)(dataOffset + (long)entry * framesPerEntry * frameBytes));
        }

        FillPayload(file, dataOffset, payload);
        return file;
    }

    public static int SeekEntryOffset(int index) =>
        HeaderSize + SeekTableHeaderSize + index * SeekTableEntrySize;

    // A resident sound with a section table and its sections after the payload.
    // The cook puts them before it. The reader has to take either.
    public static byte[] WithSections(int frames, params (uint Tag, byte[] Body)[] sections)
    {
        byte[] sound = Resident(frames);

        int tableOffset = sound.Length;
        int bodyOffset = tableOffset + SectionTableHeaderSize + sections.Length * SectionEntrySize;

        int total = bodyOffset;
        foreach ((uint _, byte[] body) in sections) total += body.Length;

        var file = new byte[total];
        sound.CopyTo(file, 0);

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(SectionTableOffsetOffset), (uint)tableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(tableOffset), (uint)sections.Length);

        for (int i = 0; i < sections.Length; i++)
        {
            int entry = SectionEntryOffset(file, i);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(entry), sections[i].Tag);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(entry + 4), (uint)bodyOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(entry + 8), (uint)sections[i].Body.Length);

            sections[i].Body.CopyTo(file, bodyOffset);
            bodyOffset += sections[i].Body.Length;
        }

        return file;
    }

    // Where entry `index` of the file's section table starts: tag, offset, length.
    public static int SectionEntryOffset(byte[] file, int index) =>
        (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(SectionTableOffsetOffset)) +
        SectionTableHeaderSize + index * SectionEntrySize;

    // Where section `index` of the file starts.
    public static int SectionOffset(byte[] file, int index) =>
        (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(SectionEntryOffset(file, index) + 4));

    // The body of a MARK section: a count, then frame, name length and name each.
    public static byte[] MarkerBody(params (long Frame, string Name)[] markers)
    {
        int size = 4;
        foreach ((long _, string name) in markers) size += 10 + Encoding.UTF8.GetByteCount(name);

        var body = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)markers.Length);

        int at = 4;
        foreach ((long frame, string name) in markers)
        {
            byte[] text = Encoding.UTF8.GetBytes(name);
            BinaryPrimitives.WriteUInt64LittleEndian(body.AsSpan(at), (ulong)frame);
            BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(at + 8), (ushort)text.Length);
            text.CopyTo(body, at + 10);
            at += 10 + text.Length;
        }

        return body;
    }

    private static void WriteHeader(
        byte[] file,
        int frames,
        int channels,
        int sampleRate,
        long loopStart,
        long loopEnd,
        byte flags,
        int seekTableOffset,
        int dataOffset)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(MagicOffset), Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(VersionOffset), 2);
        file[CodecOffset] = 0;                                   // PcmS16
        file[FlagsOffset] = flags;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(SampleRateOffset), (uint)sampleRate);
        file[ChannelsOffset] = (byte)channels;
        file[ChannelLayoutOffset] = (byte)(channels - 1);        // 0 Mono, 1 Stereo
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(FrameCountOffset), (ulong)frames);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(LoopStartOffset), (ulong)loopStart);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(LoopEndOffset), (ulong)loopEnd);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(SeekTableOffsetOffset), (uint)seekTableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(DataOffsetOffset), (uint)dataOffset);

        // SectionTableOffsetOffset stays 0: no sections.
    }

    // The payload's sample at an index, counted over every channel.
    public static short Sample(long index) => unchecked((short)(index * 37 - 4000));

    // A ramp, not zeros, so an unwritten buffer reads differently.
    private static void FillPayload(byte[] file, int at, int bytes)
    {
        for (int i = 0; i < bytes / 2; i++)
            BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(at + i * 2), Sample(i));
    }
}
