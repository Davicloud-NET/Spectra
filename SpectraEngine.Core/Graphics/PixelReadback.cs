using System;

namespace SpectraEngine.Core.Graphics;

// Bounds check and row walk shared by every 8-bit RGBA readback. Both fail
// without an exception: a short destination is a write past a buffer, and a
// walk that ignores the driver's row pitch shears the picture.
internal static class PixelReadback
{
    // 8-bit RGBA only.
    internal const int BytesPerPixel = 4;

    internal static int ByteCount(int width, int height) => checked(width * height * BytesPerPixel);

    internal static void ValidateRegion(
        RenderTarget target, int x, int y, int width, int height, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateSize(width, height, destination);

        if (x < 0 || y < 0 || x + width > target.Width || y + height > target.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                $"The region {width}x{height} at ({x}, {y}) leaves a {target.Width}x{target.Height} target.");
        }
    }

    internal static void ValidateSize(int width, int height, Span<byte> destination)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width), $"A readback needs a positive region; got {width}x{height}.");
        }

        int needed = ByteCount(width, height);
        if (destination.Length < needed)
        {
            throw new ArgumentException(
                $"A {width}x{height} readback needs {needed} bytes; the destination holds {destination.Length}.",
                nameof(destination));
        }
    }

    // A mapped D3D surface has row 0 at the top; the destination wants it last.
    // sourceRowPitch comes from the map and may be wider than width * 4.
    internal static unsafe void CopyRowsBottomFirst(
        byte* source, uint sourceRowPitch, int width, int height, Span<byte> destination)
    {
        int rowBytes = width * BytesPerPixel;
        for (int row = 0; row < height; row++)
        {
            var sourceRow = new ReadOnlySpan<byte>(source + ((nuint)row * sourceRowPitch), rowBytes);
            sourceRow.CopyTo(destination.Slice((height - 1 - row) * rowBytes, rowBytes));
        }
    }
}
