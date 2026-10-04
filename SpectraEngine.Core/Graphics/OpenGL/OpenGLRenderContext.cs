using Silk.NET.OpenGL;

namespace SpectraEngine.Core.Graphics.OpenGL;

/// <summary>
/// Per-frame inputs handed to an <see cref="IOpenGLRenderPipeline"/>.
/// </summary>
// No window here: GLFW window queries are main-thread only. Sizes come from
// the renderer.
public readonly struct OpenGLRenderContext
{
    public required OpenGLRenderer Renderer { get; init; }
    public required GL Gl { get; init; }
    public required Scene.Scene? Scene { get; init; }

    /// <summary>The frustum-culled draw list for this frame.</summary>
    public required RenderView View { get; init; }

    public required double DeltaTime { get; init; }
}
