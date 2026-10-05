using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Graphics.Tests;

/// <summary>The rectangle a run of rows covers in a mip when it is uploaded.</summary>
public sealed class TextureUploadBoxTests
{
    [Theory]
    [InlineData(2, 2)]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    public void A_compressed_mip_smaller_than_a_block_uploads_as_one_whole_block(int width, int height)
    {
        // D3D11 refuses a box that is not in whole blocks, so the 2x2 and 1x1
        // mips of every cooked texture were never written.
        var mip = new TextureMipDesc(width, height, Offset: 0, RowPitch: 16);

        TextureUploadLayout.RowBox(TextureFormat.Bc7, in mip, firstRow: 0, rowCount: 1)
            .ShouldBe((0u, 4u, 4u));
    }

    [Fact]
    public void A_run_of_block_rows_is_placed_in_texels()
    {
        var mip = new TextureMipDesc(16, 8, Offset: 0, RowPitch: 64);

        TextureUploadLayout.RowBox(TextureFormat.Bc7, in mip, firstRow: 1, rowCount: 1)
            .ShouldBe((4u, 16u, 8u));
    }

    [Fact]
    public void A_compressed_mip_that_is_not_a_multiple_of_four_is_rounded_up()
    {
        var mip = new TextureMipDesc(6, 6, Offset: 0, RowPitch: 32);

        TextureUploadLayout.RowBox(TextureFormat.Bc7, in mip, firstRow: 0, rowCount: 2)
            .ShouldBe((0u, 8u, 8u));
    }

    [Fact]
    public void An_uncompressed_mip_is_addressed_texel_for_texel()
    {
        var mip = new TextureMipDesc(2, 2, Offset: 0, RowPitch: 8);

        TextureUploadLayout.RowBox(TextureFormat.Rgba8, in mip, firstRow: 0, rowCount: 2)
            .ShouldBe((0u, 2u, 2u));
        TextureUploadLayout.RowBox(TextureFormat.Rgba8, in mip, firstRow: 1, rowCount: 1)
            .ShouldBe((1u, 2u, 2u));
    }
}
