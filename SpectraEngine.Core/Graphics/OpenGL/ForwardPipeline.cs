using Silk.NET.OpenGL;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Graphics.OpenGL;

/// <summary>
/// Forward shading: draws the frame's pre-culled <see cref="RenderView"/> items
/// with their materials' own shaders.
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
        // May compile a program, which must not happen inside an open pass.
        context.Renderer.PrepareWorldLines(gbuffer: false);

        context.Renderer.BeginPass(context.Renderer.FrameTarget, PassClear.To(ClearColors.Sky));
        try
        {
            if (context.Scene is null)
                return;

            var camera = context.Scene.Camera;
            // Aspect from the pass, not the window.
            if (context.Renderer.PassAspectRatio is { } aspect)
                camera.AspectRatio = aspect;

            DrawView(context.View, camera);

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

        // Static-world chunks, one item per (chunk, material), already in world space.
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
