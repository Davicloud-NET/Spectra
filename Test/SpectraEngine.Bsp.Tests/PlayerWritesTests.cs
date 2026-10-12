using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The one way a script changes the player: what it writes, what it
/// refuses, and that stopping the level puts the tuning back.
/// </summary>
public sealed class PlayerWritesTests
{
    private readonly FakePlayerPresence _player = new();
    private readonly EntityWorld _world = new(new Scene("Player"), NullLogger.Instance, new EntityCatalog());

    [Fact]
    public void A_walk_speed_and_a_jump_height_are_written_to_the_tuning()
    {
        PlayerWrites door = Started();

        door.TrySetWalkSpeed(9f, out string? refusal).ShouldBeTrue(refusal);
        door.TrySetJumpHeight(3f, out refusal).ShouldBeTrue(refusal);

        _player.Tuning.WalkSpeed.ShouldBe(9f);
        _player.Tuning.JumpHeight.ShouldBe(3f);
    }

    [Fact]
    public void Stop_puts_back_what_was_written_and_keeps_an_edit_made_before_the_level_started()
    {
        _player.Tuning.WalkSpeed = 6f;
        float jump = _player.Tuning.JumpHeight;
        PlayerWrites door = Started();

        door.TrySetWalkSpeed(9f, out _).ShouldBeTrue();
        door.TrySetWalkSpeed(12f, out _).ShouldBeTrue();
        door.TrySetJumpHeight(3f, out _).ShouldBeTrue();
        _world.Deactivate();

        _player.Tuning.WalkSpeed.ShouldBe(6f);
        _player.Tuning.JumpHeight.ShouldBe(jump);
    }

    [Fact]
    public void A_member_no_script_wrote_keeps_an_edit_made_while_the_level_played()
    {
        PlayerWrites door = Started();

        door.TrySetWalkSpeed(9f, out _).ShouldBeTrue();
        _player.Tuning.JumpHeight = 2.5f;
        _world.Deactivate();

        _player.Tuning.JumpHeight.ShouldBe(2.5f);
    }

    [Fact]
    public void A_second_level_starts_from_what_the_first_one_put_back()
    {
        float walk = _player.Tuning.WalkSpeed;
        Started().TrySetWalkSpeed(9f, out _).ShouldBeTrue();
        _world.Deactivate();

        PlayerWrites door = Started();
        _player.Tuning.WalkSpeed.ShouldBe(walk);
        door.TrySetWalkSpeed(2f, out _).ShouldBeTrue();
        _world.Deactivate();

        _player.Tuning.WalkSpeed.ShouldBe(walk);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1f)]
    [InlineData(PlayerWrites.MaxSpeed * 2f)]
    public void A_speed_or_a_height_that_makes_no_sense_is_refused_and_writes_nothing(float value)
    {
        PlayerWrites door = Started();
        float walk = _player.Tuning.WalkSpeed;
        float jump = _player.Tuning.JumpHeight;

        door.TrySetWalkSpeed(value, out string? walkRefusal).ShouldBeFalse();
        door.TrySetJumpHeight(value, out string? jumpRefusal).ShouldBeFalse();

        walkRefusal.ShouldStartWith("WalkSpeed cannot be ");
        walkRefusal.ShouldEndWith("it takes a number from 0 to 10000.");
        jumpRefusal.ShouldStartWith("JumpHeight cannot be ");
        _player.Tuning.WalkSpeed.ShouldBe(walk);
        _player.Tuning.JumpHeight.ShouldBe(jump);
    }

    [Fact]
    public void The_ends_of_the_range_are_taken()
    {
        PlayerWrites door = Started();

        door.TrySetWalkSpeed(0f, out string? refusal).ShouldBeTrue(refusal);
        door.TrySetWalkSpeed(PlayerWrites.MaxSpeed, out refusal).ShouldBeTrue(refusal);
        door.TrySetJumpHeight(0f, out refusal).ShouldBeTrue(refusal);
        door.TrySetJumpHeight(PlayerWrites.MaxJumpHeight, out refusal).ShouldBeTrue(refusal);
    }

    [Fact]
    public void A_velocity_reaches_the_player()
    {
        PlayerWrites door = Started();

        door.TrySetVelocity(new Vector3(1f, 8f, -2f), out string? refusal).ShouldBeTrue(refusal);

        _player.Velocity.ShouldBe(new Vector3(1f, 8f, -2f));
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    [InlineData(0f, 0f, 3e38f)]
    [InlineData(8000f, 8000f, 0f)]
    public void A_velocity_that_is_no_number_or_too_fast_is_refused(float x, float y, float z)
    {
        PlayerWrites door = Started();
        _player.Velocity = new Vector3(1f, 2f, 3f);

        door.TrySetVelocity(new Vector3(x, y, z), out string? refusal).ShouldBeFalse();

        refusal.ShouldEndWith("it takes three numbers and no more than 10000 units a second.");
        _player.Velocity.ShouldBe(new Vector3(1f, 2f, 3f));
    }

    [Fact]
    public void A_teleport_to_a_position_keeps_the_way_the_player_faces()
    {
        PlayerWrites door = Started();
        _player.Yaw = 0.5f;

        door.TryTeleport(new Vector3(4f, 1f, -2f), out string? refusal).ShouldBeTrue(refusal);

        _player.Teleports.Single().ShouldBe((new Vector3(4f, 1f, -2f), (float?)null));
        _player.Yaw.ShouldBe(0.5f);
    }

    [Fact]
    public void A_teleport_to_a_frame_turns_the_player_along_the_frames_z_axis()
    {
        PlayerWrites door = Started();

        // As a player start that points down -Z is turned.
        var frame = new CFrame(new Vector3(4f, 1f, -2f), Light.RotationForDirection(new Vector3(0f, 0f, -1f)));
        door.TryTeleport(frame, out string? refusal).ShouldBeTrue(refusal);

        _player.Feet.ShouldBe(new Vector3(4f, 1f, -2f));
        _player.Yaw.ShouldBe(-MathF.PI / 2f, 1e-5f);
    }

    [Fact]
    public void A_frame_that_looks_straight_up_keeps_the_way_the_player_faces()
    {
        PlayerWrites door = Started();
        _player.Yaw = 0.5f;

        var up = new CFrame(Vector3.Zero, Light.RotationForDirection(Vector3.UnitY));
        door.TryTeleport(up, out string? refusal).ShouldBeTrue(refusal);

        _player.Teleports.Single().Yaw.ShouldBeNull();
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    [InlineData(0f, 0f, float.NegativeInfinity)]
    public void A_place_that_is_not_finite_is_refused(float x, float y, float z)
    {
        PlayerWrites door = Started();

        door.TryTeleport(new Vector3(x, y, z), out string? refusal).ShouldBeFalse();
        door.TryTeleport(new CFrame(new Vector3(x, y, z)), out string? frameRefusal).ShouldBeFalse();

        refusal.ShouldBe("The player cannot be put there: a position must be three numbers, with none of them infinite.");
        frameRefusal.ShouldBe(refusal);
        _player.Teleports.ShouldBeEmpty();
    }

    [Fact]
    public void A_respawn_reaches_the_player()
    {
        PlayerWrites door = Started();

        door.TryRespawn(out string? refusal).ShouldBeTrue(refusal);

        _player.Respawns.ShouldBe(1);
    }

    [Fact]
    public void There_is_no_player_when_the_level_has_none_or_it_is_not_in_the_level()
    {
        PlayerWrites door = Started();
        door.Player.ShouldBeSameAs(_player);

        _player.IsPresent = false;
        door.Player.ShouldBeNull();

        _world.Player = null;
        door.Player.ShouldBeNull();
    }

    [Fact]
    public void Every_write_is_refused_when_nothing_walks_the_level()
    {
        PlayerWrites door = Started();
        _world.Player = null;

        ShouldRefuseEverything(door, "The player cannot be changed: nothing walks the level.");
    }

    [Fact]
    public void Every_write_is_refused_once_the_level_has_stopped()
    {
        PlayerWrites door = Started();
        _world.Deactivate();

        // A host that left its player with a stopped level.
        _world.Player = _player;

        ShouldRefuseEverything(door, "The player cannot be changed: the level has stopped.");
        _player.Tuning.WalkSpeed.ShouldBe(new CharacterTuning().WalkSpeed);
        _player.Teleports.ShouldBeEmpty();
        _player.Respawns.ShouldBe(0);
    }

    private static void ShouldRefuseEverything(PlayerWrites door, string why)
    {
        door.TrySetWalkSpeed(9f, out string? refusal).ShouldBeFalse();
        refusal.ShouldBe(why);
        door.TrySetJumpHeight(3f, out refusal).ShouldBeFalse();
        refusal.ShouldBe(why);
        door.TrySetVelocity(Vector3.UnitY, out refusal).ShouldBeFalse();
        refusal.ShouldBe(why);
        door.TryTeleport(Vector3.One, out refusal).ShouldBeFalse();
        refusal.ShouldBe(why);
        door.TryTeleport(CFrame.Identity, out refusal).ShouldBeFalse();
        refusal.ShouldBe(why);
        door.TryRespawn(out refusal).ShouldBeFalse();
        refusal.ShouldBe(why);
    }

    // A level that plays, with the player a host gives it once it has started.
    private PlayerWrites Started()
    {
        _world.Activate();
        _world.Player = _player;
        return new PlayerWrites(_world);
    }
}
