using System;

namespace SpectraEngine.Core.Graphics.OpenGL;

/// <summary>
/// A swappable rendering strategy for the OpenGL backend. A pipeline must leave
/// cull, blend and depth-test state as the renderer set it up.
/// </summary>
public interface IOpenGLRenderPipeline : IDisposable
{
    /// <summary>Name shown in UI and logs.</summary>
    string Name { get; }

    /// <summary>Called once when the pipeline is registered.</summary>
    void Initialize(OpenGLRenderer renderer);

    /// <summary>Renders one frame.</summary>
    void Execute(in OpenGLRenderContext context);
}
