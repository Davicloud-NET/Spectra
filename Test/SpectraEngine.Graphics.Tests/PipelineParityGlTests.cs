using System.Numerics;
using Silk.NET.Maths;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using SpectraEngine.Core.Scene;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Forward and deferred are two routes to one picture. The same scene through
/// both has to come out the same.
/// </summary>
// Each case lights the scene with one thing, so a failure names what the two
// paths disagree on.
[Collection(GlRendererCollection.Name)]
public sealed class PipelineParityGlTests
{
    private const int Size = 128;

    private readonly GlRendererFixture _fixture;

    public PipelineParityGlTests(GlRendererFixture fixture) => _fixture = fixture;

    public enum Rig
    {
        Ambient,
        Sun,
        Point,
        Spot,
        Rect,
        Disc,
        Everything,
    }

    [Theory]
    [InlineData(Rig.Ambient)]
    [InlineData(Rig.Sun)]
    [InlineData(Rig.Point)]
    [InlineData(Rig.Spot)]
    [InlineData(Rig.Rect)]
    [InlineData(Rig.Disc)]
    [InlineData(Rig.Everything)]
    public void Forward_and_deferred_draw_the_same_picture(Rig rig)
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        renderer.GetFramebufferSize(out int restoreWidth, out int restoreHeight);

        try
        {
            renderer.SetFramebufferSize(new Vector2D<int>(Size, Size));

            byte[] deferred = Render(rig, "Deferred");
            byte[] forward = Render(rig, "Forward");

            Spread(deferred).ShouldBeGreaterThan(40,
                "the scene must have light and dark in it, or two blank frames would pass");

            PipelineCompare.Reading reading = PipelineCompare.Compare(deferred, forward);

            reading.Passes.ShouldBeTrue(
                $"{reading}: deferred {Pixel(deferred, reading.WorstPixel)} against " +
                $"forward {Pixel(forward, reading.WorstPixel)}");
        }
        finally
        {
            renderer.SetFramebufferSize(new Vector2D<int>(restoreWidth, restoreHeight));
        }
    }

    [Fact]
    public void The_sun_casts_a_shadow_in_the_forward_path()
    {
        // The control for the Sun case: both paths agreeing on no shadow at
        // all would pass it.
        OpenGLRenderer renderer = _fixture.Renderer;
        bool restoreShadows = renderer.ShadowsEnabled;
        renderer.GetFramebufferSize(out int restoreWidth, out int restoreHeight);

        try
        {
            renderer.SetFramebufferSize(new Vector2D<int>(Size, Size));

            renderer.ShadowsEnabled = true;
            byte[] on = Render(Rig.Sun, "Forward");
            renderer.ShadowsEnabled = false;
            byte[] off = Render(Rig.Sun, "Forward");

            int darkened = 0;
            for (int i = 0; i < on.Length; i += 4)
            {
                if (off[i] - on[i] > 30)
                    darkened++;
            }

            darkened.ShouldBeGreaterThan(Size * Size / 100, "the block and the ball must shade the floor");
        }
        finally
        {
            renderer.ShadowsEnabled = restoreShadows;
            renderer.SetFramebufferSize(new Vector2D<int>(restoreWidth, restoreHeight));
        }
    }

    private static string Pixel(byte[] pixels, int index)
    {
        int i = index * 4;
        return $"({pixels[i]}, {pixels[i + 1]}, {pixels[i + 2]})";
    }

    // Brightest red minus darkest.
    private static int Spread(byte[] pixels)
    {
        int low = 255, high = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            low = Math.Min(low, pixels[i]);
            high = Math.Max(high, pixels[i]);
        }

        return high - low;
    }

    // The tone-mapped frame, as it would reach the window.
    private byte[] Render(Rig rig, string pipeline)
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        string restore = renderer.CurrentPipelineName;

        var (cubeVertices, cubeIndices) = Primitives.Cube();
        var (ballVertices, ballIndices) = Primitives.Sphere();
        Mesh cube = renderer.CreateMesh(cubeVertices, cubeIndices, VertexAttribute.StandardLayout);
        Mesh ball = renderer.CreateMesh(ballVertices, ballIndices, VertexAttribute.StandardLayout);
        Texture checker = renderer.CreateTexture(
            Checker(), 2, 2, TextureFormat.Rgba8, TextureColorSpace.Srgb,
            TextureFilter.Nearest, TextureWrap.Repeat);

        RenderTarget output = renderer.CreateRenderTarget(new RenderTargetDesc(Size, Size));
        var view = new RenderView();

        try
        {
            Scene scene = BuildScene(rig, cube, ball, checker);

            renderer.TrySelectPipeline(pipeline).ShouldBeTrue();
            renderer.Outlines.Clear();

            scene.BuildRenderView(scene.Camera, view);
            renderer.Render(scene, view, 1.0 / 60.0);
            renderer.ResolveForTest(renderer.SceneTarget!.ColorTexture!, output);

            var pixels = new byte[Size * Size * 4];
            renderer.ReadTargetPixels(output, pixels);
            return pixels;
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            renderer.DestroyMesh(cube);
            renderer.DestroyMesh(ball);
            renderer.DestroyTexture(checker);
            while (renderer.CurrentPipelineName != restore) renderer.NextPipeline();
        }
    }

    private static byte[] Checker() =>
    [
        255, 255, 255, 255, 150, 150, 150, 255,
        150, 150, 150, 255, 255, 255, 255, 255,
    ];

    // A floor, a rough block, a polished metal ball and a small lamp that
    // glows. Between them they use every parameter a surface has.
    private Scene BuildScene(Rig rig, Mesh cube, Mesh ball, Texture checker)
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        var scene = new Scene("parity");
        scene.Camera.Position = new Vector3(0f, 3.2f, 6.5f);
        scene.Camera.LookAt(new Vector3(0f, 0.4f, 0f));

        Material Surface(Vector3 colour, float roughness, float metallic, float occlusion, Vector3 emissive) =>
            new Material(renderer.DefaultShader)
                .SetVector3("uBaseColor", colour)
                .SetFloat("uRoughness", roughness)
                .SetFloat("uMetallic", metallic)
                .SetFloat("uAmbientOcclusion", occlusion)
                .SetVector3("uEmissive", emissive)
                .SetFloat("uShadingModel", 0f)
                .SetTexture("uDiffuse", 0, checker);

        void Place(string name, Mesh mesh, Material material, Vector3 at, Vector3 scale)
        {
            SceneNode node = scene.Root.CreateChild(name);
            node.LocalTransform = new Transform { Position = at, Rotation = Quaternion.Identity, Scale = scale };
            node.MeshRenderer = new MeshRenderer(mesh, material);
        }

        Place("Floor", cube, Surface(new Vector3(0.7f, 0.7f, 0.7f), 0.6f, 0f, 1f, Vector3.Zero),
            new Vector3(0f, -0.1f, 0f), new Vector3(14f, 0.2f, 14f));
        Place("Block", cube, Surface(new Vector3(0.8f, 0.3f, 0.2f), 0.9f, 0f, 0.5f, Vector3.Zero),
            new Vector3(-1.6f, 0.6f, 0f), new Vector3(1.2f));
        Place("Ball", ball, Surface(new Vector3(1f, 0.77f, 0.34f), 0.25f, 1f, 1f, Vector3.Zero),
            new Vector3(1.4f, 0.8f, 0.4f), new Vector3(1.6f));
        Place("Glow", cube, Surface(new Vector3(0.1f, 0.1f, 0.1f), 0.5f, 0f, 1f, new Vector3(0.2f, 1.6f, 0.5f)),
            new Vector3(0f, 0.25f, 1.6f), new Vector3(0.5f));

        void Lamp(string name, Vector3 at, Vector3 travel, Light light)
        {
            SceneNode node = scene.Root.CreateChild(name);
            node.LocalPosition = at;
            node.LocalRotation = Light.RotationForDirection(travel);
            node.Light = light;
        }

        bool all = rig == Rig.Everything;

        if (all || rig == Rig.Sun)
        {
            Lamp("Sun", Vector3.Zero, new Vector3(-0.45f, -0.8f, -0.35f),
                new Light { Kind = LightKind.Directional, Color = new Vector3(1f, 0.96f, 0.88f), Intensity = 3f });
        }

        if (all || rig == Rig.Point)
        {
            Lamp("Point", new Vector3(0.2f, 1.6f, 2.2f), -Vector3.UnitY,
                new Light { Kind = LightKind.Point, Color = new Vector3(1f, 0.7f, 0.4f), Intensity = 30f, Range = 8f });
        }

        if (all || rig == Rig.Spot)
        {
            Lamp("Spot", new Vector3(-2.5f, 3f, 2.5f), new Vector3(0.6f, -0.75f, -0.5f),
                new Light
                {
                    Kind = LightKind.Spot,
                    Color = new Vector3(0.6f, 0.8f, 1f),
                    Intensity = 80f,
                    Range = 12f,
                    InnerAngle = 14f,
                    OuterAngle = 24f,
                });
        }

        if (all || rig == Rig.Rect)
        {
            Lamp("Rect", new Vector3(0f, 2.4f, -0.5f), -Vector3.UnitY,
                new Light
                {
                    Kind = LightKind.Rect,
                    Color = Vector3.One,
                    Intensity = 25f,
                    Range = 9f,
                    Width = 3f,
                    Height = 0.6f,
                });
        }

        if (all || rig == Rig.Disc)
        {
            Lamp("Disc", new Vector3(3f, 1.4f, 1.2f), new Vector3(-1f, -0.3f, -0.2f),
                new Light
                {
                    Kind = LightKind.Disc,
                    Color = new Vector3(1f, 0.5f, 0.8f),
                    Intensity = 25f,
                    Range = 9f,
                    Radius = 0.8f,
                });
        }

        return scene;
    }
}
