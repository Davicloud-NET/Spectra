using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// How a CFrame is turned: the axes, the angle constructors and their order.
/// Every expected matrix is worked by hand from Roblox's rules.
/// </summary>
public sealed class CFrameRotationTests
{
    private const float Quarter = MathF.PI / 2f;
    private const float Half = MathF.PI;

    [Fact]
    public void The_identity_looks_along_minus_z_with_x_right_and_y_up()
    {
        CFrame identity = CFrame.Identity;

        identity.LookVector.ShouldBeNear(new Vector3(0f, 0f, -1f));
        identity.RightVector.ShouldBeNear(new Vector3(1f, 0f, 0f));
        identity.UpVector.ShouldBeNear(new Vector3(0f, 1f, 0f));
        identity.ShouldHaveComponents(0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1);
    }

    [Fact]
    public void A_quarter_turn_about_x_tips_the_look_vector_up()
    {
        CFrame frame = CFrame.Angles(Quarter, 0f, 0f);

        frame.ShouldHaveRotation(
            1, 0, 0,
            0, 0, -1,
            0, 1, 0);
        frame.LookVector.ShouldBeNear(new Vector3(0f, 1f, 0f));
    }

    [Fact]
    public void A_quarter_turn_about_y_swings_the_look_vector_to_minus_x()
    {
        CFrame frame = CFrame.Angles(0f, Quarter, 0f);

        frame.ShouldHaveRotation(
            0, 0, 1,
            0, 1, 0,
            -1, 0, 0);
        frame.LookVector.ShouldBeNear(new Vector3(-1f, 0f, 0f));
    }

    [Fact]
    public void A_quarter_turn_about_z_rolls_the_right_vector_up()
    {
        CFrame frame = CFrame.Angles(0f, 0f, Quarter);

        frame.ShouldHaveRotation(
            0, -1, 0,
            1, 0, 0,
            0, 0, 1);
        frame.RightVector.ShouldBeNear(new Vector3(0f, 1f, 0f));
        frame.LookVector.ShouldBeNear(new Vector3(0f, 0f, -1f));
    }

    [Fact]
    public void A_half_turn_about_an_axis_flips_the_other_two()
    {
        CFrame.Angles(Half, 0f, 0f).ShouldHaveRotation(1, 0, 0, 0, -1, 0, 0, 0, -1);
        CFrame.Angles(0f, Half, 0f).ShouldHaveRotation(-1, 0, 0, 0, 1, 0, 0, 0, -1);
        CFrame.Angles(0f, 0f, Half).ShouldHaveRotation(-1, 0, 0, 0, -1, 0, 0, 0, 1);
    }

    [Fact]
    public void Angles_with_x_and_y_is_the_x_turn_times_the_y_turn()
    {
        // Rx(90) * Ry(90). The other order gives the matrix of the YXZ test.
        CFrame.Angles(Quarter, Quarter, 0f).ShouldHaveRotation(
            0, 0, 1,
            1, 0, 0,
            0, 1, 0);
    }

    [Fact]
    public void Angles_with_x_and_z_is_the_x_turn_times_the_z_turn()
    {
        // Rx(90) * Rz(90). Rz(90) * Rx(90) would be 0,0,1, 1,0,0, 0,1,0.
        CFrame.Angles(Quarter, 0f, Quarter).ShouldHaveRotation(
            0, -1, 0,
            0, 0, -1,
            1, 0, 0);
    }

    [Fact]
    public void Angles_with_all_three_applies_z_first_then_y_then_x()
    {
        // Rx(90) * Ry(90) * Rz(90).
        CFrame.Angles(Quarter, Quarter, Quarter).ShouldHaveRotation(
            0, 0, 1,
            0, -1, 0,
            1, 0, 0);
    }

    [Fact]
    public void FromEulerAnglesXYZ_is_Angles()
    {
        CFrame.FromEulerAnglesXYZ(0.3f, -0.7f, 1.1f).ShouldBe(CFrame.Angles(0.3f, -0.7f, 1.1f));
    }

    [Fact]
    public void FromEulerAnglesYXZ_with_x_and_y_is_the_y_turn_times_the_x_turn()
    {
        // Ry(90) * Rx(90).
        CFrame.FromEulerAnglesYXZ(Quarter, Quarter, 0f).ShouldHaveRotation(
            0, 1, 0,
            0, 0, -1,
            -1, 0, 0);
    }

    [Fact]
    public void FromEulerAnglesYXZ_with_all_three_applies_z_first_then_x_then_y()
    {
        // Ry(90) * Rx(90) * Rz(90), which comes out as Rx(90).
        CFrame.FromEulerAnglesYXZ(Quarter, Quarter, Quarter).ShouldHaveRotation(
            1, 0, 0,
            0, 0, -1,
            0, 1, 0);
    }

    [Fact]
    public void FromOrientation_is_FromEulerAnglesYXZ()
    {
        CFrame.FromOrientation(0.3f, -0.7f, 1.1f).ShouldBe(CFrame.FromEulerAnglesYXZ(0.3f, -0.7f, 1.1f));
    }

    [Fact]
    public void FromAxisAngle_turns_the_right_handed_way_about_the_axis()
    {
        CFrame.FromAxisAngle(Vector3.UnitY, Quarter).ShouldBeNear(CFrame.Angles(0f, Quarter, 0f));
        CFrame.FromAxisAngle(Vector3.UnitX, Quarter).ShouldBeNear(CFrame.Angles(Quarter, 0f, 0f));
        CFrame.FromAxisAngle(Vector3.UnitZ, Quarter).ShouldBeNear(CFrame.Angles(0f, 0f, Quarter));
    }

    [Fact]
    public void FromAxisAngle_about_the_diagonal_by_a_third_of_a_turn_cycles_the_axes()
    {
        CFrame frame = CFrame.FromAxisAngle(new Vector3(1f, 1f, 1f), 2f * MathF.PI / 3f);

        // X goes to Y, Y to Z and Z to X.
        frame.ShouldHaveRotation(
            0, 0, 1,
            1, 0, 0,
            0, 1, 0);
    }

    [Fact]
    public void FromAxisAngle_does_not_need_a_unit_axis()
    {
        CFrame.FromAxisAngle(new Vector3(0f, 7f, 0f), Quarter).ShouldBeNear(CFrame.Angles(0f, Quarter, 0f));
    }

    // Our choice: Roblox's documentation does not say what an axis of no length gives.
    [Fact]
    public void FromAxisAngle_about_no_axis_is_the_identity()
    {
        CFrame.FromAxisAngle(Vector3.Zero, 1f).ShouldBe(CFrame.Identity);
    }

    [Fact]
    public void The_axis_vectors_are_the_columns_of_the_rotation_matrix()
    {
        CFrame frame = CFrame.Angles(Quarter, 0f, Quarter);

        // Rows 0,-1,0 / 0,0,-1 / 1,0,0.
        frame.XVector.ShouldBeNear(new Vector3(0f, 0f, 1f));
        frame.YVector.ShouldBeNear(new Vector3(-1f, 0f, 0f));
        frame.ZVector.ShouldBeNear(new Vector3(0f, -1f, 0f));
        frame.RightVector.ShouldBe(frame.XVector);
        frame.UpVector.ShouldBe(frame.YVector);
        frame.LookVector.ShouldBe(-frame.ZVector);
    }

    [Fact]
    public void GetComponents_lists_the_position_then_the_matrix_row_by_row()
    {
        // Columns of a rotation whose nine entries tell rows from columns.
        CFrame frame = CFrame.FromMatrix(
            new Vector3(1f, 2f, 3f),
            new Vector3(2f, 2f, -1f) / 3f,
            new Vector3(-1f, 2f, 2f) / 3f,
            new Vector3(2f, -1f, 2f) / 3f);

        frame.ShouldHaveComponents(
            1, 2, 3,
            2 / 3f, -1 / 3f, 2 / 3f,
            2 / 3f, 2 / 3f, -1 / 3f,
            -1 / 3f, 2 / 3f, 2 / 3f);
    }

    [Fact]
    public void GetComponents_refuses_a_span_that_is_too_short()
    {
        Should.Throw<ArgumentException>(() => CFrame.Identity.GetComponents(new float[11]));
    }
}
