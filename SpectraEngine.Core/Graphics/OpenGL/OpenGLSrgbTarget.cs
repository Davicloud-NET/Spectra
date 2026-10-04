using Silk.NET.OpenGL;

namespace SpectraEngine.Core.Graphics.OpenGL;

// sRGB offscreen buffer the frame is drawn into, then blitted to the window.
// Silk.NET 2.23 cannot create an sRGB-capable GL window (no GLFW_SRGB_CAPABLE
// in WindowOptions, and Silk resets hints before creating it), so
// GL_FRAMEBUFFER_SRGB does nothing on the default framebuffer.
// A blit, not a pow in shaders: the encode has to come after blending.
internal sealed class OpenGLSrgbTarget
{
    private uint _fbo;
    private uint _color;
    private uint _depth;
    private int _width;
    private int _height;

    // False once the driver has refused the framebuffer.
    public bool Usable { get; private set; } = true;

    // Binds the sRGB buffer, resizing first if needed. On false the caller
    // draws to the window instead.
    public bool Begin(GL gl, int width, int height)
    {
        if (!Usable || width <= 0 || height <= 0)
            return false;

        if (_fbo == 0 || width != _width || height != _height)
        {
            if (!Allocate(gl, width, height))
                return false;
        }

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        return true;
    }

    // Copies the frame to the window and leaves the window bound.
    public void Present(GL gl, int width, int height)
    {
        // Encoding off: the frame is already encoded, the blit just moves bytes.
        gl.Disable(EnableCap.FramebufferSrgb);

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        gl.BlitFramebuffer(
            0, 0, width, height,
            0, 0, width, height,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Enable(EnableCap.FramebufferSrgb);
    }

    public void Dispose(GL gl)
    {
        if (_fbo != 0) gl.DeleteFramebuffer(_fbo);
        if (_color != 0) gl.DeleteRenderbuffer(_color);
        if (_depth != 0) gl.DeleteRenderbuffer(_depth);
        _fbo = _color = _depth = 0;
        _width = _height = 0;
    }

    private bool Allocate(GL gl, int width, int height)
    {
        Dispose(gl);

        _fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);

        // Renderbuffers: nothing samples this.
        _color = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _color);
        gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer, InternalFormat.Srgb8Alpha8, (uint)width, (uint)height);
        gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, _color);

        _depth = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depth);
        gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
        gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer, _depth);

        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            Dispose(gl);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            Usable = false;
            return false;
        }

        _width = width;
        _height = height;
        return true;
    }
}
