using Silk.NET.Direct3D12;
using System;

namespace SpectraEngine.Core.Graphics.D3D12;

/// <summary>
/// A swappable D3D12 rendering strategy (forward, wireframe, ...). Runs once
/// per frame against the renderer's open command list.
/// </summary>
public interface ID3D12RenderPipeline : IDisposable
{
    string Name { get; }
    void Initialize(D3D12Renderer renderer);
    void Execute(in D3D12RenderContext context);
}

/// <summary>
/// Everything a D3D12 pipeline needs for one frame. No window: GLFW queries are
/// main-thread-only, so sizes come from <see cref="Graphics.Renderer.PassSize"/>.
/// </summary>
public readonly struct D3D12RenderContext
{
    public required D3D12Renderer Renderer { get; init; }
    public required Scene.Scene? Scene { get; init; }

    /// <summary>The frustum-culled draw list for this frame.</summary>
    public required RenderView View { get; init; }

    public required double DeltaTime { get; init; }
}
