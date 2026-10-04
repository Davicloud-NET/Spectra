using Silk.NET.OpenGL;
using System;

namespace SpectraEngine.Core.Graphics.OpenGL;

// FBO with optional colour and depth attachments. Both are textures because
// both get sampled.
internal sealed class OpenGLRenderTarget : RenderTarget
{
    private readonly GL _gl;
    private readonly OpenGLTexture? _color;
    private readonly OpenGLTexture? _depth;
    private uint _fbo;
    private bool _disposed;

    internal uint Framebuffer => _fbo;

    internal OpenGLRenderTarget(GL gl, in RenderTargetDesc desc)
    {
        desc.Validate();

        _gl = gl;
        Desc = desc;

        if (desc.Color)
        {
            _color = OpenGLTexture.CreateEmpty(
                gl, desc.Width, desc.Height, desc.ColorFormat, desc.ColorSpace, desc.Filter, desc.Wrap);
        }

        if (desc.Depth)
        {
            // Nearest: an interpolated depth lies on neither surface.
            _depth = OpenGLTexture.CreateEmpty(
                gl, desc.Width, desc.Height, TextureFormat.Depth32Float, TextureColorSpace.Linear,
                TextureFilter.Nearest, TextureWrap.Clamp);
        }

        Allocate(desc.Width, desc.Height);
    }

    public override Texture? ColorTexture => _color;

    public override Texture? DepthTexture => _depth;

    public override void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width == Width && height == Height) return;
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Render target size must be positive; got {width}x{height}.");

        // Same wrapper and GL name, so materials sampling it stay valid.
        _color?.ReallocateStorage(width, height);
        _depth?.ReallocateStorage(width, height);
        Allocate(width, height);
    }

    private void Allocate(int width, int height)
    {
        if (_fbo == 0)
            _fbo = _gl.GenFramebuffer();

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        if (_color is not null)
        {
            _gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _color.Handle, 0);
        }
        else
        {
            // Depth-only: both must be None or the framebuffer is incomplete.
            _gl.DrawBuffer(DrawBufferMode.None);
            _gl.ReadBuffer(ReadBufferMode.None);
        }

        if (_depth is not null)
        {
            // Not DepthStencilAttachment: the format has no stencil and the
            // framebuffer would be incomplete.
            _gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D, _depth.Handle, 0);
        }

        GLEnum status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        if (status != GLEnum.FramebufferComplete)
        {
            Dispose();
            throw new InvalidOperationException(
                $"OpenGL refused a {width}x{height} render target: framebuffer status {status}.");
        }

        Width = width;
        Height = height;
    }

    // Attaches the other targets of a multi-target pass as colour attachments
    // 1..N-1. DrawBuffers is required: a framebuffer writes only attachment 0
    // by default.
    internal void BindExtraColorTargets(GL gl, ReadOnlySpan<RenderTarget> targets)
    {
        if (targets.Length <= 1) return;

        Span<GLEnum> buffers = stackalloc GLEnum[targets.Length];
        buffers[0] = GLEnum.ColorAttachment0;

        for (int i = 1; i < targets.Length; i++)
        {
            var extra = (OpenGLRenderTarget)targets[i];
            var attachment = (FramebufferAttachment)(GLEnum.ColorAttachment0 + i);
            gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer, attachment,
                TextureTarget.Texture2D, ((OpenGLTexture)extra.ColorTexture!).Handle, 0);
            buffers[i] = GLEnum.ColorAttachment0 + i;
        }

        gl.DrawBuffers((uint)targets.Length, buffers);

        GLEnum status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException(
                $"OpenGL refused a {targets.Length}-attachment pass: framebuffer status {status}.");
        }
    }

    internal void UnbindExtraColorTargets(GL gl, ReadOnlySpan<RenderTarget> targets)
    {
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        for (int i = 1; i < targets.Length; i++)
        {
            var attachment = (FramebufferAttachment)(GLEnum.ColorAttachment0 + i);
            gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer, attachment, TextureTarget.Texture2D, 0, 0);
        }

        Span<GLEnum> single = [GLEnum.ColorAttachment0];
        gl.DrawBuffers(1, single);
    }

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_fbo != 0) _gl.DeleteFramebuffer(_fbo);
        _fbo = 0;
        _depth?.Dispose();

        _color?.Dispose();
    }
}
