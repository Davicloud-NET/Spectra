using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The product, the inverse and the space transforms, on cases worked by hand
/// from Roblox's rules. The two frames used throughout:
/// A sits at (1, 2, 3) turned a quarter about Y, which sends (x, y, z) to (z, y, -x).
/// B sits at (4, 5, 6) turned a quarter about X, which sends (x, y, z) to (x, -z, y).
/// </summary>
public sealed class CFrameProductTests
{
    private const float Quarter = MathF.PI / 2f;

    private static readonly CFrame A = new CFrame(1f, 2f, 3f) * CFrame.Angles(0f, Quarter, 0f);
    private static readonly CFrame B = new CFrame(4f, 5f, 6f) * CFrame.Angles(Quarter, 0f, 0f);

    [Fact]
    public void A_position_times_a_rotation_stays_at_the_position()
    {
        A.ShouldHaveComponents(
            1, 2, 3,
            0, 0, 1,
            0, 1, 0,
            -1, 0, 0);
    }

    [Fact]
    public void A_rotation_times_a_position_turns_the_position()
    {
        CFrame turned = CFrame.Angles(0f, Quarter, 0f) * new CFrame(1f, 2f, 3f);

        turned.ShouldHaveComponents(
            3, 2, -1,
            0, 0, 1,
            0, 1, 0,
            -1, 0, 0);
    }

    [Fact]
    public void A_times_B_places_B_in_the_space_of_A()
    {
        // Position (1,2,3) + Ry(90)(4,5,6) = (1,2,3) + (6,5,-4). Rotation Ry(90) Rx(90).
        (A * B).ShouldHaveComponents(
            7, 7, -1,
            0, 1, 0,
            0, 0, -1,
            -1, 0, 0);
    }

    [Fact]
    public void B_times_A_is_a_different_frame()
    {
        // Position (4,5,6) + Rx(90)(1,2,3) = (4,5,6) + (1,-3,2). Rotation Rx(90) Ry(90).
        (B * A).ShouldHaveComponents(
            5, 2, 8,
            0, 0, 1,
            1, 0, 0,
            0, 1, 0);
    }

    [Fact]
    public void A_frame_times_a_point_turns_the_point_then_moves_it()
    {
        (A * new Vector3(1f, 0f, 0f)).ShouldBeNear(new Vector3(1f, 2f, 2f));
        (A * new Vector3(0f, 0f, 1f)).ShouldBeNear(new Vector3(2f, 2f, 3f));
        (A * new Vector3(4f, 5f, 6f)).ShouldBeNear(new Vector3(7f, 7f, -1f));
    }

    [Fact]
    public void Adding_a_vector_moves_the_position_and_leaves_the_rotation()
    {
        (A + new Vector3(10f, 20f, 30f)).ShouldHaveComponents(
            11, 22, 33,
            0, 0, 1,
            0, 1, 0,
            -1, 0, 0);
    }

    [Fact]
    public void Subtracting_a_vector_moves_the_position_back()
    {
        (A - new Vector3(10f, 20f, 30f)).ShouldHaveComponents(
            -9, -18, -27,
            0, 0, 1,
            0, 1, 0,
            -1, 0, 0);
    }

    [Fact]
    public void The_inverse_is_the_rotation_transposed_and_the_position_turned_back_and_negated()
    {
        // Ry(-90) sends (x,y,z) to (-z,y,x), so (1,2,3) goes to (-3,2,1).
        A.Inverse().ShouldHaveComponents(
            3, -2, -1,
            0, 0, -1,
            0, 1, 0,
            1, 0, 0);
    }

    [Fact]
    public void ToWorldSpace_is_the_product()
    {
        A.ToWorldSpace(B).ShouldBe(A * B);
    }

    [Fact]
    public void ToObjectSpace_is_the_inverse_times_the_other_frame()
    {
        // Position Ry(-90)((4,5,6) - (1,2,3)) = (-3,3,3). Rotation Ry(-90) Rx(90).
        A.ToObjectSpace(B).ShouldHaveComponents(
            -3, 3, 3,
            0, -1, 0,
            0, 0, -1,
            1, 0, 0);
    }

    [Fact]
    public void PointToWorldSpace_is_the_frame_times_the_point()
    {
        A.PointToWorldSpace(new Vector3(1f, 0f, 0f)).ShouldBeNear(new Vector3(1f, 2f, 2f));
    }

    [Fact]
    public void PointToObjectSpace_undoes_the_move_then_the_turn()
    {
        A.PointToObjectSpace(new Vector3(1f, 2f, 2f)).ShouldBeNear(new Vector3(1f, 0f, 0f));
        A.PointToObjectSpace(Vector3.Zero).ShouldBeNear(new Vector3(3f, -2f, -1f));
    }

    [Fact]
    public void VectorToWorldSpace_turns_and_does_not_move()
    {
        A.VectorToWorldSpace(new Vector3(1f, 0f, 0f)).ShouldBeNear(new Vector3(0f, 0f, -1f));
        A.VectorToWorldSpace(new Vector3(0f, 0f, 1f)).ShouldBeNear(new Vector3(1f, 0f, 0f));
    }

    [Fact]
    public void VectorToObjectSpace_turns_back_and_does_not_move()
    {
        A.VectorToObjectSpace(new Vector3(1f, 0f, 0f)).ShouldBeNear(new Vector3(0f, 0f, 1f));
        A.VectorToObjectSpace(new Vector3(0f, 0f, -1f)).ShouldBeNear(new Vector3(1f, 0f, 0f));
    }

    [Fact]
    public void Multiplying_by_the_identity_gives_the_same_frame()
    {
        CFrame frame = new CFrame(1.5f, -2.25f, 3.125f) * CFrame.Angles(0.3f, -0.7f, 1.1f);

        (frame * CFrame.Identity).ShouldBe(frame);
        (CFrame.Identity * frame).ShouldBe(frame);
        (frame * CFrame.Identity).Rotation.ShouldBe(frame.Rotation);
    }

    [Fact]
    public void A_rotation_at_the_origin_times_the_identity_is_the_same_rotation()
    {
        CFrame turn = CFrame.Angles(0.3f, -0.7f, 1.1f);

        (turn * CFrame.Identity).Position.ShouldBe(Vector3.Zero);
        (turn * CFrame.Identity).ShouldBe(turn);
    }

    // The position of a product is a.p + a.R * b.p, and a matrix times the
    // zero vector is the zero vector, with no rounding left over.
    [Fact]
    public void A_rotation_times_a_rotation_stays_at_the_origin()
    {
        var random = new Random(4201);
        for (int i = 0; i < 1000; i++)
        {
            CFrame a = RandomFrames.Frame(random);
            CFrame b = RandomFrames.Frame(random);

            ((a - a.Position) * (b - b.Position)).Position.ShouldBe(Vector3.Zero);
            (a * (b - b.Position)).Position.ShouldBe(a.Position);
        }
    }

    [Fact]
    public void The_inverse_of_a_rotation_at_the_origin_stays_at_the_origin()
    {
        var random = new Random(4202);
        for (int i = 0; i < 1000; i++)
        {
            CFrame frame = RandomFrames.Frame(random);

            (frame - frame.Position).Inverse().Position.ShouldBe(Vector3.Zero);
        }
    }

    [Fact]
    public void Turning_the_zero_vector_gives_the_zero_vector()
    {
        var random = new Random(4203);
        for (int i = 0; i < 1000; i++)
        {
            CFrame frame = RandomFrames.Frame(random);

            frame.VectorToWorldSpace(Vector3.Zero).ShouldBe(Vector3.Zero);
            frame.VectorToObjectSpace(Vector3.Zero).ShouldBe(Vector3.Zero);
            frame.PointToObjectSpace(frame.Position).ShouldBe(Vector3.Zero);
            (frame * Vector3.Zero).ShouldBe(frame.Position);
        }
    }

    // R * (s, 0, 0) is s times the first column, however small s is.
    [Fact]
    public void A_very_short_vector_is_turned_like_a_long_one()
    {
        CFrame frame = new CFrame(1f, 2f, 3f) * CFrame.Angles(0.3f, -0.7f, 1.1f);
        const float tiny = 1e-9f;

        Vector3 there = frame.VectorToWorldSpace(new Vector3(tiny, 0f, 0f));
        Vector3 back = frame.VectorToObjectSpace(frame.XVector * tiny);

        (there / tiny).ShouldBeNear(frame.XVector);
        (back / tiny).ShouldBeNear(Vector3.UnitX);
    }

    [Fact]
    public void A_default_frame_is_the_identity()
    {
        CFrame frame = new CFrame(1f, 2f, 3f) * CFrame.Angles(0.3f, -0.7f, 1.1f);

        default(CFrame).ShouldBe(CFrame.Identity);
        default(CFrame).Rotation.ShouldBe(Quaternion.Identity);
        (default(CFrame) * frame).ShouldBe(frame);
        (frame * default(CFrame)).ShouldBe(frame);
        default(CFrame).Inverse().ShouldBe(CFrame.Identity);
    }

    [Fact]
    public void A_rotation_that_is_not_unit_length_is_brought_to_unit_length()
    {
        Quaternion unit = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f);

        var frame = new CFrame(Vector3.Zero, unit * 3f);

        frame.Rotation.Length().ShouldBe(1f, 1e-6f);
        frame.ShouldBeNear(new CFrame(Vector3.Zero, unit));
    }

    [Theory]
    [InlineData(1e20f)]
    [InlineData(1e-23f)]
    public void A_very_long_or_a_very_short_rotation_is_brought_to_unit_length(float length)
    {
        // The quaternion (1, 0, 0, 0) is a half turn about X.
        new CFrame(Vector3.Zero, new Quaternion(length, 0f, 0f, 0f)).ShouldHaveRotation(
            1, 0, 0,
            0, -1, 0,
            0, 0, -1);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_rotation_of_no_length_or_one_that_is_not_finite_is_no_rotation(float x)
    {
        new CFrame(new Vector3(1f, 2f, 3f), new Quaternion(x, 0f, 0f, 0f)).ShouldBe(new CFrame(1f, 2f, 3f));
    }

    [Fact]
    public void A_unit_rotation_is_stored_bit_for_bit()
    {
        Quaternion unit = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1f, 2f, 3f)), 0.4f);

        new CFrame(Vector3.Zero, unit).Rotation.ShouldBe(unit);
    }

    [Fact]
    public void A_long_chain_of_products_stays_a_rotation()
    {
        CFrame step = new CFrame(0.01f, 0f, 0f) * CFrame.Angles(0.011f, 0.017f, -0.013f);
        CFrame frame = CFrame.Identity;

        for (int i = 0; i < 100_000; i++)
            frame *= step;

        frame.Rotation.Length().ShouldBe(1f, 1e-6f);
        frame.XVector.Length().ShouldBe(1f, 1e-5f);
        Vector3.Dot(frame.XVector, frame.YVector).ShouldBe(0f, 1e-5f);
        Vector3.Cross(frame.XVector, frame.YVector).ShouldBeNear(frame.ZVector);
    }
}
