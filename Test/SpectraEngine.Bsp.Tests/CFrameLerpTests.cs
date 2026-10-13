using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Lerp: the position goes straight and the rotation by the shortest arc.</summary>
public sealed class CFrameLerpTests
{
    private const float Quarter = MathF.PI / 2f;
    private static readonly float Root = MathF.Sqrt(0.5f);

    private static readonly CFrame Start = new CFrame(1f, 2f, 3f) * CFrame.Angles(0.3f, -0.7f, 1.1f);
    private static readonly CFrame Goal = new CFrame(-4f, 0.5f, 9f) * CFrame.Angles(-1.2f, 0.4f, 2.5f);

    [Fact]
    public void Lerp_at_zero_is_the_start()
    {
        Start.Lerp(Goal, 0f).ShouldBeNear(Start);
    }

    [Fact]
    public void Lerp_at_one_is_the_goal()
    {
        Start.Lerp(Goal, 1f).ShouldBeNear(Goal);
    }

    [Fact]
    public void Half_way_to_a_quarter_turn_is_an_eighth_turn_at_the_midpoint()
    {
        CFrame goal = new CFrame(10f, 0f, -4f) * CFrame.Angles(0f, Quarter, 0f);

        CFrame half = CFrame.Identity.Lerp(goal, 0.5f);

        // Ry(45).
        half.ShouldHaveComponents(
            5, 0, -2,
            Root, 0, Root,
            0, 1, 0,
            -Root, 0, Root);
    }

    [Fact]
    public void A_quarter_of_the_way_turns_a_quarter_of_the_angle()
    {
        CFrame goal = CFrame.Angles(0f, 0f, 2f);

        CFrame.Identity.Lerp(goal, 0.25f).ShouldBeNear(CFrame.Angles(0f, 0f, 0.5f));
    }

    [Fact]
    public void Lerp_takes_the_short_way_round()
    {
        // Three quarters of a turn one way is a quarter turn the other way.
        CFrame goal = CFrame.Angles(0f, 3f * Quarter, 0f);

        CFrame half = CFrame.Identity.Lerp(goal, 0.5f);

        // Ry(-45), not Ry(135).
        half.ShouldHaveRotation(
            Root, 0, -Root,
            0, 1, 0,
            Root, 0, Root);
    }

    [Fact]
    public void Lerp_takes_the_short_way_whatever_the_sign_of_the_stored_quaternion()
    {
        Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Quarter);
        var goal = new CFrame(Vector3.Zero, q);
        var sameGoal = new CFrame(Vector3.Zero, Quaternion.Negate(q));

        CFrame.Identity.Lerp(sameGoal, 0.5f).ShouldBeNear(CFrame.Identity.Lerp(goal, 0.5f));
    }

    // Roblox's documentation does not say what an alpha outside 0 to 1 gives.
    // Studio keeps to the same line and the same arc, and so do the next two.
    [Fact]
    public void Lerp_at_two_goes_twice_as_far()
    {
        CFrame goal = new CFrame(10f, 0f, -4f) * CFrame.Angles(0f, Quarter, 0f);

        // Twice a quarter turn about Y is the half turn.
        CFrame.Identity.Lerp(goal, 2f).ShouldHaveComponents(
            20, 0, -8,
            -1, 0, 0,
            0, 1, 0,
            0, 0, -1);
    }

    [Fact]
    public void Lerp_at_minus_one_goes_the_other_way()
    {
        CFrame goal = new CFrame(10f, 0f, -4f) * CFrame.Angles(0f, Quarter, 0f);

        // Ry(-90).
        CFrame.Identity.Lerp(goal, -1f).ShouldHaveComponents(
            -10, 0, 4,
            0, 0, -1,
            0, 1, 0,
            1, 0, 0);
    }

    // Studio blends two rotations under about 2.2 degrees apart in a straight
    // line. Far past the goal that turns less than the arc: a thousand times
    // a thousandth of a radian comes to 0.927 of a radian, not to one.
    [Fact]
    public void Lerp_far_past_a_goal_a_small_turn_away_blends_in_a_straight_line()
    {
        double turned = StraightBlend(1e-3, 1000.0);
        turned.ShouldBe(0.92739, 1e-4);

        CFrame.Identity.Lerp(CFrame.Angles(0f, 1e-3f, 0f), 1000f).ShouldBeNear(CFrame.Angles(0f, (float)turned, 0f));
    }

    [Theory]
    [InlineData(0.03f, 10f)]
    [InlineData(0.038f, 10f)]
    [InlineData(0.038f, -4f)]
    public void Lerp_past_a_goal_under_the_switch_blends_in_a_straight_line(float angle, float alpha)
    {
        float turned = (float)StraightBlend(angle, alpha);

        CFrame.Identity.Lerp(CFrame.Angles(0f, angle, 0f), alpha).ShouldBeNear(CFrame.Angles(0f, turned, 0f));
    }

    [Theory]
    [InlineData(0.04f, 10f)]
    [InlineData(0.05f, 10f)]
    [InlineData(0.05f, -4f)]
    public void Lerp_past_a_goal_over_the_switch_follows_the_arc(float angle, float alpha)
    {
        CFrame.Identity.Lerp(CFrame.Angles(0f, angle, 0f), alpha).ShouldBeNear(CFrame.Angles(0f, angle * alpha, 0f));
    }

    [Fact]
    public void Between_the_two_ends_the_switch_does_not_show()
    {
        for (int i = 0; i <= 20; i++)
        {
            float alpha = i / 20f;
            CFrame.Identity.Lerp(CFrame.Angles(0f, 0.038f, 0f), alpha).ShouldBeNear(CFrame.Angles(0f, 0.038f * alpha, 0f));
        }
    }

    [Fact]
    public void Lerp_under_the_switch_gives_a_rotation_at_any_alpha()
    {
        foreach (float alpha in new[] { -3e38f, -1e20f, -5263.2f, 5263.2f, 1e20f, 3e38f })
        {
            CFrame frame = CFrame.Identity.Lerp(CFrame.Angles(0f, 0.01f, 0f), alpha);

            frame.Rotation.Length().ShouldBe(1f, 1e-5f);
        }
    }

    [Fact]
    public void Lerp_at_one_gives_the_goal_with_no_rounding()
    {
        Start.Lerp(Goal, 1f).ShouldBe(Goal);
        Start.Lerp(Goal, 1f).Rotation.ShouldBe(Goal.Rotation);
    }

    [Fact]
    public void Lerp_between_equal_frames_stays_there()
    {
        Start.Lerp(Start, 0.37f).ShouldBeNear(Start);
    }

    [Fact]
    public void Lerp_keeps_the_rotation_at_unit_length()
    {
        for (int i = 0; i <= 20; i++)
            Start.Lerp(Goal, i / 20f).Rotation.Length().ShouldBe(1f, 1e-6f);
    }

    // The angle a straight blend of the identity and a turn by an angle
    // comes to, once the blend is back at unit length.
    private static double StraightBlend(double angle, double alpha)
    {
        double sine = Math.Sin(angle / 2.0), cosine = Math.Cos(angle / 2.0);
        return 2.0 * Math.Atan2(alpha * sine, 1.0 + (alpha * (cosine - 1.0)));
    }
}
