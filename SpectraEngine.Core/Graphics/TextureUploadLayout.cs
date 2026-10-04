using System;

namespace SpectraEngine.Core.Graphics;

// Payload rearrangements the backends share when uploading a TextureUploadDesc.
internal static class TextureUploadLayout
{
    internal static void CopyRows(ReadOnlySpan<byte> source, int sourcePitch, Span<byte> destination,
        int destinationPitch, int width, int rows, TextureFormat format)
    {
        int tight = TextureFormatInfo.TightRowPitch(format, width);
        for (int row = 0; row < rows; row++)
        {
            var input = source.Slice(row * sourcePitch, tight);
            var output = destination.Slice(row * destinationPitch);
            if (format != TextureFormat.Rgb8) input.CopyTo(output);
            else for (int x = 0; x < width; x++)
            {
                output[x * 4] = input[x * 3]; output[x * 4 + 1] = input[x * 3 + 1];
                output[x * 4 + 2] = input[x * 3 + 2]; output[x * 4 + 3] = 255;
            }
        }
    }
    internal static int TightLevelSize(TextureFormat format, in TextureMipDesc mip) =>
        TextureFormatInfo.RowCount(format, mip.Height) * TextureFormatInfo.TightRowPitch(format, mip.Width);

    // One level with no row padding. Copies only when the pitch is not already tight.
    // For GL: glCompressedTexImage2D takes a byte count, not a stride.
    internal static ReadOnlySpan<byte> TightLevel(
        ReadOnlySpan<byte> payload, TextureFormat format, in TextureMipDesc mip, out byte[]? repacked)
    {
        int tightPitch = TextureFormatInfo.TightRowPitch(format, mip.Width);
        int rows = TextureFormatInfo.RowCount(format, mip.Height);

        if (mip.RowPitch == tightPitch)
        {
            repacked = null;
            return payload.Slice(mip.Offset, tightPitch * rows);
        }

        repacked = new byte[tightPitch * rows];
        for (int row = 0; row < rows; row++)
        {
            payload.Slice(mip.Offset + row * mip.RowPitch, tightPitch)
                .CopyTo(repacked.AsSpan(row * tightPitch, tightPitch));
        }
        return repacked;
    }

    // Rgb8 to RGBA8, alpha 255: no backend has a 24-bit texture format.
    // One buffer for every level, so the upload pins under a single fixed.
    internal static byte[] ExpandRgbToRgba(
        ReadOnlySpan<byte> payload, ReadOnlySpan<TextureMipDesc> mips, out TextureMipDesc[] expandedMips)
    {
        expandedMips = new TextureMipDesc[mips.Length];

        int total = 0;
        for (int level = 0; level < mips.Length; level++)
        {
            TextureMipDesc mip = mips[level];
            int pitch = mip.Width * 4;
            expandedMips[level] = new TextureMipDesc(mip.Width, mip.Height, total, pitch);
            total += pitch * mip.Height;
        }

        var expanded = new byte[total];
        for (int level = 0; level < mips.Length; level++)
        {
            TextureMipDesc source = mips[level];
            TextureMipDesc destination = expandedMips[level];
            for (int y = 0; y < source.Height; y++)
            {
                int sourceRow = source.Offset + y * source.RowPitch;
                int destinationRow = destination.Offset + y * destination.RowPitch;
                for (int x = 0; x < source.Width; x++)
                {
                    expanded[destinationRow + x * 4 + 0] = payload[sourceRow + x * 3 + 0];
                    expanded[destinationRow + x * 4 + 1] = payload[sourceRow + x * 3 + 1];
                    expanded[destinationRow + x * 4 + 2] = payload[sourceRow + x * 3 + 2];
                    expanded[destinationRow + x * 4 + 3] = 255;
                }
            }
        }

        return expanded;
    }

    // Packs a software-built mip chain into one buffer with tight pitches.
    internal static byte[] Flatten(
        TextureFormat format,
        System.Collections.Generic.IReadOnlyList<(byte[] Pixels, int Width, int Height)> levels,
        out TextureMipDesc[] mips)
    {
        mips = new TextureMipDesc[levels.Count];

        int total = 0;
        for (int level = 0; level < levels.Count; level++)
        {
            (_, int width, int height) = levels[level];
            int pitch = TextureFormatInfo.TightRowPitch(format, width);
            mips[level] = new TextureMipDesc(width, height, total, pitch);
            total += pitch * TextureFormatInfo.RowCount(format, height);
        }

        var packed = new byte[total];
        for (int level = 0; level < levels.Count; level++)
            levels[level].Pixels.CopyTo(packed.AsSpan(mips[level].Offset));

        return packed;
    }
}
