using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace SpectraEngine.Core.Graphics.D3D11;

/// <summary>
/// Per-frame inputs handed to an <see cref="ID3D11RenderPipeline"/>. The
/// device and context are borrowed for the duration of
/// <see cref="ID3D11RenderPipeline.Execute"/>.
/// </summary>
// No window here: GLFW window queries are main-thread only. Read sizes from
// Renderer.PassSize.
public readonly unsafe struct D3D11RenderContext
{
    public required D3D11Renderer Renderer { get; init; }
    public required ComPtr<ID3D11Device> Device { get; init; }
    public required ComPtr<ID3D11DeviceContext> Context { get; init; }
    public required Scene.Scene? Scene { get; init; }

    /// <summary>The frustum-culled draw list for this frame.</summary>
    public required RenderView View { get; init; }

    public required double DeltaTime { get; init; }
}
