using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The character as the entity runtime sees it.</summary>
public sealed class CharacterPresenceTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    [Fact]
    public void A_character_is_present_once_it_has_spawned()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);

        presence.IsPresent.ShouldBeFalse();

        character.Spawn();

        presence.IsPresent.ShouldBeTrue();
    }

    [Fact]
    public void The_capsule_stands_on_the_feet()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        character.Spawn();
        Vector3 feet = character.State.Position;
        float radius = character.Tuning.Radius;

        CharacterCapsule capsule = presence.Capsule;

        capsule.Radius.ShouldBe(radius);
        capsule.Center1.ShouldBe(feet + new Vector3(0f, radius, 0f));
        capsule.Center2.ShouldBe(feet + new Vector3(0f, character.Tuning.StandHeight - radius, 0f));
    }

    [Fact]
    public void A_teleport_puts_the_character_at_rest_and_off_the_ground()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        character.Spawn();
        var walk = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Yaw = 0f };
        for (int i = 0; i < 30; i++)
            character.Tick(in walk, Dt);

        character.State.Grounded.ShouldBeTrue();
        character.State.Velocity.Length().ShouldBeGreaterThan(1f);

        var destination = new Vector3(-3f, 0.05f, 2f);
        presence.Teleport(destination);

        character.State.Position.ShouldBe(destination);
        character.State.Velocity.ShouldBe(Vector3.Zero);
        character.State.Grounded.ShouldBeFalse();
        character.State.GroundNodeId.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void A_teleport_keeps_held_buttons_so_a_held_jump_is_not_a_new_press()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        character.Spawn();
        var holdJump = new CharacterCommand { Buttons = CharacterButtons.Jump };
        for (int i = 0; i < 5; i++)
            character.Tick(in holdJump, Dt);

        presence.Teleport(new Vector3(1f, 0.05f, 1f));

        character.State.PrevButtons.ShouldBe(CharacterButtons.Jump);
    }

    [Fact]
    public void A_teleport_waits_for_a_view_to_take_it_once()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        character.Spawn();

        character.TryTakeTeleport(out _).ShouldBeFalse();

        presence.Teleport(new Vector3(1f, 0.05f, 1f), yaw: 1.5f);

        character.TryTakeTeleport(out float? yaw).ShouldBeTrue();
        yaw.ShouldBe(1.5f);
        character.TryTakeTeleport(out yaw).ShouldBeFalse();
        yaw.ShouldBeNull();
    }

    [Fact]
    public void A_teleport_with_no_yaw_keeps_the_facing_an_earlier_one_asked_for()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        character.Spawn();

        presence.Teleport(new Vector3(1f, 0.05f, 1f), yaw: 0.75f);
        presence.Teleport(new Vector3(2f, 0.05f, 2f));

        character.TryTakeTeleport(out float? yaw).ShouldBeTrue();
        yaw.ShouldBe(0.75f);
    }

    [Fact]
    public void A_spawn_drops_a_teleport_no_view_took()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        character.Spawn();
        presence.Teleport(new Vector3(1f, 0.05f, 1f), yaw: 1.5f);

        character.Spawn();

        character.TryTakeTeleport(out float? yaw).ShouldBeFalse();
        yaw.ShouldBeNull();
    }

    [Fact]
    public void The_character_faces_as_its_spawn_does_until_a_command_turns_it()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        character.SpawnYaw = 1.25f;
        character.Spawn();

        presence.State.Yaw.ShouldBe(1.25f);

        var turned = new CharacterCommand { Yaw = -0.5f };
        character.Tick(in turned, Dt);

        presence.State.Yaw.ShouldBe(-0.5f);
    }

    [Fact]
    public void A_teleport_that_names_a_facing_turns_the_character_and_one_that_names_none_does_not()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        character.SpawnYaw = 1.25f;
        character.Spawn();

        presence.Teleport(new Vector3(1f, 0.05f, 1f));
        presence.State.Yaw.ShouldBe(1.25f);

        presence.Teleport(new Vector3(2f, 0.05f, 2f), yaw: 0.75f);
        presence.State.Yaw.ShouldBe(0.75f);
    }

    [Fact]
    public void The_state_is_the_characters_own()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        character.Spawn();
        var walk = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f) };
        for (int i = 0; i < 30; i++)
            character.Tick(in walk, Dt);

        presence.State.Position.ShouldBe(character.State.Position);
        presence.State.Velocity.ShouldBe(character.State.Velocity);
        presence.State.Grounded.ShouldBeTrue();
    }

    [Fact]
    public void What_an_entity_gets_of_the_player_does_not_hand_out_its_tuning()
    {
        // The tuning outlives the level. A write to it that records nothing
        // would still be there on the next Play.
        Type[] handedOut =
        [
            .. typeof(IPlayerPresence).GetProperties().Select(property => property.PropertyType),
            .. typeof(IPlayerPresence).GetMethods().Select(method => method.ReturnType),
        ];

        handedOut.ShouldContain(typeof(CharacterState), "the members of the presence were not found");
        handedOut.ShouldNotContain(typeof(CharacterTuning));
    }

    [Fact]
    public void A_respawn_puts_the_character_back_at_its_spawn_at_rest_and_facing_as_it_started()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        character.SpawnYaw = 1.25f;
        character.Spawn();
        var walk = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Yaw = 0.25f };
        for (int i = 0; i < 30; i++)
            character.Tick(in walk, Dt);

        presence.Respawn();

        character.State.Position.ShouldBe(character.SpawnPosition);
        character.State.Velocity.ShouldBe(Vector3.Zero);
        presence.State.Yaw.ShouldBe(1.25f);

        // A view snaps to it as to a teleport. It is not the fall-out guard.
        character.TryTakeTeleport(out float? yaw).ShouldBeTrue();
        yaw.ShouldBe(1.25f);
        character.Respawns.ShouldBe(0);
    }

    [Fact]
    public void A_velocity_that_points_up_lifts_a_standing_character_off_the_ground()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        Vector3 standing = Settle(character);
        const float Up = 6f;

        presence.SetVelocity(new Vector3(0f, Up, 0f));
        character.State.Grounded.ShouldBeFalse();

        float highest = standing.Y;
        int ticks = 0;
        for (; ticks < 240 && (ticks < 5 || !character.State.Grounded); ticks++)
        {
            character.Tick(default, Dt);
            highest = MathF.Max(highest, character.State.Position.Y);
        }

        // As high as a throw at that speed goes, and down again.
        float rise = Up * Up / (2f * MathF.Abs(PhysicsDefaults.Gravity.Y));
        (highest - standing.Y).ShouldBe(rise, 0.15f);
        character.State.Grounded.ShouldBeTrue();
        character.State.Position.Y.ShouldBe(standing.Y, 0.02f);
    }

    [Fact]
    public void A_velocity_along_the_ground_pushes_a_standing_character_and_leaves_it_standing()
    {
        IPlayerPresence presence = OnAFloor(out CharacterSimulation character);
        Vector3 standing = Settle(character);

        presence.SetVelocity(new Vector3(5f, 0f, 0f));
        character.State.Grounded.ShouldBeTrue();
        for (int i = 0; i < 60; i++)
            character.Tick(default, Dt);

        character.State.Grounded.ShouldBeTrue();
        character.State.Position.X.ShouldBeGreaterThan(standing.X + 0.2f);
        character.State.Position.Y.ShouldBe(standing.Y, 0.02f);
        character.State.Velocity.Length().ShouldBeLessThan(0.01f);
    }

    // Spawns the character and lets it land. Hands back where it stands.
    private static Vector3 Settle(CharacterSimulation character)
    {
        character.Spawn();
        for (int i = 0; i < 30; i++)
            character.Tick(default, Dt);

        character.State.Grounded.ShouldBeTrue();
        return character.State.Position;
    }

    private static IPlayerPresence OnAFloor(out CharacterSimulation character)
    {
        var scene = new Scene("Presence");
        SceneNode floor = scene.Root.CreateChild("floor");
        floor.LocalPosition = new Vector3(0f, -0.5f, 0f);
        floor.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(
            new Vector3(-16f, -0.5f, -16f), new Vector3(16f, 0.5f, 16f));
        scene.RebuildStaticWorld(new FakeRenderer());

        character = new CharacterSimulation(scene) { SpawnPosition = new Vector3(0f, 0.05f, 0f) };
        return character;
    }
}
