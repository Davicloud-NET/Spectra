using SpectraEngine.Editor.Shell;
using System;
using System.Buffers.Binary;
using System.IO;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// An image's size, read from its header rather than by decoding it.
/// </summary>
/// <remarks>
/// <b>A decode to learn two numbers is the wrong price</b>: a 4K texture costs
/// 32 MB of managed memory to produce a line of text saying "4096 x 4096". Every
/// shape this cannot read is refused rather than guessed at, because the failure
/// of guessing at a header is a length read out of the middle of pixel data,
/// which then allocates or loops on a number nobody wrote.
/// </remarks>
public sealed class ImageHeaderTests
{
    private static MemoryStream Png(int width, int height)
    {
        var bytes = new byte[24];
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
        bytes[4] = 0x0D; bytes[5] = 0x0A; bytes[6] = 0x1A; bytes[7] = 0x0A;

        // Chunk length and "IHDR", then the two dimensions, big-endian.
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        bytes[12] = (byte)'I'; bytes[13] = (byte)'H'; bytes[14] = (byte)'D'; bytes[15] = (byte)'R';
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);

        return new MemoryStream(bytes);
    }

    private static MemoryStream Bmp(int width, int height)
    {
        var bytes = new byte[26];
        bytes[0] = 0x42; bytes[1] = 0x4D;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22, 4), height);
        return new MemoryStream(bytes);
    }

    private static MemoryStream Jpeg(int width, int height, bool withPadding)
    {
        using var buffer = new MemoryStream();

        buffer.WriteByte(0xFF); buffer.WriteByte(0xD8);

        // An APP0 segment first, so the frame header is genuinely walked to
        // rather than found at a fixed offset.
        buffer.WriteByte(0xFF); buffer.WriteByte(0xE0);
        buffer.WriteByte(0x00); buffer.WriteByte(0x10);
        for (int i = 0; i < 14; i++) buffer.WriteByte(0);

        // Fill bytes are legal between segments and a walker that stopped at the
        // first one would find nothing on a file some encoders really produce.
        if (withPadding) { buffer.WriteByte(0xFF); buffer.WriteByte(0xFF); }

        buffer.WriteByte(0xFF); buffer.WriteByte(0xC0);
        buffer.WriteByte(0x00); buffer.WriteByte(0x11);
        buffer.WriteByte(8);

        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(size[..2], (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(size[2..], (ushort)width);
        buffer.Write(size);

        for (int i = 0; i < 10; i++) buffer.WriteByte(0);

        return new MemoryStream(buffer.ToArray());
    }

    [Fact]
    public void A_png_reports_its_dimensions()
    {
        using MemoryStream stream = Png(4096, 2048);

        ImageHeader.TryRead(stream, out int width, out int height).ShouldBeTrue();
        width.ShouldBe(4096);
        height.ShouldBe(2048);
    }

    [Fact]
    public void A_bmp_reports_its_dimensions()
    {
        using MemoryStream stream = Bmp(64, 32);

        ImageHeader.TryRead(stream, out int width, out int height).ShouldBeTrue();
        width.ShouldBe(64);
        height.ShouldBe(32);
    }

    [Fact]
    public void A_top_down_bmp_reports_a_positive_height()
    {
        // A negative height means the rows are stored top-down. It is still a
        // size, and reporting "-32" would be a fact about the storage rather
        // than about the picture.
        using MemoryStream stream = Bmp(64, -32);

        ImageHeader.TryRead(stream, out _, out int height).ShouldBeTrue();
        height.ShouldBe(32);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_jpeg_is_walked_to_its_frame_header(bool withPadding)
    {
        using MemoryStream stream = Jpeg(800, 600, withPadding);

        ImageHeader.TryRead(stream, out int width, out int height).ShouldBeTrue();
        width.ShouldBe(800);
        height.ShouldBe(600);
    }

    [Fact]
    public void A_file_that_is_not_an_image_is_refused()
    {
        using var stream = new MemoryStream(new byte[64]);

        ImageHeader.TryRead(stream, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_truncated_header_is_refused_rather_than_read_past()
    {
        // The signature is there and the dimensions are not, which is exactly
        // what a half-copied file looks like on disk.
        using var stream = new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]);

        ImageHeader.TryRead(stream, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_zero_dimension_is_refused()
    {
        using MemoryStream stream = Png(0, 512);

        // Not a picture, and a zero here is the shape a length read from the
        // wrong offset usually takes.
        ImageHeader.TryRead(stream, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_missing_file_is_false_rather_than_a_throw()
    {
        ImageHeader.TryReadFile(
            Path.Combine(Path.GetTempPath(), "spectra-no-such-image.png"), out _, out _)
            .ShouldBeFalse();
    }
}
