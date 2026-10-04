using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using static SpectraEngine.Bsp.Tests.SpatialTestHelpers;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The CPU side of a shadow map: where the light's box goes, and how a world point
/// becomes a lookup into it.
/// </summary>
public sealed class ShadowMapTests
{
    private const int Resolution = 1024;
    private const float Distance = 40f;
    private const float Near = 0.1f;

    private static Camera MakeCamera(Vector3 position, Vector3 lookAt)
    {
        var camera = new Camera { Position = position, AspectRatio = 16f / 9f };
        camera.LookAt(lookAt);
        return camera;
    }

    [Fact]
    public void The_light_box_moves_only_in_whole_texels_as_the_camera_walks()
    {
        // Without the snap the box slides with the camera and every shadow edge
        // shimmers. Measured on a fixed world point, since where the world lands
        // in the map is what shimmers.
        var probe = new Vector3(1.5f, 0.25f, -2f);
        Vector3 lightDirection = Vector3.Normalize(new Vector3(0.2f, -1f, 0.15f));

        Camera baseline = MakeCamera(new Vector3(0f, 2f, 10f), new Vector3(0f, 0f, 9f));
        ShadowMap.TryFitLightMatrix(baseline, lightDirection, Near, Distance, Resolution, out Matrix4x4 first, out float texel)
            .ShouldBeTrue();
        float reference = TexelX(probe, first);

        // One texel of camera travel in twelve steps: some stay inside a texel,
        // at least one crosses a boundary.
        for (int step = 1; step <= 12; step++)
        {
            float offset = texel * step / 12f;
            Camera moved = MakeCamera(new Vector3(offset, 2f, 10f), new Vector3(offset, 0f, 9f));
            ShadowMap.TryFitLightMatrix(moved, lightDirection, Near, Distance, Resolution, out Matrix4x4 fit, out _)
                .ShouldBeTrue();

            float shift = TexelX(probe, fit) - reference;
            MathF.Abs(shift - MathF.Round(shift)).ShouldBeLessThan(1e-2f,
                $"after a {offset:0.####} unit camera move the probe shifted {shift:0.####} texels, " +
                "which is not a whole number: the light box is sliding rather than snapping");
        }
    }

    [Fact]
    public void The_near_cascade_is_much_finer_than_the_far_one()
    {
        // A ratio near 1 means the splits have gone uniform and the near
        // shadows are no sharper than the far ones.
        Camera camera = MakeCamera(new Vector3(0f, 2f, 10f), Vector3.Zero);
        Vector3 direction = Vector3.Normalize(new Vector3(0.2f, -1f, 0.15f));

        Span<float> splits = stackalloc float[ShadowMap.MaxCascades];
        ShadowMap.ComputeSplits(Near, Distance, 4, 0.88f, splits);

        ShadowMap.TryFitLightMatrix(camera, direction, Near, splits[0], Resolution, out _, out float nearest)
            .ShouldBeTrue();
        ShadowMap.TryFitLightMatrix(camera, direction, splits[2], splits[3], Resolution, out _, out float coarsest)
            .ShouldBeTrue();

        (coarsest / nearest).ShouldBeGreaterThan(5f,
            $"the near cascade's texel is {nearest:0.0000} and the far one's {coarsest:0.0000}; " +
            "cascades that do not differ are one cascade with extra passes");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void The_splits_cover_the_whole_range_and_never_go_backwards(int count)
    {
        // A gap or inversion is a band of the world no cascade shadows.
        Span<float> splits = stackalloc float[ShadowMap.MaxCascades];
        ShadowMap.ComputeSplits(Near, Distance, count, 0.88f, splits);

        float previous = Near;
        for (int i = 0; i < count; i++)
        {
            splits[i].ShouldBeGreaterThan(previous);
            previous = splits[i];
        }

        // The last split is the shadow distance the caller set, not a rounded blend.
        splits[count - 1].ShouldBe(Distance, 1e-4f);
    }

    [Fact]
    public void Counted_from_further_out_the_first_cascade_reaches_further()
    {
        // Counted from the near plane, the first cascade is spent on the two
        // units in front of the lens.
        Span<float> fromNearPlane = stackalloc float[ShadowMap.MaxCascades];
        Span<float> fromStart = stackalloc float[ShadowMap.MaxCascades];
        ShadowMap.ComputeSplits(Near, 60f, 4, 0.88f, fromNearPlane);
        ShadowMap.ComputeSplits(2.5f, 60f, 4, 0.88f, fromStart);

        fromNearPlane[0].ShouldBeLessThan(3f);
        fromStart[0].ShouldBeGreaterThan(6f);
        fromStart[3].ShouldBe(60f, 1e-4f);
    }

    [Fact]
    public void A_caster_far_toward_the_light_is_kept_for_the_cascade_it_shades()
    {
        // A cascade's own box ends a short way above what it shades, so the
        // top of a tower is outside it. The box casters are culled against
        // reaches back toward the light.
        Camera camera = MakeCamera(new Vector3(0f, 2f, 10f), Vector3.Zero);
        ShadowMap.TryFitLightMatrix(
            camera, -Vector3.UnitY, Near, 6f, Resolution, casterReach: 500f,
            out Matrix4x4 own, out Matrix4x4 reaching, out _).ShouldBeTrue();

        Vector3 inView = camera.Position + (camera.Forward * 3f);

        var scene = new Scene("Test");
        SceneNode low = CreateMeshNode(scene.Root, "low", inView);
        SceneNode high = CreateMeshNode(scene.Root, "high", inView + new Vector3(0f, 300f, 0f));

        var view = new RenderView();
        scene.BuildShadowView(own, view);
        view.Items.Count.ShouldBe(1, "the cascade's own box does not reach the high caster");
        view.Items[0].Mesh.ShouldBeSameAs(low.MeshRenderer!.Mesh);

        scene.BuildShadowView(reaching, view);
        view.Items.Count.ShouldBe(2);
        view.Items.ShouldContain(item => ReferenceEquals(item.Mesh, high.MeshRenderer!.Mesh));
    }

    [Fact]
    public void The_caster_volume_is_no_wider_than_the_cascade()
    {
        // Reaching sideways too would draw casters that cannot shade it.
        Camera camera = MakeCamera(new Vector3(0f, 2f, 10f), Vector3.Zero);
        ShadowMap.TryFitLightMatrix(
            camera, -Vector3.UnitY, Near, 6f, Resolution, casterReach: 500f,
            out Matrix4x4 own, out Matrix4x4 reaching, out _).ShouldBeTrue();

        foreach (Vector3 point in new[] { new Vector3(3f, 0f, 4f), new Vector3(-40f, 1f, 9f), new Vector3(0f, 5f, -70f) })
        {
            Vector4 a = Vector4.Transform(point, own);
            Vector4 b = Vector4.Transform(point, reaching);
            b.X.ShouldBe(a.X, 1e-4f);
            b.Y.ShouldBe(a.Y, 1e-4f);
        }
    }

    private static float TexelX(Vector3 world, in Matrix4x4 lightViewProjection)
    {
        Vector4 clip = Vector4.Transform(world, lightViewProjection);
        return (clip.X / clip.W * 0.5f + 0.5f) * Resolution;
    }

    [Fact]
    public void Turning_the_camera_does_not_change_the_texel_size()
    {
        // Why the fit bounds the slice with a sphere: a box around the corners
        // changes size as the camera turns, and the texel snap needs a fixed grid.
        Camera facingZ = MakeCamera(new Vector3(0f, 2f, 0f), new Vector3(0f, 2f, -1f));
        Camera facingX = MakeCamera(new Vector3(0f, 2f, 0f), new Vector3(1f, 2f, 0f));
        Camera diagonal = MakeCamera(new Vector3(0f, 2f, 0f), new Vector3(1f, 2.6f, -1f));

        ShadowMap.TryFitLightMatrix(facingZ, -Vector3.UnitY, Near, Distance, Resolution, out _, out float a).ShouldBeTrue();
        ShadowMap.TryFitLightMatrix(facingX, -Vector3.UnitY, Near, Distance, Resolution, out _, out float b).ShouldBeTrue();
        ShadowMap.TryFitLightMatrix(diagonal, -Vector3.UnitY, Near, Distance, Resolution, out _, out float c).ShouldBeTrue();

        b.ShouldBe(a, 1e-5f);
        c.ShouldBe(a, 1e-5f);
    }

    [Fact]
    public void What_the_camera_can_see_lands_inside_the_light_box()
    {
        Camera camera = MakeCamera(new Vector3(0f, 3f, 12f), Vector3.Zero);
        ShadowMap.TryFitLightMatrix(
            camera, Vector3.Normalize(new Vector3(0.3f, -1f, 0.2f)), Near, Distance, Resolution,
            out Matrix4x4 lightViewProjection, out _).ShouldBeTrue();

        // Frustum corners at three depths, out to just short of the shadow distance.
        float tan = MathF.Tan(camera.FieldOfView * 0.5f);
        foreach (float depth in new[] { 1f, Distance * 0.5f, Distance * 0.98f })
        {
            float halfHeight = depth * tan;
            float halfWidth = halfHeight * camera.AspectRatio;
            Vector3 middle = camera.Position + camera.Forward * depth;

            foreach (int sx in new[] { -1, 1 })
            foreach (int sy in new[] { -1, 1 })
            {
                Vector3 corner = middle + camera.Right * (halfWidth * sx) + camera.Up * (halfHeight * sy);
                Vector4 clip = Vector4.Transform(corner, lightViewProjection);

                clip.X.ShouldBeInRange(-1.0001f, 1.0001f);
                clip.Y.ShouldBeInRange(-1.0001f, 1.0001f);
                clip.Z.ShouldBeInRange(-0.0001f, 1.0001f);
            }
        }
    }

    [Fact]
    public void A_light_with_no_direction_fits_nothing()
    {
        Camera camera = MakeCamera(new Vector3(0f, 2f, 5f), Vector3.Zero);
        ShadowMap.TryFitLightMatrix(camera, Vector3.Zero, Near, Distance, Resolution, out _, out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_light_pointing_straight_down_still_fits()
    {
        // Straight down is parallel to world up, so a naive basis is NaN.
        Camera camera = MakeCamera(new Vector3(0f, 2f, 5f), Vector3.Zero);
        ShadowMap.TryFitLightMatrix(camera, -Vector3.UnitY, Near, Distance, Resolution, out Matrix4x4 m, out _)
            .ShouldBeTrue();

        float sum = m.M11 + m.M22 + m.M33 + m.M44 + m.M41 + m.M42 + m.M43;
        float.IsNaN(sum).ShouldBeFalse();
    }

    public static TheoryData<bool, float, float> OriginConventions() => new()
    {
        // topLeftOrigin, ndc y, expected v
        { false, -1f, 0f },   // OpenGL: v = 0 is the bottom
        { false, 1f, 1f },
        { true, -1f, 1f },    // D3D: v = 1 is the bottom
        { true, 1f, 0f },
    };

    [Theory]
    [MemberData(nameof(OriginConventions))]
    public void The_lookup_flips_v_on_the_backends_whose_targets_start_at_the_top(
        bool topLeftOrigin, float ndcY, float expectedV)
    {
        // Row zero of a render target is the bottom on OpenGL and the top on D3D.
        // A wrong flip mirrors every shadow on one backend with no error.
        Matrix4x4 m = ShadowMap.NdcToShadowTexture(new Vector2(2f, -1f), topLeftOrigin);
        Vector4 mapped = Vector4.Transform(new Vector4(0f, ndcY, 0f, 1f), m);

        mapped.Y.ShouldBe(expectedV, 1e-5f);
    }

    [Theory]
    // OpenGL: clip z runs -1..1 and the buffer stores 0..1.
    [InlineData(2f, -1f, -1f, 0f)]
    [InlineData(2f, -1f, 1f, 1f)]
    // D3D: clip z already runs 0..1, so the lookup passes it through.
    [InlineData(1f, 0f, 0f, 0f)]
    [InlineData(1f, 0f, 1f, 1f)]
    public void The_lookup_undoes_exactly_what_the_depth_texel_conversion_does(
        float scale, float bias, float ndcZ, float expectedDepth)
    {
        // The z row is the inverse of Renderer.DepthToNdcZ.
        Matrix4x4 m = ShadowMap.NdcToShadowTexture(new Vector2(scale, bias), topLeftOrigin: true);
        Vector4 mapped = Vector4.Transform(new Vector4(0f, 0f, ndcZ, 1f), m);

        mapped.Z.ShouldBe(expectedDepth, 1e-5f);
    }
}
