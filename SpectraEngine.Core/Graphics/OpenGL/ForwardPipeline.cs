using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Graphics.OpenGL;

/// <summary>
/// Forward shading: draws the frame's pre-culled <see cref="RenderView"/> items
/// with their materials' own shaders. The picture matches the deferred one.
/// </summary>
public sealed class ForwardPipeline : IOpenGLRenderPipeline
{
    private OpenGLRenderer? _renderer;

    public string Name => "Forward";

    /// <summary>Ambient light level, added to every surface.</summary>
    public float Ambient { get; set; } = 0.18f;

    public void Initialize(OpenGLRenderer renderer)
    {
        _renderer = renderer;
    }

    public void Execute(in OpenGLRenderContext context)
    {
        OpenGLRenderer renderer = context.Renderer;

        // May compile a program, which must not happen inside an open pass.
        renderer.PrepareWorldLines(gbuffer: false);

        // Its own pass, so before the scene's.
        int shadowLight = context.Scene is { } lit ? renderer.RenderShadowMap(lit, context.View) : -1;

        renderer.BeginPass(renderer.FrameTarget, PassClear.To(ClearColors.Sky));
        try
        {
            if (context.Scene is null)
                return;

            var camera = context.Scene.Camera;
            // Aspect from the pass, not the window.
            if (renderer.PassAspectRatio is { } aspect)
                camera.AspectRatio = aspect;

            renderer.DrawLit(context.View, camera, Ambient, shadowLight);

            // Inside the pass: world lines are depth-tested against the scene.
            renderer.FlushWorldLines(camera);
        }
        finally
        {
            renderer.EndPass();
        }
    }

    public void Dispose()
    {
    }
}
