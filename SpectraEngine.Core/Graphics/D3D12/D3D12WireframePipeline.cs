using Silk.NET.Direct3D12;

namespace SpectraEngine.Core.Graphics.D3D12;

/// <summary>
/// Wireframe variant of <see cref="D3D12ForwardPipeline"/>: same draw list,
/// drawn with <see cref="FillMode.Wireframe"/>.
/// </summary>
public sealed class D3D12WireframePipeline : ID3D12RenderPipeline
{
    private D3D12Renderer? _renderer;

    public string Name => "Wireframe";

    /// <summary>Ambient light level added to every surface.</summary>
    public float Ambient { get; set; } = 0.05f;

    public void Initialize(D3D12Renderer renderer) => _renderer = renderer;

    public void Execute(in D3D12RenderContext context)
    {
        var renderer = _renderer!;

        // Before the pass opens: this may create a program.
        renderer.PrepareWorldLines(gbuffer: false);

        renderer.BeginPass(renderer.FrameTarget, PassClear.To(ClearColors.Wireframe));
        try
        {
            if (context.Scene is null) return;

            var camera = context.Scene.Camera;
            if (renderer.PassAspectRatio is { } aspect)
                camera.AspectRatio = aspect;

            renderer.CurrentFillMode = FillMode.Wireframe;
            renderer.DrawLit(context.View, camera, Ambient, shadowLightIndex: -1);
            renderer.CurrentFillMode = FillMode.Solid;

            // Inside this pass: world lines need the scene's depth.
            renderer.FlushWorldLines(camera);
        }
        finally
        {
            renderer.EndPass();
        }
    }

    public void Dispose() { }
}
