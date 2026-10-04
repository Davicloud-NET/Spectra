using SpectraEngine.Core.Bsp;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>A face's texture frame edited as an alignment and an angle.</summary>
public sealed class FaceAxesTests
{
    private static readonly Vector3 Up = Vector3.UnitY;

    [Fact]
    public void A_world_aligned_face_reads_zero_rotation()
    {
        FaceSurface.Default.IsWorldAligned.ShouldBeTrue();
        FaceAxes.RotationDegrees(FaceSurface.Default, Up).ShouldBe(0f);
        FaceAxes.AlignmentLabel(FaceSurface.Default).ShouldBe("World");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(15f)]
    [InlineData(45f)]
    [InlineData(90f)]
    [InlineData(-30f)]
    [InlineData(179f)]
    public void An_angle_written_reads_back_as_itself(float degrees)
    {
        FaceSurface turned = FaceAxes.WithRotation(FaceSurface.Default, Up, degrees);

        FaceAxes.RotationDegrees(turned, Up).ShouldBe(degrees, 1e-3f);

        // Writing an angle gives the face explicit axes (texture lock).
        turned.IsWorldAligned.ShouldBeFalse();
    }

    [Fact]
    public void Turning_and_turning_back_restores_the_axes()
    {
        FaceSurface start = FaceAxes.WithRotation(FaceSurface.Default, Up, 30f);
        FaceSurface there = FaceAxes.WithRotation(start, Up, 75f);
        FaceSurface back = FaceAxes.WithRotation(there, Up, 30f);

        // Each write is computed from the base frame, so nothing accumulates.
        Vector3.Distance(back.UAxis, start.UAxis).ShouldBeLessThan(1e-4f);
        Vector3.Distance(back.VAxis, start.VAxis).ShouldBeLessThan(1e-4f);
    }

    [Fact]
    public void A_rotation_leaves_the_scales_and_offsets_alone()
    {
        FaceSurface scaled = FaceSurface.Default.WithAxes(
            Vector3.Zero, Vector3.Zero, 0.25f, -1.5f, 2f, 3f);

        FaceSurface turned = FaceAxes.WithRotation(scaled, Up, 40f);

        turned.UScale.ShouldBe(2f);
        turned.VScale.ShouldBe(3f);
        turned.UOffset.ShouldBe(0.25f);
        turned.VOffset.ShouldBe(-1.5f);
    }

    [Fact]
    public void Face_alignment_puts_both_axes_in_the_face_plane()
    {
        var normal = Vector3.Normalize(new Vector3(0.3f, 0.8f, -0.5f));

        FaceSurface aligned = FaceAxes.AlignedToFace(FaceSurface.Default, normal);

        FaceAxes.IsFaceAligned(aligned).ShouldBeTrue();
        FaceAxes.AlignmentLabel(aligned).ShouldBe("Face");

        Vector3.Dot(Vector3.Normalize(aligned.UAxis), normal).ShouldBe(0f, 1e-4f);
        Vector3.Dot(Vector3.Normalize(aligned.VAxis), normal).ShouldBe(0f, 1e-4f);
    }

    [Fact]
    public void World_alignment_zeroes_the_axes_and_keeps_the_scales()
    {
        FaceSurface aligned = FaceAxes.AlignedToFace(
            FaceSurface.Default.WithAxes(Vector3.Zero, Vector3.Zero, 1f, 2f, 4f, 8f), Up);

        FaceSurface back = FaceAxes.AlignedToWorld(aligned);

        back.IsWorldAligned.ShouldBeTrue();
        back.UScale.ShouldBe(4f);
        back.VScale.ShouldBe(8f);
        back.UOffset.ShouldBe(1f);
        back.VOffset.ShouldBe(2f);
    }

    [Fact]
    public void An_angle_on_a_face_aligned_surface_is_measured_from_its_own_frame()
    {
        var normal = Vector3.Normalize(new Vector3(1f, 1f, 0f));

        FaceSurface aligned = FaceAxes.AlignedToFace(FaceSurface.Default, normal);

        // The angle is measured against the in-plane base frame, so rest is 0.
        FaceAxes.RotationDegrees(aligned, normal).ShouldBe(0f, 1e-3f);

        FaceSurface turned = FaceAxes.WithRotation(aligned, normal, 25f);
        FaceAxes.RotationDegrees(turned, normal).ShouldBe(25f, 1e-3f);
        FaceAxes.IsFaceAligned(turned).ShouldBeTrue();
    }

    [Fact]
    public void An_edit_on_a_placed_brush_round_trips_through_the_world_matrix()
    {
        // The panel edits in world space; the file stores brush-local axes.
        Matrix4x4 world =
            Matrix4x4.CreateFromYawPitchRoll(0.7f, -0.4f, 1.1f) *
            Matrix4x4.CreateTranslation(new Vector3(12f, -3f, 40f));

        Matrix4x4.Invert(world, out Matrix4x4 inverse).ShouldBeTrue();

        FaceSurface local = FaceAxes.WithRotation(FaceSurface.Default, Up, 20f);

        FaceSurface there = local.Transformed(world);
        FaceSurface back = there.Transformed(inverse);

        Vector3.Distance(back.UAxis, local.UAxis).ShouldBeLessThan(1e-3f);
        Vector3.Distance(back.VAxis, local.VAxis).ShouldBeLessThan(1e-3f);
        back.UOffset.ShouldBe(local.UOffset, 1e-3f);
        back.VOffset.ShouldBe(local.VOffset, 1e-3f);
    }

    [Fact]
    public void A_plane_normal_reaches_world_space_through_the_rotation_only()
    {
        var plane = new Plane(Vector3.UnitX, -1f);
        Matrix4x4 world =
            Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, float.Pi * 0.5f) *
            Matrix4x4.CreateTranslation(new Vector3(400f, 0f, 0f));

        Vector3 normal = FaceAxes.WorldNormal(in plane, in world);

        normal.Length().ShouldBe(1f, 1e-4f);
        Vector3.Distance(normal, -Vector3.UnitZ).ShouldBeLessThan(1e-4f);
    }
}
