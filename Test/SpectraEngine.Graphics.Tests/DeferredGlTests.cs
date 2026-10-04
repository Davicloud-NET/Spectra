using System.Numerics;
using Silk.NET.OpenGL;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using SpectraEngine.Core.Scene;
using Texture = SpectraEngine.Core.Graphics.Texture;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Pixel tests of the deferred pipeline on a real driver, rendered through
/// <see cref="Renderer.ProbeTarget"/>.
/// </summary>
[Collection(GlRendererCollection.Name)]
public sealed class DeferredGlTests
{
    private const int ProbeSize = 64;

    private readonly GlRendererFixture _fixture;

    public DeferredGlTests(GlRendererFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Pixels_no_geometry_covered_come_out_sky_coloured()
    {
        // The light pass covers every pixel, so it has to spot cleared depth
        // and write the sky itself.
        var scene = new Scene("empty");
        scene.Camera.Position = new Vector3(0f, 0f, 3f);
        scene.Camera.LookAt(Vector3.Zero);

        (int r, int g, int b) = RenderDeferred(scene);

        b.ShouldBeGreaterThan(r, "the sky is cornflower blue, so blue must dominate red");
        b.ShouldBeGreaterThan(g, "the sky is cornflower blue, so blue must dominate green");
    }

    [Fact]
    public void A_surface_within_a_lights_range_is_lit_and_outside_it_is_not()
    {
        // Really a reconstruction test: the light is one unit from the wall,
        // so it only reaches if the position rebuilt from depth is right.
        (int litR, int litG, int litB) = RenderDeferred(BuildSurfaceScene(lightRange: 2f));
        (int dimR, int dimG, int dimB) = RenderDeferred(BuildSurfaceScene(lightRange: 0.4f));

        int lit = litR + litG + litB;
        int dim = dimR + dimG + dimB;

        lit.ShouldBeGreaterThan(dim * 3,
            "a light one unit from the surface with a range of two should dominate ambient; " +
            "if it does not, the world position reconstructed from depth is not where the " +
            "geometry pass put this pixel");
    }

    [Fact]
    public void A_metal_and_a_dielectric_of_the_same_colour_shade_differently()
    {
        // Dim light: both results must stay under 255 or they compare equal.
        (int dR, int dG, int dB) = RenderDeferred(
            BuildSurfaceScene(lightRange: 2f, metallic: 0f, intensity: 6f));
        (int mR, int mG, int mB) = RenderDeferred(
            BuildSurfaceScene(lightRange: 2f, metallic: 1f, intensity: 6f));

        (dR == mR && dG == mG && dB == mB).ShouldBeFalse(
            $"metallic 0 and metallic 1 both shaded to ({dR}, {dG}, {dB}); " +
            "the metallic channel is not reaching the BRDF");
    }

    [Fact]
    public void A_caster_darkens_the_ground_beneath_it_and_nothing_else()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        bool restoreShadows = renderer.ShadowsEnabled;

        try
        {
            renderer.ShadowsEnabled = true;
            (int litR, int litG, int litB) = RenderDeferred(BuildShadowScene(), ProbeSize / 2, ProbeSize / 2);
            (int farR, int farG, int farB) = RenderDeferred(BuildShadowScene(), ProbeSize / 2, 3);

            renderer.ShadowsEnabled = false;
            (int noneR, int noneG, int noneB) = RenderDeferred(BuildShadowScene(), ProbeSize / 2, ProbeSize / 2);
            (int noneFarR, int noneFarG, int noneFarB) = RenderDeferred(BuildShadowScene(), ProbeSize / 2, 3);

            int shadowed = litR + litG + litB;
            int unshadowed = noneR + noneG + noneB;
            shadowed.ShouldBeLessThan(unshadowed - 30,
                $"the ground under the caster read {shadowed} with shadows on and {unshadowed} with them " +
                "off; a caster directly overhead has to darken it");

            // Control: a map that shadows everything would pass the check above.
            int nearOn = farR + farG + farB;
            int nearOff = noneFarR + noneFarG + noneFarB;
            Math.Abs(nearOn - nearOff).ShouldBeLessThan(12,
                $"ground outside the caster's footprint read {nearOn} with shadows on and {nearOff} with " +
                "them off; the shadow is not localised to the caster");
        }
        finally
        {
            renderer.ShadowsEnabled = restoreShadows;
        }
    }

    [Fact]
    public void A_lit_surface_with_nothing_over_it_is_not_shadowed_by_itself()
    {
        // Shadow acne. Grazing light is the worst case; this fails with
        // neither ShadowMap.RasterBias nor ShadowMap.NormalOffset.
        bool restore = _fixture.Renderer.ShadowsEnabled;
        try
        {
            _fixture.Renderer.ShadowsEnabled = true;
            (int onR, int onG, int onB) = RenderDeferred(BuildGrazingGroundScene());

            _fixture.Renderer.ShadowsEnabled = false;
            (int offR, int offG, int offB) = RenderDeferred(BuildGrazingGroundScene());

            int on = onR + onG + onB;
            int off = offR + offG + offB;
            (off - on).ShouldBeLessThan(12,
                $"open ground read {on} with shadows on and {off} with them off; nothing casts onto " +
                "this pixel, so any difference is the surface shadowing itself");
        }
        finally
        {
            _fixture.Renderer.ShadowsEnabled = restore;
        }
    }

    // Ground plane and a grazing sun, no caster.
    private Scene BuildGrazingGroundScene()
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        var scene = new Scene("grazing");
        scene.Camera.Position = new Vector3(0f, 1.2f, 6f);
        scene.Camera.LookAt(new Vector3(0f, 0f, 0f));

        var (vertices, indices) = Primitives.Cube();
        Mesh cube = renderer.CreateMesh(vertices, indices, VertexAttribute.StandardLayout);
        Texture white = renderer.CreateTexture(
            [255, 255, 255, 255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);

        var ground = scene.Root.CreateChild("Ground");
        ground.LocalTransform = new Transform
        {
            Position = new Vector3(0f, -0.5f, 0f),
            Rotation = Quaternion.Identity,
            Scale = new Vector3(60f, 1f, 60f),
        };
        ground.MeshRenderer = new MeshRenderer(cube, new Material(renderer.DefaultShader)
            .SetVector3("uBaseColor", new Vector3(0.8f, 0.8f, 0.8f))
            .SetFloat("uRoughness", 0.9f)
            .SetFloat("uMetallic", 0f)
            .SetFloat("uAmbientOcclusion", 1f)
            .SetVector3("uEmissive", Vector3.Zero)
            .SetFloat("uShadingModel", 0f)
            .SetTexture("uDiffuse", 0, white));

        var sun = scene.Root.CreateChild("Sun");
        sun.LocalRotation = Light.RotationForDirection(new Vector3(-0.97f, -0.24f, 0f));
        sun.Light = new Light
        {
            Kind = LightKind.Directional,
            Color = new Vector3(1f, 1f, 1f),
            Intensity = 12f,
        };

        return scene;
    }

    // Ground with a plate over the middle, sun straight above. The camera
    // looks along the ground so the plate never hides the pixel it shadows.
    private Scene BuildShadowScene()
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        var scene = new Scene("shadowed");
        scene.Camera.Position = new Vector3(0f, 1f, 8f);
        scene.Camera.LookAt(new Vector3(0f, 0.2f, 0f));

        var (vertices, indices) = Primitives.Cube();
        Mesh cube = renderer.CreateMesh(vertices, indices, VertexAttribute.StandardLayout);

        Texture white = renderer.CreateTexture(
            [255, 255, 255, 255], 1, 1, TextureFormat.Rgba8, TextureColorSpace.Linear,
            TextureFilter.Nearest, TextureWrap.Clamp);

        Material Surface()
        {
            var material = new Material(renderer.DefaultShader);
            material
                .SetVector3("uBaseColor", new Vector3(0.8f, 0.8f, 0.8f))
                .SetFloat("uRoughness", 0.9f)
                .SetFloat("uMetallic", 0f)
                .SetFloat("uAmbientOcclusion", 1f)
                .SetVector3("uEmissive", Vector3.Zero)
                .SetFloat("uShadingModel", 0f)
                .SetTexture("uDiffuse", 0, white);
            return material;
        }

        var ground = scene.Root.CreateChild("Ground");
        ground.LocalTransform = new Transform
        {
            Position = new Vector3(0f, -0.5f, 0f),
            Rotation = Quaternion.Identity,
            Scale = new Vector3(40f, 1f, 40f),
        };
        ground.MeshRenderer = new MeshRenderer(cube, Surface());

        var plate = scene.Root.CreateChild("Plate");
        plate.LocalTransform = new Transform
        {
            Position = new Vector3(0f, 3f, 0f),
            Rotation = Quaternion.Identity,
            Scale = new Vector3(6f, 0.4f, 6f),
        };
        plate.MeshRenderer = new MeshRenderer(cube, Surface());

        var sun = scene.Root.CreateChild("Sun");
        sun.LocalRotation = Light.RotationForDirection(-Vector3.UnitY);
        sun.Light = new Light
        {
            Kind = LightKind.Directional,
            Color = new Vector3(1f, 1f, 1f),
            Intensity = 3f,
        };

        return scene;
    }

    // Wall square-on to the camera with one point light, so N, L and V agree.
    private Scene BuildSurfaceScene(float lightRange, float metallic = 0f, float intensity = 40f)
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        var scene = new Scene("surface");
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
            .SetFloat("uRoughness", 0.5f)
            .SetFloat("uMetallic", metallic)
            .SetFloat("uAmbientOcclusion", 1f)
            .SetVector3("uEmissive", Vector3.Zero)
            .SetFloat("uShadingModel", 0f)
            .SetTexture("uDiffuse", 0, white);

        var wall = scene.Root.CreateChild("Wall");
        wall.LocalTransform = new Transform
        {
            Position = new Vector3(0f, 0f, 0f),
            Rotation = Quaternion.Identity,
            Scale = new Vector3(8f, 8f, 0.5f),
        };
        wall.MeshRenderer = new MeshRenderer(mesh, material);

        // One unit in front of the wall's +z face at z = 0.25.
        var lamp = scene.Root.CreateChild("Lamp");
        lamp.LocalPosition = new Vector3(0f, 0f, 1.25f);
        lamp.Light = new Light
        {
            Kind = LightKind.Point,
            Color = new Vector3(1f, 1f, 1f),
            Intensity = intensity,
            Range = lightRange,
        };

        return scene;
    }

    private (int R, int G, int B) RenderDeferred(Scene scene) =>
        RenderDeferred(scene, ProbeSize / 2, ProbeSize / 2);

    private (int R, int G, int B) RenderDeferred(Scene scene, int x, int y)
    {
        OpenGLRenderer renderer = _fixture.Renderer;

        string restore = renderer.CurrentPipelineName;
        RenderTarget probe = renderer.CreateRenderTarget(new RenderTargetDesc(ProbeSize, ProbeSize));
        var view = new RenderView();

        try
        {
            renderer.TrySelectPipeline("Deferred").ShouldBeTrue();
            renderer.ProbeTarget = probe;

            scene.BuildRenderView(scene.Camera, view);
            renderer.Render(scene, view, 1.0 / 60.0);

            return ReadPixel(probe, x, y);
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
            TextureTarget.Texture2D, ((OpenGLTexture)target.ColorTexture).Handle, 0);

        var pixel = new byte[4];
        fixed (byte* p = pixel)
            gl.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.DeleteFramebuffer(fbo);
        return (pixel[0], pixel[1], pixel[2]);
    }
}
