using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Equality by the twelve components, and FuzzyEq with a tolerance.</summary>
public sealed class CFrameEqualityTests
{
    private static readonly CFrame Frame = new CFrame(1f, 2f, 3f) * CFrame.Angles(0.3f, -0.7f, 1.1f);

    [Fact]
    public void Frames_built_the_same_way_are_equal()
    {
        CFrame again = new CFrame(1f, 2f, 3f) * CFrame.Angles(0.3f, -0.7f, 1.1f);

        (Frame == again).ShouldBeTrue();
        (Frame != again).ShouldBeFalse();
        Frame.Equals((object)again).ShouldBeTrue();
        Frame.GetHashCode().ShouldBe(again.GetHashCode());
    }

    [Fact]
    public void A_different_position_is_a_different_frame()
    {
        (Frame == Frame + new Vector3(0f, 0f, 1e-3f)).ShouldBeFalse();
        (Frame != Frame + new Vector3(0f, 0f, 1e-3f)).ShouldBeTrue();
    }

    [Fact]
    public void A_different_rotation_is_a_different_frame()
    {
        (Frame == Frame * CFrame.Angles(0f, 1e-3f, 0f)).ShouldBeFalse();
    }

    [Fact]
    public void A_quaternion_and_its_negative_are_the_same_frame()
    {
        // Both name one rotation and give the same twelve components.
        var plus = new CFrame(Frame.Position, Frame.Rotation);
        var minus = new CFrame(Frame.Position, Quaternion.Negate(Frame.Rotation));

        (plus == minus).ShouldBeTrue();
        plus.GetHashCode().ShouldBe(minus.GetHashCode());
        minus.Components().ShouldBe(plus.Components());
    }

    [Fact]
    public void Equal_frames_have_equal_components()
    {
        CFrame other = new CFrame(Frame.Position, Frame.Rotation);

        (Frame == other).ShouldBeTrue();
        other.Components().ShouldBe(Frame.Components());
    }

    [Fact]
    public void FuzzyEq_accepts_a_frame_within_the_tolerance()
    {
        CFrame near = (Frame + new Vector3(0f, 4e-4f, 0f)) * CFrame.Angles(4e-4f, 0f, 0f);

        Frame.FuzzyEq(near, 1e-3f).ShouldBeTrue();
        near.FuzzyEq(Frame, 1e-3f).ShouldBeTrue();
    }

    [Fact]
    public void FuzzyEq_refuses_a_position_past_the_tolerance()
    {
        Frame.FuzzyEq(Frame + new Vector3(0f, 2e-3f, 0f), 1e-3f).ShouldBeFalse();
    }

    [Fact]
    public void FuzzyEq_refuses_a_rotation_past_the_tolerance()
    {
        Frame.FuzzyEq(Frame * CFrame.Angles(0f, 0f, 5e-3f), 1e-3f).ShouldBeFalse();
    }

    [Fact]
    public void FuzzyEq_with_no_tolerance_given_allows_rounding_and_little_more()
    {
        CFrame there = Frame * CFrame.Angles(0.2f, 0.4f, -0.6f);
        CFrame back = there * CFrame.Angles(0.2f, 0.4f, -0.6f).Inverse();

        Frame.FuzzyEq(back).ShouldBeTrue();
        Frame.FuzzyEq(Frame * CFrame.Angles(0f, 1e-3f, 0f)).ShouldBeFalse();
    }

    [Fact]
    public void ToString_lists_the_twelve_components()
    {
        CFrame frame = new CFrame(1f, 2.5f, -3f) * CFrame.Angles(0f, MathF.PI, 0f);

        string text = frame.ToString();

        text.Split(", ").Length.ShouldBe(12);
        text.ShouldStartWith("1, 2.5, -3, -1, ");
    }
}
