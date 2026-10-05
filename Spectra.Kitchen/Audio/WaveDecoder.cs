using SpectraEngine.Core.Audio;
using System;
using System.Buffers.Binary;
using System.IO;

namespace Spectra.Kitchen.Audio;

/// <summary>
/// Reads a RIFF/WAVE file into interleaved PCM16 at the file's own rate.
/// Loop points come from the <c>smpl</c> chunk, markers from the cue points
/// and their labels.
/// </summary>
public static class WaveDecoder
{
    // WAVE_FORMAT tags. Extensible wraps one of the other two in a GUID.
    private const ushort FormatPcm = 0x0001;
    private const ushort FormatIeeeFloat = 0x0003;
    private const ushort FormatExtensible = 0xFFFE;

    // The runtime only loops forward.
    private const uint LoopTypeForward = 0;

    /// <summary>
    /// Decodes a whole WAV file. Throws <see cref="InvalidDataException"/> naming
    /// what was wrong when it cannot.
    /// </summary>
    /// <param name="file">The whole file.</param>
    /// <param name="originForErrors">Path or label naming the file in messages.</param>
    public static DecodedAudio Decode(ReadOnlySpan<byte> file, string originForErrors = "<memory>")
    {
        if (file.Length < 12 || !Matches(file, 0, "RIFF") || !Matches(file, 8, "WAVE"))
        {
            throw Refuse(
                originForErrors,
                "it does not open with a RIFF/WAVE header, so it is not a WAV file at all.");
        }

        bool haveFormat = false;
        ushort tag = 0;
        int channels = 0;
        int sampleRate = 0;
        int bitsPerSample = 0;

        ReadOnlySpan<byte> data = default;
        bool haveData = false;
        long loopStart = 0;
        long loopEnd = 0;
        bool haveLoop = false;
        bool loopRefused = false;
        var cues = new WaveCues();

        int at = 12;
        while (at + 8 <= file.Length)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 4)..]);

            // Truncated file. Refuse, don't clamp to a shorter sound.
            if (size > (uint)(file.Length - at - 8))
            {
                throw Refuse(
                    originForErrors,
                    $"its '{Fourcc(file, at)}' chunk claims {size} bytes and only " +
                    $"{file.Length - at - 8} are left in the file.");
            }

            ReadOnlySpan<byte> body = file.Slice(at + 8, (int)size);

            if (Matches(file, at, "fmt "))
            {
                ReadFormat(body, originForErrors, out tag, out channels, out sampleRate, out bitsPerSample);
                haveFormat = true;
            }
            else if (Matches(file, at, "data"))
            {
                data = body;
                haveData = true;
            }
            else if (Matches(file, at, "smpl"))
            {
                haveLoop = TryReadLoop(body, out loopStart, out loopEnd, out loopRefused);
            }
            else if (Matches(file, at, "cue "))
            {
                cues.ReadCueChunk(body);
            }
            else if (Matches(file, at, "LIST"))
            {
                cues.ReadListChunk(body);
            }

            // Chunks are word-aligned: an odd body has a pad byte not counted in size.
            at += 8 + (int)size + ((int)size & 1);
        }

        if (!haveFormat) throw Refuse(originForErrors, "it has no 'fmt ' chunk, so nothing states its format.");
        if (!haveData) throw Refuse(originForErrors, "it has no 'data' chunk, so it carries no samples.");

        short[] samples = Widen(data, tag, bitsPerSample, originForErrors);
        if (samples.Length < channels)
        {
            throw Refuse(
                originForErrors,
                $"its data chunk holds {samples.Length} samples, which is less than one {channels}-channel frame.");
        }

        // Drop a trailing partial frame; carrying it would misalign the channels.
        int frames = samples.Length / channels;
        if (samples.Length != frames * channels) Array.Resize(ref samples, frames * channels);

        LoopRegion loop = LoopRegion.None;
        if (haveLoop)
        {
            // smpl end is inclusive, LoopRegion is half-open. A DAW can write a
            // loop past the end of the data after an edit, so check bounds here.
            long end = loopEnd + 1;
            if (loopStart >= 0 && end > loopStart && end <= frames)
                loop = new LoopRegion(loopStart, end);
            else
                loopRefused = true;
        }

        return new DecodedAudio(sampleRate, channels, samples, loop, loopRefused, cues.ToMarkers());
    }

    private static void ReadFormat(
        ReadOnlySpan<byte> body,
        string origin,
        out ushort tag,
        out int channels,
        out int sampleRate,
        out int bitsPerSample)
    {
        if (body.Length < 16)
            throw Refuse(origin, $"its 'fmt ' chunk is {body.Length} bytes and the smallest legal one is 16.");

        tag = BinaryPrimitives.ReadUInt16LittleEndian(body);
        channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
        sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(body[4..]);
        bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);

        if (tag == FormatExtensible)
        {
            // The real tag is the first two bytes of the SubFormat GUID at byte 24.
            if (body.Length < 40)
            {
                throw Refuse(
                    origin,
                    $"its 'fmt ' chunk says WAVE_FORMAT_EXTENSIBLE and is {body.Length} bytes; the subformat " +
                    "it needs to name lives at byte 24 of the extension.");
            }

            tag = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);
        }

        if (tag is not (FormatPcm or FormatIeeeFloat))
        {
            throw Refuse(
                origin,
                $"its format tag is 0x{tag:X4}; this cooker reads uncompressed PCM (0x0001) and IEEE float " +
                "(0x0003), which is what a DAW exports.");
        }

        if (channels is not (1 or 2))
        {
            throw Refuse(
                origin,
                $"it has {channels} channels; OpenAL's PCM16 buffers are mono or stereo, so a cooked sound is " +
                "one or the other.");
        }

        if (sampleRate <= 0)
            throw Refuse(origin, $"its sample rate is {sampleRate}.");
    }

    // Shifts, not divisions: division truncates toward zero and leaves a DC
    // step at every zero crossing.
    private static short[] Widen(ReadOnlySpan<byte> data, ushort tag, int bitsPerSample, string origin)
    {
        if (tag == FormatIeeeFloat)
        {
            return bitsPerSample switch
            {
                32 => WidenFloat32(data),
                _ => throw Refuse(
                    origin,
                    $"it is IEEE float at {bitsPerSample} bits a sample; this cooker reads 32-bit float."),
            };
        }

        return bitsPerSample switch
        {
            8 => WidenPcm8(data),
            16 => WidenPcm16(data),
            24 => WidenPcm24(data),
            32 => WidenPcm32(data),
            _ => throw Refuse(
                origin,
                $"it is {bitsPerSample}-bit PCM; this cooker reads 8, 16, 24 and 32."),
        };
    }

    // 8-bit WAV is unsigned with 128 as silence.
    private static short[] WidenPcm8(ReadOnlySpan<byte> data)
    {
        var samples = new short[data.Length];
        for (int i = 0; i < data.Length; i++) samples[i] = (short)((data[i] - 128) << 8);
        return samples;
    }

    private static short[] WidenPcm16(ReadOnlySpan<byte> data)
    {
        var samples = new short[data.Length / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..]);

        return samples;
    }

    private static short[] WidenPcm24(ReadOnlySpan<byte> data)
    {
        var samples = new short[data.Length / 3];
        for (int i = 0; i < samples.Length; i++)
        {
            int at = i * 3;

            // Bytes go in the top of the int so the shift down sign-extends.
            int value = (data[at] << 8) | (data[at + 1] << 16) | (data[at + 2] << 24);
            samples[i] = (short)(value >> 16);
        }

        return samples;
    }

    private static short[] WidenPcm32(ReadOnlySpan<byte> data)
    {
        var samples = new short[data.Length / 4];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)(BinaryPrimitives.ReadInt32LittleEndian(data[(i * 4)..]) >> 16);

        return samples;
    }

    private static short[] WidenFloat32(ReadOnlySpan<byte> data)
    {
        var samples = new short[data.Length / 4];
        for (int i = 0; i < samples.Length; i++)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(data[(i * 4)..]);

            // NaN becomes silence. Clamp so an overshoot does not wrap.
            if (float.IsNaN(value)) value = 0f;
            double scaled = Math.Clamp(value, -1.0, 1.0) * short.MaxValue;
            samples[i] = (short)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        return samples;
    }

    // Takes the first forward loop; LoopRegion holds one region.
    private static bool TryReadLoop(ReadOnlySpan<byte> body, out long start, out long end, out bool refused)
    {
        start = 0;
        end = 0;
        refused = false;

        if (body.Length < 36) return false;

        uint loopCount = BinaryPrimitives.ReadUInt32LittleEndian(body[28..]);
        if (loopCount == 0) return false;

        for (uint i = 0; i < loopCount; i++)
        {
            int at = 36 + (int)i * 24;
            if (at + 24 > body.Length) break;

            uint type = BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 4)..]);
            if (type != LoopTypeForward)
            {
                // Ping-pong and backward loops are dropped and reported, not
                // played forward.
                refused = true;
                continue;
            }

            start = BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 8)..]);
            end = BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 12)..]);
            refused = false;
            return true;
        }

        return false;
    }

    private static bool Matches(ReadOnlySpan<byte> file, int at, string fourcc) =>
        file.Length >= at + 4 &&
        file[at] == fourcc[0] && file[at + 1] == fourcc[1] &&
        file[at + 2] == fourcc[2] && file[at + 3] == fourcc[3];

    private static string Fourcc(ReadOnlySpan<byte> file, int at)
    {
        Span<char> text = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            byte value = file[at + i];
            text[i] = value is >= 0x20 and < 0x7F ? (char)value : '?';
        }

        return new string(text);
    }

    private static InvalidDataException Refuse(string origin, string because) =>
        new($"'{origin}' is not a WAV this cooker can read: {because}");
}
