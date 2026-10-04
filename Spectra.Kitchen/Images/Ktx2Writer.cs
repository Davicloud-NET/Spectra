using SpectraEngine.Core.Assets.Images;
using SpectraEngine.Core.Graphics;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Spectra.Kitchen.Images;

/// <summary>
/// Writes a <c>.simage</c>: KTX2 restricted to the <see cref="SimageFormat"/> profile.
/// The output depends only on the arguments.
/// </summary>
// KTX2 stores level data smallest first but the level index base first.
// The DFD is required by the spec and used by external tools; SimageReader never parses it.
public static class Ktx2Writer
{
    // KHR_DF_MODEL_*, Khronos Data Format 1.3.
    private const byte ModelRgbsda = 1;
    private const byte ModelBc1A = 128;
    private const byte ModelBc3 = 130;
    private const byte ModelBc4 = 131;
    private const byte ModelBc5 = 132;
    private const byte ModelBc6H = 133;
    private const byte ModelBc7 = 134;

    private const byte PrimariesBt709 = 1;
    private const byte TransferLinear = 1;
    private const byte TransferSrgb = 2;

    // Qualifier bit in the high nibble of the channel byte.
    private const byte ChannelFloat = 0x80;

    // KHR_DF_CHANNEL_RGBSDA_*.
    private const byte ChannelRed = 0;
    private const byte ChannelGreen = 1;
    private const byte ChannelBlue = 2;
    private const byte ChannelAlpha = 15;

    private const int BasicBlockHeaderBytes = 24;
    private const int SampleBytes = 16;

    /// <summary>Assembles the file.</summary>
    /// <param name="levels">
    /// Each level's tightly packed bytes, most detailed first. Sizes are checked.
    /// </param>
    /// <param name="profileVersion">Value of the <c>SpectraProfile</c> key.</param>
    public static byte[] Write(
        TextureFormat format,
        int width,
        int height,
        IReadOnlyList<byte[]> levels,
        SimageRowOrder rowOrder,
        int profileVersion)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (levels.Count == 0)
            throw new ArgumentException("A .simage carries at least one level.", nameof(levels));

        // A short level would still write a valid index and only fail at load.
        for (int level = 0; level < levels.Count; level++)
        {
            int levelWidth = Math.Max(1, width >> level);
            int levelHeight = Math.Max(1, height >> level);
            int expected = TextureFormatInfo.RowCount(format, levelHeight)
                * TextureFormatInfo.TightRowPitch(format, levelWidth);

            if (levels[level].Length != expected)
            {
                throw new ArgumentException(
                    $"Level {level} of a {width}x{height} {format} image is {levelWidth}x{levelHeight}, which " +
                    $"occupies {expected} bytes; {levels[level].Length} were supplied.",
                    nameof(levels));
            }
        }

        byte[] dfd = BuildDataFormatDescriptor(format);
        byte[] kvd = BuildKeyValueData(rowOrder, profileVersion);

        int levelIndexBytes = levels.Count * SimageFormat.LevelIndexEntrySize;
        int dfdOffset = SimageFormat.LevelIndexOffset + levelIndexBytes;
        int kvdOffset = dfdOffset + dfd.Length;

        // Smallest level first. Aligned so a mapped payload uploads without a copy.
        int alignment = SimageFormat.LevelAlignment(format);
        var offsets = new long[levels.Count];
        long at = Align(kvdOffset + kvd.Length, alignment);
        for (int level = levels.Count - 1; level >= 0; level--)
        {
            offsets[level] = at;
            at = Align(at + levels[level].Length, alignment);
        }

        // No padding after the base level, which is written last.
        long totalBytes = offsets[0] + levels[0].Length;
        var file = new byte[checked((int)totalBytes)];

        SimageFormat.Identifier.CopyTo(file);
        WriteU32(file, 12, SimageFormat.ToVkFormat(format));
        WriteU32(file, 16, 1);                       // typeSize: one byte per component in every profile format
        WriteU32(file, 20, (uint)width);
        WriteU32(file, 24, (uint)height);
        WriteU32(file, 28, 0);                       // pixelDepth: 2D only
        WriteU32(file, 32, 0);                       // layerCount: not an array
        WriteU32(file, 36, 1);                       // faceCount: not a cube map
        WriteU32(file, 40, (uint)levels.Count);
        WriteU32(file, 44, SimageFormat.SupercompressionNone);
        WriteU32(file, 48, (uint)dfdOffset);
        WriteU32(file, 52, (uint)dfd.Length);
        WriteU32(file, 56, (uint)kvdOffset);
        WriteU32(file, 60, (uint)kvd.Length);
        WriteU64(file, 64, 0);                       // sgdByteOffset: no supercompression global data
        WriteU64(file, 72, 0);                       // sgdByteLength

        for (int level = 0; level < levels.Count; level++)
        {
            int entry = SimageFormat.LevelIndexOffset + level * SimageFormat.LevelIndexEntrySize;
            WriteU64(file, entry, (ulong)offsets[level]);
            WriteU64(file, entry + 8, (ulong)levels[level].Length);

            // Uncompressed length. Zero would mean "unknown" and some readers act on it.
            WriteU64(file, entry + 16, (ulong)levels[level].Length);
        }

        dfd.CopyTo(file, dfdOffset);
        kvd.CopyTo(file, kvdOffset);
        for (int level = 0; level < levels.Count; level++)
            levels[level].CopyTo(file, (int)offsets[level]);

        return file;
    }

    // KTX2 requires keys sorted by codepoint. The two keys are written in that order by hand.
    private static byte[] BuildKeyValueData(SimageRowOrder rowOrder, int profileVersion)
    {
        string orientation = rowOrder == SimageRowOrder.BottomUp
            ? SimageFormat.OrientationBottomUp
            : SimageFormat.OrientationTopDown;

        var bytes = new List<byte>(64);
        AppendPair(bytes, SimageFormat.OrientationKey, orientation);

        AppendPair(bytes, SimageFormat.ProfileKey, profileVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return [.. bytes];
    }

    private static void AppendPair(List<byte> bytes, string key, string value)
    {
        // KTX2 requires the NUL after the key. The one after the value keeps
        // the padding out of a string a reader takes whole.
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        byte[] valueBytes = Encoding.UTF8.GetBytes(value);
        int pairLength = keyBytes.Length + 1 + valueBytes.Length + 1;

        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)pairLength);
        bytes.AddRange(length);
        bytes.AddRange(keyBytes);
        bytes.Add(0);
        bytes.AddRange(valueBytes);
        bytes.Add(0);

        while (bytes.Count % 4 != 0) bytes.Add(0);
    }

    // One basic descriptor block; every format here is single-plane.
    private static byte[] BuildDataFormatDescriptor(TextureFormat format)
    {
        (byte model, Sample[] samples) = DescribeFormat(format);

        int blockSize = BasicBlockHeaderBytes + samples.Length * SampleBytes;
        var dfd = new byte[4 + blockSize];

        WriteU32(dfd, 0, (uint)dfd.Length);          // dfdTotalSize, including itself
        WriteU32(dfd, 4, 0);                         // vendorId 0 (Khronos), descriptorType 0 (basic)
        WriteU32(dfd, 8, 2u | ((uint)blockSize << 16));  // versionNumber 2 (KDF 1.3), descriptorBlockSize

        dfd[12] = model;
        dfd[13] = PrimariesBt709;

        // Linear, to match the UNORM vkFormat. Colour space belongs to the material slot.
        dfd[14] = TransferLinear;
        dfd[15] = 0;                                 // flags: straight (unpremultiplied) alpha

        dfd[16] = (byte)(TextureFormatInfo.BlockWidth(format) - 1);
        dfd[17] = (byte)(TextureFormatInfo.BlockHeight(format) - 1);
        dfd[18] = 0;                                 // depth: one texel
        dfd[19] = 0;                                 // no fourth dimension
        dfd[20] = (byte)TextureFormatInfo.BytesPerBlock(format);   // bytesPlane0
        // bytesPlane1..7 stay zero.

        for (int i = 0; i < samples.Length; i++)
        {
            int at = 4 + BasicBlockHeaderBytes + i * SampleBytes;
            Sample sample = samples[i];

            // bitLength is stored minus one.
            WriteU32(dfd, at, (uint)sample.BitOffset
                | ((uint)(sample.BitLength - 1) << 16)
                | ((uint)sample.Channel << 24));

            // samplePosition stays zero: nothing is subsampled.
            WriteU32(dfd, at + 8, sample.Lower);
            WriteU32(dfd, at + 12, sample.Upper);
        }

        return dfd;
    }

    private static (byte Model, Sample[] Samples) DescribeFormat(TextureFormat format) => format switch
    {
        // BC colour is not separable into channels, so a whole block is one sample.
        TextureFormat.Bc1 => (ModelBc1A, [new Sample(0, 64, ChannelRed, 0, uint.MaxValue)]),

        // Alpha block first, then colour.
        TextureFormat.Bc3 => (ModelBc3,
        [
            new Sample(0, 64, ChannelAlpha, 0, uint.MaxValue),
            new Sample(64, 64, ChannelRed, 0, uint.MaxValue),
        ]),

        TextureFormat.Bc4 => (ModelBc4, [new Sample(0, 64, ChannelRed, 0, uint.MaxValue)]),

        TextureFormat.Bc5 => (ModelBc5,
        [
            new Sample(0, 64, ChannelRed, 0, uint.MaxValue),
            new Sample(64, 64, ChannelGreen, 0, uint.MaxValue),
        ]),

        // BC6H decodes to half-floats: float qualifier, float bit-pattern upper bound.
        TextureFormat.Bc6H => (ModelBc6H,
            [new Sample(0, 128, (byte)(ChannelRed | ChannelFloat), 0, 0x7F7FFFFF)]),

        TextureFormat.Bc7 => (ModelBc7, [new Sample(0, 128, ChannelRed, 0, uint.MaxValue)]),

        TextureFormat.Rgba8 => (ModelRgbsda,
        [
            new Sample(0, 8, ChannelRed, 0, 255),
            new Sample(8, 8, ChannelGreen, 0, 255),
            new Sample(16, 8, ChannelBlue, 0, 255),
            new Sample(24, 8, ChannelAlpha, 0, 255),
        ]),

        TextureFormat.R8 => (ModelRgbsda, [new Sample(0, 8, ChannelRed, 0, 255)]),

        _ => throw new ArgumentOutOfRangeException(
            nameof(format), format, $"{format} has no data format descriptor in the .simage profile."),
    };

    private static long Align(long value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private static void WriteU32(byte[] bytes, int at, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);

    private static void WriteU64(byte[] bytes, int at, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), value);

    private readonly record struct Sample(int BitOffset, int BitLength, byte Channel, uint Lower, uint Upper);
}
