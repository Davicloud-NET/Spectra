using System;

namespace SpectraEngine.Core.Graphics.D3D11;

/// <summary>
/// A rendering strategy for the D3D11 backend. Pipelines do the per-frame
/// work; the renderer owns GPU resources and the pipelines' lifetime.
/// </summary>
public interface ID3D11RenderPipeline : IDisposable
{
    string Name { get; }
    void Initialize(D3D11Renderer renderer);
    void Execute(in D3D11RenderContext context);
}
