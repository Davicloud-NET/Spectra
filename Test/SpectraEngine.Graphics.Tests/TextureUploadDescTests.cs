using SpectraEngine.Core.Graphics;
using System;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// What a texture upload descriptor refuses, and what the format table says
/// about colour space and block geometry.
/// </summary>
public sealed class TextureUploadDescTests
{
    [Fact]
    public void A_mip_whose_pitch_overruns_the_payload_is_refused_by_name()
    {
        // 16x16 BC7 is four block rows of 64 bytes. The second mip's declared
        // pitch of 128 runs past the end of the payload.
        var payload = new byte[256];
        TextureMipDesc[] mips =
        [
            new TextureMipDesc(16, 16, 0, 64),
            new TextureMipDesc(8, 8, 256, 128),
        ];

        var exception = Should.Throw<ArgumentException>(() => new TextureUploadDesc(
            TextureFormat.Bc7, TextureColorSpace.Linear, payload, mips).Validate());

        exception.Message.ShouldContain("Mip 1");
        exception.Message.ShouldContain("8x8");
    }

    [Fact]
    public void A_pitch_below_the_tight_one_is_refused_by_name()
    {
        // Tight pitch for a 16-wide BC7 row is 64.
        var payload = new byte[4096];
        TextureMipDesc[] mips = [new TextureMipDesc(16, 16, 0, 32)];

        var exception = Should.Throw<ArgumentException>(() => new TextureUploadDesc(
            TextureFormat.Bc7, TextureColorSpace.Linear, payload, mips).Validate());

        exception.Message.ShouldContain("Mip 0");
        exception.Message.ShouldContain("64");
    }

    [Fact]
    public void A_chain_that_does_not_halve_is_refused_by_name()
    {
        // The graphics API derives each level's size from the base one.
        var payload = new byte[8192];
        TextureMipDesc[] mips =
        [
            new TextureMipDesc(16, 16, 0, 64),
            new TextureMipDesc(4, 4, 4096, 16),
        ];

        var exception = Should.Throw<ArgumentException>(() => new TextureUploadDesc(
            TextureFormat.Bc7, TextureColorSpace.Linear, payload, mips).Validate());

        exception.Message.ShouldContain("Mip 1");
        exception.Message.ShouldContain("8x8");
    }

    [Fact]
    public void A_tightly_packed_two_level_BC7_payload_is_accepted()
    {
        byte[] payload = Bc7Fixture.BuildTwoLevelPayload(padded: false, out TextureMipDesc[] mips);

        Should.NotThrow(() => new TextureUploadDesc(
            TextureFormat.Bc7, TextureColorSpace.Linear, payload, mips).Validate());
    }

    [Fact]
    public void A_padded_two_level_BC7_payload_is_accepted()
    {
        byte[] payload = Bc7Fixture.BuildTwoLevelPayload(padded: true, out TextureMipDesc[] mips);

        Should.NotThrow(() => new TextureUploadDesc(
            TextureFormat.Bc7, TextureColorSpace.Linear, payload, mips).Validate());
    }

    [Fact]
    public void A_payload_that_stops_after_the_last_row_is_accepted()
    {
        // The last row needs only its real bytes, not a full pitch. Same bound
        // GetCopyableFootprints reports as its total size.
        var payload = new byte[64 * 3 + 32];
        TextureMipDesc[] mips = [new TextureMipDesc(8, 16, 0, 64)];

        Should.NotThrow(() => new TextureUploadDesc(
            TextureFormat.Bc7, TextureColorSpace.Linear, payload, mips).Validate());
    }

    [Fact]
    public void A_float_format_is_still_refused_out_of_range()
    {
        // A different exception type from the layout refusals; callers test for it.
        var pixels = new byte[16];

        Should.Throw<ArgumentOutOfRangeException>(() => TextureUploadDesc
            .SingleLevel(pixels, 1, 1, TextureFormat.Rgba16Float, TextureColorSpace.Linear)
            .Validate());
    }

    [Theory]
    [InlineData(TextureFormat.Rgba8, true)]
    [InlineData(TextureFormat.Rgb8, true)]
    [InlineData(TextureFormat.R8, false)]
    [InlineData(TextureFormat.Bc1, true)]
    [InlineData(TextureFormat.Bc3, true)]
    [InlineData(TextureFormat.Bc4, false)]
    [InlineData(TextureFormat.Bc5, false)]
    [InlineData(TextureFormat.Bc6H, false)]
    [InlineData(TextureFormat.Bc7, true)]
    [InlineData(TextureFormat.Rgba16Float, false)]
    [InlineData(TextureFormat.Depth32Float, false)]
    public void The_format_table_knows_which_formats_have_an_sRGB_twin(
        TextureFormat format, bool expected)
    {
        // BC4 and BC5 have no sRGB form in any API, and BC6H is float.
        TextureFormatInfo.SupportsSrgb(format).ShouldBe(expected);

        TextureFormatInfo.Resolve(format, TextureColorSpace.Srgb)
            .ShouldBe(expected ? TextureColorSpace.Srgb : TextureColorSpace.Linear);
        TextureFormatInfo.Resolve(format, TextureColorSpace.Linear)
            .ShouldBe(TextureColorSpace.Linear);
    }

    [Theory]
    [InlineData(TextureFormat.Bc1, 8)]
    [InlineData(TextureFormat.Bc3, 16)]
    [InlineData(TextureFormat.Bc4, 8)]
    [InlineData(TextureFormat.Bc5, 16)]
    [InlineData(TextureFormat.Bc6H, 16)]
    [InlineData(TextureFormat.Bc7, 16)]
    public void A_block_format_reports_its_block_geometry(TextureFormat format, int bytesPerBlock)
    {
        TextureFormatInfo.IsBlockCompressed(format).ShouldBeTrue();
        TextureFormatInfo.BlockWidth(format).ShouldBe(4);
        TextureFormatInfo.BlockHeight(format).ShouldBe(4);
        TextureFormatInfo.BytesPerBlock(format).ShouldBe(bytesPerBlock);

        // A 1x1 mip still costs a whole block.
        TextureFormatInfo.TightRowPitch(format, 1).ShouldBe(bytesPerBlock);
        TextureFormatInfo.RowCount(format, 1).ShouldBe(1);
        TextureFormatInfo.RowCount(format, 5).ShouldBe(2);
    }

    [Fact]
    public void BC6H_is_not_reported_as_a_float_format()
    {
        // IsFloat guards formats a byte array cannot fill. BC6H holds half-floats
        // but is uploaded as blocks of bytes.
        TextureFormatInfo.IsFloat(TextureFormat.Bc6H).ShouldBeFalse();
        TextureFormatInfo.IsFloat(TextureFormat.Rgba16Float).ShouldBeTrue();
        TextureFormatInfo.IsFloat(TextureFormat.Depth32Float).ShouldBeTrue();
    }

    [Fact]
    public void An_uncompressed_format_is_one_texel_per_block()
    {
        TextureFormatInfo.IsBlockCompressed(TextureFormat.Rgba8).ShouldBeFalse();
        TextureFormatInfo.BlockWidth(TextureFormat.Rgba8).ShouldBe(1);
        TextureFormatInfo.BlockHeight(TextureFormat.Rgba8).ShouldBe(1);
        TextureFormatInfo.TightRowPitch(TextureFormat.Rgba8, 7).ShouldBe(28);
        TextureFormatInfo.TightRowPitch(TextureFormat.Rgb8, 7).ShouldBe(21);
        TextureFormatInfo.TightRowPitch(TextureFormat.R8, 7).ShouldBe(7);
        TextureFormatInfo.RowCount(TextureFormat.Rgba8, 7).ShouldBe(7);
    }

    [Fact]
    public void A_single_level_descriptor_states_the_tight_pitch()
    {
        var pixels = new byte[3 * 2 * 4];
        TextureUploadDesc desc = TextureUploadDesc.SingleLevel(
            pixels, 3, 2, TextureFormat.Rgba8, TextureColorSpace.Srgb);

        desc.MipCount.ShouldBe(1);
        desc.HasSuppliedMipChain.ShouldBeFalse();
        desc.Width.ShouldBe(3);
        desc.Height.ShouldBe(2);
        desc.Mips[0].RowPitch.ShouldBe(12);
        desc.Mips[0].Offset.ShouldBe(0);

        // Called directly: a ref struct cannot be captured by a lambda.
        desc.Validate();
    }
}
