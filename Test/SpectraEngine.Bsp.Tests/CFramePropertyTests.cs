using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Laws that hold for every frame, checked over seeded random frames.</summary>
public sealed class CFramePropertyTests
{
    private const int Rounds = 400;
    private const float Loose = 1e-4f;

    [Fact]
    public void A_product_times_a_point_applies_the_right_hand_frame_first()
    {
        var random = new Random(4101);
        for (int i = 0; i < Rounds; i++)
        {
            CFrame a = RandomFrames.Frame(random);
            CFrame b = RandomFrames.Frame(random);
            Vector3 v = RandomFrames.Vector(random);

            ((a * b) * v).ShouldBeNear(a * (b * v), Loose);
        }
    }

    [Fact]
    public void The_product_is_associative()
    {
        var random = new Random(4102);
        for (int i = 0; i < Rounds; i++)
        {
            CFrame a = RandomFrames.Frame(random);
            CFrame b = RandomFrames.Frame(random);
            CFrame c = RandomFrames.Frame(random);

            ((a * b) * c).ShouldBeNear(a * (b * c), Loose);
        }
    }

    [Fact]
    public void A_frame_times_its_inverse_is_the_identity_on_either_side()
    {
        var random = new Random(4103);
        for (int i = 0; i < Rounds; i++)
        {
            CFrame a = RandomFrames.Frame(random);

            (a * a.Inverse()).ShouldBeNear(CFrame.Identity, Loose);
            (a.Inverse() * a).ShouldBeNear(CFrame.Identity, Loose);
        }
    }

    [Fact]
    public void The_inverse_of_a_product_is_the_inverses_multiplied_the_other_way_round()
    {
        var random = new Random(4104);
        for (int i = 0; i < Rounds; i++)
        {
            CFrame a = RandomFrames.Frame(random);
            CFrame b = RandomFrames.Frame(random);

            (a * b).Inverse().ShouldBeNear(b.Inverse() * a.Inverse(), Loose);
        }
    }

    [Fact]
    public void ToObjectSpace_undoes_ToWorldSpace()
    {
        var random = new Random(4105);
        for (int i = 0; i < Rounds; i++)
        {
            CFrame a = RandomFrames.Frame(random);
            CFrame b = RandomFrames.Frame(random);

            a.ToObjectSpace(a.ToWorldSpace(b)).ShouldBeNear(b, Loose);
            a.ToWorldSpace(a.ToObjectSpace(b)).ShouldBeNear(b, Loose);
            a.ToObjectSpace(b).ShouldBeNear(a.Inverse() * b, Loose);
        }
    }

    [Fact]
    public void The_point_transforms_undo_each_other_and_match_the_inverse()
    {
        var random = new Random(4106);
        for (int i = 0; i < Rounds; i++)
        {
            CFrame a = RandomFrames.Frame(random);
            Vector3 v = RandomFrames.Vector(random);

            a.PointToObjectSpace(a.PointToWorldSpace(v)).ShouldBeNear(v, Loose);
            a.PointToObjectSpace(v).ShouldBeNear(a.Inverse() * v, Loose);
        }
    }

    [Fact]
    public void The_vector_transforms_undo_each_other_and_ignore_the_position()
    {
        var random = new Random(4107);
        for (int i = 0; i < Rounds; i++)
        {
            CFrame a = RandomFrames.Frame(random);
            Vector3 v = RandomFrames.Vector(random);

            a.VectorToObjectSpace(a.VectorToWorldSpace(v)).ShouldBeNear(v, Loose);
            a.VectorToWorldSpace(v).ShouldBeNear((a * v) - a.Position, Loose);
            a.VectorToWorldSpace(v).ShouldBeNear((a + new Vector3(5f, -6f, 7f)).VectorToWorldSpace(v), Loose);
        }
    }

    [Fact]
    public void The_three_axis_vectors_are_a_right_handed_unit_frame()
    {
        var random = new Random(4108);
        for (int i = 0; i < Rounds; i++)
        {
            CFrame a = RandomFrames.Frame(random);

            a.XVector.Length().ShouldBe(1f, Loose);
            a.YVector.Length().ShouldBe(1f, Loose);
            Vector3.Cross(a.XVector, a.YVector).ShouldBeNear(a.ZVector, Loose);
            a.VectorToWorldSpace(Vector3.UnitX).ShouldBeNear(a.XVector, Loose);
            a.VectorToWorldSpace(-Vector3.UnitZ).ShouldBeNear(a.LookVector, Loose);
        }
    }

    [Fact]
    public void The_angles_given_to_Angles_come_back_from_ToEulerAnglesXYZ_away_from_the_poles()
    {
        var random = new Random(4109);
        for (int i = 0; i < Rounds; i++)
        {
            var angles = new Vector3(
                RandomFrames.Angle(random), RandomFrames.Between(random, -1.4f, 1.4f), RandomFrames.Angle(random));

            Vector3 back = CFrame.Angles(angles.X, angles.Y, angles.Z).ToEulerAnglesXYZ();

            back.ShouldBeNear(angles, Loose);
        }
    }

    [Fact]
    public void The_angles_given_to_FromOrientation_come_back_from_ToOrientation_away_from_the_poles()
    {
        var random = new Random(4110);
        for (int i = 0; i < Rounds; i++)
        {
            var angles = new Vector3(
                RandomFrames.Between(random, -1.4f, 1.4f), RandomFrames.Angle(random), RandomFrames.Angle(random));

            Vector3 back = CFrame.FromOrientation(angles.X, angles.Y, angles.Z).ToOrientation();

            back.ShouldBeNear(angles, Loose);
        }
    }

    [Fact]
    public void The_angles_read_from_any_frame_build_its_rotation_again()
    {
        var random = new Random(4111);
        for (int i = 0; i < Rounds; i++)
        {
            CFrame frame = RandomFrames.Frame(random);
            CFrame rotation = frame - frame.Position;

            Vector3 xyz = rotation.ToEulerAnglesXYZ();
            Vector3 yxz = rotation.ToOrientation();

            CFrame.Angles(xyz.X, xyz.Y, xyz.Z).ShouldBeNear(rotation, Loose);
            CFrame.FromOrientation(yxz.X, yxz.Y, yxz.Z).ShouldBeNear(rotation, Loose);
            rotation.ToEulerAnglesYXZ().ShouldBe(yxz);
        }
    }

    // Our choice at a pole, where only the sum or the difference of the two
    // outer angles is fixed: the last angle is zero.
    [Theory]
    [InlineData(MathF.PI / 2f)]
    [InlineData(-MathF.PI / 2f)]
    public void At_a_pole_the_angles_still_build_the_rotation_and_the_z_angle_is_zero(float middle)
    {
        CFrame xyz = CFrame.Angles(0.4f, middle, -0.9f);
        CFrame yxz = CFrame.FromOrientation(middle, 0.4f, -0.9f);

        Vector3 fromXyz = xyz.ToEulerAnglesXYZ();
        Vector3 fromYxz = yxz.ToOrientation();

        fromXyz.Z.ShouldBe(0f);
        fromYxz.Z.ShouldBe(0f);
        fromXyz.Y.ShouldBe(middle, 1e-5f);
        fromYxz.X.ShouldBe(middle, 1e-5f);
        CFrame.Angles(fromXyz.X, fromXyz.Y, fromXyz.Z).ShouldBeNear(xyz, 1e-5f);
        CFrame.FromOrientation(fromYxz.X, fromYxz.Y, fromYxz.Z).ShouldBeNear(yxz, 1e-5f);
    }

    [Theory]
    [InlineData(1.5f)]
    [InlineData(1.57f)]
    [InlineData(1.5705f)]
    [InlineData(1.5707f)]
    [InlineData(1.57079f)]
    [InlineData(-1.5707f)]
    public void Close_to_a_pole_the_angles_still_build_the_rotation(float middle)
    {
        CFrame xyz = CFrame.Angles(0.4f, middle, -0.9f);
        CFrame yxz = CFrame.FromOrientation(middle, 0.4f, -0.9f);

        Vector3 fromXyz = xyz.ToEulerAnglesXYZ();
        Vector3 fromYxz = yxz.ToOrientation();

        fromXyz.Y.ShouldBe(middle, 1e-4f);
        fromYxz.X.ShouldBe(middle, 1e-4f);
        CFrame.Angles(fromXyz.X, fromXyz.Y, fromXyz.Z).ShouldBeNear(xyz, 5e-4f);
        CFrame.FromOrientation(fromYxz.X, fromYxz.Y, fromYxz.Z).ShouldBeNear(yxz, 5e-4f);
    }

    [Fact]
    public void FromOrientation_is_the_engines_yaw_pitch_roll()
    {
        var random = new Random(4112);
        for (int i = 0; i < Rounds; i++)
        {
            float rx = RandomFrames.Angle(random), ry = RandomFrames.Angle(random), rz = RandomFrames.Angle(random);

            var engine = new CFrame(Vector3.Zero, Quaternion.CreateFromYawPitchRoll(yaw: ry, pitch: rx, roll: rz));

            CFrame.FromOrientation(rx, ry, rz).ShouldBeNear(engine, Loose);
        }
    }

    [Fact]
    public void Lerp_moves_the_point_a_frame_carries_along_the_arc_between_start_and_goal()
    {
        var random = new Random(4113);
        for (int i = 0; i < Rounds; i++)
        {
            CFrame start = RandomFrames.Frame(random);
            Vector3 axis = Vector3.Normalize(RandomFrames.Axis(random));
            float angle = RandomFrames.Between(random, -3f, 3f);
            Vector3 offset = RandomFrames.Vector(random);
            CFrame goal = (start + offset) * CFrame.FromAxisAngle(axis, angle);
            float alpha = RandomFrames.Between(random, 0f, 1f);

            CFrame expected = (start + (offset * alpha)) * CFrame.FromAxisAngle(axis, angle * alpha);

            start.Lerp(goal, alpha).ShouldBeNear(expected, Loose);
        }
    }
}
