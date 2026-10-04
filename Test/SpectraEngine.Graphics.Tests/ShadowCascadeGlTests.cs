using System.Numerics;
using Silk.NET.Maths;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using SpectraEngine.Core.Scene;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Where one shadow cascade hands over to the next, and how far out a shadow
/// still meets the thing that casts it.
/// </summary>
[Collection(GlRendererCollection.Name)]
public sealed class ShadowCascadeGlTests
{
    private const int Size = 256;

    private static readonly Vector3 SunTravel = new(0.5f, -0.75f, 0.35f);

    private readonly GlRendererFixture _fixture;

    public ShadowCascadeGlTests(GlRendererFixture fixture) => _fixture = fixture;

    // With fewer than four cascades a tile of the atlas stays empty, which is
    // what a border has to read from to show. With four the roof fills them all.
    [Theory]
    [InlineData("Deferred", 2)]
    [InlineData("Deferred", 3)]
    [InlineData("Deferred", 4)]
    [InlineData("Forward", 2)]
    [InlineData("Forward", 3)]
    [InlineData("Forward", 4)]
    public void A_cascade_border_draws_no_line_through_a_shadow(string pipeline, int cascades)
    {
        // A floor wholly in the shade of a low roof, seen from head height out
        // past every border. Shade on a flat floor is one colour, so a pixel
        // that is brighter than the rest is a border showing.
        Scene scene = BuildShadedFloor(out Mesh cube, out Texture white);
        byte[] pixels;
        try
        {
            pixels = Render(scene, pipeline, cascades);
        }
        finally
        {
            Release(cube, white);
        }

        var reds = new int[Size * Size];
        for (int i = 0; i < reds.Length; i++)
            reds[i] = pixels[i * 4];

        int[] sorted = [.. reds];
        Array.Sort(sorted);
        int median = sorted[sorted.Length / 2];

        int brightest = 0, at = 0, bright = 0;
        for (int i = 0; i < reds.Length; i++)
        {
            if (reds[i] > median + 10) bright++;
            if (reds[i] > brightest)
            {
                brightest = reds[i];
                at = i;
            }
        }

        median.ShouldBeLessThan(120, "the floor has to be in the shade, or there is no shade to draw a line through");
        bright.ShouldBe(0,
            $"{bright} pixels are lit inside the shade; the floor reads {median} and the brightest " +
            $"reads {brightest} at ({at % Size}, {at / Size})");
    }

    [Theory]
    [InlineData("Deferred")]
    [InlineData("Forward")]
    public void A_wall_seen_from_across_a_yard_still_shades_the_ground_at_its_foot(string pipeline)
    {
        // The depth bias grows with the cascade's texel, and it pushes a thin
        // wall's shadow away from the wall. Thirty-two units out, the ground a
        // hand's width behind the wall has to be in it.
        const float Distance = 32f;

        OpenGLRenderer renderer = _fixture.Renderer;
        bool restoreShadows = renderer.ShadowsEnabled;

        Scene scene = BuildWall(Distance, out Mesh cube, out Texture white);
        try
        {
            renderer.ShadowsEnabled = true;
            int shaded = Centre(Render(scene, pipeline, ShadowMap.MaxCascades));
            renderer.ShadowsEnabled = false;
            int lit = Centre(Render(scene, pipeline, ShadowMap.MaxCascades));

            shaded.ShouldBeLessThan(lit - 40,
                $"the ground behind the wall reads {shaded} with shadows on and {lit} with them off");
        }
        finally
        {
            renderer.ShadowsEnabled = restoreShadows;
            Release(cube, white);
        }

        static int Centre(byte[] pixels) => pixels[(((Size / 2) * Size) + (Size / 2)) * 4];
    }

    // The tone-mapped frame. The cascade count is put back afterwards: the
    // fixture's renderer keeps its shadow map between tests.
    private byte[] Render(Scene scene, string pipeline, int cascades)
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        renderer.GetFramebufferSize(out int restoreWidth, out int restoreHeight);
        string restore = renderer.CurrentPipelineName;

        RenderTarget output = renderer.CreateRenderTarget(new RenderTargetDesc(Size, Size));
        var view = new RenderView();

        try
        {
            renderer.SetFramebufferSize(new Vector2D<int>(Size, Size));
            renderer.TrySelectPipeline(pipeline).ShouldBeTrue();
            renderer.Outlines.Clear();

            scene.BuildRenderView(scene.Camera, view);

            // The map does not exist until a frame has drawn one.
            renderer.Render(scene, view, 1.0 / 60.0);
            if (renderer.ShadowMap is { } map)
            {
                map.CascadeCount = cascades;
                renderer.Render(scene, view, 1.0 / 60.0);
            }

            renderer.ResolveForTest(renderer.SceneTarget!.ColorTexture!, output);

            var pixels = new byte[Size * Size * 4];
            renderer.ReadTargetPixels(output, pixels);
            return pixels;
        }
        finally
        {
            if (renderer.ShadowMap is { } map)
                map.CascadeCount = ShadowMap.MaxCascades;

            renderer.DestroyRenderTarget(output);
            while (renderer.CurrentPipelineName != restore) renderer.NextPipeline();
            renderer.SetFramebufferSize(new Vector2D<int>(restoreWidth, restoreHeight));
        }
    }

    private void Release(Mesh cube, Texture white)
    {
        _fixture.Renderer.DestroyMesh(cube);
        _fixture.Renderer.DestroyTexture(white);
    }

    private Scene NewScene(out Mesh cube, out Action<string, Vector3, Vector3> slab, out Texture white)
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        var (vertices, indices) = Primitives.Cube();
        Mesh mesh = renderer.CreateMesh(vertices, indices, VertexAttribute.StandardLayout);
        Texture texture = renderer.CreateTexture(
            [255, 255, 255, 255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);

        var scene = new Scene("shade");
        scene.Camera.AspectRatio = 1f;

        // Fully rough, so the sun's highlight does not grade the ground.
        Material matte = new Material(renderer.DefaultShader)
            .SetVector3("uBaseColor", new Vector3(0.75f, 0.75f, 0.75f))
            .SetFloat("uRoughness", 1f)
            .SetFloat("uMetallic", 0f)
            .SetFloat("uAmbientOcclusion", 1f)
            .SetVector3("uEmissive", Vector3.Zero)
            .SetFloat("uShadingModel", 0f)
            .SetTexture("uDiffuse", 0, texture);

        slab = (name, at, scale) =>
        {
            SceneNode node = scene.Root.CreateChild(name);
            node.LocalTransform = new Transform { Position = at, Rotation = Quaternion.Identity, Scale = scale };
            node.MeshRenderer = new MeshRenderer(mesh, matte);
        };

        SceneNode sun = scene.Root.CreateChild("Sun");
        sun.LocalRotation = Light.RotationForDirection(SunTravel);
        sun.Light = new Light { Kind = LightKind.Directional, Color = Vector3.One, Intensity = 3f };

        cube = mesh;
        white = texture;
        return scene;
    }

    private Scene BuildShadedFloor(out Mesh cube, out Texture white)
    {
        Scene scene = NewScene(out cube, out Action<string, Vector3, Vector3> slab, out white);

        // Tilted down far enough that the top row is still floor, three
        // degrees under the horizon and about thirty units out.
        const float EyeHeight = 1.6f;
        Camera camera = scene.Camera;
        float pitch = (camera.FieldOfView * 0.5f) + (3f * MathF.PI / 180f);
        camera.Position = new Vector3(0f, EyeHeight, 0f);
        camera.LookAt(new Vector3(0f, 0f, EyeHeight / MathF.Tan(pitch)));

        slab("Floor", new Vector3(0f, -0.25f, 0f), new Vector3(240f, 0.5f, 240f));
        slab("Roof", new Vector3(0f, 3.25f, 0f), new Vector3(200f, 0.5f, 200f));
        return scene;
    }

    // A thin wall on open ground, and a camera on its shaded side looking at
    // the ground just behind its foot.
    private Scene BuildWall(float distance, out Mesh cube, out Texture white)
    {
        Scene scene = NewScene(out cube, out Action<string, Vector3, Vector3> slab, out white);

        const float HalfThickness = 0.1f;
        slab("Ground", new Vector3(0f, -0.25f, 0f), new Vector3(240f, 0.5f, 240f));
        slab("Wall", new Vector3(0f, 1.5f, 0f), new Vector3(HalfThickness * 2f, 3f, 6f));

        // The sun travels toward +x, so +x is the shaded side.
        var foot = new Vector3(HalfThickness + 0.25f, 0f, 0f);
        scene.Camera.Position = foot + (Vector3.Normalize(new Vector3(0.8f, 0.55f, 0.2f)) * distance);
        scene.Camera.LookAt(foot);
        return scene;
    }
}
