using Silk.NET.Maths;
using Silk.NET.OpenGL;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// A lone sphere must not shadow itself along its terminator.
/// </summary>
// Two things stop it: the lookup is lifted off the surface along its normal,
// and the depth pass pushes stored depth back. The lift does most of it here.
// Widening the filter without raising them brings the artifact back.
// Receiver-plane depth bias was tried and made it worse.
// ShadowCascadeGlTests pulls the other way: it fails when the bias is so large
// that a shadow leaves its caster.
//
// The G-buffer is sized to the window, not the frame target, so these tests
// drive the framebuffer latch to keep the two matched.
[Collection(GlRendererCollection.Name)]
public sealed class ShadowCurvedReceiverGlTests
{
    private readonly GlRendererFixture _fixture;

    public ShadowCurvedReceiverGlTests(GlRendererFixture fixture) => _fixture = fixture;

    // Fills the frame, so the terminator band is many pixels wide.
    private const float Radius = 0.45f;

    // The artifact stops growing with resolution here. Smaller sizes understate it.
    private const int ConvergedSize = 512;

    // The other two tests depend on this: with the sizes mismatched they would
    // measure resampling and still print a plausible number.
    [Fact]
    public void The_gbuffer_follows_the_framebuffer_latch()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        renderer.GetFramebufferSize(out int restoreWidth, out int restoreHeight);

        try
        {
            foreach (int size in new[] { 64, 128, 256 })
            {
                renderer.SetFramebufferSize(new Vector2D<int>(size, size));
                RenderBlock(size);

                GBuffer gbuffer = renderer.GBuffer!;
                gbuffer.Width.ShouldBe(size, "the G-buffer must follow the framebuffer latch");
                gbuffer.Height.ShouldBe(size, "the G-buffer must follow the framebuffer latch");
            }
        }
        finally
        {
            renderer.SetFramebufferSize(new Vector2D<int>(restoreWidth, restoreHeight));
        }
    }

    [Fact]
    public void A_lit_sphere_is_not_shadowed_by_itself()
    {
        // A lone convex receiver has nothing to cast onto it, so shadows on
        // and off must match.
        OpenGLRenderer renderer = _fixture.Renderer;
        bool restoreShadows = renderer.ShadowsEnabled;
        renderer.GetFramebufferSize(out int restoreWidth, out int restoreHeight);

        try
        {
            renderer.SetFramebufferSize(new Vector2D<int>(ConvergedSize, ConvergedSize));

            renderer.ShadowsEnabled = true;
            int[,] on = RenderBlock(ConvergedSize);
            renderer.GBuffer!.Width.ShouldBe(ConvergedSize, "or this measures resampling");

            renderer.ShadowsEnabled = false;
            int[,] off = RenderBlock(ConvergedSize);

            (int worstX, int worstY, int worstDrop) = WorstDarkening(on, off, ConvergedSize);

            // A few levels for 8-bit rounding on another driver. Acne is a
            // far bigger drop.
            worstDrop.ShouldBeLessThan(10,
                $"a lone sphere darkened by {worstDrop} at ({worstX}, {worstY}) when shadows were " +
                "turned on; nothing in the scene can cast onto it, so that darkening is the sphere " +
                "shadowing itself");
        }
        finally
        {
            renderer.ShadowsEnabled = restoreShadows;
            renderer.SetFramebufferSize(new Vector2D<int>(restoreWidth, restoreHeight));
        }
    }

    [Fact]
    public void A_deferred_frame_does_not_depend_on_the_target_size()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        bool restoreShadows = renderer.ShadowsEnabled;
        renderer.GetFramebufferSize(out int restoreWidth, out int restoreHeight);

        try
        {
            renderer.GetFramebufferSize(out int width, out int height);
            int matched = Math.Min(width, height);
            int mismatched = matched + (matched / 2);

            renderer.ShadowsEnabled = true;
            int[,] onA = RenderBlock(matched);
            renderer.ShadowsEnabled = false;
            int[,] offA = RenderBlock(matched);

            renderer.ShadowsEnabled = true;
            int[,] onB = RenderBlock(mismatched);
            renderer.ShadowsEnabled = false;
            int[,] offB = RenderBlock(mismatched);

            int atMatched = WorstDarkening(onA, offA, matched).Drop;
            int atMismatched = WorstDarkening(onB, offB, mismatched).Drop;

            atMismatched.ShouldBeLessThan(atMatched + 32,
                $"the same scene self-shadowed by {atMatched} rendered at {matched}x{matched} " +
                $"(the G-buffer's own size) and by {atMismatched} at {mismatched}x{mismatched}; " +
                "the picture must not depend on the target's size");
        }
        finally
        {
            renderer.ShadowsEnabled = restoreShadows;
            renderer.SetFramebufferSize(new Vector2D<int>(restoreWidth, restoreHeight));
        }
    }

    // One sphere, one sun. The camera is placed so the terminator crosses
    // the visible face.
    private Scene BuildScene(Mesh mesh, SpectraEngine.Core.Graphics.Texture white)
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        var scene = new Scene("curved-receiver");
        scene.Camera.Position = new Vector3(0f, 0f, 2.2f);
        scene.Camera.LookAt(Vector3.Zero);

        var node = scene.Root.CreateChild("Receiver");
        node.LocalTransform = new Transform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            // The demo's sphere size: radius 0.5 scaled by 0.9.
            Scale = new Vector3(Radius * 2f),
        };
        node.MeshRenderer = new MeshRenderer(mesh, new Material(renderer.DefaultShader)
            .SetVector3("uBaseColor", new Vector3(0.9f, 0.9f, 0.9f))
            .SetFloat("uRoughness", 0.85f)
            .SetFloat("uMetallic", 0f)
            .SetFloat("uAmbientOcclusion", 1f)
            .SetVector3("uEmissive", Vector3.Zero)
            .SetFloat("uShadingModel", 0f)
            .SetTexture("uDiffuse", 0, white));

        // The demo's sun direction, where the artifact was seen. A more
        // grazing light reproduces something louder than that defect.
        var sun = scene.Root.CreateChild("Sun");
        sun.LocalRotation = Light.RotationForDirection(new Vector3(-0.35f, -0.85f, -0.4f));
        sun.Light = new Light
        {
            Kind = LightKind.Directional,
            Color = new Vector3(1f, 1f, 1f),
            Intensity = 14f,
        };

        return scene;
    }

    private int[,] RenderBlock(int size)
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        (float[] vertices, uint[] indices) = Primitives.Sphere();
        Mesh mesh = renderer.CreateMesh(vertices, indices, VertexAttribute.StandardLayout);
        SpectraEngine.Core.Graphics.Texture white = renderer.CreateTexture(
            [255, 255, 255, 255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);

        Scene scene = BuildScene(mesh, white);

        string restorePipeline = renderer.CurrentPipelineName;
        RenderTarget probe = renderer.CreateRenderTarget(new RenderTargetDesc(size, size));
        var view = new RenderView();

        try
        {
            renderer.TrySelectPipeline("Deferred").ShouldBeTrue();
            renderer.ProbeTarget = probe;

            scene.BuildRenderView(scene.Camera, view);
            renderer.Render(scene, view, 1.0 / 60.0);

            return ReadLuminance(probe, size);
        }
        finally
        {
            renderer.ProbeTarget = null;
            renderer.DestroyRenderTarget(probe);
            renderer.DestroyMesh(mesh);
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

    // Largest drop where the surface is lit. Pixels already dark with
    // shadows off are skipped.
    private static (int X, int Y, int Drop) WorstDarkening(int[,] on, int[,] off, int size)
    {
        int worst = 0, worstX = -1, worstY = -1;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (off[x, y] < 40)
                    continue;

                int drop = off[x, y] - on[x, y];
                if (drop > worst)
                {
                    worst = drop;
                    worstX = x;
                    worstY = y;
                }
            }
        }
        return (worstX, worstY, worst);
    }
}
