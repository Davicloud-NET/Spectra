using Silk.NET.Direct3D12;
using Silk.NET.Maths;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Graphics.D3D12;

/// <summary>
/// Forward rendering on D3D12: draws the frame's <see cref="RenderView"/>
/// items with their materials' shaders.
/// </summary>
public sealed unsafe class D3D12ForwardPipeline : ID3D12RenderPipeline
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

        renderer.BeginPass(renderer.FrameTarget, PassClear.To(ClearColors.Sky));
        try
        {
            if (context.Scene is null) return;

            var camera = context.Scene.Camera;
            // The pass's aspect, not the window's: the target may be offscreen.
            if (renderer.PassAspectRatio is { } aspect)
                camera.AspectRatio = aspect;

            DrawView(context.View, camera);

            // Inside the pass: world lines are tested against the scene's depth.
            renderer.FlushWorldLines(camera);
        }
        finally
        {
            renderer.EndPass();
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

        shader.SetUniform("uModel", model);
        shader.SetUniform("uView", camera.View);
        shader.SetUniform("uProjection", camera.Projection * D3D12Renderer.GlToD3dClipZ);
        LightUpload.Apply(shader, view, Ambient);
        material.Apply();
        shader.Use();

        mesh.Draw();
    }

    public void Dispose() { }
}
