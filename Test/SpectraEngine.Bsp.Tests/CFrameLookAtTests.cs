using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Frames built from directions: lookAt, lookAlong and fromMatrix. Roblox's
/// rule is right = look cross up, up = right cross look, Z = minus look.
/// </summary>
public sealed class CFrameLookAtTests
{
    [Fact]
    public void Looking_along_minus_z_is_the_identity_rotation()
    {
        CFrame frame = CFrame.LookAt(Vector3.Zero, new Vector3(0f, 0f, -5f));

        frame.ShouldHaveComponents(0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1);
    }

    [Fact]
    public void Looking_along_plus_z_is_a_half_turn_about_y()
    {
        CFrame.LookAt(Vector3.Zero, new Vector3(0f, 0f, 5f)).ShouldHaveRotation(
            -1, 0, 0,
            0, 1, 0,
            0, 0, -1);
    }

    [Fact]
    public void Looking_along_plus_x_puts_the_right_vector_on_plus_z()
    {
        CFrame frame = CFrame.LookAt(Vector3.Zero, new Vector3(5f, 0f, 0f));

        frame.ShouldHaveRotation(
            0, 0, -1,
            0, 1, 0,
            1, 0, 0);
        frame.RightVector.ShouldBeNear(new Vector3(0f, 0f, 1f));
    }

    [Fact]
    public void Looking_along_minus_x_puts_the_right_vector_on_minus_z()
    {
        CFrame frame = CFrame.LookAt(Vector3.Zero, new Vector3(-5f, 0f, 0f));

        frame.ShouldHaveRotation(
            0, 0, 1,
            0, 1, 0,
            -1, 0, 0);
        frame.RightVector.ShouldBeNear(new Vector3(0f, 0f, -1f));
    }

    [Fact]
    public void The_frame_sits_at_the_first_point_and_looks_at_the_second()
    {
        var at = new Vector3(1f, 2f, 3f);
        var target = new Vector3(4f, 2f, -1f);

        CFrame frame = CFrame.LookAt(at, target);

        // Look (0.6, 0, -0.8), right (0.8, 0, 0.6), up (0, 1, 0).
        frame.ShouldHaveComponents(
            1, 2, 3,
            0.8f, 0, -0.6f,
            0, 1, 0,
            0.6f, 0, 0.8f);
        frame.LookVector.ShouldBeNear(new Vector3(0.6f, 0f, -0.8f));
    }

    [Fact]
    public void Looking_upwards_at_a_slant_pitches_about_x()
    {
        // Look (0, 0.6, -0.8), right (1, 0, 0), up (0, 0.8, 0.6).
        CFrame.LookAt(Vector3.Zero, new Vector3(0f, 3f, -4f)).ShouldHaveRotation(
            1, 0, 0,
            0, 0.8f, -0.6f,
            0, 0.6f, 0.8f);
    }

    [Fact]
    public void A_slanted_look_builds_right_from_look_cross_up_and_up_from_right_cross_look()
    {
        // Look (2, 1, -2) / 3. Right (1, 0, 1) / sqrt 2. Up (-1, 4, 1) / sqrt 18.
        float r = 1f / MathF.Sqrt(2f);
        float u = 1f / MathF.Sqrt(18f);

        CFrame.LookAt(new Vector3(1f, 1f, 1f), new Vector3(3f, 2f, -1f)).ShouldHaveComponents(
            1, 1, 1,
            r, -u, -2 / 3f,
            0, 4 * u, -1 / 3f,
            r, u, 2 / 3f);
    }

    [Fact]
    public void A_given_up_vector_rolls_the_frame()
    {
        // Look (0, 0, -1) with up along X: right (0, -1, 0), up (1, 0, 0).
        CFrame.LookAt(Vector3.Zero, new Vector3(0f, 0f, -1f), Vector3.UnitX).ShouldHaveRotation(
            0, 1, 0,
            -1, 0, 0,
            0, 0, 1);
    }

    [Fact]
    public void The_up_vector_does_not_need_unit_length()
    {
        CFrame.LookAt(Vector3.Zero, new Vector3(3f, 0f, -4f), new Vector3(0f, 9f, 0f))
            .ShouldBeNear(CFrame.LookAt(Vector3.Zero, new Vector3(3f, 0f, -4f)));
    }

    [Fact]
    public void LookAlong_is_LookAt_from_a_direction()
    {
        var at = new Vector3(1f, 2f, 3f);
        var direction = new Vector3(2f, 1f, -2f);

        CFrame.LookAlong(at, direction).ShouldBeNear(CFrame.LookAt(at, at + direction));
        CFrame.LookAlong(at, direction, Vector3.UnitX).ShouldBeNear(CFrame.LookAt(at, at + direction, Vector3.UnitX));
    }

    // Our choice. Roblox's documentation gives no answer for a look along up.
    // Ours is the identity pitched a quarter turn: right stays on world +X.
    [Fact]
    public void Looking_straight_up_keeps_the_right_vector_on_plus_x()
    {
        CFrame frame = CFrame.LookAt(new Vector3(1f, 2f, 3f), new Vector3(1f, 9f, 3f));

        frame.ShouldHaveComponents(
            1, 2, 3,
            1, 0, 0,
            0, 0, -1,
            0, 1, 0);
        frame.LookVector.ShouldBeNear(new Vector3(0f, 1f, 0f));
    }

    // Our choice, as above.
    [Fact]
    public void Looking_straight_down_keeps_the_right_vector_on_plus_x()
    {
        CFrame frame = CFrame.LookAt(Vector3.Zero, new Vector3(0f, -4f, 0f));

        frame.ShouldHaveRotation(
            1, 0, 0,
            0, 0, 1,
            0, -1, 0);
        frame.LookVector.ShouldBeNear(new Vector3(0f, -1f, 0f));
    }

    // Our choice: with a look along the given up, the frame is the one the
    // default up gives.
    [Fact]
    public void Looking_along_a_given_up_falls_back_to_the_default_up()
    {
        CFrame.LookAt(Vector3.Zero, new Vector3(5f, 0f, 0f), Vector3.UnitX)
            .ShouldBeNear(CFrame.LookAt(Vector3.Zero, new Vector3(5f, 0f, 0f)));
        CFrame.LookAt(Vector3.Zero, new Vector3(-5f, 0f, 0f), Vector3.UnitX)
            .ShouldBeNear(CFrame.LookAt(Vector3.Zero, new Vector3(-5f, 0f, 0f)));
    }

    // Our choice: with nowhere to look there is no rotation.
    [Fact]
    public void Looking_at_its_own_position_gives_no_rotation()
    {
        var at = new Vector3(1f, 2f, 3f);

        CFrame.LookAt(at, at).ShouldBe(new CFrame(at));
        CFrame.LookAlong(at, Vector3.Zero).ShouldBe(new CFrame(at));
    }

    [Theory]
    [InlineData(0f, 1f, 0f, 0f, 1f, 0f)]
    [InlineData(0f, -1f, 0f, 0f, 1f, 0f)]
    [InlineData(0f, 1f, 1e-7f, 0f, 1f, 0f)]
    [InlineData(1f, 0f, 0f, 1f, 0f, 0f)]
    [InlineData(1f, 2f, 3f, 0f, 0f, 0f)]
    [InlineData(0f, 0f, 0f, 0f, 0f, 0f)]
    [InlineData(1e-30f, 0f, 0f, 0f, 1f, 0f)]
    [InlineData(0f, 1e20f, 0f, 0f, 1e20f, 0f)]
    public void Any_direction_and_up_give_a_rotation_and_never_a_NaN(
        float dx, float dy, float dz, float ux, float uy, float uz)
    {
        CFrame frame = CFrame.LookAlong(Vector3.Zero, new Vector3(dx, dy, dz), new Vector3(ux, uy, uz));

        foreach (float component in frame.Components())
            float.IsFinite(component).ShouldBeTrue();
        frame.Rotation.Length().ShouldBe(1f, 1e-5f);
        Vector3.Cross(frame.XVector, frame.YVector).ShouldBeNear(frame.ZVector);
    }

    [Fact]
    public void FromMatrix_takes_the_three_columns()
    {
        CFrame frame = CFrame.FromMatrix(
            new Vector3(1f, 2f, 3f),
            new Vector3(0f, 0f, -1f),
            new Vector3(0f, 1f, 0f),
            new Vector3(1f, 0f, 0f));

        frame.ShouldHaveComponents(
            1, 2, 3,
            0, 0, 1,
            0, 1, 0,
            -1, 0, 0);
        frame.XVector.ShouldBeNear(new Vector3(0f, 0f, -1f));
        frame.ZVector.ShouldBeNear(new Vector3(1f, 0f, 0f));
    }

    [Fact]
    public void FromMatrix_without_the_third_column_takes_x_cross_y()
    {
        CFrame frame = CFrame.FromMatrix(Vector3.Zero, new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f));

        frame.ZVector.ShouldBeNear(new Vector3(1f, 0f, 0f));
        frame.ShouldBeNear(CFrame.Angles(0f, MathF.PI / 2f, 0f));
    }

    [Fact]
    public void FromMatrix_builds_the_frame_LookAt_builds_from_the_same_vectors()
    {
        CFrame looking = CFrame.LookAt(new Vector3(1f, 1f, 1f), new Vector3(3f, 2f, -1f));

        CFrame rebuilt = CFrame.FromMatrix(looking.Position, looking.XVector, looking.YVector, looking.ZVector);

        rebuilt.ShouldBeNear(looking);
    }

    // Our choice. A Roblox CFrame keeps columns that are scaled or slanted.
    // Ours cannot: x keeps its direction and y is squared up against it.
    [Fact]
    public void FromMatrix_squares_up_columns_that_are_not_a_rotation()
    {
        CFrame frame = CFrame.FromMatrix(
            Vector3.Zero, new Vector3(4f, 0f, 0f), new Vector3(3f, 2f, 0f), new Vector3(0f, 0f, 7f));

        frame.ShouldHaveRotation(1, 0, 0, 0, 1, 0, 0, 0, 1);
    }

    // Our choice: columns that span no frame give no rotation.
    [Fact]
    public void FromMatrix_with_parallel_columns_gives_no_rotation()
    {
        CFrame.FromMatrix(new Vector3(1f, 2f, 3f), Vector3.UnitX, Vector3.UnitX * 2f)
            .ShouldBe(new CFrame(1f, 2f, 3f));
    }
}
