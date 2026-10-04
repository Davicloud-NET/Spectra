using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

public sealed class CameraOrthographicTests
{
    private static Camera Ortho(float height = 10f)
    {
        var camera = new Camera
        {
            ProjectionKind = CameraProjectionKind.Orthographic,
            OrthographicHeight = height,
            AspectRatio = 16f / 9f,
            Position = new Vector3(0f, 20f, 0f),
        };

        camera.SetVerticalView(lookingDown: true, yaw: -float.Pi * 0.5f);
        return camera;
    }

    [Fact]
    public void Every_screen_ray_is_parallel_to_forward()
    {
        Camera camera = Ortho();
        var viewport = new Vector2(1280f, 720f);

        Ray3 middle = camera.ScreenPointToRay(new Vector2(640f, 360f), viewport);
        Ray3 corner = camera.ScreenPointToRay(new Vector2(4f, 4f), viewport);

        Vector3.Dot(middle.Direction, corner.Direction).ShouldBe(1f, 1e-4f);
        Vector3.Dot(middle.Direction, camera.Forward).ShouldBe(1f, 1e-4f);
    }

    [Fact]
    public void The_height_maps_the_viewport_to_world_units()
    {
        Camera camera = Ortho(height: 40f);
        var viewport = new Vector2(1280f, 720f);

        Ray3 top = camera.ScreenPointToRay(new Vector2(640f, 0f), viewport);
        Ray3 bottom = camera.ScreenPointToRay(new Vector2(640f, 720f), viewport);

        // Looking down, so the screen's vertical is world Z.
        float span = Vector3.Distance(
            new Vector3(top.Origin.X, 0f, top.Origin.Z),
            new Vector3(bottom.Origin.X, 0f, bottom.Origin.Z));

        span.ShouldBe(40f, 1e-2f);
    }

    [Fact]
    public void The_width_follows_the_aspect_ratio()
    {
        Camera camera = Ortho(height: 40f);
        var viewport = new Vector2(1280f, 720f);

        Ray3 left = camera.ScreenPointToRay(new Vector2(0f, 360f), viewport);
        Ray3 right = camera.ScreenPointToRay(new Vector2(1280f, 360f), viewport);

        float span = Vector3.Distance(left.Origin, right.Origin);
        span.ShouldBe(40f * (16f / 9f), 1e-2f);
    }

    [Fact]
    public void A_zero_or_negative_height_is_declined_rather_than_thrown()
    {
        Camera camera = Ortho(height: 12f);

        // Set from the render thread every frame; a throw would end the session.
        camera.OrthographicHeight = 0f;
        camera.OrthographicHeight = -5f;
        camera.OrthographicHeight = float.NaN;

        camera.OrthographicHeight.ShouldBe(12f);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_vertical_view_keeps_an_orthonormal_basis(bool lookingDown)
    {
        var camera = new Camera();
        camera.SetVerticalView(lookingDown, yaw: -float.Pi * 0.5f);

        // Forward cross world up is zero at the poles, which a top view needs.
        Finite(camera.Forward);
        Finite(camera.Right);
        Finite(camera.Up);

        camera.Forward.Length().ShouldBe(1f, 1e-4f);
        camera.Right.Length().ShouldBe(1f, 1e-4f);
        camera.Up.Length().ShouldBe(1f, 1e-4f);

        Vector3.Dot(camera.Forward, camera.Right).ShouldBe(0f, 1e-4f);
        Vector3.Dot(camera.Right, camera.Up).ShouldBe(0f, 1e-4f);

        camera.Forward.Y.ShouldBe(lookingDown ? -1f : 1f, 1e-4f);
    }

    [Fact]
    public void A_top_view_puts_x_to_the_right_and_minus_z_up_the_screen()
    {
        var camera = new Camera();
        camera.SetVerticalView(lookingDown: true, yaw: -float.Pi * 0.5f);

        // A transposed axis draws the level mirrored.
        Vector3.Dot(camera.Right, Vector3.UnitX).ShouldBe(1f, 1e-3f);
        Vector3.Dot(camera.Up, -Vector3.UnitZ).ShouldBe(1f, 1e-3f);
    }

    [Fact]
    public void The_slab_is_symmetric_about_the_eye()
    {
        Camera camera = Ortho();
        camera.Position = new Vector3(0f, 20f, 0f);

        Frustum frustum = camera.GetFrustum();

        // The eye sits at the focus, so geometry behind it has to render.
        frustum.Contains(new Vector3(0f, 60f, 0f)).ShouldBeTrue();
        frustum.Contains(new Vector3(0f, -20f, 0f)).ShouldBeTrue();
    }

    [Fact]
    public void A_point_outside_the_slab_sideways_is_still_culled()
    {
        Camera camera = Ortho(height: 10f);

        // Still culled sideways.
        camera.GetFrustum().Contains(new Vector3(0f, 20f, 400f)).ShouldBeFalse();
    }

    [Fact]
    public void Switching_projection_rebuilds_the_matrix()
    {
        var camera = new Camera { AspectRatio = 1f };
        Matrix4x4 perspective = camera.Projection;

        camera.ProjectionKind = CameraProjectionKind.Orthographic;
        camera.Projection.ShouldNotBe(perspective);

        camera.ProjectionKind = CameraProjectionKind.Perspective;
        camera.Projection.ShouldBe(perspective);
    }

    private static void Finite(Vector3 value)
    {
        float.IsFinite(value.X).ShouldBeTrue();
        float.IsFinite(value.Y).ShouldBeTrue();
        float.IsFinite(value.Z).ShouldBeTrue();
    }
}
