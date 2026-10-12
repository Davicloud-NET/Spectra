using System.Numerics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>A character's yaw as a frame, and a frame's heading as a yaw.</summary>
public sealed class CharacterFacingTests
{
    [Theory]
    [InlineData(0f, 1f, 0f)]
    [InlineData(MathF.PI / 2f, 0f, 1f)]
    [InlineData(MathF.PI, -1f, 0f)]
    [InlineData(-MathF.PI / 2f, 0f, -1f)]
    public void A_frame_looks_along_its_z_axis_the_way_a_command_of_that_yaw_walks(float yaw, float x, float z)
    {
        CFrame frame = CharacterFacing.FrameAt(new Vector3(1f, 2f, 3f), yaw);

        frame.Position.ShouldBe(new Vector3(1f, 2f, 3f));
        frame.ZVector.X.ShouldBe(x, 1e-6f);
        frame.ZVector.Y.ShouldBe(0f, 1e-6f);
        frame.ZVector.Z.ShouldBe(z, 1e-6f);
        frame.UpVector.Y.ShouldBe(1f, 1e-6f);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.3f)]
    [InlineData(2.5f)]
    [InlineData(-1.9f)]
    public void The_yaw_of_a_characters_frame_is_the_yaw_it_was_made_with(float yaw)
    {
        CFrame frame = CharacterFacing.FrameAt(Vector3.Zero, yaw);

        CharacterFacing.YawOf(in frame).ShouldNotBeNull().ShouldBe(yaw, 1e-5f);
    }

    [Fact]
    public void A_tilted_frame_has_the_heading_of_its_z_axis()
    {
        // Looks along +X and a little down.
        Quaternion tilted = Light.RotationForDirection(Vector3.Normalize(new Vector3(1f, -0.4f, 0f)));

        CharacterFacing.YawOf(new CFrame(Vector3.Zero, tilted)).ShouldNotBeNull().ShouldBe(0f, 1e-5f);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void A_frame_that_looks_straight_up_or_down_has_no_heading(float y)
    {
        Quaternion vertical = Light.RotationForDirection(new Vector3(0f, y, 0f));

        CharacterFacing.YawOf(new CFrame(Vector3.Zero, vertical)).ShouldBeNull();
    }
}
