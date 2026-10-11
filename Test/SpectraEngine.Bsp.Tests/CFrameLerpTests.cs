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

    // Our choice for the next three: Roblox's documentation does not say what
    // an alpha outside 0 to 1 gives. Ours keeps to the same line and the same arc.
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

    [Fact]
    public void Lerp_far_past_a_goal_a_small_turn_away_still_follows_the_arc()
    {
        // A thousand times a thousandth of a radian about Y is one radian.
        float cosine = MathF.Cos(1f), sine = MathF.Sin(1f);

        CFrame.Identity.Lerp(CFrame.Angles(0f, 1e-3f, 0f), 1000f).ShouldHaveRotation(
            cosine, 0, sine,
            0, 1, 0,
            -sine, 0, cosine);
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
}
