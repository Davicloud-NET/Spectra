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
