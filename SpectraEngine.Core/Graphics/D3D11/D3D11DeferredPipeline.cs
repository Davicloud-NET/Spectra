using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Graphics.D3D11;

/// <summary>
/// Deferred shading on D3D11. Same steps as <c>OpenGL.DeferredPipeline</c>.
/// </summary>
public sealed unsafe class D3D11DeferredPipeline : ID3D11RenderPipeline
{
    private D3D11Renderer? _renderer;

    public string Name => "Deferred";

    /// <summary>Ambient light level, added to every surface regardless of the lights.</summary>
    public float Ambient { get; set; } = 0.18f;

    public void Initialize(D3D11Renderer renderer) => _renderer = renderer;

    public void Execute(in D3D11RenderContext context)
    {
        D3D11Renderer renderer = context.Renderer;
        if (context.Scene is null) return;

        GBuffer? gbuffer = renderer.EnsureGBuffer();
        if (gbuffer is null) return;

        ShaderProgram surfaceShader = renderer.EnsureGBufferShader();
        Camera camera = context.Scene.Camera;

        // Shadows first: the light pass reads this frame's map.
        int shadowLight = renderer.RenderShadowMap(context.Scene, context.View);

        // Both may create a shader program, which must not happen inside an
        // open pass.
        renderer.PrepareGeometryInstancing();

        renderer.PrepareWorldLines(gbuffer: true);


        // Depth only. The colour attachments are never read where depth is
        // still 1, so clearing them is wasted work.
        using (renderer.Profiler.Measure(SpectraEngine.Core.Diagnostics.FramePhase.Geometry))
        {
        renderer.BeginPass(gbuffer.Targets, PassClear.DepthOnly);
        try
        {
            // The pass's aspect, not the window's.
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

        // After the light pass: the lines blend over the lit result.
        renderer.FlushWorldLinesDeferred(camera, gbuffer);
    }

    public void Dispose() { }
}
