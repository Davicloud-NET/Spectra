using Silk.NET.OpenGL;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Offscreen render targets against a real driver, checked by reading pixels back.
/// </summary>
[Collection(GlRendererCollection.Name)]
public sealed class GlRenderTargetTests
{
    private readonly GlRendererFixture _fixture;

    public GlRenderTargetTests(GlRendererFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void A_pass_lands_in_the_target_and_not_on_the_window()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(32, 32));
        try
        {
            // Nothing else in this process clears to green.
            var green = new System.Numerics.Vector4(0f, 1f, 0f, 1f);

            renderer.BeginPass(target, PassClear.To(green));
            renderer.PassSize.X.ShouldBe(32);
            renderer.PassSize.Y.ShouldBe(32);
            renderer.EndPass();

            ReadPixel(target).ShouldBe((0, 255, 0));
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void The_pass_size_comes_from_the_target_not_the_window()
    {
        // The fixture's window is 64x64; this target is not square, so a
        // leaked window size shows as the wrong aspect.
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(80, 20));
        try
        {
            renderer.BeginPass(target, PassClear.Keep);

            renderer.PassSize.X.ShouldBe(80);
            renderer.PassSize.Y.ShouldBe(20);
            renderer.PassAspectRatio!.Value.ShouldBe(4f, 1e-6f);

            renderer.EndPass();
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void A_resize_keeps_the_colour_texture_identity()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(16, 16));
        try
        {
            Texture before = target.ColorTexture!;
            uint handleBefore = ((OpenGLTexture)before).Handle;

            target.Resize(48, 24);

            // Materials sampling this target hold this object.
            target.ColorTexture!.ShouldBeSameAs(before);
            ((OpenGLTexture)target.ColorTexture!).Handle.ShouldBe(handleBefore);

            before.Width.ShouldBe(48);
            before.Height.ShouldBe(24);
            target.Width.ShouldBe(48);
            target.Height.ShouldBe(24);
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void A_resized_target_still_renders()
    {
        // An FBO whose attachment storage was respecified must be re-completed,
        // or it draws nothing with no error.
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(16, 16));
        try
        {
            target.Resize(64, 8);

            var blue = new System.Numerics.Vector4(0f, 0f, 1f, 1f);
            renderer.BeginPass(target, PassClear.To(blue));
            renderer.PassSize.X.ShouldBe(64);
            renderer.EndPass();

            ReadPixel(target).ShouldBe((0, 0, 255));
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void A_resize_to_the_same_size_is_a_no_op()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(24, 24));
        try
        {
            target.Resize(24, 24);
            target.Width.ShouldBe(24);
            target.Height.ShouldBe(24);
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void An_srgb_target_encodes_what_is_written_into_it()
    {
        // 0.2140 linear is byte 128.
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(
            new RenderTargetDesc(8, 8, ColorSpace: TextureColorSpace.Srgb));
        try
        {
            target.ColorTexture!.ColorSpace.ShouldBe(TextureColorSpace.Srgb);

            renderer.BeginPass(target, PassClear.To(new System.Numerics.Vector4(0.2140f, 0.2140f, 0.2140f, 1f)));
            renderer.EndPass();

            (int r, _, _) = ReadPixel(target);
            r.ShouldBeInRange(127, 129);
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void A_linear_target_leaves_what_is_written_alone()
    {
        // Control for the test above.
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(
            new RenderTargetDesc(8, 8, ColorSpace: TextureColorSpace.Linear));
        try
        {
            renderer.BeginPass(target, PassClear.To(new System.Numerics.Vector4(0.2140f, 0.2140f, 0.2140f, 1f)));
            renderer.EndPass();

            (int r, _, _) = ReadPixel(target);
            r.ShouldBeInRange(53, 56); // 0.2140 * 255, unconverted
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void Ending_a_target_pass_puts_the_window_back()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(16, 16));
        try
        {
            renderer.BeginPass(target, PassClear.Keep);
            renderer.EndPass();

            _fixture.Gl.GetInteger(GLEnum.DrawFramebufferBinding, out int bound);
            bound.ShouldBe(0);
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void A_target_cannot_be_destroyed_while_a_pass_is_drawing_into_it()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(16, 16));

        renderer.BeginPass(target, PassClear.Keep);
        Should.Throw<InvalidOperationException>(() => renderer.DestroyRenderTarget(target));
        renderer.EndPass();

        renderer.DestroyRenderTarget(target);
    }

    [Fact]
    public void An_hdr_target_stores_values_above_one()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(
            new RenderTargetDesc(8, 8, TextureFormat.Rgba16Float));
        try
        {
            target.ColorTexture!.Format.ShouldBe(TextureFormat.Rgba16Float);
            // Float formats have no sRGB variant.
            target.ColorTexture!.ColorSpace.ShouldBe(TextureColorSpace.Linear);

            renderer.BeginPass(target, PassClear.To(new System.Numerics.Vector4(4f, 2f, 0.5f, 1f)));
            renderer.EndPass();

            (float r, float g, float b) = ReadPixelFloat(target);
            r.ShouldBe(4f, 0.01f);
            g.ShouldBe(2f, 0.01f);
            b.ShouldBe(0.5f, 0.01f);
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void An_eight_bit_target_clamps_the_same_value()
    {
        // Control for the test above: same clear, Rgba8 target.
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(8, 8));
        try
        {
            renderer.BeginPass(target, PassClear.To(new System.Numerics.Vector4(4f, 2f, 0.5f, 1f)));
            renderer.EndPass();

            (int r, int g, _) = ReadPixel(target);
            r.ShouldBe(255);
            g.ShouldBe(255);
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void A_float_format_is_refused_as_an_uploaded_texture()
    {
        var pixels = new byte[] { 255, 255, 255, 255 };

        Should.Throw<ArgumentOutOfRangeException>(() => _fixture.Renderer.CreateTexture(
            pixels, 1, 1, TextureFormat.Rgba16Float, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp));
    }

    [Fact]
    public void A_multi_target_pass_writes_every_attachment()
    {
        // An FBO writes attachment 0 only unless the draw-buffer list says otherwise.
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget a = renderer.CreateRenderTarget(new RenderTargetDesc(8, 8));
        RenderTarget b = renderer.CreateRenderTarget(new RenderTargetDesc(8, 8, Depth: false));
        RenderTarget c = renderer.CreateRenderTarget(
            new RenderTargetDesc(8, 8, TextureFormat.Rgba16Float, Depth: false));

        try
        {
            RenderTarget[] targets = [a, b, c];
            renderer.BeginPass(targets, PassClear.To(new System.Numerics.Vector4(0.5f, 0f, 0f, 1f)));
            renderer.EndPass();

            ReadPixel(a).R.ShouldBeInRange(126, 130);
            ReadPixel(b).R.ShouldBeInRange(126, 130);
            ReadPixelFloat(c).R.ShouldBe(0.5f, 0.01f);
        }
        finally
        {
            renderer.DestroyRenderTarget(c);
            renderer.DestroyRenderTarget(b);
            renderer.DestroyRenderTarget(a);
        }
    }

    [Fact]
    public void Targets_of_different_sizes_are_refused()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget a = renderer.CreateRenderTarget(new RenderTargetDesc(8, 8));
        RenderTarget b = renderer.CreateRenderTarget(new RenderTargetDesc(16, 8, Depth: false));

        try
        {
            RenderTarget[] targets = [a, b];
            Should.Throw<ArgumentException>(() => renderer.BeginPass(targets, PassClear.Keep));
        }
        finally
        {
            renderer.DestroyRenderTarget(b);
            renderer.DestroyRenderTarget(a);
        }
    }

    [Fact]
    public void A_multi_target_pass_leaves_the_framebuffer_single_target()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget a = renderer.CreateRenderTarget(new RenderTargetDesc(8, 8));
        RenderTarget b = renderer.CreateRenderTarget(new RenderTargetDesc(8, 8, Depth: false));

        try
        {
            RenderTarget[] targets = [a, b];
            renderer.BeginPass(targets, PassClear.To(new System.Numerics.Vector4(1f, 0f, 0f, 1f)));
            renderer.EndPass();

            renderer.BeginPass(a, PassClear.To(new System.Numerics.Vector4(0f, 0f, 0f, 1f)));
            renderer.EndPass();

            ReadPixel(a).R.ShouldBe(0);
            ReadPixel(b).R.ShouldBe(255, "the second attachment should have been left alone");
        }
        finally
        {
            renderer.DestroyRenderTarget(b);
            renderer.DestroyRenderTarget(a);
        }
    }

    [Fact]
    public void Depth_is_a_sampleable_texture_carrying_what_was_written()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(8, 8));

        try
        {
            target.DepthTexture.ShouldNotBeNull();
            target.DepthTexture!.Format.ShouldBe(TextureFormat.Depth32Float);

            // Neither default depth, so the read-back can't be a coincidence.
            renderer.BeginPass(target, new PassClear(null, 0.25f));
            renderer.EndPass();

            ReadDepth(target).ShouldBe(0.25f, 0.001f);
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void A_depthless_target_has_no_depth_texture()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(8, 8, Depth: false));

        try
        {
            target.DepthTexture.ShouldBeNull();
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    [Fact]
    public void A_resize_keeps_the_depth_texture_identity_too()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(8, 8));

        try
        {
            Texture before = target.DepthTexture!;
            target.Resize(32, 16);

            target.DepthTexture.ShouldBeSameAs(before);
            before.Width.ShouldBe(32);
            before.Height.ShouldBe(16);

            renderer.BeginPass(target, new PassClear(null, 0.75f));
            renderer.EndPass();
            ReadDepth(target).ShouldBe(0.75f, 0.001f);
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
        }
    }

    private unsafe float ReadDepth(RenderTarget target)
    {
        GL gl = _fixture.Gl;
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
        gl.FramebufferTexture2D(
            FramebufferTarget.ReadFramebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, ((OpenGLTexture)target.DepthTexture!).Handle, 0);

        float value = 0f;
        gl.ReadPixels(0, 0, 1, 1, PixelFormat.DepthComponent, PixelType.Float, &value);

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.DeleteFramebuffer(fbo);
        return value;
    }

    // Texel (0,0) as floats. The byte reader clamps to [0,1].
    private unsafe (float R, float G, float B) ReadPixelFloat(RenderTarget target)
    {
        GL gl = _fixture.Gl;
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
        gl.FramebufferTexture2D(
            FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, ((OpenGLTexture)target.ColorTexture!).Handle, 0);

        var pixel = new float[4];
        fixed (float* p = pixel)
            gl.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.Float, p);

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.DeleteFramebuffer(fbo);
        return (pixel[0], pixel[1], pixel[2]);
    }

    // Texel (0,0) as bytes.
    private unsafe (int R, int G, int B) ReadPixel(RenderTarget target)
    {
        GL gl = _fixture.Gl;
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
        gl.FramebufferTexture2D(
            FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, ((OpenGLTexture)target.ColorTexture!).Handle, 0);

        var pixel = new byte[4];
        fixed (byte* p = pixel)
            gl.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.DeleteFramebuffer(fbo);
        return (pixel[0], pixel[1], pixel[2]);
    }
}
