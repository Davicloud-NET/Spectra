using Silk.NET.Direct3D12;

namespace SpectraEngine.Core.Graphics.D3D12;

/// <summary>
/// Forward rendering on D3D12: draws the frame's <see cref="RenderView"/>
/// items with their materials' shaders.
/// </summary>
public sealed class D3D12ForwardPipeline : ID3D12RenderPipeline
{
    private D3D12Renderer? _renderer;

    public string Name => "Forward";

    /// <summary>Ambient light level, added to every surface.</summary>
    public float Ambient { get; set; } = 0.18f;

    public void Initialize(D3D12Renderer renderer) => _renderer = renderer;

    public void Execute(in D3D12RenderContext context)
    {
        var renderer = _renderer!;
        renderer.CurrentFillMode = FillMode.Solid;

        // Before the pass: this may create a shader program.
        renderer.PrepareWorldLines(gbuffer: false);

        // Its own pass, so before the scene's.
        int shadowLight = context.Scene is { } lit ? renderer.RenderShadowMap(lit, context.View) : -1;

        renderer.BeginPass(renderer.FrameTarget, PassClear.To(ClearColors.Sky));
        try
        {
            if (context.Scene is null) return;

            var camera = context.Scene.Camera;
            // The pass's aspect, not the window's: the target may be offscreen.
            if (renderer.PassAspectRatio is { } aspect)
                camera.AspectRatio = aspect;

            renderer.DrawLit(context.View, camera, Ambient, shadowLight);

            // Inside the pass: world lines are tested against the scene's depth.
            renderer.FlushWorldLines(camera);
        }
        finally
        {
            renderer.EndPass();
        }
    }

    public void Dispose() { }
}
