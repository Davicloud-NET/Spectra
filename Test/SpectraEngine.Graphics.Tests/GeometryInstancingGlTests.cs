using Silk.NET.OpenGL;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// The deferred geometry pass collapses repeated meshes into instanced draws,
/// and the picture does not change when it does.
/// </summary>
// Two scenes with the same geometry: nodes sharing a Brush instance get one
// GPU mesh and batch, nodes with separate equal brushes don't.
[Collection(GlRendererCollection.Name)]
public sealed class GeometryInstancingGlTests
{
    private readonly GlRendererFixture _fixture;

    public GeometryInstancingGlTests(GlRendererFixture fixture) => _fixture = fixture;

    // Two groups, so the second batch starts at a non-zero offset into the
    // transform array. One batch would hide offset bugs.
    private const int GroupSize = 5;
    private const int Copies = GroupSize * 2;

    // With ProbeTarget set a frame renders the scene twice: probe, then window.
    private const int ExecutionsPerFrame = 2;

    private static Brush Box(float halfExtent = 0.5f) =>
        Brush.CreateBox(new Vector3(-halfExtent), new Vector3(halfExtent), default);

    [Fact]
    public void Shared_brushes_batch_and_render_exactly_as_separate_ones()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        renderer.GetFramebufferSize(out int width, out int height);
        int size = Math.Min(width, height);

        // Alternating, so the two batches interleave in the draw list.
        Brush small = Box(0.5f);
        Brush large = Box(0.62f);
        (int[,] batched, int savedBatched, int batchCount, int visibleBatched) =
            Render(i => (i % 2 == 0) ? small : large, size);

        (int[,] separate, int savedSeparate, int separateBatches, int visibleSeparate) =
            Render(i => Box((i % 2 == 0) ? 0.5f : 0.62f), size);

        visibleBatched.ShouldBe(Copies, "a culled prop would silently shrink a group below the batch minimum");
        visibleSeparate.ShouldBe(Copies, "a culled prop would silently shrink a group below the batch minimum");

        savedBatched.ShouldBe((GroupSize - 1) * 2 * ExecutionsPerFrame,
            "each group of five collapses to one instanced draw, so four draws go per group "
            + "per execution of the pipeline");
        batchCount.ShouldBe(2, "two shared meshes under one material is two batches");
        savedSeparate.ShouldBe(0, "ten distinct meshes cannot be collapsed");
        separateBatches.ShouldBe(0, "nothing repeats, so nothing should be partitioned into a batch");

        // Two blank frames would also agree.
        CountLit(separate, size).ShouldBeGreaterThan(size * 4,
            "the props must cover a meaningful part of the frame for this comparison to mean anything");

        (int x, int y, int worst) = WorstDifference(batched, separate, size);
        worst.ShouldBeLessThanOrEqualTo(2,
            $"the instanced frame differed from the unbatched one by {worst} at ({x}, {y}); " +
            "the same geometry drawn through the generated per-instance stage must land " +
            "in the same place with the same shading");
    }

    private (int[,] Pixels, int DrawsSaved, int Batches, int Visible) Render(
        Func<int, Brush> brushFor, int size)
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        var scene = new Scene("instancing");
        // No AssetManager here, so part brushes need this material or the
        // geometry pass skips them.
        SpectraEngine.Core.Graphics.Texture white = renderer.CreateTexture(
            [255, 255, 255, 255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);
        scene.StaticWorldMaterial = new Material(renderer.DefaultShader)
            .SetVector3("uBaseColor", new Vector3(0.8f, 0.8f, 0.8f))
            .SetFloat("uRoughness", 0.7f)
            .SetFloat("uMetallic", 0f)
            .SetFloat("uAmbientOcclusion", 1f)
            .SetVector3("uEmissive", Vector3.Zero)
            .SetFloat("uShadingModel", 0f)
            .SetTexture("uDiffuse", 0, white);

        for (int i = 0; i < Copies; i++)
        {
            SceneNode node = scene.Root.CreateChild($"Prop{i}");
            // A grid: a row of ten runs off the frustum, and a culled prop
            // drops its group below the batch minimum.
            node.LocalPosition = new Vector3(
                ((i % 5) - 2) * 1.6f, ((i / 5) - 0.5f) * 1.8f, 0f);
            // Kind before brush, so the part is never admitted to the static world.
            node.BrushKind = BrushKind.Part;
            node.Brush = brushFor(i);
        }

        var sun = scene.Root.CreateChild("Sun");
        sun.LocalRotation = Light.RotationForDirection(new Vector3(-0.35f, -0.85f, -0.4f));
        sun.Light = new Light
        {
            Kind = LightKind.Directional,
            Color = new Vector3(1f, 1f, 1f),
            Intensity = 12f,
        };

        scene.Camera.Position = new Vector3(0f, 0f, 7f);
        scene.Camera.LookAt(Vector3.Zero);

        string restorePipeline = renderer.CurrentPipelineName;
        RenderTarget probe = renderer.CreateRenderTarget(new RenderTargetDesc(size, size));
        var view = new RenderView();

        try
        {
            renderer.TrySelectPipeline("Deferred").ShouldBeTrue();
            renderer.ProbeTarget = probe;

            scene.ProcessPartBrushMeshes(renderer);

            // Two frames. The instance buffer is sized from the previous
            // frame, so the first frame draws unbatched and only the second
            // takes the instanced path.
            for (int frame = 0; frame < 2; frame++)
            {
                scene.BuildRenderView(scene.Camera, view);
                renderer.Render(scene, view, 1.0 / 60.0);
            }

            return (ReadLuminance(probe, size), renderer.GeometryDrawsSaved,
                    view.Batches.Count, view.PartBrushesVisible);
        }
        finally
        {
            renderer.ProbeTarget = null;
            renderer.DestroyRenderTarget(probe);
            scene.ReleasePartBrushMeshes(renderer);
            renderer.DestroyTexture(white);
            while (renderer.CurrentPipelineName != restorePipeline)
                renderer.NextPipeline();
        }
    }

    private unsafe int[,] ReadLuminance(RenderTarget target, int size)
    {
        GL gl = _fixture.Gl;
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
        gl.FramebufferTexture2D(
            FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, ((OpenGLTexture)target.ColorTexture!).Handle, 0);

        var pixels = new byte[size * size * 4];
        fixed (byte* p = pixels)
            gl.ReadPixels(0, 0, (uint)size, (uint)size, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.DeleteFramebuffer(fbo);

        var luma = new int[size, size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = ((y * size) + x) * 4;
                luma[x, y] = pixels[i] + pixels[i + 1] + pixels[i + 2];
            }
        }
        return luma;
    }

    // Pixels that differ from the sky.
    private static int CountLit(int[,] frame, int size)
    {
        int background = frame[0, 0];
        int lit = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (Math.Abs(frame[x, y] - background) > 12)
                    lit++;
            }
        }
        return lit;
    }

    private static (int X, int Y, int Worst) WorstDifference(int[,] a, int[,] b, int size)
    {
        int worst = 0, wx = -1, wy = -1;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int difference = Math.Abs(a[x, y] - b[x, y]);
                if (difference > worst)
                {
                    worst = difference;
                    wx = x;
                    wy = y;
                }
            }
        }
        return (wx, wy, worst);
    }
}
