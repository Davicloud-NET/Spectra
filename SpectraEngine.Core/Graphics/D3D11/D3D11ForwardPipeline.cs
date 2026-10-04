using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.Maths;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Graphics.D3D11;

/// <summary>
/// Forward shading on D3D11: draws the frame's <see cref="RenderView"/> with
/// each material's own shader. Same steps as <c>OpenGL.ForwardPipeline</c>.
/// </summary>
public sealed unsafe class D3D11ForwardPipeline : ID3D11RenderPipeline
{
    private D3D11Renderer? _renderer;

    public string Name => "Forward";

    /// <summary>Ambient light level, added to every surface regardless of the lights.</summary>
    public float Ambient { get; set; } = 0.18f;

    public void Initialize(D3D11Renderer renderer) => _renderer = renderer;

    public void Execute(in D3D11RenderContext context)
    {
        // May create a shader program, which must not happen inside an open pass.
        context.Renderer.PrepareWorldLines(gbuffer: false);

        context.Renderer.BeginPass(context.Renderer.FrameTarget, PassClear.To(ClearColors.Sky));
        try
        {
            if (context.Scene is null) return;

            var camera = context.Scene.Camera;
            // The pass's aspect, not the window's.
            if (context.Renderer.PassAspectRatio is { } aspect)
                camera.AspectRatio = aspect;

            DrawView(context.View, camera);

            // Inside this pass: world lines are tested against the scene's depth.
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

        // Static-world chunks, already culled and in world space.
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
        // A material whose shader failed to resolve is skipped.
        if (material.Shader is not { } shader) return;

        // Use() comes last on D3D: it flushes the staged uniforms.
        shader.SetUniform("uModel", model);
        shader.SetUniform("uView", camera.View);
        shader.SetUniform("uProjection", camera.Projection * D3D11Renderer.GlToD3dClipZ);
        LightUpload.Apply(shader, view, Ambient);
        material.Apply();
        shader.Use();

        mesh.Draw();
    }

    public void Dispose() { }
}
