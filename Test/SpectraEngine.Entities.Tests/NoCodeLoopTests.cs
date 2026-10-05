using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;
using System;
using System.Text;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// A level made with no code, played from its file: a start, a trigger, a door
/// and one wire between them. The player walks in and the door lets them
/// through.
/// </summary>
public sealed class NoCodeLoopTests
{
    private const float Dt = PhysicsDefaults.FixedDeltaTime;

    // The door's front face, and how far along a player is once through it.
    private const float DoorFront = 3.8f;
    private const float PastTheDoor = 5f;

    [Fact]
    public void Walking_into_the_trigger_opens_the_door_and_the_player_goes_through()
    {
        SceneManager manager = Loaded();
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        SceneNode door = Named(scene, "Door");
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);

        session.Enter();
        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();
        FuncDoor live = EntityRuntime.Live<FuncDoor>(world, door);

        // On the start, facing down the corridor.
        character.State.Position.X.ShouldBe(0f);
        character.State.Position.Z.ShouldBe(-4f);
        character.SpawnYaw.ShouldBe(MathF.PI / 2f, 1e-5f);

        Walk(session, character);

        live.IsFullyOpen.ShouldBeTrue();
        door.LocalPosition.Y.ShouldBe(3f, 1e-5f);
        EntityRuntime.Live<TriggerOnce>(world, Named(scene, "Zone")).TriggerCount.ShouldBe(1);
        character.State.Position.Z.ShouldBeGreaterThan(PastTheDoor);
        character.State.Grounded.ShouldBeTrue();
    }

    [Fact]
    public void A_wiring_view_sees_the_trigger_fire_its_wire_to_the_door_once()
    {
        SceneManager manager = Loaded();
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        var activity = new WireActivityTrace();
        manager.EntityTrace = activity;
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);

        session.Enter();
        Walk(session, character);

        LogicPlayInfo play = activity.Capture(null, []);
        LogicWireActivity wire = play.Wires.ShouldHaveSingleItem();
        wire.NodeId.ShouldBe(Named(scene, "Zone").Id);
        wire.Wire.ShouldBe(0);
        wire.Fired.ShouldBe(1);
        wire.Missed.ShouldBe(0);

        LogicEventInfo sent = play.Recent.ShouldHaveSingleItem();
        sent.SourceName.ShouldBe("Zone");
        sent.Output.ShouldBe("OnTrigger");
        sent.TargetId.ShouldBe(Named(scene, "Door").Id);
        sent.Input.ShouldBe("Open");
    }

    [Fact]
    public void A_door_nothing_opens_stops_the_player()
    {
        SceneManager manager = Loaded();
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        Named(scene, "Zone").Entity!.SetValue("startdisabled", "1");
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);
        session.Enter();

        Walk(session, character);

        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();
        EntityRuntime.Live<FuncDoor>(world, Named(scene, "Door")).IsFullyClosed.ShouldBeTrue();
        float reach = DoorFront - character.Tuning.Radius;
        character.State.Position.Z.ShouldBeInRange(reach - 0.05f, reach + 0.01f);
    }

    [Fact]
    public void The_level_saves_the_same_bytes_once_it_stops()
    {
        byte[] source = NoCodeLoopLevel.Utf8();
        SceneManager manager = Loaded();
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        Same(source, MapWriter.Write(MapSceneBinder.FromScene(scene)));

        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);
        session.Enter();
        Walk(session, character);
        MapWriter.Write(MapSceneBinder.FromScene(scene)).ShouldNotBe(source, "the door is open now");

        session.Exit();

        Same(source, MapWriter.Write(MapSceneBinder.FromScene(scene)));
    }

    [Fact]
    public void A_second_play_runs_the_same_as_the_first()
    {
        SceneManager manager = Loaded();
        CharacterSimulation character = Walker(manager);
        var session = new PlaySession(manager, character);

        session.Enter();
        int first = Walk(session, character);
        CharacterState firstEnd = character.State;
        session.Exit();

        session.Enter();
        int second = Walk(session, character);

        second.ShouldBe(first);
        character.State.Position.ShouldBe(firstEnd.Position);
        character.State.Velocity.ShouldBe(firstEnd.Velocity);
    }

    // Holds forward down the corridor until the player is past the door, or
    // for four seconds. Returns the ticks it took.
    private static int Walk(PlaySession session, CharacterSimulation character)
    {
        var forward = new CharacterCommand
        {
            MoveForward = CharacterCommand.Axis(1f),
            Yaw = character.SpawnYaw,
        };

        int ticks = 0;
        while (ticks < 240 && character.State.Position.Z <= PastTheDoor)
        {
            session.Tick(Dt, in forward);
            ticks++;
        }

        return ticks;
    }

    // The baseplate is only there to give the manager a scene. The level
    // replaces all of it.
    private static SceneManager Loaded()
    {
        BuiltinEntities.EnsureRegistered();
        var renderer = new FakeRenderer();
        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = EntityRuntime.Catalog([]),
        };

        manager.LoadStartupScene(renderer, new AssetManager(NullLogger<AssetManager>.Instance));
        Scene scene = manager.ActiveScene.ShouldNotBeNull();
        MapSceneBinder.ApplyTo(MapReader.Read(NoCodeLoopLevel.Utf8()), scene);
        scene.RebuildStaticWorld(renderer);
        return manager;
    }

    // No spawn: the session places it on every Enter.
    private static CharacterSimulation Walker(SceneManager manager) =>
        new(manager.ActiveScene.ShouldNotBeNull())
        {
            FallOutHeight = manager.PlayerFallOutHeight,
        };

    private static SceneNode Named(Scene scene, string name) =>
        scene.Root.Children.Single(node => node.Name == name);

    private static void Same(byte[] expected, byte[] actual)
    {
        if (expected.AsSpan().SequenceEqual(actual))
            return;

        throw new Xunit.Sdk.XunitException(
            "The document changed on the way through.\n"
            + $"--- expected ---\n{Encoding.UTF8.GetString(expected)}\n"
            + $"--- actual ---\n{Encoding.UTF8.GetString(actual)}");
    }
}
