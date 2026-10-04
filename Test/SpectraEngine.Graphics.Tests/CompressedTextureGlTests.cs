using Silk.NET.OpenGL;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using System;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// A block-compressed texture uploaded through <see cref="TextureUploadDesc"/>
/// and read back texel by texel on a real driver.
/// </summary>
[Collection(GlRendererCollection.Name)]
public sealed class CompressedTextureGlTests
{
    private readonly GlRendererFixture _fixture;

    private const int TargetSize = 16;

    public CompressedTextureGlTests(GlRendererFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void The_driver_offers_BPTC_at_all()
    {
        // So a machine without BC7 fails here, not in the pixel assertions.
        GL gl = _fixture.Gl;
        string version = gl.GetStringS(StringName.Version) ?? string.Empty;

        bool extension = false;
        gl.GetInteger(GetPName.NumExtensions, out int count);
        for (int i = 0; i < count && !extension; i++)
            extension = gl.GetStringS(StringName.Extensions, (uint)i) == "GL_ARB_texture_compression_bptc";

        extension.ShouldBeTrue(
            $"BC7 needs GL_ARB_texture_compression_bptc (core since 4.2); this context reports '{version}'.");
    }

    [Fact]
    public void A_two_mip_BC7_texture_samples_its_base_level()
    {
        // Nearest: no mip selection, so only level 0 is measured.
        AssertBaseLevelIsUpright(padded: false, TextureFilter.Nearest);
    }

    [Fact]
    public void A_padded_row_pitch_produces_the_same_picture()
    {
        // Pitch 256 against a tight 64, the layout D3D12's copy alignment produces.
        AssertBaseLevelIsUpright(padded: true, TextureFilter.Nearest);
    }

    [Fact]
    public void The_second_level_of_a_supplied_chain_is_the_level_that_was_supplied()
    {
        // Every level 0 colour has a dark channel and level 1 has none, so
        // bright everywhere means level 1. An incomplete texture samples black.
        OpenGLRenderer renderer = _fixture.Renderer;
        byte[] payload = Bc7Fixture.BuildTwoLevelPayload(padded: false, out TextureMipDesc[] mips);

        Texture source = renderer.CreateTexture(new TextureUploadDesc(
            TextureFormat.Bc7, TextureColorSpace.Linear, payload, mips,
            TextureFilter.LinearMipmap, TextureWrap.Clamp));

        // 16 texels over 4 pixels is LOD 2, clamped to level 1.
        const int minifiedSize = 4;
        RenderTarget output = renderer.CreateRenderTarget(
            new RenderTargetDesc(minifiedSize, minifiedSize, TextureFormat.Rgba8, TextureColorSpace.Linear));

        try
        {
            renderer.DrawOrientationQuad(source, output, OrientationQuad.Coverage.Full);

            for (int y = 0; y < minifiedSize; y++)
            {
                for (int x = 0; x < minifiedSize; x++)
                {
                    (byte r, byte g, byte b, _) = renderer.ReadTargetPixel(output, x, y);
                    string where = $"({x}, {y}) read {r}, {g}, {b}";
                    r.ShouldBeGreaterThan((byte)120, where);
                    g.ShouldBeGreaterThan((byte)120, where);
                    b.ShouldBeGreaterThan((byte)120, where);
                }
            }
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            renderer.DestroyTexture(source);
        }
    }

    [Fact]
    public void A_BC7_upload_reports_its_own_size_and_resolved_colour_space()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        byte[] payload = Bc7Fixture.BuildTwoLevelPayload(padded: false, out TextureMipDesc[] mips);

        Texture source = renderer.CreateTexture(new TextureUploadDesc(
            TextureFormat.Bc7, TextureColorSpace.Srgb, payload, mips,
            TextureFilter.Nearest, TextureWrap.Clamp));

        try
        {
            source.Width.ShouldBe(Bc7Fixture.BaseSize);
            source.Height.ShouldBe(Bc7Fixture.BaseSize);
            source.Format.ShouldBe(TextureFormat.Bc7);
            // BC7 has an sRGB form. BC4 and BC5 would come back linear.
            source.ColorSpace.ShouldBe(TextureColorSpace.Srgb);
        }
        finally
        {
            renderer.DestroyTexture(source);
        }
    }

    private void AssertBaseLevelIsUpright(bool padded, TextureFilter filter)
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        byte[] payload = Bc7Fixture.BuildTwoLevelPayload(padded, out TextureMipDesc[] mips);

        // Linear on both sides: only the tone curve sits between the bytes.
        Texture source = renderer.CreateTexture(new TextureUploadDesc(
            TextureFormat.Bc7, TextureColorSpace.Linear, payload, mips, filter, TextureWrap.Clamp));
        RenderTarget output = renderer.CreateRenderTarget(
            new RenderTargetDesc(TargetSize, TargetSize, TextureFormat.Rgba8, TextureColorSpace.Linear));

        try
        {
            renderer.DrawOrientationQuad(source, output, OrientationQuad.Coverage.Full);

            var reading = new TextureOrientationProbe.Reading(
                Read(renderer, output, 0, TargetSize - 1),
                Read(renderer, output, TargetSize - 1, TargetSize - 1),
                Read(renderer, output, 0, 0),
                Read(renderer, output, TargetSize - 1, 0));

            reading.MatchesAuthoredImage.ShouldBeTrue(
                $"a {(padded ? "padded" : "tight")} BC7 upload rendered as: {reading}. Verdict: {reading.Verdict}");
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            renderer.DestroyTexture(source);
        }
    }

    private static TextureOrientationProbe.Quadrant Read(
        Renderer renderer, RenderTarget target, int x, int y)
    {
        (byte r, byte g, byte b, _) = renderer.ReadTargetPixel(target, x, y);
        return TextureOrientationProbe.Classify(r, g, b);
    }
}
