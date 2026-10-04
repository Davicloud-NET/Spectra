using Silk.NET.OpenGL;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

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

            DrawView(context.View, camera);

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

    private void DrawView(RenderView view, Camera camera)
    {
        IReadOnlyList<RenderItem> items = view.Items;
        for (int i = 0; i < items.Count; i++)
        {
            RenderItem item = items[i];
            if (item.Material is { } material)
                DrawRenderable(item.Mesh, material, item.World, camera, view);
        }

        // Static-world chunks: already culled, already in world space.
        IReadOnlyList<RenderItem> worldItems = view.WorldItems;
        for (int i = 0; i < worldItems.Count; i++)
        {
            RenderItem item = worldItems[i];
            if (item.Material is { } material)
                DrawRenderable(item.Mesh, material, item.World, camera, view);
        }
    }

    private void DrawRenderable(Mesh mesh, Material material, Matrix4x4 model, Camera camera, RenderView view)
    {
        // Skip a material whose shader failed to resolve.
        if (material.Shader is not { } shader) return;

        shader.Use();
        shader.SetUniform("uModel", model);
        shader.SetUniform("uView", camera.View);
        shader.SetUniform("uProjection", camera.Projection);
        LightUpload.Apply(shader, view, Ambient);
        material.Apply();

        mesh.Draw();
    }

    public void Dispose()
    {
    }
}
