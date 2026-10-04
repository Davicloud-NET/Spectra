using System;

namespace SpectraEngine.Core.Assets.Audio;

/// <summary>
/// How the samples in a <c>.saudio</c> payload are encoded.
/// </summary>
// The values are written to disk. Append only, never renumber.
public enum SaudioCodec : byte
{
    /// <summary>Interleaved little-endian signed 16-bit PCM. The only codec v1 carries.</summary>
    PcmS16 = 0,

    /// <summary>Reserved: Vorbis in an Ogg stream.</summary>
    Vorbis = 1,

    /// <summary>Reserved: Opus in an Ogg stream.</summary>
    Opus = 2,

    /// <summary>Reserved: IMA ADPCM.</summary>
    ImaAdpcm = 3,
}

/// <summary>
/// What a <c>.saudio</c> declares about how it is meant to be played. These are
/// hints; a file plays whichever way they are set.
/// </summary>
[Flags]
public enum SaudioFlags : byte
{
    /// <summary>Resident, non-positional, no seek table.</summary>
    None = 0,

    /// <summary>
    /// The sound is long enough to be fed through a buffer queue rather than
    /// held whole. A file carrying this bit carries a seek table.
    /// </summary>
    Streaming = 1 << 0,

    /// <summary>
    /// The sound is meant to be placed in the world. Only set on mono files:
    /// OpenAL does not spatialise a stereo buffer.
    /// </summary>
    PositionalIntent = 1 << 1,
}

/// <summary>
/// How the channels in a frame are laid out.
/// </summary>
// Separate from the channel count: surround counts have more than one arrangement.
public enum SaudioChannelLayout : byte
{
    /// <summary>One channel.</summary>
    Mono = 0,

    /// <summary>Two interleaved channels, left then right.</summary>
    Stereo = 1,
}

/// <summary>
/// The byte layout of a <c>.saudio</c> file, shared by the cook rule that writes
/// one and the reader. Loop points and the seek stride are in sample frames.
/// </summary>
public static class SaudioFormat
{
    /// <summary>The cooked extension, dot included.</summary>
    public const string FileExtension = ".saudio";

    /// <summary>
    /// File magic, <c>"SAUD"</c>. Stored as a little-endian <see cref="uint"/>,
    /// so the first four bytes on disk read <c>S A U D</c> in a hex dump.
    /// </summary>
    public const uint Magic = 'S' | ('A' << 8) | ('U' << 16) | ((uint)'D' << 24);

    /// <summary>Bytes in the header, which lives at offset 0.</summary>
    public const int HeaderSize = 48;

    /// <summary><see cref="Magic"/>, four bytes.</summary>
    public const int MagicOffset = 0x00;

    /// <summary>The format version, <c>u16</c>.</summary>
    public const int VersionOffset = 0x04;

    /// <summary><see cref="SaudioCodec"/>, one byte.</summary>
    public const int CodecOffset = 0x06;

    /// <summary><see cref="SaudioFlags"/>, one byte.</summary>
    public const int FlagsOffset = 0x07;

    /// <summary>Sample frames per second, <c>u32</c>.</summary>
    public const int SampleRateOffset = 0x08;

    /// <summary>Interleaved channels per frame, one byte.</summary>
    public const int ChannelsOffset = 0x0C;

    /// <summary><see cref="SaudioChannelLayout"/>, one byte.</summary>
    public const int ChannelLayoutOffset = 0x0D;

    /// <summary>Two reserved bytes, written zero.</summary>
    public const int ReservedOffset = 0x0E;

    /// <summary>Total decoded sample frames, <c>u64</c>.</summary>
    public const int FrameCountOffset = 0x10;

    /// <summary>First frame of the loop region, <c>u64</c>.</summary>
    public const int LoopStartOffset = 0x18;

    /// <summary>One past the last frame of the loop region, <c>u64</c>; 0 means no loop.</summary>
    public const int LoopEndOffset = 0x20;

    /// <summary>Byte offset of the seek table, <c>u32</c>; 0 means none.</summary>
    public const int SeekTableOffsetOffset = 0x28;

    /// <summary>Byte offset of the payload, <c>u32</c>.</summary>
    public const int DataOffsetOffset = 0x2C;

    /// <summary>
    /// Bytes in the seek table's own header: <c>u32 entryCount</c> then
    /// <c>u32 framesPerEntry</c>.
    /// </summary>
    public const int SeekTableHeaderSize = 8;

    /// <summary>Bytes in one seek-table entry: a <c>u64</c> byte offset.</summary>
    public const int SeekTableEntrySize = 8;

    /// <summary>Bytes one sample of one channel occupies under <see cref="SaudioCodec.PcmS16"/>.</summary>
    public const int PcmBytesPerSample = sizeof(short);

    /// <summary>
    /// The largest frame count the reader accepts, so every derived byte size
    /// fits in an <see cref="int"/>. About three hours of 48 kHz stereo.
    /// </summary>
    public const long MaxFrameCount = int.MaxValue / 4;

    /// <summary>Bytes <paramref name="frames"/> of PCM16 occupy at <paramref name="channels"/> channels.</summary>
    public static long PcmByteLength(long frames, int channels) =>
        frames * channels * PcmBytesPerSample;

    /// <summary>The channel layout <paramref name="channels"/> interleaved channels means.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Neither mono nor stereo.</exception>
    public static SaudioChannelLayout LayoutFor(int channels) => channels switch
    {
        1 => SaudioChannelLayout.Mono,
        2 => SaudioChannelLayout.Stereo,
        _ => throw new ArgumentOutOfRangeException(
            nameof(channels), channels, "A .saudio carries mono or stereo; 5.1 and 7.1 are reserved."),
    };

    /// <summary>Interleaved channels <paramref name="layout"/> describes, or 0 for a layout this build has no count for.</summary>
    public static int ChannelsFor(SaudioChannelLayout layout) => layout switch
    {
        SaudioChannelLayout.Mono => 1,
        SaudioChannelLayout.Stereo => 2,
        _ => 0,
    };
}
