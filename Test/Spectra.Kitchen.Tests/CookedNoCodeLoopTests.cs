using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using SpectraEngine.Entities;
using SpectraEngine.Entities.Tests;
using System;
using System.Linq;
using System.Numerics;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The no-code level cooked and loaded the way a game loads it. The trigger,
/// the door and the wire between them come through the cook, and the level
/// plays as the authored one does.
/// </summary>
public class CookedNoCodeLoopTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    // The same marks NoCodeLoopTests walks to.
    private const float DoorFront = 3.8f;
    private const float PastTheDoor = 5f;

    [Fact]
    public void Walking_into_the_trigger_opens_the_door_in_a_cooked_level()
    {
        using CookedLevel level = CookedLevel.Bake(Authored());

        Outcome cooked = Play(level.Scene);

        cooked.DoorIsOpen.ShouldBeTrue();
        cooked.TriggerCount.ShouldBe(1);
        cooked.Feet.Z.ShouldBeGreaterThan(PastTheDoor);
    }

    [Fact]
    public void A_cooked_level_plays_tick_for_tick_as_the_authored_one()
    {
        using CookedLevel level = CookedLevel.Bake(Authored());

        Outcome cooked = Play(level.Scene);
        Outcome authored = Play(level.Authored);

        cooked.Ticks.ShouldBe(authored.Ticks);
        cooked.Feet.ShouldBe(authored.Feet);
        cooked.DoorHeight.ShouldBe(authored.DoorHeight);
    }

    [Fact]
    public void A_cooked_door_nothing_opens_stops_the_player()
    {
        using CookedLevel level = CookedLevel.Bake(Authored());
        Named(level.Scene, "Zone").Entity!.SetValue("startdisabled", "1");

        Outcome cooked = Play(level.Scene);

        cooked.DoorIsOpen.ShouldBeFalse();
        float reach = DoorFront - new CharacterTuning().Radius;
        cooked.Feet.Z.ShouldBeInRange(reach - 0.05f, reach + 0.01f);
    }

    private static Scene Authored()
    {
        var scene = new Scene("NoCodeLoop");
        MapSceneBinder.ApplyTo(MapReader.Read(NoCodeLoopLevel.Utf8()), scene);
        return scene;
    }

    // Entities, then the character, as a play session orders a tick. Holds
    // forward down the corridor until the player is past the door, or for
    // four seconds.
    private static Outcome Play(Scene scene)
    {
        // Own catalogue: EntityCatalog.Shared freezes on first read.
        var catalog = new EntityCatalog();
        catalog.Add(FuncDoor.SpectraSchema, static () => new FuncDoor());
        catalog.Add(TriggerOnce.SpectraSchema, static () => new TriggerOnce());
        catalog.Add(InfoPlayerStart.SpectraSchema, static () => new InfoPlayerStart());

        var world = new EntityWorld(scene, NullLogger.Instance, catalog);
        world.Activate();

        // The start faces +Z, a quarter turn from the mover's zero.
        const float Facing = MathF.PI / 2f;
        var character = new CharacterSimulation(scene)
        {
            SpawnPosition = Named(scene, "Start").WorldPosition + new Vector3(0f, 0.05f, 0f),
            SpawnYaw = Facing,
        };
        character.Spawn();
        world.Player = character;

        var forward = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Yaw = Facing };
        int ticks = 0;
        while (ticks < 240 && character.State.Position.Z <= PastTheDoor)
        {
            world.Tick(Dt);
            character.Tick(in forward, Dt);
            ticks++;
        }

        SceneNode door = Named(scene, "Door");
        var outcome = new Outcome(
            ticks,
            character.State.Position,
            door.LocalPosition.Y,
            Live<FuncDoor>(world, door).IsFullyOpen,
            Live<TriggerOnce>(world, Named(scene, "Zone")).TriggerCount);

        world.Deactivate();
        return outcome;
    }

    private static T Live<T>(EntityWorld world, SceneNode node)
        where T : Entity
    {
        world.Index.ShouldNotBeNull().TryGetByNodeId(node.Id, out Entity? entity).ShouldBeTrue();
        return entity.ShouldBeOfType<T>();
    }

    private static SceneNode Named(Scene scene, string name) =>
        scene.Root.Children.Single(node => node.Name == name);

    private readonly record struct Outcome(
        int Ticks, Vector3 Feet, float DoorHeight, bool DoorIsOpen, int TriggerCount);
}
