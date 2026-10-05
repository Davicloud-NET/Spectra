using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The demo level's start room, played with the real classes: the player
/// starts in it and the door lets them out onto the course.
/// </summary>
public sealed class DemoStartRoomTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    // The course side of the west wall, clear of the door's volume.
    private const float OnTheCourse = 133f;

    [Fact]
    public void Play_starts_in_the_start_room_facing_the_door()
    {
        SceneManager manager = Demo();
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);

        session.Enter();

        character.State.Position.X.ShouldBe(125f);
        character.State.Position.Z.ShouldBe(0f);
        character.SpawnYaw.ShouldBe(0f, 1e-5f);

        // Standing there sets nothing off.
        for (int i = 0; i < 120; i++)
            session.Tick(Dt, default);

        Door(manager).IsFullyClosed.ShouldBeTrue();
        character.State.Grounded.ShouldBeTrue();
    }

    [Fact]
    public void Walking_at_the_door_opens_it_and_the_course_is_on_the_other_side()
    {
        SceneManager manager = Demo();
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);
        session.Enter();

        int ticks = WalkEast(session, character);

        character.State.Position.X.ShouldBeGreaterThan(OnTheCourse, $"stopped after {ticks} ticks");
        character.State.Grounded.ShouldBeTrue();
        Math.Abs(character.State.Position.Z).ShouldBeLessThan(0.01f);
    }

    [Fact]
    public void The_door_shuts_behind_the_player_and_opens_again_on_the_way_back()
    {
        SceneManager manager = Demo();
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);
        session.Enter();
        WalkEast(session, character);
        FuncDoor door = Door(manager);

        // Out of the volume: the door waits three seconds, then closes.
        for (int i = 0; i < 360; i++)
            session.Tick(Dt, default);
        door.IsFullyClosed.ShouldBeTrue();

        var west = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Yaw = MathF.PI };
        for (int i = 0; i < 300 && character.State.Position.X > 128f; i++)
            session.Tick(Dt, in west);

        character.State.Position.X.ShouldBeLessThanOrEqualTo(128f);
    }

    [Fact]
    public void The_button_sends_the_lift_up_with_the_player_on_it_and_it_comes_back_down()
    {
        SceneManager manager = Demo();
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);
        session.Enter();
        SceneNode liftNode = scene.Root.Children.Single(node => node.Name == DemoPlayArea.LiftName);
        FuncMoveLinear lift = EntityRuntime.Live<FuncMoveLinear>(manager.EntityWorld.ShouldNotBeNull(), liftNode);

        // Onto the middle of the lift, whose top is 0.3 up.
        character.Teleport(new Vector3(123f, 0.35f, -3f));
        Idle(session, 30);
        character.State.Grounded.ShouldBeTrue();
        character.State.GroundNodeId.ShouldBe(liftNode.Id);

        // The button is on the west wall, to the south of the lift.
        var press = new CharacterCommand
        {
            Yaw = MathF.Atan2(1.5f, -0.85f),
            Buttons = CharacterButtons.Use,
        };
        session.Tick(Dt, in press);
        Idle(session, lift.TravelTicks + 5);

        lift.TicksTravelled.ShouldBe(lift.TravelTicks);
        liftNode.LocalPosition.Y.ShouldBe(2.35f, 1e-4f);
        character.State.Grounded.ShouldBeTrue();

        // Level with the tops of the walls, which are 2.5 high.
        character.State.Position.Y.ShouldBe(2.5f, 0.05f);

        // Three seconds up there, then the way down.
        Idle(session, 180 + lift.TravelTicks + 5);

        lift.TicksTravelled.ShouldBe(0);
        character.State.Grounded.ShouldBeTrue();
        character.State.Position.Y.ShouldBe(0.3f, 0.05f);
    }

    private static void Idle(PlaySession session, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            session.Tick(Dt, default);
    }

    private static int WalkEast(PlaySession session, CharacterSimulation character)
    {
        var east = new CharacterCommand { MoveForward = CharacterCommand.Axis(1f), Yaw = 0f };

        int ticks = 0;
        while (ticks < 300 && character.State.Position.X <= OnTheCourse)
        {
            session.Tick(Dt, in east);
            ticks++;
        }

        return ticks;
    }

    private static FuncDoor Door(SceneManager manager)
    {
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        SceneNode door = scene.Root.Children.Single(node => node.Name == DemoPlayArea.StartDoorName);
        return EntityRuntime.Live<FuncDoor>(manager.EntityWorld.ShouldNotBeNull(), door);
    }

    // The course alone, without the rest of the demo scene. The baseplate is
    // only there to give the manager a scene.
    private static SceneManager Demo()
    {
        var renderer = new FakeRenderer();
        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = EntityRuntime.Catalog([]),
        };

        manager.LoadStartupScene(renderer, new AssetManager(NullLogger<AssetManager>.Instance));
        Scene scene = manager.ActiveScene.ShouldNotBeNull();

        for (int i = scene.Root.Children.Count - 1; i >= 0; i--)
            scene.Root.RemoveChild(scene.Root.Children[i]);

        DemoPlayArea.Build(scene, MaterialRef.Default, MaterialRef.Default, MaterialRef.Default);
        scene.RebuildStaticWorld(renderer);
        return manager;
    }

    // No spawn: the session places it on every Enter.
    private static CharacterSimulation Walker(SceneManager manager) =>
        new(manager.ActiveScene.ShouldNotBeNull())
        {
            FallOutHeight = DemoPlayArea.FallOutHeight,
        };
}
