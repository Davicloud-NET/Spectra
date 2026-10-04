using Silk.NET.OpenGL;

namespace SpectraEngine.Core.Graphics.OpenGL;

/// <summary>Draws every mesh as wireframe. A diagnostic pipeline.</summary>
public sealed class WireframePipeline : IOpenGLRenderPipeline
{
    private OpenGLRenderer? _renderer;

    public string Name => "Wireframe";

    /// <summary>Ambient light level, added to every surface.</summary>
    public float Ambient { get; set; } = 0.05f;

    public void Initialize(OpenGLRenderer renderer)
    {
        _renderer = renderer;
    }

    public void Execute(in OpenGLRenderContext context)
    {
        var gl = context.Gl;
        // Before the pass opens: this may create a program.
        context.Renderer.PrepareWorldLines(gbuffer: false);

        context.Renderer.BeginPass(context.Renderer.FrameTarget, PassClear.To(ClearColors.Wireframe));
        try
        {
            if (context.Scene is null)
                return;

            var camera = context.Scene.Camera;
            // Pass size, not window size: the target may not be the back buffer.
            if (context.Renderer.PassAspectRatio is { } aspect)
                camera.AspectRatio = aspect;

            gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
            gl.Disable(EnableCap.CullFace);

            context.Renderer.DrawLit(context.View, camera, Ambient, shadowLightIndex: -1);

            gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
            gl.Enable(EnableCap.CullFace);

            // Inside the pass: world lines are depth-tested against the scene.
            context.Renderer.FlushWorldLines(camera);
        }
        finally
        {
            context.Renderer.EndPass();
        }
    }

    public void Dispose()
    {
    }
}
