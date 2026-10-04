using Silk.NET.Direct3D12;
using Silk.NET.Maths;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Graphics.D3D12;

/// <summary>
/// Wireframe variant of <see cref="D3D12ForwardPipeline"/>: same draw list,
/// drawn with <see cref="FillMode.Wireframe"/>.
/// </summary>
public sealed unsafe class D3D12WireframePipeline : ID3D12RenderPipeline
{
    private D3D12Renderer? _renderer;

    public string Name => "Wireframe";

    /// <summary>Ambient light level added to every surface.</summary>
    public float Ambient { get; set; } = 0.05f;

    public void Initialize(D3D12Renderer renderer) => _renderer = renderer;

    public void Execute(in D3D12RenderContext context)
    {
        var renderer = _renderer!;

        // Before the pass opens: this may create a program.
        renderer.PrepareWorldLines(gbuffer: false);

        renderer.BeginPass(renderer.FrameTarget, PassClear.To(ClearColors.Wireframe));
        try
        {
            if (context.Scene is null) return;

            var camera = context.Scene.Camera;
            if (renderer.PassAspectRatio is { } aspect)
                camera.AspectRatio = aspect;

            renderer.CurrentFillMode = FillMode.Wireframe;
            DrawView(context.View, camera);
            renderer.CurrentFillMode = FillMode.Solid;

            // Inside this pass: world lines need the scene's depth.
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
        // A material with no program is skipped, not a crash.
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
