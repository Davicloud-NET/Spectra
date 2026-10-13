using System.Numerics;
using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The way a command asks to walk, which every mover starts from: level,
/// along the yaw, and of length one or zero.
/// </summary>
public sealed class CharacterCommandDirectionTests
{
    [Fact]
    public void A_command_at_rest_asks_to_walk_nowhere()
    {
        new CharacterCommand { Yaw = 1.2f, Pitch = 0.4f }.MoveDirection.ShouldBe(Vector3.Zero);
    }

    [Theory]
    [InlineData(0f, 1f, 0f)]
    [InlineData(MathF.PI / 2f, 0f, 1f)]
    [InlineData(MathF.PI, -1f, 0f)]
    public void Forward_is_along_the_yaw_with_plus_X_at_zero_and_plus_Z_a_quarter_turn_on(float yaw, float x, float z)
    {
        Vector3 direction = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Yaw = yaw }.MoveDirection;

        direction.X.ShouldBe(x, 1e-6f);
        direction.Y.ShouldBe(0f);
        direction.Z.ShouldBe(z, 1e-6f);
    }

    [Fact]
    public void A_strafe_is_a_quarter_turn_to_the_right_of_forward()
    {
        Vector3 direction = new CharacterCommand { MoveStrafe = CharacterCommand.Axis(1f) }.MoveDirection;

        direction.X.ShouldBe(0f, 1e-6f);
        direction.Z.ShouldBe(1f, 1e-6f);
    }

    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(0.25f, 0f)]
    [InlineData(-0.5f, 0.1f)]
    public void However_far_the_axes_are_pushed_the_direction_has_a_length_of_one(float forward, float strafe)
    {
        var command = new CharacterCommand
        {
            MoveForward = CharacterCommand.Axis(forward),
            MoveStrafe = CharacterCommand.Axis(strafe),
            Yaw = 0.7f,
            Pitch = -1f,
        };

        command.MoveDirection.Length().ShouldBe(1f, 1e-6f);
        command.MoveDirection.Y.ShouldBe(0f);
    }
}
