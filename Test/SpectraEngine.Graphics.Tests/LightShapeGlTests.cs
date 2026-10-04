using System.Numerics;
using Silk.NET.OpenGL;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using SpectraEngine.Core.Scene;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Pixel tests of spot and rect lights on a real driver.
/// </summary>
[Collection(GlRendererCollection.Name)]
public sealed class LightShapeGlTests
{
    private const int ProbeSize = 64;

    private readonly GlRendererFixture _fixture;

    public LightShapeGlTests(GlRendererFixture fixture) => _fixture = fixture;

    [Fact]
    public void A_spot_lights_the_surface_inside_its_cone_and_not_the_one_outside()
    {
        (int wR, int wG, int wB) = Render(BuildSpotScene(outerAngle: 60f));
        (int nR, int nG, int nB) = Render(BuildSpotScene(outerAngle: 2f));

        int wide = wR + wG + wB;
        int narrow = nR + nG + nB;

        wide.ShouldBeGreaterThan(narrow * 3,
            "a surface inside the cone must be dominated by the spot and one outside it must " +
            "fall to ambient; if the two agree, the cone axis or its cosine comparison is not " +
            "reaching the light pass");
    }

    [Fact]
    public void A_spot_aimed_away_lights_nothing()
    {
        // Catches the sign of l in SpotFactor.
        (int r, int g, int b) = Render(BuildSpotScene(outerAngle: 30f, aimAtWall: false));
        (int aR, int aG, int aB) = Render(BuildSpotScene(outerAngle: 2f));

        (r + g + b).ShouldBeLessThan((aR + aG + aB) * 2,
            "a spot pointing away from the wall must leave it at ambient");
    }

    [Fact]
    public void A_rect_light_lights_the_face_it_faces_and_not_the_one_behind_it()
    {
        (int fR, int fG, int fB) = Render(BuildRectScene(facingWall: true));
        (int bR, int bG, int bB) = Render(BuildRectScene(facingWall: false));

        (fR + fG + fB).ShouldBeGreaterThan((bR + bG + bB) * 3,
            "a rect light turned away from the wall must not light it");
    }

    [Fact]
    public void A_rect_light_with_no_extent_matches_a_point_light_of_the_same_intensity()
    {
        (int rR, int rG, int rB) = Render(BuildRectScene(facingWall: true, width: 0.001f, height: 0.001f));
        (int pR, int pG, int pB) = Render(BuildRectScene(facingWall: true, asPoint: true));

        // A few codes of slack for the representative point's rounding.
        Close(rR, pR).ShouldBeTrue($"red {rR} against {pR}");
        Close(rG, pG).ShouldBeTrue($"green {rG} against {pG}");
        Close(rB, pB).ShouldBeTrue($"blue {rB} against {pB}");

        static bool Close(int a, int b) => System.Math.Abs(a - b) <= 6;
    }

    [Theory]
    [InlineData(LightKind.Rect, "Deferred")]
    [InlineData(LightKind.Rect, "Forward")]
    [InlineData(LightKind.Disc, "Deferred")]
    [InlineData(LightKind.Disc, "Forward")]
    public void What_a_panel_lays_on_a_rough_wall_does_not_follow_the_camera(LightKind kind, string pipeline)
    {
        // One spot on the wall, seen from the left and from the right. Lit
        // from the point the mirror ray lands on, the two readings part.
        var spot = new Vector3(1.5f, 0f, WallFace);
        var left = new Vector3(-2.6f, 0f, 1.5f);
        var right = new Vector3(2.6f, 0f, 1.5f);

        int fromLeft = Render(BuildPanelScene(kind, spot + left, spot), pipeline).R;
        int fromRight = Render(BuildPanelScene(kind, spot + right, spot), pipeline).R;
        int unlit = Render(BuildPanelScene(kind, spot + left, spot, facingWall: false), pipeline).R;

        fromLeft.ShouldBeGreaterThan(unlit + 20, "the panel has to light the spot, or there is nothing to compare");
        System.Math.Abs(fromLeft - fromRight).ShouldBeLessThanOrEqualTo(4,
            $"the spot reads {fromLeft} from the left and {fromRight} from the right");
    }

    [Theory]
    [InlineData("Deferred")]
    [InlineData("Forward")]
    public void A_panel_mirrored_in_a_smooth_wall_is_dimmer_than_a_point_of_the_same_intensity(string pipeline)
    {
        // Square-on, the mirror ray lands on the lamp's centre either way. The
        // panel's highlight is spread over the panel, the point's is not.
        int point = Render(BuildMirrorScene(asPoint: true), pipeline).R;
        int panel = Render(BuildMirrorScene(asPoint: false), pipeline).R;

        point.ShouldBeLessThan(250, "a clipped reading compares nothing");
        panel.ShouldBeLessThan(point - 15);
    }

    private const float WallFace = 0.25f;

    // Wall with its +z face at z = 0.25 and one lamp in front, square-on so
    // N, L and V agree.
    private Scene BuildScene(out SceneNode lamp, float roughness = 0.5f)
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        var scene = new Scene("shapes");
        scene.Camera.Position = new Vector3(0f, 0f, 3f);
        scene.Camera.LookAt(Vector3.Zero);

        var (vertices, indices) = Primitives.Cube();
        Mesh mesh = renderer.CreateMesh(vertices, indices, VertexAttribute.StandardLayout);

        Texture white = renderer.CreateTexture(
            [255, 255, 255, 255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);

        var material = new Material(renderer.DefaultShader);
        material
            .SetVector3("uBaseColor", new Vector3(0.8f, 0.8f, 0.8f))
            .SetFloat("uRoughness", roughness)
            .SetFloat("uMetallic", 0f)
            .SetFloat("uAmbientOcclusion", 1f)
            .SetVector3("uEmissive", Vector3.Zero)
            .SetFloat("uShadingModel", 0f)
            .SetTexture("uDiffuse", 0, white);

        var wall = scene.Root.CreateChild("Wall");
        wall.LocalTransform = new Transform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = new Vector3(8f, 8f, 0.5f),
        };
        wall.MeshRenderer = new MeshRenderer(mesh, material);

        lamp = scene.Root.CreateChild("Lamp");
        lamp.LocalPosition = new Vector3(0f, 0f, 1.25f);
        return scene;
    }

    private Scene BuildSpotScene(float outerAngle, bool aimAtWall = true)
    {
        Scene scene = BuildScene(out SceneNode lamp);

        // Direction of travel: toward the wall is -z.
        lamp.LocalTransform = lamp.LocalTransform with
        {
            Rotation = Light.RotationForDirection(aimAtWall ? -Vector3.UnitZ : Vector3.UnitZ),
        };

        lamp.Light = new Light
        {
            Kind = LightKind.Spot,
            Color = Vector3.One,
            Intensity = 40f,
            Range = 3f,
            InnerAngle = System.Math.Max(outerAngle - 1f, 0f),
            OuterAngle = outerAngle,
        };

        return scene;
    }

    private Scene BuildRectScene(
        bool facingWall, float width = 1f, float height = 1f, bool asPoint = false)
    {
        Scene scene = BuildScene(out SceneNode lamp);

        lamp.LocalTransform = lamp.LocalTransform with
        {
            Rotation = Light.RotationForDirection(facingWall ? -Vector3.UnitZ : Vector3.UnitZ),
        };

        lamp.Light = new Light
        {
            Kind = asPoint ? LightKind.Point : LightKind.Rect,
            Color = Vector3.One,
            Intensity = 40f,
            Range = 3f,
            Width = width,
            Height = height,
        };

        return scene;
    }

    // A wide panel one unit in front of a fully rough wall, and a camera
    // looking at one spot on it. Rough, so the highlight is next to nothing.
    private Scene BuildPanelScene(LightKind kind, Vector3 eye, Vector3 spot, bool facingWall = true)
    {
        Scene scene = BuildScene(out SceneNode lamp, roughness: 1f);
        scene.Camera.Position = eye;
        scene.Camera.LookAt(spot);

        lamp.LocalTransform = lamp.LocalTransform with
        {
            Rotation = Light.RotationForDirection(facingWall ? -Vector3.UnitZ : Vector3.UnitZ),
        };

        lamp.Light = new Light
        {
            Kind = kind,
            Color = Vector3.One,
            Intensity = 2f,
            Range = 8f,
            Width = 4f,
            Height = 0.5f,
            Radius = 2f,
        };

        return scene;
    }

    private Scene BuildMirrorScene(bool asPoint)
    {
        Scene scene = BuildScene(out SceneNode lamp, roughness: 0.25f);

        lamp.LocalTransform = lamp.LocalTransform with
        {
            Rotation = Light.RotationForDirection(-Vector3.UnitZ),
        };

        lamp.Light = new Light
        {
            Kind = asPoint ? LightKind.Point : LightKind.Rect,
            Color = Vector3.One,
            Intensity = 1f,
            Range = 3f,
            Width = 1f,
            Height = 1f,
        };

        return scene;
    }

    private (int R, int G, int B) Render(Scene scene, string pipeline = "Deferred")
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        string restore = renderer.CurrentPipelineName;
        RenderTarget probe = renderer.CreateRenderTarget(new RenderTargetDesc(ProbeSize, ProbeSize));
        var view = new RenderView();

        try
        {
            renderer.TrySelectPipeline(pipeline).ShouldBeTrue();
            renderer.ProbeTarget = probe;

            scene.BuildRenderView(scene.Camera, view);
            renderer.Render(scene, view, 1.0 / 60.0);

            return ReadPixel(probe, ProbeSize / 2, ProbeSize / 2);
        }
        finally
        {
            renderer.ProbeTarget = null;
            renderer.DestroyRenderTarget(probe);

            // The fixture is shared, so put the pipeline back.
            while (renderer.CurrentPipelineName != restore)
                renderer.NextPipeline();
        }
    }

    private unsafe (int R, int G, int B) ReadPixel(RenderTarget target, int x, int y)
    {
        GL gl = _fixture.Gl;
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
        gl.FramebufferTexture2D(
            FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, ((OpenGLTexture)target.ColorTexture!).Handle, 0);

        var pixel = new byte[4];
        fixed (byte* p = pixel)
            gl.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.DeleteFramebuffer(fbo);

        return (pixel[0], pixel[1], pixel[2]);
    }
}
