using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Graphics.OpenGL;

/// <summary>
/// Deferred shading: rasterise every surface's properties into the G-buffer
/// once, then light the whole screen in a second pass. No MSAA and no blended
/// transparency; use forward for those.
/// </summary>
// Every surface is drawn with the one G-buffer program. A material only
// contributes its parameters, through Material.ApplyTo.
public sealed class DeferredPipeline : IOpenGLRenderPipeline
{
    private OpenGLRenderer? _renderer;

    public string Name => "Deferred";

    /// <summary>Ambient light level, added to every surface.</summary>
    public float Ambient { get; set; } = 0.18f;

    public void Initialize(OpenGLRenderer renderer) => _renderer = renderer;

    public void Execute(in OpenGLRenderContext context)
    {
        OpenGLRenderer renderer = context.Renderer;
        if (context.Scene is null) return;

        GBuffer? gbuffer = renderer.EnsureGBuffer();
        if (gbuffer is null) return;

        ShaderProgram surfaceShader = renderer.EnsureGBufferShader();
        Camera camera = context.Scene.Camera;

        // Shadows first, so the light pass reads this frame's map.
        int shadowLight = renderer.RenderShadowMap(context.Scene, context.View);

        // Both may compile a program, which must not happen inside an open pass.
        renderer.PrepareGeometryInstancing();

        renderer.PrepareWorldLines(gbuffer: true);


        // Depth only. Depth is the coverage mask: the light pass returns sky
        // where it is still 1, so the colour attachments need no clear.
        using (renderer.Profiler.Measure(SpectraEngine.Core.Diagnostics.FramePhase.Geometry))
        {
        renderer.BeginPass(gbuffer.Targets, PassClear.DepthOnly);
        try
        {
            // Aspect from the pass, not the window.
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

        // World lines blend over the lit result. The shader depth-tests against
        // the G-buffer's depth texture.
        renderer.FlushWorldLinesDeferred(camera, gbuffer);
    }

    public void Dispose() { }
}
