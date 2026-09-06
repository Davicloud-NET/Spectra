using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;

namespace SpectraEngine.Graphics.Tests;

internal static class GBufferLayoutParity
{
    internal static void Check(Renderer renderer)
    {
        float[] vertices = [-.7f,-.7f,.3f, 0,0,1, 0,0, .7f,-.7f,.3f, 0,0,1, 1,0,
            .7f,.7f,.3f, 0,0,1, 1,1, -.7f,.7f,.3f, 0,0,1, 0,1];
        Mesh mesh = renderer.CreateMesh(vertices, [0,1,2,0,2,3], VertexAttribute.StandardLayout);
        Texture white = renderer.CreateTexture([255,255,255,255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear);
        try
        {
            byte[][] extended = Draw(GBufferLayout.Extended);
            byte[][] standard = Draw(GBufferLayout.Standard);
            for (int i = 0; i < 4; i++) standard[i].ShouldBe(extended[i], $"G-buffer attachment {i} must preserve pixels.");
            int center = (8 * 16 + 8) * 4;
            standard[3][center].ShouldBeGreaterThan((byte)80, "emissive must remain present");
            standard[3][center].ShouldBeGreaterThan(standard[3][center + 1]);
        }
        finally { renderer.DestroyTexture(white); renderer.DestroyMesh(mesh); }

        byte[][] Draw(GBufferLayout layout)
        {
            using var targets = new GBuffer(renderer, 16, 16, layout);
            using var shader = renderer.CreateShaderFromSource(layout == GBufferLayout.Standard ? BaseShaders.GBufferFillCompact : BaseShaders.GBufferFill);
            targets.Targets.Length.ShouldBe(layout == GBufferLayout.Standard ? 4 : 5);
            renderer.BeginOutOfFrameCommands();
            try
            {
                renderer.BeginPass(targets.Targets, PassClear.To(Vector4.Zero));
                shader.Use();
                shader.SetUniform("uModel", Matrix4x4.Identity);
                shader.SetUniform("uView", Matrix4x4.Identity);
                shader.SetUniform("uProjection", Matrix4x4.Identity);
                shader.SetUniform("uBaseColor", new Vector3(.3f,.5f,.2f));
                shader.SetUniform("uRoughness", .45f);
                shader.SetUniform("uMetallic", .25f);
                shader.SetUniform("uAmbientOcclusion", .8f);
                shader.SetUniform("uEmissive", new Vector3(.4f,.1f,.2f));
                shader.SetUniform("uShadingModel", 0f);
                shader.SetTexture("uDiffuse", 0, white);
                shader.Use(); mesh.Draw(); renderer.EndPass();
            }
            finally { renderer.EndOutOfFrameCommands(); }
            byte[][] result = new byte[4][];
            for (int i = 0; i < 4; i++) result[i] = TextureUploadParity.RenderWhole(renderer, targets.Targets[i].ColorTexture!);
            return result;
        }
    }
}
