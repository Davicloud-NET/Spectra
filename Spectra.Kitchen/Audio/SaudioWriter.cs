using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Audio;
using System;
using System.Buffers.Binary;

namespace Spectra.Kitchen.Audio;

/// <summary>
/// Writes a <c>.saudio</c>: the <see cref="SaudioFormat"/> header, an optional
/// seek table, then interleaved PCM16.
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
    public static byte[] Write(
        AudioFormat format,
        ReadOnlySpan<short> pcm,
        LoopRegion loop,
        bool positional,
        int framesPerSeekEntry = 0)
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

        bool streaming = framesPerSeekEntry > 0;
        long entryCount = streaming ? (frames + framesPerSeekEntry - 1) / framesPerSeekEntry : 0;
        int tableBytes = streaming
            ? SaudioFormat.SeekTableHeaderSize + checked((int)entryCount) * SaudioFormat.SeekTableEntrySize
            : 0;

        int dataOffset = SaudioFormat.HeaderSize + tableBytes;
        int payloadBytes = checked((int)SaudioFormat.PcmByteLength(frames, format.Channels));
        var file = new byte[dataOffset + payloadBytes];

        SaudioFlags flags = SaudioFlags.None;
        if (streaming) flags |= SaudioFlags.Streaming;

        // Mono only: OpenAL never spatialises a stereo buffer.
        if (positional && format.Channels == 1) flags |= SaudioFlags.PositionalIntent;

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(SaudioFormat.MagicOffset), SaudioFormat.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(
            file.AsSpan(SaudioFormat.VersionOffset), (ushort)EngineInfo.AudioFormatVersion);

        file[SaudioFormat.CodecOffset] = (byte)SaudioCodec.PcmS16;
        file[SaudioFormat.FlagsOffset] = (byte)flags;

        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(SaudioFormat.SampleRateOffset), (uint)format.SampleRate);

        file[SaudioFormat.ChannelsOffset] = (byte)format.Channels;
        file[SaudioFormat.ChannelLayoutOffset] = (byte)SaudioFormat.LayoutFor(format.Channels);

        // ReservedOffset stays zero.

        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(SaudioFormat.FrameCountOffset), (ulong)frames);
        BinaryPrimitives.WriteUInt64LittleEndian(
            file.AsSpan(SaudioFormat.LoopStartOffset), (ulong)(loop.IsLooping ? loop.StartFrame : 0));
        BinaryPrimitives.WriteUInt64LittleEndian(
            file.AsSpan(SaudioFormat.LoopEndOffset), (ulong)(loop.IsLooping ? loop.EndFrame : 0));

        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(SaudioFormat.SeekTableOffsetOffset),
            streaming ? SaudioFormat.HeaderSize : 0u);

        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(SaudioFormat.DataOffsetOffset), (uint)dataOffset);

        if (streaming)
        {
            int at = SaudioFormat.HeaderSize;
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), (uint)entryCount);
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4), (uint)framesPerSeekEntry);

            int frameBytes = format.Channels * SaudioFormat.PcmBytesPerSample;
            for (long entry = 0; entry < entryCount; entry++)
            {
                long offset = dataOffset + entry * framesPerSeekEntry * frameBytes;
                BinaryPrimitives.WriteUInt64LittleEndian(
                    file.AsSpan(at + SaudioFormat.SeekTableHeaderSize +
                        checked((int)(entry * SaudioFormat.SeekTableEntrySize))),
                    (ulong)offset);
            }
        }

        // Per sample, not a span cast, so the file is little-endian on any host.
        Span<byte> payload = file.AsSpan(dataOffset, payloadBytes);
        for (int i = 0; i < pcm.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(payload[(i * 2)..], pcm[i]);

        return file;
    }
}
