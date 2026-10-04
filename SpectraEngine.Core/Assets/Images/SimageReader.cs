using SpectraEngine.Core.Graphics;
using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace SpectraEngine.Core.Assets.Images;

/// <summary>
/// Reads a <c>.simage</c>: the strict KTX2 subset described by
/// <see cref="SimageFormat"/>. Anything outside the profile is refused with a
/// message naming the rule it broke.
/// </summary>
// The DFD is never parsed: vkFormat and the level index are enough.
// Spans, not streams: a pack hands out a mapped view and a stream would copy it.
public static class SimageReader
{
    /// <summary>
    /// Whether <paramref name="file"/> opens with KTX2's identifier. Says nothing
    /// about the rest of the file.
    /// </summary>
    public static bool LooksLikeSimage(ReadOnlySpan<byte> file) =>
        file.Length >= SimageFormat.Identifier.Length &&
        file[..SimageFormat.Identifier.Length].SequenceEqual(SimageFormat.Identifier);

    /// <summary>
    /// Parses <paramref name="file"/>, or refuses it saying which rule it broke.
    /// </summary>
    /// <param name="file">The whole file. Mip offsets in the result are relative to its start.</param>
    /// <param name="originForErrors">Path or label naming the file in messages.</param>
    /// <exception cref="InvalidDataException">
    /// The bytes are not a <c>.simage</c> this engine can upload.
    /// </exception>
    public static SimageInfo Read(ReadOnlySpan<byte> file, string originForErrors = "<memory>")
    {
        if (!LooksLikeSimage(file))
        {
            throw Refuse(
                originForErrors,
                "it does not start with the KTX2 identifier, so it is not a KTX2 file at all.");
        }

        if (file.Length < SimageFormat.LevelIndexOffset)
        {
            throw Refuse(
                originForErrors,
                $"it is {file.Length} bytes, which is shorter than the {SimageFormat.LevelIndexOffset}-byte " +
                "KTX2 header and index.");
        }

        uint vkFormat = ReadU32(file, 12);
        uint typeSize = ReadU32(file, 16);
        uint pixelWidth = ReadU32(file, 20);
        uint pixelHeight = ReadU32(file, 24);
        uint pixelDepth = ReadU32(file, 28);
        uint layerCount = ReadU32(file, 32);
        uint faceCount = ReadU32(file, 36);
        uint levelCount = ReadU32(file, 40);
        uint supercompression = ReadU32(file, 44);
        uint kvdOffset = ReadU32(file, 56);
        uint kvdLength = ReadU32(file, 60);

        // Checked first: the level sizes below only make sense for raw payloads.
        if (supercompression != SimageFormat.SupercompressionNone)
        {
            throw Refuse(originForErrors, supercompression switch
            {
                SimageFormat.SupercompressionBasisLz =>
                    "it uses BasisLZ supercompression (scheme 1), which this engine will never support; " +
                    "cook it to an uncompressed BC format instead.",

                // In the profile, but .NET ships no Zstandard decoder.
                SimageFormat.SupercompressionZstd =>
                    "it uses Zstandard supercompression (scheme 2), which is in the .simage profile and is " +
                    "not implemented yet; cook it with supercompression off.",

                SimageFormat.SupercompressionZlib =>
                    "it uses ZLIB supercompression (scheme 3), which the .simage profile does not admit.",

                _ => $"it declares supercompression scheme {supercompression}, which is not a scheme KTX2 defines.",
            });
        }

        if (!SimageFormat.TryResolveVkFormat(vkFormat, out TextureFormat format, out TextureColorSpace declared))
        {
            throw Refuse(
                originForErrors,
                $"its vkFormat is {vkFormat}, which is not on the .simage allowlist " +
                "(R8, RGBA8, BC1, BC3, BC4, BC5, BC6H and BC7).");
        }

        // KTX2 requires 1 for block-compressed and 8-bit formats.
        if (typeSize != 1)
            throw Refuse(originForErrors, $"its typeSize is {typeSize}; every format in this profile is 1.");

        if (pixelDepth != 0)
        {
            throw Refuse(
                originForErrors,
                $"its pixelDepth is {pixelDepth}: it is a 3D texture, and the .simage profile is 2D and cube only.");
        }

        if (layerCount > 1)
        {
            throw Refuse(
                originForErrors,
                $"its layerCount is {layerCount}: it is a texture array, which the .simage profile reserves " +
                "and does not carry yet.");
        }

        if (faceCount is not (1 or 6))
        {
            throw Refuse(
                originForErrors,
                $"its faceCount is {faceCount}; a KTX2 file has 1 face or 6.");
        }

        // In the profile, but Renderer.CreateTexture has no cube path.
        if (faceCount == 6)
        {
            throw Refuse(
                originForErrors,
                "it is a cube map, which the .simage profile carries and this engine has no upload path for yet.");
        }

        if (levelCount == 0)
        {
            // 0 means "generate mips at load", which a BC level cannot do on the GPU.
            throw Refuse(
                originForErrors,
                "its levelCount is 0, which asks the loader to generate the mip chain; a cooked image must " +
                "carry its own levels.");
        }

        // The bound keeps the int casts below positive.
        const uint maxDimension = 65536;
        if (pixelWidth is 0 or > maxDimension || pixelHeight is 0 or > maxDimension)
        {
            throw Refuse(
                originForErrors,
                $"its base level is {pixelWidth}x{pixelHeight}; a .simage is between 1 and {maxDimension} texels " +
                "on each axis.");
        }

        long indexEnd = (long)SimageFormat.LevelIndexOffset + (long)levelCount * SimageFormat.LevelIndexEntrySize;
        if (indexEnd > file.Length)
        {
            throw Refuse(
                originForErrors,
                $"it declares {levelCount} levels, whose index needs {indexEnd} bytes, and the file is " +
                $"{file.Length} bytes.");
        }

        var mips = new TextureMipDesc[levelCount];
        int alignment = SimageFormat.LevelAlignment(format);
        int payloadBytes = 0;

        for (int level = 0; level < mips.Length; level++)
        {
            // KTX2: index entry 0 is the base level, but level data is stored
            // smallest-first.
            int entry = SimageFormat.LevelIndexOffset + level * SimageFormat.LevelIndexEntrySize;
            ulong byteOffset = ReadU64(file, entry);
            ulong byteLength = ReadU64(file, entry + 8);
            ulong uncompressedLength = ReadU64(file, entry + 16);

            int width = Math.Max(1, (int)pixelWidth >> level);
            int height = Math.Max(1, (int)pixelHeight >> level);

            // KTX2 rows are tightly packed, so the pitch can be derived; the
            // length check below confirms it.
            int rowPitch = TextureFormatInfo.TightRowPitch(format, width);
            long expected = (long)TextureFormatInfo.RowCount(format, height) * rowPitch;

            if ((long)byteLength != expected)
            {
                throw Refuse(
                    originForErrors,
                    $"level {level} is {width}x{height}, which occupies {expected} bytes of {format}, and the " +
                    $"level index says {byteLength}.");
            }

            if (supercompression == SimageFormat.SupercompressionNone && uncompressedLength != byteLength)
            {
                throw Refuse(
                    originForErrors,
                    $"level {level} is not supercompressed, so its uncompressedByteLength must equal its " +
                    $"byteLength; the index says {uncompressedLength} and {byteLength}.");
            }

            if (byteOffset % (ulong)alignment != 0)
            {
                throw Refuse(
                    originForErrors,
                    $"level {level} starts at byte {byteOffset}, which is not a multiple of the {alignment}-byte " +
                    $"mip padding {format} requires.");
            }

            if (byteOffset + byteLength > (ulong)file.Length)
            {
                throw Refuse(
                    originForErrors,
                    $"level {level} claims {byteLength} bytes at offset {byteOffset}, and the file is " +
                    $"{file.Length} bytes.");
            }

            mips[level] = new TextureMipDesc(width, height, (int)byteOffset, rowPitch);
            payloadBytes += (int)byteLength;
        }

        ReadKeyValues(file, kvdOffset, kvdLength, originForErrors, out SimageRowOrder rowOrder, out int profile);

        return new SimageInfo(format, declared, rowOrder, profile, mips, payloadBytes);
    }

    // Unknown keys are skipped: KTX2's key space is open.
    private static void ReadKeyValues(
        ReadOnlySpan<byte> file,
        uint kvdOffset,
        uint kvdLength,
        string origin,
        out SimageRowOrder rowOrder,
        out int profileVersion)
    {
        string? orientation = null;
        string? profile = null;

        if (kvdLength != 0)
        {
            if ((long)kvdOffset + kvdLength > file.Length)
            {
                throw Refuse(
                    origin,
                    $"its key/value block claims {kvdLength} bytes at offset {kvdOffset}, and the file is " +
                    $"{file.Length} bytes.");
            }

            ReadOnlySpan<byte> kvd = file.Slice((int)kvdOffset, (int)kvdLength);
            int at = 0;
            while (at + 4 <= kvd.Length)
            {
                uint pairLength = ReadU32(kvd, at);
                at += 4;
                if (pairLength == 0 || at + pairLength > kvd.Length)
                {
                    throw Refuse(
                        origin,
                        $"its key/value block declares a {pairLength}-byte entry with {kvd.Length - at} bytes left.");
                }

                ReadOnlySpan<byte> pair = kvd.Slice(at, (int)pairLength);
                int nul = pair.IndexOf((byte)0);
                if (nul >= 0)
                {
                    string key = Encoding.UTF8.GetString(pair[..nul]);
                    string value = ReadNulTerminated(pair[(nul + 1)..]);
                    if (key == SimageFormat.OrientationKey) orientation = value;
                    else if (key == SimageFormat.ProfileKey) profile = value;
                }

                // Entries are padded to four bytes.
                at += (int)pairLength;
                at = (at + 3) & ~3;
            }
        }

        // Both keys are required. A plain KTX2 file has neither.
        if (profile is null)
        {
            throw Refuse(
                origin,
                $"it carries no '{SimageFormat.ProfileKey}' key, so it was not cooked by scook; recook it.");
        }

        if (!int.TryParse(profile, NumberStyles.None, CultureInfo.InvariantCulture, out profileVersion))
        {
            throw Refuse(
                origin,
                $"its '{SimageFormat.ProfileKey}' key reads '{profile}', which is not a version number.");
        }

        // Exact match, not a floor: a cooked file can always be recooked.
        if (profileVersion != EngineInfo.TextureFormatVersion)
        {
            throw Refuse(
                origin,
                $"it was cooked for texture format version {profileVersion} and this engine reads version " +
                $"{EngineInfo.TextureFormatVersion}; recook it.");
        }

        if (orientation is null)
        {
            throw Refuse(
                origin,
                $"it carries no '{SimageFormat.OrientationKey}' key, so which way up its rows are stored is " +
                "undeclared; recook it.");
        }

        rowOrder = orientation switch
        {
            SimageFormat.OrientationBottomUp => SimageRowOrder.BottomUp,

            // Uploading it anyway would render every texture upside down.
            SimageFormat.OrientationTopDown => throw Refuse(
                origin,
                $"its rows are stored top-down ('{SimageFormat.OrientationKey}' = " +
                $"'{SimageFormat.OrientationTopDown}') and this engine samples v = 0 at the bottom of the " +
                "picture; a block-compressed payload cannot be flipped at load, so recook it."),

            _ => throw Refuse(
                origin,
                $"its '{SimageFormat.OrientationKey}' key reads '{orientation}', which is neither " +
                $"'{SimageFormat.OrientationBottomUp}' nor '{SimageFormat.OrientationTopDown}'."),
        };
    }

    private static string ReadNulTerminated(ReadOnlySpan<byte> value)
    {
        int nul = value.IndexOf((byte)0);
        return Encoding.UTF8.GetString(nul >= 0 ? value[..nul] : value);
    }

    private static uint ReadU32(ReadOnlySpan<byte> bytes, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);

    private static ulong ReadU64(ReadOnlySpan<byte> bytes, int at) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[at..]);

    // InvalidDataException is what AssetManager's texture path catches, so a
    // bad cooked image degrades to the placeholder like an unreadable PNG.
    private static InvalidDataException Refuse(string origin, string because) =>
        new($"'{origin}' is not a .simage this engine can read: {because}");
}
