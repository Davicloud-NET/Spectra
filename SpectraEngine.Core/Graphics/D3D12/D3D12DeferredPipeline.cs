using Silk.NET.Direct3D12;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Graphics.D3D12;

/// <summary>
/// Deferred shading on D3D12: a G-buffer geometry pass, then a full-screen light pass.
/// </summary>
public sealed unsafe class D3D12DeferredPipeline : ID3D12RenderPipeline
{
    private D3D12Renderer? _renderer;

    public string Name => "Deferred";

    /// <summary>Ambient light level, added to every surface.</summary>
    // Stands in for sky light and bounce. Too low and shadows go black.
    public float Ambient { get; set; } = 0.18f;

    public void Initialize(D3D12Renderer renderer) => _renderer = renderer;

    public void Execute(in D3D12RenderContext context)
    {
        D3D12Renderer renderer = _renderer!;
        renderer.CurrentFillMode = FillMode.Solid;

        if (context.Scene is null) return;

        GBuffer? gbuffer = renderer.EnsureGBuffer();
        if (gbuffer is null) return;

        ShaderProgram surfaceShader = renderer.EnsureGBufferShader();
        Camera camera = context.Scene.Camera;

        // Shadows first, so the light pass reads this frame's map.
        int shadowLight = renderer.RenderShadowMap(context.Scene, context.View);

        // Both before the pass: they may create shader programs, which must
        // not happen inside an open pass.
        renderer.PrepareGeometryInstancing();

        renderer.PrepareWorldLines(gbuffer: true);


        // Depth-only clear. Depth is the coverage mask: the light pass draws
        // sky where depth is still 1 and never reads the colour attachments there.
        using (renderer.Profiler.Measure(SpectraEngine.Core.Diagnostics.FramePhase.Geometry))
        {
        renderer.BeginPass(gbuffer.Targets, PassClear.DepthOnly);
        try
        {
            // The pass's aspect, not the window's: the target may be offscreen.
            if (renderer.PassAspectRatio is { } aspect)
                camera.AspectRatio = aspect;

            renderer.DrawGeometry(context.View, camera, surfaceShader);
        }
        finally
        {
            renderer.EndPass();
        }
        }

        renderer.DrawDeferredLightPass(gbuffer, context.View, camera, Ambient, shadowLight);

        // After the light pass: world lines blend over the lit result and
        // depth-test in the shader against the G-buffer depth.
        renderer.FlushWorldLinesDeferred(camera, gbuffer);
    }

    public void Dispose() { }
}
