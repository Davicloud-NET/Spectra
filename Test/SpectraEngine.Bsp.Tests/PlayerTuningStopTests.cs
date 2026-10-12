using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The character and its tuning outlive a play session. What a running
/// level changed on the tuning goes back whichever way the level ends.
/// </summary>
public sealed class PlayerTuningStopTests
{
    private static readonly CharacterCommand Forward = new() { MoveForward = CharacterCommand.Axis(1f) };

    private readonly PlayRig _rig = new();

    [Fact]
    public void A_walk_speed_written_while_playing_is_how_fast_the_player_walks_from_the_next_tick()
    {
        _rig.Session.Enter();
        _rig.Tick(default, 60);

        new PlayerWrites(_rig.Entities).TrySetWalkSpeed(9f, out string? refusal).ShouldBeTrue(refusal);
        _rig.Tick(in Forward, 30);

        _rig.Character.HorizontalSpeed.ShouldBe(9f, 0.05f);
    }

    [Fact]
    public void Leaving_play_puts_back_the_tuning_the_level_changed()
    {
        CharacterTuning tuning = _rig.Character.Tuning;
        tuning.WalkSpeed = 6f;
        float jump = tuning.JumpHeight;
        _rig.Session.Enter();
        var door = new PlayerWrites(_rig.Entities);

        door.TrySetWalkSpeed(9f, out _).ShouldBeTrue();
        door.TrySetJumpHeight(3f, out _).ShouldBeTrue();
        _rig.Tick(in Forward, 30);
        _rig.Session.Exit();

        tuning.WalkSpeed.ShouldBe(6f);
        tuning.JumpHeight.ShouldBe(jump);
    }

    [Fact]
    public void A_map_loaded_during_play_puts_it_back_while_the_player_walks_on()
    {
        float walk = _rig.Character.Tuning.WalkSpeed;
        _rig.Session.Enter();
        new PlayerWrites(_rig.Entities).TrySetWalkSpeed(9f, out _).ShouldBeTrue();

        _rig.Manager.OnSceneReplaced();

        _rig.Session.IsActive.ShouldBeTrue();
        _rig.Character.Tuning.WalkSpeed.ShouldBe(walk);
    }

    [Fact]
    public void A_second_play_starts_with_the_tuning_as_it_was_edited_between_the_two()
    {
        CharacterTuning tuning = _rig.Character.Tuning;
        _rig.Session.Enter();
        new PlayerWrites(_rig.Entities).TrySetWalkSpeed(9f, out _).ShouldBeTrue();
        _rig.Session.Exit();

        tuning.WalkSpeed = 7f;
        _rig.Session.Enter();
        tuning.WalkSpeed.ShouldBe(7f);

        new PlayerWrites(_rig.Entities).TrySetWalkSpeed(2f, out _).ShouldBeTrue();
        _rig.Session.Exit();

        tuning.WalkSpeed.ShouldBe(7f);
    }
}
