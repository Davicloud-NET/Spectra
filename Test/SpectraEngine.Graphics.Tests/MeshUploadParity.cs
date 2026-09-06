using System;
using System.IO;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;

namespace SpectraEngine.Graphics.Tests;

internal static class MeshUploadParity
{
    internal static void Check(Renderer renderer)
    {
        const int baseVertex = 32768;
        float[] quad = [-.7f,-.7f,0, 0,0,1, 0,0, .7f,-.7f,0, 0,0,1, 1,0,
            .7f,.7f,0, 0,0,1, 1,1, -.7f,.7f,0, 0,0,1, 0,1];
        float[] vertices = new float[(baseVertex + 4) * 8];
        quad.CopyTo(vertices, baseVertex * 8);
        uint[] indices = [0,0,0, 0,1,2, 0,2,3];
        using var upload = renderer.BeginMeshUpload(vertices, indices, VertexAttribute.StandardLayout);
        int steps = 0;
        while (!upload.IsComplete) { upload.Step(16 * 1024).ShouldBeInRange(1, 16 * 1024); steps++; }
        steps.ShouldBeGreaterThan(64);
        using var storage = new SharedMeshStorage(renderer, upload.Complete());
        Mesh range = storage.CreateRange(new(3, 6, baseVertex), new Aabb(new(-.7f), new(.7f)));
        range.Indices[0].ShouldBe((uint)baseVertex);
        range.Positions[(int)range.Indices[0]].X.ShouldBe(-.7f);
        storage.Dispose(); // The view alone now owns the buffers.
        Mesh reference = renderer.CreateMesh(quad, indices.AsSpan(3), VertexAttribute.StandardLayout);
        ShaderProgram shader = renderer.CreateShaderFromSource(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "InstancedVertex.spectrashade")));
        using InstanceBuffer instances = renderer.CreateInstanceBuffer(2,
            [.. VertexAttribute.StandardInstanceLayout, new VertexAttribute(7, 4, VertexAttribute.InstanceSlot, VertexInputRate.PerInstance)], shader);
        float[] instanceData = new float[40];
        Fill(instanceData.AsSpan(0, 20), 4);
        Fill(instanceData.AsSpan(20, 20), 0);
        instances.Update(instanceData, 2);
        RenderTarget target = renderer.CreateRenderTarget(new RenderTargetDesc(16, 16));
        try
        {
            byte[] expected = Draw(reference);
            ViewportCompare.HasVariation(expected).ShouldBeTrue();
            Draw(range).ShouldBe(expected, "Shared range, base vertex, first instance, and sliced buffers must preserve pixels.");
        }
        finally
        {
            renderer.DestroyRenderTarget(target);
            renderer.DestroyMesh(reference); renderer.DestroyMesh(range); shader.Dispose();
        }

        byte[] Draw(Mesh mesh)
        {
            renderer.BeginOutOfFrameCommands();
            try
            {
                renderer.BeginPass(target, PassClear.To(new(0, 0, 0, 1)));
                shader.Use();
                shader.SetUniform("viewProjection", Matrix4x4.Identity);
                shader.Use();
                mesh.DrawInstanced(instances, 1, firstInstance: 1);
                renderer.EndPass();
            }
            finally { renderer.EndOutOfFrameCommands(); }
            var pixels = new byte[16 * 16 * 4]; renderer.ReadTargetPixels(target, pixels); return pixels;
        }
        static void Fill(Span<float> destination, float x)
        {
            destination.Clear(); destination[0] = destination[5] = destination[10] = destination[15] = 1;
            destination[12] = x; destination[16] = 1; destination[19] = 1;
        }
    }
}
