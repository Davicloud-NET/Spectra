namespace SpectraEngine.Core.Graphics.D3D11;

/// <summary>
/// Forward shading on D3D11: draws the frame's <see cref="RenderView"/> with
/// each material's own shader. Same steps as <c>OpenGL.ForwardPipeline</c>.
/// </summary>
public sealed class D3D11ForwardPipeline : ID3D11RenderPipeline
{
    private D3D11Renderer? _renderer;

    public string Name => "Forward";

    /// <summary>Ambient light level, added to every surface regardless of the lights.</summary>
    public float Ambient { get; set; } = 0.18f;

    public void Initialize(D3D11Renderer renderer) => _renderer = renderer;

    public void Execute(in D3D11RenderContext context)
    {
        D3D11Renderer renderer = context.Renderer;

        // May create a shader program, which must not happen inside an open pass.
        renderer.PrepareWorldLines(gbuffer: false);

        // Its own pass, so before the scene's.
        int shadowLight = context.Scene is { } lit ? renderer.RenderShadowMap(lit, context.View) : -1;

        renderer.BeginPass(renderer.FrameTarget, PassClear.To(ClearColors.Sky));
        try
        {
            if (context.Scene is null) return;

            var camera = context.Scene.Camera;
            // The pass's aspect, not the window's.
            if (renderer.PassAspectRatio is { } aspect)
                camera.AspectRatio = aspect;

            renderer.DrawLit(context.View, camera, Ambient, shadowLight);

            // Inside this pass: world lines are tested against the scene's depth.
            renderer.FlushWorldLines(camera);
        }
        finally
        {
            renderer.EndPass();
        }
    }

    public void Dispose() { }
}
