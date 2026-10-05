using System;
using System.Buffers.Binary;
using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// What a WAV file says about itself, read from its chunk headers without
/// decoding its samples. Uncompressed PCM and float only, which is what the
/// cook reads.
/// </summary>
/// <param name="SampleRate">Frames a second.</param>
/// <param name="Channels">How many channels a frame holds.</param>
/// <param name="FrameCount">Sample frames in the data chunk.</param>
/// <param name="HasLoop">Whether it carries a forward loop region inside its samples.</param>
/// <param name="MarkerCount">How many cue points it carries.</param>
public readonly record struct WaveHeader(
    int SampleRate, int Channels, long FrameCount, bool HasLoop, int MarkerCount)
{
    private const ushort FormatPcm = 0x0001;
    private const ushort FormatIeeeFloat = 0x0003;
    private const ushort FormatExtensible = 0xFFFE;

    // A corrupt size can chain chunks for ever.
    private const int MaxChunks = 4096;

    /// <summary>Seconds of sound.</summary>
    public double Seconds => SampleRate > 0 ? (double)FrameCount / SampleRate : 0d;

    /// <summary>Reads a file's header, or returns false when it is not such a WAV.</summary>
    public static bool TryReadFile(string fullPath, out WaveHeader header)
    {
        header = default;

        try
        {
            using FileStream stream = File.OpenRead(fullPath);
            return TryRead(stream, out header);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Reads a header from a seekable stream, or returns false.</summary>
    public static bool TryRead(Stream stream, out WaveHeader header)
    {
        ArgumentNullException.ThrowIfNull(stream);

        header = default;

        Span<byte> head = stackalloc byte[12];
        if (!stream.CanSeek || !Fill(stream, head) || !Is(head, "RIFF") || !Is(head[8..], "WAVE"))
            return false;

        var found = new Chunks();
        Span<byte> chunk = stackalloc byte[8];

        for (int guard = 0; guard < MaxChunks && Fill(stream, chunk); guard++)
        {
            long size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            long body = stream.Position;
            long left = stream.Length - body;

            if (Is(chunk, "fmt ") && !found.ReadFormat(stream, size))
                return false;

            if (Is(chunk, "data"))
                found.DataBytes = Math.Min(size, left);
            else if (Is(chunk, "smpl"))
                found.ReadLoop(stream, size);
            else if (Is(chunk, "cue "))
                found.ReadCueCount(stream, size);

            // Chunks are word-aligned: an odd body has a pad byte not counted in size.
            long next = body + size + (size & 1);
            if (next >= stream.Length)
                break;

            stream.Position = next;
        }

        return found.TryFinish(out header);
    }

    private static bool Is(ReadOnlySpan<byte> bytes, string fourcc) =>
        bytes[0] == fourcc[0] && bytes[1] == fourcc[1] && bytes[2] == fourcc[2] && bytes[3] == fourcc[3];

    private static bool Fill(Stream stream, Span<byte> buffer) =>
        stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;

    // What the walk has gathered so far.
    private struct Chunks
    {
        // Null until a data chunk is found.
        public long? DataBytes;

        private bool _hasFormat;
        private int _sampleRate;
        private int _channels;
        private int _bytesPerSample;
        private bool _hasLoop;
        private long _loopStart;
        private long _loopEnd;
        private int _markers;

        public bool ReadFormat(Stream stream, long size)
        {
            Span<byte> body = stackalloc byte[26];
            int wanted = (int)Math.Min(size, body.Length);
            if (wanted < 16 || !Fill(stream, body[..wanted]))
                return false;

            ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(body);
            _channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
            _sampleRate = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(body[4..]), int.MaxValue);
            _bytesPerSample = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]) / 8;

            // The real tag is the first two bytes of the SubFormat GUID at byte 24.
            if (tag == FormatExtensible)
            {
                if (wanted < 26)
                    return false;

                tag = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]);
            }

            _hasFormat = tag is FormatPcm or FormatIeeeFloat;
            return _hasFormat;
        }

        // The first forward loop, as the cook takes it.
        public void ReadLoop(Stream stream, long size)
        {
            Span<byte> head = stackalloc byte[36];
            if (size < head.Length || !Fill(stream, head))
                return;

            long loops = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(head[28..]), (size - 36) / 24);
            Span<byte> loop = stackalloc byte[24];

            for (long i = 0; i < loops && !_hasLoop && Fill(stream, loop); i++)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(loop[4..]) != 0)
                    continue;

                _hasLoop = true;
                _loopStart = BinaryPrimitives.ReadUInt32LittleEndian(loop[8..]);

                // Inclusive in the file.
                _loopEnd = BinaryPrimitives.ReadUInt32LittleEndian(loop[12..]) + 1L;
            }
        }

        public void ReadCueCount(Stream stream, long size)
        {
            Span<byte> count = stackalloc byte[4];
            if (size < count.Length || !Fill(stream, count))
                return;

            _markers = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(count), (size - 4) / 24);
        }

        public readonly bool TryFinish(out WaveHeader header)
        {
            header = default;

            int frameBytes = _channels * _bytesPerSample;
            if (!_hasFormat || DataBytes is not { } bytes || _sampleRate <= 0 || frameBytes <= 0)
                return false;

            long frames = bytes / frameBytes;
            bool loops = _hasLoop && _loopEnd > _loopStart && _loopEnd <= frames;

            header = new WaveHeader(_sampleRate, _channels, frames, loops, _markers);
            return true;
        }
    }
}
