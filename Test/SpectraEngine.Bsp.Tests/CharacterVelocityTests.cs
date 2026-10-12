using System.Numerics;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A velocity given to a character that stands: which one takes it off the
/// ground, and which one leaves it walking.
/// </summary>
public sealed class CharacterVelocityTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    private static readonly CharacterCommand Walk = new() { MoveForward = CharacterCommand.Axis(1f) };

    private static readonly CharacterCommand Jump = new() { Buttons = CharacterButtons.Jump };

    [Fact]
    public void Writing_back_the_velocity_of_a_walk_up_a_ramp_changes_nothing()
    {
        CharacterSimulation character = WalkingUpTheRamp();
        CharacterState before = character.State;
        before.Velocity.Y.ShouldBeGreaterThan(1f);

        character.SetVelocity(before.Velocity);

        character.State.ShouldBe(before);
    }

    [Fact]
    public void A_push_added_on_every_tick_of_a_walk_up_a_ramp_leaves_the_character_on_the_ground()
    {
        CharacterSimulation character = WalkingUpTheRamp();
        float from = character.State.Position.Z;

        // Across the ramp, as a wind from the side.
        for (int i = 0; i < 30; i++)
        {
            character.SetVelocity(character.State.Velocity + new Vector3(0f, 0f, 1f));
            character.Tick(in Walk, Dt);
            character.State.Grounded.ShouldBeTrue($"tick {i + 1}");
        }

        character.State.Position.Z.ShouldBeGreaterThan(from + 1f);

        // It still walks, so a jump fires on the tick it is pressed.
        character.SetVelocity(character.State.Velocity + new Vector3(0f, 0f, 1f));
        character.Tick(in Jump, Dt);
        character.State.Grounded.ShouldBeFalse();
        character.State.Velocity.Y.ShouldBeGreaterThan(character.Tuning.JumpVelocity - 0.5f);
    }

    [Fact]
    public void A_velocity_that_rises_off_a_ramp_lifts_the_character_off_it()
    {
        CharacterSimulation character = WalkingUpTheRamp();
        float standing = character.State.Position.Y;
        const float Up = 6f;

        character.SetVelocity(new Vector3(0f, Up, 0f));
        character.State.Grounded.ShouldBeFalse();

        float highest = standing;
        for (int i = 0; i < 240 && (i < 5 || !character.State.Grounded); i++)
        {
            character.Tick(default, Dt);
            highest = MathF.Max(highest, character.State.Position.Y);
        }

        float rise = Up * Up / (2f * MathF.Abs(PhysicsDefaults.Gravity.Y));
        (highest - standing).ShouldBe(rise, 0.15f);
        character.State.Grounded.ShouldBeTrue();
    }

    [Fact]
    public void A_push_down_a_ramp_that_does_not_point_up_leaves_the_character_on_it()
    {
        CharacterSimulation character = WalkingUpTheRamp();
        float from = character.State.Position.X;

        // Level and away from the slope, which falls off under it.
        character.SetVelocity(new Vector3(-3f, 0f, 0f));
        character.State.Grounded.ShouldBeTrue();

        for (int i = 0; i < 20; i++)
        {
            character.Tick(default, Dt);
            character.State.Grounded.ShouldBeTrue($"tick {i + 1}");
        }

        character.State.Position.X.ShouldBeLessThan(from - 0.1f);
    }

    [Fact]
    public void A_rise_too_slow_to_leave_the_ground_leaves_the_character_standing_and_able_to_jump()
    {
        CharacterSimulation character = WalkingUpTheRamp(ticks: 20);
        character.State.GroundNormal.ShouldBe(Vector3.UnitY);

        character.SetVelocity(new Vector3(0f, 0.3f, 0f));
        character.State.Grounded.ShouldBeTrue();

        character.Tick(in Jump, Dt);
        character.State.Grounded.ShouldBeFalse();
        character.State.Velocity.Y.ShouldBeGreaterThan(character.Tuning.JumpVelocity - 0.5f);
    }

    [Fact]
    public void In_the_air_a_velocity_is_taken_and_nothing_else_of_the_state_changes()
    {
        CharacterSimulation character = WalkingUpTheRamp();
        character.Tick(in Jump, Dt);
        character.Tick(default, Dt);
        CharacterState rising = character.State;
        rising.Grounded.ShouldBeFalse();
        rising.GroundSuppressTicks.ShouldBeLessThan(character.Tuning.GroundSuppressTicks);

        character.SetVelocity(new Vector3(1f, 7f, -2f));

        CharacterState thrown = rising;
        thrown.Velocity = new Vector3(1f, 7f, -2f);
        character.State.ShouldBe(thrown);
    }

    // A floor that ends at x = 0 in a ramp, 12 long and 4 high. The
    // character starts on the floor and walks along +X, which after 60
    // ticks is some way up the ramp.
    private static CharacterSimulation WalkingUpTheRamp(int ticks = 60)
    {
        var scene = new Scene("Velocity");
        SceneNode floor = scene.Root.CreateChild("floor");
        floor.LocalPosition = new Vector3(-6f, -0.5f, 0f);
        floor.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(
            new Vector3(-6f, -0.5f, -8f), new Vector3(6f, 0.5f, 8f));

        const float Run = 12f;
        const float Rise = 4f;
        float length = MathF.Sqrt((Run * Run) + (Rise * Rise));
        SceneNode ramp = scene.Root.CreateChild("ramp");
        ramp.Brush = new SpectraEngine.Core.Bsp.Brush(
        [
            new Plane(new Vector3(1f, 0f, 0f), -Run),
            new Plane(new Vector3(-1f, 0f, 0f), 0f),
            new Plane(new Vector3(0f, -1f, 0f), -1f),
            new Plane(new Vector3(0f, 0f, 1f), -8f),
            new Plane(new Vector3(0f, 0f, -1f), -8f),
            new Plane(new Vector3(-Rise / length, Run / length, 0f), 0f),
        ]);
        scene.RebuildStaticWorld(new FakeRenderer());

        var character = new CharacterSimulation(scene) { SpawnPosition = new Vector3(-2f, 0.05f, 0f) };
        character.Spawn();
        for (int i = 0; i < ticks; i++)
            character.Tick(in Walk, Dt);

        character.State.Grounded.ShouldBeTrue();
        return character;
    }
}
