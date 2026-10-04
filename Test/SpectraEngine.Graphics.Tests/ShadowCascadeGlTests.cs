using System.Numerics;
using Silk.NET.Maths;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using SpectraEngine.Core.Scene;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Where one shadow cascade hands over to the next, what gets to cast into a
/// cascade, and whether a shadow meets the thing that casts it.
/// </summary>
// ShadowCurvedReceiverGlTests holds the other end of the same rope: these fail
// when the bias is too large, that one when it is too small.
[Collection(GlRendererCollection.Name)]
public sealed class ShadowCascadeGlTests
{
    private const int Size = 256;

    private static readonly Vector3 SunTravel = Vector3.Normalize(new Vector3(0.5f, -0.75f, 0.35f));

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
        Scene scene = BuildShadedFloor(roofHeight: 3f, out Mesh cube, out Texture white);
        byte[] pixels;
        try
        {
            pixels = Render(scene, pipeline, cascades);
        }
        finally
        {
            Release(cube, white);
        }

        (int median, int brightest, int at, int bright) = Shade(pixels);

        median.ShouldBeLessThan(120, "the floor has to be in the shade, or there is no shade to draw a line through");
        bright.ShouldBe(0,
            $"{bright} pixels are lit inside the shade; the floor reads {median} and the brightest " +
            $"reads {brightest} at ({at % Size}, {at / Size})");
    }

    [Theory]
    [InlineData("Deferred")]
    [InlineData("Forward")]
    public void A_roof_far_overhead_still_shades_the_floor(string pipeline)
    {
        // The near cascades' volumes end a few units above the floor, for
        // depth precision. A roof 45 up is outside them and has to be drawn
        // into them all the same.
        Scene scene = BuildShadedFloor(roofHeight: 45f, out Mesh cube, out Texture white);
        byte[] pixels;
        try
        {
            pixels = Render(scene, pipeline, ShadowMap.MaxCascades);
        }
        finally
        {
            Release(cube, white);
        }

        (int median, int brightest, _, int bright) = Shade(pixels);

        median.ShouldBeLessThan(120, "the floor under the roof is lit");
        bright.ShouldBe(0, $"{bright} pixels of the floor are lit; the brightest reads {brightest}");
    }

    [Theory]
    [InlineData("Deferred")]
    [InlineData("Forward")]
    public void A_wall_seen_from_across_a_yard_still_shades_the_ground_at_its_foot(string pipeline)
    {
        // Every bias is a number of texels, so a coarse cascade pushes a
        // shadow further from its caster. Thirty-two units out, the ground a
        // hand's width behind a wall has to be in the wall's shadow.
        var foot = new Vector3(0.1f + 0.25f, 0f, 0f);
        Scene scene = BuildSlabOnGround(thickness: 0.2f, out Mesh cube, out Texture white);
        scene.Camera.Position = foot + (Vector3.Normalize(new Vector3(0.8f, 0.55f, 0.2f)) * 32f);
        scene.Camera.LookAt(foot);

        AssertCentreShaded(scene, pipeline, Size, cube, white, "the ground behind the wall");
    }

    [Theory]
    [InlineData("Deferred")]
    [InlineData("Forward")]
    public void A_sheets_shadow_starts_at_the_sheet(string pipeline)
    {
        // The depth pass pushes a caster's stored depth away from the light.
        // For a sheet one centimetre thick that push is the whole gap between
        // it and its shadow. From 32 units up, the ground eleven centimetres
        // behind the sheet has to be shaded. With a slope bias of 8 the shadow
        // began at seventeen.
        const float Thickness = 0.01f;
        const int Resolution = 512;

        var behind = new Vector3((Thickness * 0.5f) + 0.11f, 0f, 0f);
        Scene scene = BuildSlabOnGround(Thickness, out Mesh cube, out Texture white);

        // Nearly straight down, so the ground is not foreshortened.
        scene.Camera.Position = behind + new Vector3(0.64f, 32f, 0.96f);
        scene.Camera.LookAt(behind);

        AssertCentreShaded(scene, pipeline, Resolution, cube, white, "the ground behind the sheet");
    }

    [Theory]
    [InlineData("Deferred", 6f)]
    [InlineData("Deferred", 25f)]
    [InlineData("Forward", 6f)]
    [InlineData("Forward", 25f)]
    public void A_slope_the_sun_grazes_does_not_shade_itself(string pipeline, float distance)
    {
        // Eight degrees off edge-on. Along such a surface the stored depth
        // changes fastest from one texel to the next, so this is where a
        // surface most easily shades itself. With no bias it darkens by 18.
        OpenGLRenderer renderer = _fixture.Renderer;
        bool restoreShadows = renderer.ShadowsEnabled;

        Scene scene = BuildGrazedSlope(tiltDegrees: 82f, distance, out Mesh cube, out Texture white);
        try
        {
            renderer.ShadowsEnabled = true;
            byte[] on = Render(scene, pipeline, ShadowMap.MaxCascades);
            renderer.ShadowsEnabled = false;
            byte[] off = Render(scene, pipeline, ShadowMap.MaxCascades);

            int worst = 0, lit = 0;
            for (int i = 0; i < on.Length; i += 4)
            {
                if (off[i] < 40) continue;
                lit++;
                worst = Math.Max(worst, off[i] - on[i]);
            }

            lit.ShouldBeGreaterThan(Size * Size / 4, "the slope has to be in view and lit");
            worst.ShouldBeLessThanOrEqualTo(6, $"turning shadows on darkened the bare slope by {worst}");
        }
        finally
        {
            renderer.ShadowsEnabled = restoreShadows;
            Release(cube, white);
        }
    }

    [Theory]
    [InlineData("Deferred")]
    [InlineData("Forward")]
    public void A_shadow_fades_out_before_its_range_ends(string pipeline)
    {
        // A floor shaded all the way to the horizon, seen from high up. Where
        // the shadow range ends the shade has to thin out, not stop at a line.
        OpenGLRenderer renderer = _fixture.Renderer;
        bool restoreShadows = renderer.ShadowsEnabled;

        Scene scene = BuildShadedPlain(out Mesh cube, out Texture white);
        try
        {
            // The range is the map's, and the map exists once a frame has drawn.
            renderer.ShadowsEnabled = true;
            Render(scene, pipeline, ShadowMap.MaxCascades);
            float range = renderer.ShadowMap!.Distance;
            renderer.ShadowMap.FadeRange.ShouldBe(0.2f, "the distances below are picked around a fade over the last fifth");

            float height = scene.Camera.Position.Y;
            Vector3 At(float fraction)
            {
                float reach = range * fraction;
                return new Vector3(0f, 0f, MathF.Sqrt((reach * reach) - (height * height)));
            }

            scene.Camera.LookAt(At(0.9f));

            byte[] on = Render(scene, pipeline, ShadowMap.MaxCascades);
            renderer.ShadowsEnabled = false;
            byte[] off = Render(scene, pipeline, ShadowMap.MaxCascades);

            Matrix4x4 viewProjection = scene.Camera.View * scene.Camera.Projection;
            int Read(byte[] pixels, float fraction)
            {
                Vector4 clip = Vector4.Transform(new Vector4(At(fraction), 1f), viewProjection);
                int x = (int)(((clip.X / clip.W * 0.5f) + 0.5f) * Size);
                int y = (int)(((clip.Y / clip.W * 0.5f) + 0.5f) * Size);
                x.ShouldBeInRange(0, Size - 1);
                y.ShouldBeInRange(0, Size - 1);
                return pixels[((y * Size) + x) * 4];
            }

            int near = Read(on, 0.5f);
            int before = Read(on, 0.7f);
            int halfway = Read(on, 0.9f);
            int past = Read(on, 1.05f);
            string readings = $"{near} at half the range, {before} before the fade, {halfway} halfway through it, {past} past it";

            near.ShouldBeLessThan(Read(off, 0.5f) - 40, $"the floor has to be shaded to begin with: {readings}");
            Math.Abs(before - near).ShouldBeLessThanOrEqualTo(6, $"the fade must not start early: {readings}");
            halfway.ShouldBeGreaterThan(before + 15, $"halfway through the fade the shade has thinned: {readings}");
            halfway.ShouldBeLessThan(past - 15, $"and has not gone yet: {readings}");
            Math.Abs(past - Read(off, 1.05f)).ShouldBeLessThanOrEqualTo(4, $"past the range nothing is shaded: {readings}");
        }
        finally
        {
            renderer.ShadowsEnabled = restoreShadows;
            Release(cube, white);
        }
    }

    // The floor's reading, and what is brighter than it.
    private static (int Median, int Brightest, int At, int Bright) Shade(byte[] pixels)
    {
        var reds = new int[pixels.Length / 4];
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

        return (median, brightest, at, bright);
    }

    // The camera looks at one spot of ground. It has to be clearly darker with
    // shadows on than with them off.
    private void AssertCentreShaded(
        Scene scene, string pipeline, int size, Mesh cube, Texture white, string what)
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        bool restoreShadows = renderer.ShadowsEnabled;

        try
        {
            renderer.ShadowsEnabled = true;
            int shaded = Centre(Render(scene, pipeline, ShadowMap.MaxCascades, size));
            renderer.ShadowsEnabled = false;
            int lit = Centre(Render(scene, pipeline, ShadowMap.MaxCascades, size));

            shaded.ShouldBeLessThan(lit - 40, $"{what} reads {shaded} with shadows on and {lit} with them off");
        }
        finally
        {
            renderer.ShadowsEnabled = restoreShadows;
            Release(cube, white);
        }

        int Centre(byte[] pixels) => pixels[(((size / 2) * size) + (size / 2)) * 4];
    }

    // The tone-mapped frame. The cascade count is put back afterwards: the
    // fixture's renderer keeps its shadow map between tests.
    private byte[] Render(Scene scene, string pipeline, int cascades, int size = Size)
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        renderer.GetFramebufferSize(out int restoreWidth, out int restoreHeight);
        string restore = renderer.CurrentPipelineName;

        RenderTarget output = renderer.CreateRenderTarget(new RenderTargetDesc(size, size));
        var view = new RenderView();

        try
        {
            renderer.SetFramebufferSize(new Vector2D<int>(size, size));
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

            var pixels = new byte[size * size * 4];
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

    // A scene with a sun in it, and a way to put matte slabs into it.
    private Scene NewScene(out Mesh cube, out Action<Vector3, Quaternion, Vector3> slab, out Texture white)
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

        slab = (at, rotation, scale) =>
        {
            SceneNode node = scene.Root.CreateChild("Slab");
            node.LocalTransform = new Transform { Position = at, Rotation = rotation, Scale = scale };
            node.MeshRenderer = new MeshRenderer(mesh, matte);
        };

        SceneNode sun = scene.Root.CreateChild("Sun");
        sun.LocalRotation = Light.RotationForDirection(SunTravel);
        sun.Light = new Light { Kind = LightKind.Directional, Color = Vector3.One, Intensity = 3f };

        cube = mesh;
        white = texture;
        return scene;
    }

    // A floor under a roof wide enough to shade everything in view.
    private Scene BuildShadedFloor(float roofHeight, out Mesh cube, out Texture white)
    {
        Scene scene = NewScene(out cube, out Action<Vector3, Quaternion, Vector3> slab, out white);

        // Tilted down far enough that the top row is still floor, three
        // degrees under the horizon and about thirty units out.
        const float EyeHeight = 1.6f;
        Camera camera = scene.Camera;
        float pitch = (camera.FieldOfView * 0.5f) + (3f * MathF.PI / 180f);
        camera.Position = new Vector3(0f, EyeHeight, 0f);
        camera.LookAt(new Vector3(0f, 0f, EyeHeight / MathF.Tan(pitch)));

        slab(new Vector3(0f, -0.25f, 0f), Quaternion.Identity, new Vector3(400f, 0.5f, 400f));
        slab(new Vector3(0f, roofHeight + 0.25f, 0f), Quaternion.Identity, new Vector3(300f, 0.5f, 300f));
        return scene;
    }

    // A plain shaded out to the horizon by a roof just over the camera, which
    // looks down on it from sixty units up.
    private Scene BuildShadedPlain(out Mesh cube, out Texture white)
    {
        Scene scene = NewScene(out cube, out Action<Vector3, Quaternion, Vector3> slab, out white);

        const float Height = 60f;
        scene.Camera.Position = new Vector3(0f, Height, 0f);
        scene.Camera.LookAt(new Vector3(0f, 0f, 100f));

        slab(new Vector3(0f, -0.25f, 0f), Quaternion.Identity, new Vector3(6000f, 0.5f, 6000f));
        slab(new Vector3(0f, Height + 10.25f, 0f), Quaternion.Identity, new Vector3(4000f, 0.5f, 4000f));
        return scene;
    }

    // An upright slab on open ground, three high and eight long, its faces at
    // plus and minus half its thickness in x. The sun travels toward +x, so +x
    // is the shaded side.
    private Scene BuildSlabOnGround(float thickness, out Mesh cube, out Texture white)
    {
        Scene scene = NewScene(out cube, out Action<Vector3, Quaternion, Vector3> slab, out white);
        slab(new Vector3(0f, -0.25f, 0f), Quaternion.Identity, new Vector3(240f, 0.5f, 240f));
        slab(new Vector3(0f, 1.5f, 0f), Quaternion.Identity, new Vector3(thickness, 3f, 8f));
        return scene;
    }

    // One big slab alone, its lit face tilted tiltDegrees away from facing the sun.
    private Scene BuildGrazedSlope(float tiltDegrees, float distance, out Mesh cube, out Texture white)
    {
        Scene scene = NewScene(out cube, out Action<Vector3, Quaternion, Vector3> slab, out white);

        Vector3 toLight = -SunTravel;
        Vector3 hinge = Vector3.Normalize(Vector3.Cross(toLight, Vector3.UnitY));
        Vector3 normal = Vector3.Normalize(Vector3.Transform(
            toLight, Quaternion.CreateFromAxisAngle(hinge, tiltDegrees * MathF.PI / 180f)));

        // The rotation that stands the slab's +y face along that normal.
        Vector3 turn = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal));
        float angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitY, normal), -1f, 1f));
        slab(-normal * 0.1f, Quaternion.CreateFromAxisAngle(turn, angle), new Vector3(120f, 0.2f, 120f));

        Vector3 along = Vector3.Normalize(Vector3.Cross(normal, hinge));
        scene.Camera.Position = ((normal * 0.7f) + (along * 0.5f) + (hinge * 0.5f)) * distance;
        scene.Camera.LookAt(Vector3.Zero);
        return scene;
    }
}
