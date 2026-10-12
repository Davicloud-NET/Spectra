using System.Numerics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A teleport to a place inside something solid. It is taken, and the mover
/// then pushes the player out through the nearest face.
/// </summary>
public sealed class TeleportIntoSolidTests
{
    private readonly PlayRig _rig = new();

    [Fact]
    public void A_teleport_to_just_under_the_floor_stands_the_player_on_it()
    {
        PlayerWrites door = Playing();
        var under = new Vector3(5f, -0.3f, 0f);

        door.TryTeleport(under, out string? refusal).ShouldBeTrue(refusal);
        _rig.Character.State.Position.ShouldBe(under);

        _rig.Tick(default, 5);

        CharacterState state = _rig.Character.State;
        state.Grounded.ShouldBeTrue();
        state.Position.X.ShouldBe(5f, 1e-4f);
        state.Position.Y.ShouldBeInRange(0f, 0.02f);
    }

    [Fact]
    public void A_teleport_into_a_part_is_taken_and_the_player_comes_out_of_its_nearest_side()
    {
        // Four to a side on the floor. The feet go in low, half a unit off
        // its middle towards +Z, so that side is the nearest way out.
        _rig.Part("block", new Vector3(10f, 2f, 0f), new Vector3(2f));
        PlayerWrites door = Playing();
        var inside = new Vector3(10f, 0.5f, 0.5f);

        door.TryTeleport(inside, out string? refusal).ShouldBeTrue(refusal);
        _rig.Character.State.Position.ShouldBe(inside);

        _rig.Tick(default, 30);

        CharacterState state = _rig.Character.State;
        state.Position.Z.ShouldBe(2f + _rig.Character.Tuning.Radius, 0.02f);
        state.Position.X.ShouldBe(10f, 1e-3f);
        state.Grounded.ShouldBeTrue();
        state.Position.Y.ShouldBeInRange(0f, 0.02f);
    }

    [Fact]
    public void A_teleport_into_the_middle_of_a_part_lifts_the_player_out_of_its_top_half_a_unit_a_tick()
    {
        _rig.Part("block", new Vector3(10f, 2f, 0f), new Vector3(2f));
        PlayerWrites door = Playing();

        door.TryTeleport(new Vector3(10f, 2f, 0f), out string? refusal).ShouldBeTrue(refusal);

        _rig.Tick(default);
        _rig.Character.State.Position.Y.ShouldBe(2.5f, 1e-3f);

        _rig.Tick(default, 10);
        _rig.Character.State.Position.Y.ShouldBe(4f, 0.02f);
        _rig.Character.State.Grounded.ShouldBeTrue();
    }

    [Fact]
    public void A_teleport_to_under_the_floor_falls_out_of_the_level_and_the_player_starts_again()
    {
        PlayerWrites door = Playing();

        // The plate is one thick. Feet this deep leave only the head in it.
        door.TryTeleport(new Vector3(5f, -1.7f, 0f), out string? refusal).ShouldBeTrue(refusal);

        int ticks = 0;
        for (; ticks < 600 && _rig.Character.Respawns == 0; ticks++)
            _rig.Tick(default);

        _rig.Character.Respawns.ShouldBe(1);
        _rig.Character.State.Position.ShouldBe(_rig.Character.SpawnPosition);
    }

    // A level that plays, with the player standing where it started.
    private PlayerWrites Playing()
    {
        _rig.Session.Enter();
        _rig.Tick(default, 60);
        _rig.Character.State.Grounded.ShouldBeTrue();
        return new PlayerWrites(_rig.Entities);
    }
}
