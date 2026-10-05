using SpectraEngine.Core.Audio;
using System;
using System.Buffers.Binary;

namespace SpectraEngine.Core.Assets.Audio;

/// <summary>
/// Reads a <c>.saudio</c>: the header described by <see cref="SaudioFormat"/>,
/// its optional seek table, its sections, and where the payload sits. Refuses
/// anything malformed with a message naming the file and the rule it broke.
/// </summary>
// Every offset and length is bounds-checked before use: the bytes are usually
// a mapped view, where a bad index is an access violation, not an exception.
// Takes a span, not a stream, so reading the header copies nothing.
public static class SaudioReader
{
    // Sanity bound so a garbage header is refused.
    private const uint MaxSampleRate = 768_000;

    // Unknown bits are refused, not masked.
    private const byte KnownFlags = (byte)(SaudioFlags.Streaming | SaudioFlags.PositionalIntent);

    /// <summary>
    /// Whether <paramref name="file"/> opens with the <c>SAUD</c> magic. Cheap,
    /// and says nothing about whether the rest of the file is readable.
    /// </summary>
    public static bool LooksLikeSaudio(ReadOnlySpan<byte> file) =>
        file.Length >= 4 &&
        BinaryPrimitives.ReadUInt32LittleEndian(file) == SaudioFormat.Magic;

    /// <summary>
    /// Parses <paramref name="file"/>, or refuses it saying which rule it broke.
    /// </summary>
    /// <param name="file">The whole file. Offsets in the result are relative to its start.</param>
    /// <param name="originForErrors">Path or label naming the file in messages.</param>
    /// <exception cref="SaudioFormatException">
    /// The bytes are not a <c>.saudio</c> this engine can play.
    /// </exception>
    public static SaudioInfo Read(ReadOnlySpan<byte> file, string originForErrors = "<memory>")
    {
        // Magic before length, so a non-sound file is not reported as a short one.
        if (!LooksLikeSaudio(file))
        {
            throw Refuse(
                originForErrors,
                "it does not start with the 'SAUD' magic, so it is not a cooked sound at all.");
        }

        // Version before length: a file from a version with a shorter header
        // should be told to recook, not that it is short.
        int version = file.Length >= SaudioFormat.VersionOffset + sizeof(ushort)
            ? BinaryPrimitives.ReadUInt16LittleEndian(file[SaudioFormat.VersionOffset..])
            : EngineInfo.AudioFormatVersion;

        if (version != EngineInfo.AudioFormatVersion)
        {
            throw Refuse(
                originForErrors,
                $"it was cooked for audio format version {version} and this engine reads version " +
                $"{EngineInfo.AudioFormatVersion}; recook it.");
        }

        if (file.Length < SaudioFormat.HeaderSize)
        {
            throw Refuse(
                originForErrors,
                $"it is {file.Length} bytes, which is shorter than the {SaudioFormat.HeaderSize}-byte header.");
        }

        var codec = (SaudioCodec)file[SaudioFormat.CodecOffset];
        if (codec != SaudioCodec.PcmS16)
        {
            throw Refuse(originForErrors, codec switch
            {
                SaudioCodec.Vorbis or SaudioCodec.Opus or SaudioCodec.ImaAdpcm =>
                    $"its codec is {codec} ({(byte)codec}), which the .saudio format reserves and this engine " +
                    "has no decoder for; cook it as PcmS16.",

                _ => $"its codec byte is {(byte)codec}, which is not a codec the .saudio format defines.",
            });
        }

        byte flagBits = file[SaudioFormat.FlagsOffset];
        if ((flagBits & ~KnownFlags) != 0)
        {
            throw Refuse(
                originForErrors,
                $"its flag byte is 0x{flagBits:X2} and this engine only defines 0x{KnownFlags:X2}; it was " +
                "written by a newer cooker, so recook it.");
        }

        var flags = (SaudioFlags)flagBits;

        uint sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(file[SaudioFormat.SampleRateOffset..]);
        if (sampleRate is 0 or > MaxSampleRate)
        {
            throw Refuse(
                originForErrors,
                $"its sample rate is {sampleRate}; a .saudio is between 1 and {MaxSampleRate} frames a second.");
        }

        int channels = file[SaudioFormat.ChannelsOffset];
        if (channels is not (1 or 2))
        {
            throw Refuse(
                originForErrors,
                $"it declares {channels} channels; PCM16 in OpenAL is mono or stereo, and 5.1 and 7.1 are " +
                "reserved rather than carried.");
        }

        var layout = (SaudioChannelLayout)file[SaudioFormat.ChannelLayoutOffset];
        if (SaudioFormat.ChannelsFor(layout) != channels)
        {
            throw Refuse(
                originForErrors,
                $"it declares channel layout {layout} ({(byte)layout}) and {channels} channels, which do not " +
                "describe the same frame.");
        }

        long frameCount = ReadI64(file, SaudioFormat.FrameCountOffset, originForErrors, "FrameCount");
        if (frameCount <= 0 || frameCount > SaudioFormat.MaxFrameCount)
        {
            throw Refuse(
                originForErrors,
                $"it declares {frameCount} sample frames; a .saudio holds between 1 and " +
                $"{SaudioFormat.MaxFrameCount}.");
        }

        long loopStart = ReadI64(file, SaudioFormat.LoopStartOffset, originForErrors, "LoopStart");
        long loopEnd = ReadI64(file, SaudioFormat.LoopEndOffset, originForErrors, "LoopEnd");
        LoopRegion loop = ReadLoop(loopStart, loopEnd, frameCount, originForErrors);

        uint seekTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(file[SaudioFormat.SeekTableOffsetOffset..]);
        uint dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(file[SaudioFormat.DataOffsetOffset..]);

        long payloadBytes = SaudioFormat.PcmByteLength(frameCount, channels);
        if (dataOffset < SaudioFormat.HeaderSize || dataOffset + payloadBytes > file.Length)
        {
            throw Refuse(
                originForErrors,
                $"its {frameCount}-frame payload occupies {payloadBytes} bytes at offset {dataOffset}, and the " +
                $"file is {file.Length} bytes.");
        }

        if (dataOffset % SaudioFormat.PcmBytesPerSample != 0)
        {
            // The payload is read in place as shorts, so it must be aligned.
            throw Refuse(
                originForErrors,
                $"its payload starts at byte {dataOffset}, which is not a multiple of the " +
                $"{SaudioFormat.PcmBytesPerSample}-byte PCM16 sample it is read as.");
        }

        (int framesPerEntry, long[] seekTable) = ReadSeekTable(
            file, flags, seekTableOffset, (int)dataOffset, payloadBytes, frameCount, channels, originForErrors);

        uint sectionTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(
            file[SaudioFormat.SectionTableOffsetOffset..]);

        AudioMarker[] markers = SaudioSectionReader.Read(
            file, sectionTableOffset, frameCount, originForErrors, out int skippedSections);

        return new SaudioInfo(
            version,
            codec,
            flags,
            new AudioFormat((int)sampleRate, channels),
            layout,
            frameCount,
            loop,
            (int)dataOffset,
            (int)payloadBytes,
            framesPerEntry,
            seekTable,
            markers,
            skippedSections);
    }

    // An empty loop region would hang the fill loop; one past the end reads
    // frames that are not there.
    private static LoopRegion ReadLoop(long start, long end, long frameCount, string origin)
    {
        if (end == 0)
        {
            if (start != 0)
            {
                throw Refuse(
                    origin,
                    $"it declares a loop starting at frame {start} and no loop end, which is a loop nothing " +
                    "can play; a sound with no loop writes 0 for both.");
            }

            return LoopRegion.None;
        }

        if (start < 0 || end <= start)
        {
            throw Refuse(
                origin,
                $"its loop is [{start}, {end}), which contains no frames; a loop region holds at least one.");
        }

        if (end > frameCount)
        {
            throw Refuse(
                origin,
                $"its loop ends at frame {end} and the sound is {frameCount} frames long.");
        }

        return new LoopRegion(start, end);
    }

    // The streaming flag and the seek table must come together.
    private static (int FramesPerEntry, long[] Table) ReadSeekTable(
        ReadOnlySpan<byte> file,
        SaudioFlags flags,
        uint tableOffset,
        int dataOffset,
        long payloadBytes,
        long frameCount,
        int channels,
        string origin)
    {
        bool streaming = (flags & SaudioFlags.Streaming) != 0;

        if (tableOffset == 0)
        {
            if (streaming)
            {
                throw Refuse(
                    origin,
                    "it is flagged streaming and carries no seek table, so starting it part-way through would " +
                    "have to decode from the beginning.");
            }

            return (0, []);
        }

        if (!streaming)
        {
            throw Refuse(
                origin,
                $"it carries a seek table at byte {tableOffset} and is not flagged streaming; a seek table is " +
                "streaming's, and one nothing reads is a claim about the file that is not true.");
        }

        if (tableOffset < SaudioFormat.HeaderSize ||
            tableOffset + SaudioFormat.SeekTableHeaderSize > file.Length)
        {
            throw Refuse(
                origin,
                $"its seek table starts at byte {tableOffset}, which is outside the {file.Length}-byte file.");
        }

        uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(file[(int)tableOffset..]);
        uint framesPerEntry = BinaryPrimitives.ReadUInt32LittleEndian(
            file[((int)tableOffset + 4)..]);

        if (framesPerEntry == 0)
        {
            throw Refuse(
                origin,
                "its seek table declares 0 frames per entry, so no entry describes any part of the sound.");
        }

        long expected = (frameCount + framesPerEntry - 1) / framesPerEntry;
        if (entryCount != expected)
        {
            throw Refuse(
                origin,
                $"its seek table has {entryCount} entries and a {frameCount}-frame sound at {framesPerEntry} " +
                $"frames an entry needs {expected}.");
        }

        long tableBytes = SaudioFormat.SeekTableHeaderSize + (long)entryCount * SaudioFormat.SeekTableEntrySize;
        if (tableOffset + tableBytes > dataOffset)
        {
            throw Refuse(
                origin,
                $"its seek table needs {tableBytes} bytes at offset {tableOffset} and the payload starts at " +
                $"{dataOffset}, so the two overlap.");
        }

        var table = new long[entryCount];
        long payloadEnd = dataOffset + payloadBytes;
        long previous = long.MinValue;

        for (int i = 0; i < table.Length; i++)
        {
            int at = (int)tableOffset + SaudioFormat.SeekTableHeaderSize + i * SaudioFormat.SeekTableEntrySize;
            long offset = ReadI64(file, at, origin, $"seek entry {i}");

            if (offset < dataOffset || offset >= payloadEnd)
            {
                throw Refuse(
                    origin,
                    $"its seek entry {i} points at byte {offset}, which is outside the payload " +
                    $"[{dataOffset}, {payloadEnd}).");
            }

            if (offset <= previous)
            {
                throw Refuse(
                    origin,
                    $"its seek entry {i} is at byte {offset} and entry {i - 1} is at {previous}; the table must " +
                    "ascend, or a seek walks backwards through the sound.");
            }

            if ((offset - dataOffset) % (channels * SaudioFormat.PcmBytesPerSample) != 0)
            {
                // A seek landing mid-frame swaps the channels from there on.
                throw Refuse(
                    origin,
                    $"its seek entry {i} is at byte {offset}, which is not a whole number of " +
                    $"{channels}-channel frames from the payload at {dataOffset}.");
            }

            previous = offset;
            table[i] = offset;
        }

        return ((int)framesPerEntry, table);
    }

    // A u64 cast straight to long can go negative and slip past the bounds checks.
    private static long ReadI64(ReadOnlySpan<byte> file, int at, string origin, string field)
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(file[at..]);
        if (value > long.MaxValue)
            throw Refuse(origin, $"its {field} is {value}, which is not a number of frames anything wrote.");

        return (long)value;
    }

    internal static SaudioFormatException Refuse(string origin, string because) =>
        new($"'{origin}' is not a .saudio this engine can read: {because}");
}
