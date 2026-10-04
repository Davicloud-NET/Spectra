using Spectra.Kitchen.Images;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Images;
using SpectraEngine.Core.Graphics;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The <c>.simage</c> container: what the writer produces and what the reader
/// refuses. Refusal fixtures patch one field of a file the writer just made.
/// </summary>
public class SimageCodecTests
{
    [Fact]
    public void A_written_image_reads_back_as_the_levels_that_went_in()
    {
        List<byte[]> levels = Chain(TextureFormat.Bc7, 16, 16);
        byte[] file = Ktx2Writer.Write(
            TextureFormat.Bc7, 16, 16, levels, SimageRowOrder.BottomUp, EngineInfo.TextureFormatVersion);

        SimageInfo info = SimageReader.Read(file, "round-trip.simage");

        info.Format.ShouldBe(TextureFormat.Bc7);
        info.Width.ShouldBe(16);
        info.Height.ShouldBe(16);
        info.MipCount.ShouldBe(levels.Count);
        info.RowOrder.ShouldBe(SimageRowOrder.BottomUp);
        info.ProfileVersion.ShouldBe(EngineInfo.TextureFormatVersion);

        // KTX2: index 0 is the base level, but level data is stored smallest
        // first. Compare each level's bytes so a backwards pairing fails.
        for (int level = 0; level < levels.Count; level++)
        {
            TextureMipDesc mip = info.Mips[level];
            mip.Width.ShouldBe(Math.Max(1, 16 >> level));
            mip.Height.ShouldBe(Math.Max(1, 16 >> level));
            file.AsSpan(mip.Offset, levels[level].Length).ToArray().ShouldBe(levels[level]);
        }

        // The base level is last in the file.
        info.Mips[0].Offset.ShouldBeGreaterThan(info.Mips[^1].Offset);
    }

    [Fact]
    public void Every_level_starts_on_the_alignment_its_format_requires()
    {
        // Alignment lets a mapped payload reach the GPU with no copy.
        foreach ((TextureFormat format, int alignment) in
                 new[] { (TextureFormat.Bc7, 16), (TextureFormat.Bc4, 8), (TextureFormat.R8, 4) })
        {
            byte[] file = Ktx2Writer.Write(
                format, 8, 8, Chain(format, 8, 8), SimageRowOrder.BottomUp, EngineInfo.TextureFormatVersion);

            SimageInfo info = SimageReader.Read(file, $"{format}.simage");
            SimageFormat.LevelAlignment(format).ShouldBe(alignment);

            foreach (TextureMipDesc mip in info.Mips)
                (mip.Offset % alignment).ShouldBe(0, $"{format} level at {mip.Offset}");
        }
    }

    [Fact]
    public void A_written_image_is_still_a_KTX2_file_any_other_tool_can_open()
    {
        byte[] file = Valid();

        // No Spectra magic: the bytes are plain KTX2.
        file.AsSpan(0, 12).ToArray().ShouldBe(SimageFormat.Identifier.ToArray());

        // KTX2 requires a DFD. Its first field is its own total size.
        uint dfdOffset = ReadU32(file, 48);
        uint dfdLength = ReadU32(file, 52);
        dfdLength.ShouldBeGreaterThan(0u);
        ReadU32(file, (int)dfdOffset).ShouldBe(dfdLength);
    }

    [Fact]
    public void Anything_that_is_not_KTX2_is_refused_by_the_identifier()
    {
        byte[] file = Valid();
        file[3] ^= 0xFF;

        Refuse(file).Message.ShouldContain("KTX2 identifier");
    }

    [Fact]
    public void Zstandard_supercompression_is_refused_BY_NAME()
    {
        // Zstandard is in the profile, but .NET ships no decoder yet.
        byte[] file = Valid();
        WriteU32(file, 44, SimageFormat.SupercompressionZstd);

        InvalidDataException refused = Refuse(file);
        refused.Message.ShouldContain("Zstandard");
        refused.Message.ShouldContain("not implemented yet");
    }

    [Fact]
    public void BasisLZ_supercompression_is_refused_permanently_rather_than_as_a_gap()
    {
        // Unlike Zstandard this is outside the profile for good: it needs a transcoder.
        byte[] file = Valid();
        WriteU32(file, 44, SimageFormat.SupercompressionBasisLz);

        Refuse(file).Message.ShouldContain("BasisLZ");
    }

    [Fact]
    public void A_vkFormat_off_the_allowlist_is_refused_with_its_own_number()
    {
        byte[] file = Valid();
        WriteU32(file, 12, 109);  // VK_FORMAT_R16G16B16A16_SFLOAT

        InvalidDataException refused = Refuse(file);
        refused.Message.ShouldContain("109");
        refused.Message.ShouldContain("allowlist");
    }

    [Fact]
    public void A_container_shape_outside_the_profile_is_refused_and_says_which()
    {
        byte[] depth = Valid();
        WriteU32(depth, 28, 1);
        Refuse(depth).Message.ShouldContain("3D texture");

        byte[] layers = Valid();
        WriteU32(layers, 32, 4);
        Refuse(layers).Message.ShouldContain("texture array");

        byte[] faces = Valid();
        WriteU32(faces, 36, 2);
        Refuse(faces).Message.ShouldContain("1 face or 6");

        // Six faces is legal KTX2, but Renderer.CreateTexture has no cube path.
        byte[] cube = Valid();
        WriteU32(cube, 36, 6);
        Refuse(cube).Message.ShouldContain("cube map");

        byte[] generated = Valid();
        WriteU32(generated, 40, 0);
        Refuse(generated).Message.ShouldContain("generate the mip chain");
    }

    [Fact]
    public void A_level_index_the_file_is_too_short_to_hold_is_refused_rather_than_read()
    {
        // Reading past a mapped view is an access violation, not an exception.
        byte[] file = Valid();
        WriteU32(file, 40, 64);  // the level index alone is larger than the file

        Refuse(file).Message.ShouldContain("64 levels");
    }

    [Fact]
    public void A_level_whose_length_disagrees_with_its_own_size_is_refused()
    {
        // KTX2 rows are tightly packed, so the reader derives the expected length.
        byte[] file = Valid();
        int baseEntry = SimageFormat.LevelIndexOffset;
        WriteU64(file, baseEntry + 8, 12);

        Refuse(file).Message.ShouldContain("level 0");
    }

    [Fact]
    public void A_file_cooked_for_another_profile_version_names_BOTH_numbers_and_says_recook()
    {
        // Exact match or refuse: a cooked file can always be regenerated.
        int stale = EngineInfo.TextureFormatVersion + 1;
        byte[] file = Ktx2Writer.Write(
            TextureFormat.Bc7, 8, 8, Chain(TextureFormat.Bc7, 8, 8), SimageRowOrder.BottomUp, stale);

        InvalidDataException refused = Refuse(file);
        refused.Message.ShouldContain(stale.ToString(CultureInfo.InvariantCulture));
        refused.Message.ShouldContain(EngineInfo.TextureFormatVersion.ToString(CultureInfo.InvariantCulture));
        refused.Message.ShouldContain("recook");
    }

    [Fact]
    public void A_top_down_file_is_refused_because_nothing_here_can_flip_a_block()
    {
        // The engine samples v = 0 at the bottom, and block-compressed data
        // cannot be flipped at load.
        byte[] file = Ktx2Writer.Write(
            TextureFormat.Bc7,
            8,
            8,
            Chain(TextureFormat.Bc7, 8, 8),
            SimageRowOrder.TopDown,
            EngineInfo.TextureFormatVersion);

        Refuse(file).Message.ShouldContain("top-down");
    }

    [Fact]
    public void A_KTX2_file_that_is_not_ours_is_refused_for_saying_neither_thing()
    {
        // A plain KTX2 file carries neither the orientation nor the profile key.
        byte[] file = Valid();
        WriteU32(file, 60, 0);  // kvdByteLength

        Refuse(file).Message.ShouldContain(SimageFormat.ProfileKey);
    }

    [Fact]
    public void The_orientation_key_is_read_rather_than_assumed()
    {
        byte[] file = Valid();
        int at = Encoding.UTF8.GetString(file).IndexOf(SimageFormat.OrientationKey, StringComparison.Ordinal);
        at.ShouldBeGreaterThan(0);

        // "rl" is a legal KTXorientation value the profile does not accept.
        file[at + SimageFormat.OrientationKey.Length + 1] = (byte)'r';
        file[at + SimageFormat.OrientationKey.Length + 2] = (byte)'l';

        Refuse(file).Message.ShouldContain("'rl'");
    }

    private static byte[] Valid() => Ktx2Writer.Write(
        TextureFormat.Bc7,
        8,
        8,
        Chain(TextureFormat.Bc7, 8, 8),
        SimageRowOrder.BottomUp,
        EngineInfo.TextureFormatVersion);

    // Right-sized levels, not real BC blocks. The container never reads inside one.
    private static List<byte[]> Chain(TextureFormat format, int width, int height)
    {
        var levels = new List<byte[]>();
        for (int level = 0; ; level++)
        {
            int levelWidth = Math.Max(1, width >> level);
            int levelHeight = Math.Max(1, height >> level);
            int size = TextureFormatInfo.RowCount(format, levelHeight)
                * TextureFormatInfo.TightRowPitch(format, levelWidth);

            var bytes = new byte[size];
            for (int i = 0; i < size; i++) bytes[i] = (byte)(level * 41 + i);
            levels.Add(bytes);

            if (levelWidth == 1 && levelHeight == 1) break;
        }

        return levels;
    }

    private static InvalidDataException Refuse(byte[] file) =>
        Should.Throw<InvalidDataException>(() => SimageReader.Read(file, "fixture.simage"));

    private static uint ReadU32(byte[] file, int at) =>
        BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(at));

    private static void WriteU32(byte[] file, int at, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), value);

    private static void WriteU64(byte[] file, int at, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(at), value);
}
