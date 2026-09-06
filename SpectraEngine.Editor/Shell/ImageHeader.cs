using System;
using System.Buffers.Binary;
using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// An image's dimensions, read from its header rather than by decoding it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A decode to learn two numbers is the wrong price.</b> A 4K texture costs
/// 32 MB of managed memory to decode and the details strip wants "4096 x 4096";
/// the numbers are in the first few dozen bytes of every format here.
/// </para>
/// <para>
/// <b>Every read is bounded and every unknown shape is refused.</b> A truncated
/// or hand-edited file is ordinary in a content folder, and the failure of
/// guessing at a header is a length read out of the middle of pixel data, which
/// then allocates or loops on a number nobody wrote. False is the answer, and
/// the row keeps its kind glyph.
/// </para>
/// </remarks>
public static class ImageHeader
{
    /// <summary>Reads the pixel dimensions, or returns false.</summary>
    public static bool TryRead(Stream stream, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(stream);

        width = 0;
        height = 0;

        Span<byte> head = stackalloc byte[32];
        int read = ReadAtLeast(stream, head, 32);
        if (read < 8) return false;

        if (IsPng(head)) return TryReadPng(head, read, out width, out height);
        if (head[0] == 0x42 && head[1] == 0x4D) return TryReadBmp(head, read, out width, out height);
        if (head[0] == 0xFF && head[1] == 0xD8) return TryReadJpeg(stream, head, read, out width, out height);

        return false;
    }

    /// <summary>Reads the dimensions of a file, or returns false.</summary>
    public static bool TryReadFile(string fullPath, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using FileStream stream = File.OpenRead(fullPath);
            return TryRead(stream, out width, out height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsPng(ReadOnlySpan<byte> head) =>
        head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47;

    // IHDR is required by the specification to be the first chunk, so its
    // width and height sit at fixed offsets: 8 signature + 8 chunk header.
    private static bool TryReadPng(ReadOnlySpan<byte> head, int read, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (read < 24) return false;

        width = BinaryPrimitives.ReadInt32BigEndian(head[16..20]);
        height = BinaryPrimitives.ReadInt32BigEndian(head[20..24]);

        return Plausible(width, height);
    }

    // A BITMAPINFOHEADER's width and height are signed 32-bit at offset 18 and
    // 22; a negative height means a top-down bitmap and is still a size.
    private static bool TryReadBmp(ReadOnlySpan<byte> head, int read, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (read < 26) return false;

        width = BinaryPrimitives.ReadInt32LittleEndian(head[18..22]);
        height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(head[22..26]));

        return Plausible(width, height);
    }

    /// <summary>
    /// Walks a JPEG's marker chain to the frame header.
    /// </summary>
    /// <remarks>
    /// <b>The size is not at a fixed offset in a JPEG</b>, so this is the one
    /// format that has to be walked: segments carry their own length and the
    /// frame marker can sit behind any number of them. The walk is bounded by a
    /// segment count as well as by the stream, because a corrupt length can
    /// point at itself.
    /// </remarks>
    private static bool TryReadJpeg(Stream stream, ReadOnlySpan<byte> head, int read, out int width, out int height)
    {
        width = 0;
        height = 0;

        // Rewind past what was already read: the walk needs the whole chain.
        if (!stream.CanSeek) return false;
        stream.Position = 2;

        Span<byte> segment = stackalloc byte[4];

        for (int guard = 0; guard < 256; guard++)
        {
            int marker = NextMarker(stream);
            if (marker < 0) return false;

            // SOF0 through SOF15, skipping the four that are not frame headers.
            bool isFrame = marker is >= 0xC0 and <= 0xCF
                && marker is not (0xC4 or 0xC8 or 0xCC);

            if (ReadAtLeast(stream, segment[..2], 2) < 2) return false;
            int length = BinaryPrimitives.ReadUInt16BigEndian(segment[..2]);
            if (length < 2) return false;

            if (isFrame)
            {
                Span<byte> frame = stackalloc byte[5];
                if (ReadAtLeast(stream, frame, 5) < 5) return false;

                height = BinaryPrimitives.ReadUInt16BigEndian(frame[1..3]);
                width = BinaryPrimitives.ReadUInt16BigEndian(frame[3..5]);
                return Plausible(width, height);
            }

            // Start of scan: the entropy-coded data begins and there is no
            // frame header after it.
            if (marker == 0xDA) return false;

            stream.Position += length - 2;
        }

        return false;
    }

    private static int NextMarker(Stream stream)
    {
        int guard = 0;

        while (guard++ < 4096)
        {
            int b = stream.ReadByte();
            if (b < 0) return -1;
            if (b != 0xFF) continue;

            // Fill bytes are legal between segments.
            int marker;
            do
            {
                marker = stream.ReadByte();
                if (marker < 0) return -1;
            }
            while (marker == 0xFF);

            if (marker != 0x00) return marker;
        }

        return -1;
    }

    // A dimension of zero is not a picture, and the cap is well above any real
    // texture: past it the number came from somewhere other than a header.
    private static bool Plausible(int width, int height) =>
        width > 0 && height > 0 && width <= 65535 && height <= 65535;

    private static int ReadAtLeast(Stream stream, Span<byte> buffer, int wanted)
    {
        int total = 0;

        while (total < wanted)
        {
            int read = stream.Read(buffer[total..]);
            if (read <= 0) break;
            total += read;
        }

        return total;
    }
}
