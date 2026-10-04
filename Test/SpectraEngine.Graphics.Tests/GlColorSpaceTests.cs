using Silk.NET.OpenGL;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
// Silk.NET.OpenGL has its own Texture.
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// sRGB encode and decode on OpenGL, against a real driver.
/// </summary>
[Collection(GlRendererCollection.Name)]
public sealed class GlColorSpaceTests
{
    private readonly GlRendererFixture _fixture;

    public GlColorSpaceTests(GlRendererFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Output_reaches_the_display_encoded_by_one_route_or_the_other()
    {
        _fixture.Renderer.FramebufferSrgb.ShouldBeTrue(
            "neither the window framebuffer nor the offscreen fallback is encoding, so shader " +
            "output reaches the display uncorrected and will not match D3D11 or D3D12");
    }

    [Fact]
    public unsafe void A_linear_clear_lands_on_the_window_as_its_display_value()
    {
        // GL_FRAMEBUFFER_SRGB on a framebuffer that isn't sRGB-capable does
        // nothing and reports no error, so read a pixel back.
        // 0.2140 linear is byte 128 encoded, 55 if the encode was skipped.
        GL gl = _fixture.Gl;
        const int W = 8, H = 8;
        const float Linear = 0.2140f;

        bool offscreen = _fixture.Renderer.BeginSrgbTargetForTest(W, H);
        try
        {
            gl.Viewport(0, 0, W, H);
            gl.ClearColor(Linear, Linear, Linear, 1f);
            gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        }
        finally
        {
            if (offscreen)
                _fixture.Renderer.PresentSrgbTargetForTest(W, H);
        }

        var pixel = new byte[4];
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        fixed (byte* p = pixel)
            gl.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        // One code of slack for driver rounding.
        ((int)pixel[0]).ShouldBeInRange(127, 129);
        ((int)pixel[1]).ShouldBeInRange(127, 129);
        ((int)pixel[2]).ShouldBeInRange(127, 129);
    }

    [Fact]
    public unsafe void A_colour_texture_gets_an_srgb_internal_format()
    {
        ReadOnlySpan<byte> pixels = [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255];

        Texture texture = _fixture.Renderer.CreateTexture(
            pixels, 2, 2, TextureFormat.Rgba8, TextureColorSpace.Srgb,
            TextureFilter.Linear, TextureWrap.Clamp);

        // GL_SRGB8_ALPHA8 makes the hardware decode before filtering.
        InternalFormat(texture).ShouldBe((int)GLEnum.Srgb8Alpha8);
        texture.ColorSpace.ShouldBe(TextureColorSpace.Srgb);

        _fixture.Renderer.DestroyTexture(texture);
    }

    [Fact]
    public unsafe void A_data_texture_is_left_alone()
    {
        ReadOnlySpan<byte> pixels = [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255];

        Texture texture = _fixture.Renderer.CreateTexture(
            pixels, 2, 2, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Linear, TextureWrap.Clamp);

        InternalFormat(texture).ShouldBe((int)GLEnum.Rgba8);
        texture.ColorSpace.ShouldBe(TextureColorSpace.Linear);

        _fixture.Renderer.DestroyTexture(texture);
    }

    [Fact]
    public unsafe void A_single_channel_texture_asked_for_srgb_stays_linear()
    {
        ReadOnlySpan<byte> pixels = [0, 64, 128, 255];

        // There is no GL_SR8.
        Texture texture = _fixture.Renderer.CreateTexture(
            pixels, 2, 2, TextureFormat.R8, TextureColorSpace.Srgb,
            TextureFilter.Nearest, TextureWrap.Clamp);

        InternalFormat(texture).ShouldBe((int)GLEnum.R8);
        texture.ColorSpace.ShouldBe(TextureColorSpace.Linear);

        _fixture.Renderer.DestroyTexture(texture);
    }

    // What the driver says it stored.
    private unsafe int InternalFormat(Texture texture)
    {
        GL gl = _fixture.Gl;
        gl.BindTexture(TextureTarget.Texture2D, ((OpenGLTexture)texture).Handle);

        int format = 0;
        gl.GetTexLevelParameter(GLEnum.Texture2D, 0, GLEnum.TextureInternalFormat, &format);

        gl.BindTexture(TextureTarget.Texture2D, 0);
        return format;
    }
}
