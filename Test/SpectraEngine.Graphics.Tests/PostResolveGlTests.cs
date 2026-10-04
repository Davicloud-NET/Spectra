using Silk.NET.OpenGL;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Pixel tests of the tone-mapping resolve on a real driver: orientation and
/// brightness.
/// </summary>
[Collection(GlRendererCollection.Name)]
public sealed class PostResolveGlTests
{
    private readonly GlRendererFixture _fixture;

    public PostResolveGlTests(GlRendererFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void The_resolve_shader_compiles()
    {
        // The analyser does no name resolution or type checking, so the
        // driver is the first thing to reject a typo.
        _fixture.Renderer.CreateShaderFromSource(BaseShaders.PostResolve).ShouldNotBeNull();
    }

    [Fact]
    public void The_bottom_of_the_source_lands_at_the_bottom_of_the_output()
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        // CreateTexture takes rows bottom-up: red below, green above.
        var pixels = new byte[]
        {
            255, 0, 0, 255,   // row 0, the bottom
            0, 255, 0, 255,   // row 1, the top
        };
        Texture source = renderer.CreateTexture(
            pixels, 1, 2, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);
        RenderTarget output = renderer.CreateRenderTarget(new RenderTargetDesc(4, 4));

        try
        {
            renderer.ResolveForTest(source, output);

            // ReadPixels(0,0) is GL's bottom-left texel of the output.
            (int r, int g, _) = ReadPixel(output);
            r.ShouldBeGreaterThan(g,
                "the source's bottom row is red, so a red-dominant bottom-left texel means the " +
                "pass preserved orientation; a green one means it flipped vertically");

            (int topR, int topG, _) = ReadPixel(output, x: 0, y: 3);
            topG.ShouldBeGreaterThan(topR);
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            renderer.DestroyTexture(source);
        }
    }

    [Fact]
    public void The_tone_curve_maps_white_to_just_under_white()
    {
        // The ACES fit sends 1.0 to about 0.80. 255 would mean no curve ran.
        OpenGLRenderer renderer = _fixture.Renderer;
        var pixels = new byte[] { 255, 255, 255, 255 };
        Texture source = renderer.CreateTexture(
            pixels, 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);
        RenderTarget output = renderer.CreateRenderTarget(new RenderTargetDesc(4, 4));

        try
        {
            renderer.ResolveForTest(source, output);

            (int r, _, _) = ReadPixel(output);
            // 2.54 / 3.16 = 0.8038 -> 205.
            r.ShouldBeInRange(200, 210);
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            renderer.DestroyTexture(source);
        }
    }

    [Fact]
    public void An_overbright_input_survives_instead_of_clipping()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget hdr = renderer.CreateRenderTarget(
            new RenderTargetDesc(4, 4, TextureFormat.Rgba16Float));
        RenderTarget output = renderer.CreateRenderTarget(new RenderTargetDesc(4, 4));

        try
        {
            renderer.BeginPass(hdr, PassClear.To(new System.Numerics.Vector4(1f, 1f, 1f, 1f)));
            renderer.EndPass();
            renderer.ResolveForTest(hdr.ColorTexture!, output);
            (int atOne, _, _) = ReadPixel(output);

            renderer.BeginPass(hdr, PassClear.To(new System.Numerics.Vector4(8f, 8f, 8f, 1f)));
            renderer.EndPass();
            renderer.ResolveForTest(hdr.ColorTexture!, output);
            (int atEight, _, _) = ReadPixel(output);

            atEight.ShouldBeGreaterThan(atOne,
                "8.0 and 1.0 must reach the display as different values, or the intermediate " +
                "target is not carrying the range the tone curve exists to compress");
            atEight.ShouldBeLessThanOrEqualTo(255);
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            renderer.DestroyRenderTarget(hdr);
        }
    }

    [Fact]
    public void Exposure_scales_before_the_curve()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        var pixels = new byte[] { 64, 64, 64, 255 };
        Texture source = renderer.CreateTexture(
            pixels, 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);
        RenderTarget output = renderer.CreateRenderTarget(new RenderTargetDesc(4, 4));

        float restore = renderer.Exposure;
        try
        {
            renderer.Exposure = 1f;
            renderer.ResolveForTest(source, output);
            (int dim, _, _) = ReadPixel(output);

            renderer.Exposure = 4f;
            renderer.ResolveForTest(source, output);
            (int bright, _, _) = ReadPixel(output);

            bright.ShouldBeGreaterThan(dim);
        }
        finally
        {
            renderer.Exposure = restore;
            renderer.DestroyRenderTarget(output);
            renderer.DestroyTexture(source);
        }
    }

    private unsafe (int R, int G, int B) ReadPixel(RenderTarget target, int x = 0, int y = 0)
    {
        GL gl = _fixture.Gl;
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
        gl.FramebufferTexture2D(
            FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, ((OpenGLTexture)target.ColorTexture!).Handle, 0);

        var pixel = new byte[4];
        fixed (byte* p = pixel)
            gl.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.DeleteFramebuffer(fbo);
        return (pixel[0], pixel[1], pixel[2]);
    }
}
