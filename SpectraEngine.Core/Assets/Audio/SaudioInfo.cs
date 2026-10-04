using SpectraEngine.Core.Audio;
using System;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Assets.Audio;

/// <summary>
/// What <see cref="SaudioReader"/> found in a <c>.saudio</c>: the codec, the
/// shape of the sound, its loop points and where its payload sits in the file's
/// own bytes. Holds offsets, not bytes, so it can outlive the span it was read from.
/// </summary>
public sealed class SaudioInfo
{
    internal SaudioInfo(
        int formatVersion,
        SaudioCodec codec,
        SaudioFlags flags,
        AudioFormat format,
        SaudioChannelLayout layout,
        long frameCount,
        LoopRegion loop,
        int dataOffset,
        int dataLength,
        int framesPerSeekEntry,
        long[] seekTable)
    {
        FormatVersion = formatVersion;
        Codec = codec;
        Flags = flags;
        Format = format;
        ChannelLayout = layout;
        FrameCount = frameCount;
        Loop = loop;
        DataOffset = dataOffset;
        DataLength = dataLength;
        FramesPerSeekEntry = framesPerSeekEntry;
        SeekTable = seekTable;
    }

    /// <summary>The <c>.saudio</c> version this file was cooked under.</summary>
    public int FormatVersion { get; }

    /// <summary>How the payload is encoded.</summary>
    public SaudioCodec Codec { get; }

    /// <summary>What the file declares about how it is meant to be played.</summary>
    public SaudioFlags Flags { get; }

    /// <summary>Rate and channel count, in the runtime's own vocabulary.</summary>
    public AudioFormat Format { get; }

    /// <summary>How the channels in a frame are arranged.</summary>
    public SaudioChannelLayout ChannelLayout { get; }

    /// <summary>Total decoded sample frames.</summary>
    public long FrameCount { get; }

    /// <summary>
    /// The region the sound repeats, in sample frames, or
    /// <see cref="LoopRegion.None"/>.
    /// </summary>
    public LoopRegion Loop { get; }

    /// <summary>Byte offset of the payload from the start of the file.</summary>
    public int DataOffset { get; }

    /// <summary>Bytes of payload.</summary>
    public int DataLength { get; }

    /// <summary>
    /// Sample frames between two seek entries, or 0 when there is no seek table.
    /// </summary>
    public int FramesPerSeekEntry { get; }

    /// <summary>
    /// Byte offsets of each seekable point, from the start of the file; empty
    /// for a resident sound.
    /// </summary>
    public long[] SeekTable { get; }

    /// <summary>True when the file asks to be played through a buffer queue.</summary>
    public bool IsStreaming => (Flags & SaudioFlags.Streaming) != 0;

    /// <summary>True when the file declares it is meant to be placed in the world.</summary>
    public bool IsPositional => (Flags & SaudioFlags.PositionalIntent) != 0;

    /// <summary>Seconds of audio.</summary>
    public double Duration => Format.FramesToSeconds(FrameCount);

    /// <summary>
    /// The PCM payload inside <paramref name="file"/>, as interleaved samples.
    /// A view, not a copy: keep the <c>ContentBlob</c> the bytes came from alive
    /// for as long as the span is used.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file is not PCM16.</exception>
    // The cast assumes a little-endian host; the file is little-endian.
    public ReadOnlySpan<short> Pcm(ReadOnlySpan<byte> file)
    {
        if (Codec != SaudioCodec.PcmS16)
        {
            throw new InvalidOperationException(
                $"A {Codec} payload is encoded, not PCM; decode it before asking for samples.");
        }

        return MemoryMarshal.Cast<byte, short>(file.Slice(DataOffset, DataLength));
    }
}
