using System.Numerics;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The way a character faces is part of its state. A restore brings it
/// back, and the facing a teleport named holds until a view has taken the
/// teleport.
/// </summary>
public sealed class CharacterYawTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    private static readonly Vector3 Elsewhere = new(3f, 0.05f, -2f);

    [Fact]
    public void A_restore_brings_back_the_facing_of_the_state_it_is_given()
    {
        CharacterSimulation character = Spawned();
        character.Tick(Facing(0.4f), Dt);
        CharacterState captured = character.State;

        character.Tick(Facing(-1.1f), Dt);
        character.Yaw.ShouldBe(-1.1f);

        character.Restore(in captured);

        character.Yaw.ShouldBe(0.4f);
        CharacterFacing.FrameOf(character.State).LookVector.X.ShouldBe(MathF.Cos(0.4f), 1e-6f);
    }

    [Fact]
    public void The_facing_a_teleport_named_reads_back_until_a_view_has_taken_the_teleport()
    {
        CharacterSimulation character = Spawned();
        character.Tick(Facing(0.2f), Dt);

        character.Teleport(Elsewhere, yaw: 1.5f);

        // The view has not turned yet, so its commands still carry its old yaw.
        for (int i = 0; i < 3; i++)
        {
            character.Tick(Facing(0.2f), Dt);
            character.Yaw.ShouldBe(1.5f, $"tick {i + 1}");
        }

        character.TryTakeTeleport(out float? taken).ShouldBeTrue();
        taken.ShouldBe(1.5f);

        // From here the commands are the view's again.
        character.Tick(Facing(1.5f), Dt);
        character.Yaw.ShouldBe(1.5f);
        character.Tick(Facing(0.9f), Dt);
        character.Yaw.ShouldBe(0.9f);
    }

    [Fact]
    public void A_teleport_that_names_no_facing_leaves_the_facing_to_the_commands()
    {
        CharacterSimulation character = Spawned();
        character.Tick(Facing(0.2f), Dt);

        character.Teleport(Elsewhere);
        character.Yaw.ShouldBe(0.2f);

        character.Tick(Facing(0.7f), Dt);
        character.Yaw.ShouldBe(0.7f);
    }

    [Fact]
    public void A_second_teleport_with_no_facing_keeps_the_facing_the_first_one_named()
    {
        CharacterSimulation character = Spawned();
        character.Tick(Facing(0.2f), Dt);

        character.Teleport(Elsewhere, yaw: 0.75f);
        character.Tick(Facing(0.2f), Dt);
        character.Teleport(Elsewhere + Vector3.UnitX);

        // What a script reads is what the view is about to turn to.
        character.Yaw.ShouldBe(0.75f);
        character.TryTakeTeleport(out float? taken).ShouldBeTrue();
        taken.ShouldBe(0.75f);
    }

    [Fact]
    public void A_fall_out_of_the_level_lets_go_of_a_facing_a_teleport_named()
    {
        CharacterSimulation character = Spawned();
        character.SpawnYaw = 0.3f;
        character.FallOutHeight = -5f;

        character.Teleport(new Vector3(100f, -4f, 0f), yaw: 1.5f);
        bool fellOut = false;
        for (int i = 0; i < 120 && !fellOut; i++)
            fellOut = character.Tick(Facing(1.5f), Dt);

        fellOut.ShouldBeTrue();
        character.Yaw.ShouldBe(0.3f);

        character.Tick(Facing(0.3f), Dt);
        character.Tick(Facing(-0.6f), Dt);
        character.Yaw.ShouldBe(-0.6f);
    }

    private static CharacterCommand Facing(float yaw) => new() { Yaw = yaw };

    private static CharacterSimulation Spawned()
    {
        var scene = new Scene("Yaw");
        SceneNode floor = scene.Root.CreateChild("floor");
        floor.LocalPosition = new Vector3(0f, -0.5f, 0f);
        floor.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(
            new Vector3(-16f, -0.5f, -16f), new Vector3(16f, 0.5f, 16f));
        scene.RebuildStaticWorld(new FakeRenderer());

        var character = new CharacterSimulation(scene) { SpawnPosition = new Vector3(0f, 0.05f, 0f) };
        character.Spawn();
        return character;
    }
}
