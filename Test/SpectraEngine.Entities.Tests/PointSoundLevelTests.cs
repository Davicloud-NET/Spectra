using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The sound entity among the other built-in classes, wired with no code: a
/// trigger plays it, its end and its markers move a door.
/// </summary>
public sealed class PointSoundLevelTests
{
    private const string Chime = "Sounds/chime.wav";
    private const string Line = "Sounds/line.wav";

    private static readonly Vector3 Inside = Vector3.Zero;
    private static readonly Vector3 Outside = new(6f, 0f, 0f);

    private readonly SoundRig _rig = new();
    private readonly FakePlayerPresence _player = new() { Feet = Outside };

    [Fact]
    public void A_trigger_plays_the_sound_and_the_end_of_the_sound_opens_the_door()
    {
        // Half a second of chime.
        _rig.Catalog.Add(Chime, new SoundDescription(24_000, SoundRig.Rate));
        SceneNode zone = Zone();
        SceneNode chime = _rig.SoundUnder(_rig.Scene.Root, "chime", Chime);
        SceneNode doorNode = Door();
        EntityRuntime.Wire(zone, TriggerOnce.OnTrigger, "chime", "Play");
        EntityRuntime.Wire(chime, PointSound.OnEnded, "door", "Open");

        EntityWorld world = _rig.Start();
        world.Player = _player;
        FuncDoor door = EntityRuntime.Live<FuncDoor>(world, doorNode);
        Movers.Run(world, 1);
        world.Sounds.Count.ShouldBe(0);

        // The player walks in on tick 2, and the trigger's wire is delivered
        // on the tick it fires.
        _player.Feet = Inside;
        Movers.Run(world, 1);
        world.Sounds.Count.ShouldBe(1);
        world.Sounds.Playing[0].Node.ShouldBeSameAs(chime);
        world.Sounds.Playing[0].StartTick.ShouldBe(2L);

        // 30 ticks of chime end on tick 32.
        Movers.Run(world, 30);
        world.Sounds.Count.ShouldBe(0);
        door.IsFullyClosed.ShouldBeTrue();

        Movers.Run(world, 1);
        door.TicksTravelled.ShouldBe(1);

        Movers.Run(world, door.TravelTicks);
        door.IsFullyOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_case_tells_two_markers_apart_and_the_one_named_now_opens_the_door()
    {
        _rig.Catalog.Add(
            Line,
            new SoundDescription(
                SoundRig.Rate, SoundRig.Rate, default, [new AudioMarker(12_000, "wait"), new AudioMarker(36_000, "now")]));
        SceneNode line = _rig.SoundUnder(_rig.Scene.Root, "line", Line, ("startplaying", "1"));
        SceneNode doorNode = Door();

        var pick = new EntityData("logic_case");
        pick.SetValue("case01", "now");
        SceneNode cases = _rig.Scene.Root.CreateChild("pick");
        cases.Entity = pick;

        // The wire carries no parameter. The marker's name takes its place.
        EntityRuntime.Wire(line, PointSound.OnMarker, "pick", "InValue");
        EntityRuntime.Wire(cases, LogicCase.OnCase01, "door", "Open");

        EntityWorld world = _rig.Start();
        FuncDoor door = EntityRuntime.Live<FuncDoor>(world, doorNode);

        // "wait" passes on tick 15 and "now" on tick 45.
        Movers.Run(world, 45);
        door.IsFullyClosed.ShouldBeTrue();

        Movers.Run(world, 1);
        door.TicksTravelled.ShouldBe(1);
    }

    [Fact]
    public void A_sound_placed_under_a_door_moves_with_the_door_and_goes_back_with_it()
    {
        SceneNode doorNode = Door();
        SceneNode hum = _rig.SoundUnder(doorNode, "hum", SoundRig.OneSecond, ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();
        FuncDoor door = EntityRuntime.Live<FuncDoor>(world, doorNode);
        Vector3 closed = world.Sounds.Playing[0].Node.WorldPosition;

        EntityRuntime.Send(door, "Open");
        Movers.Run(world, door.TravelTicks);

        // The door is 2 high and slides up by that, less its lip.
        SoundEmitter emitter = world.Sounds.Playing[0];
        emitter.Node.ShouldBeSameAs(hum);
        Vector3.Distance(emitter.Node.WorldPosition, closed + new Vector3(0f, 1.95f, 0f)).ShouldBeLessThan(1e-5f);

        world.Deactivate();
        hum.WorldPosition.ShouldBe(closed);
    }

    [Fact]
    public void A_sound_deleted_and_restored_while_it_plays_still_moves_with_its_door()
    {
        SceneNode doorNode = Door();
        SceneNode hum = _rig.SoundUnder(doorNode, "hum", SoundRig.OneSecond, ("startplaying", "1"), ("looped", "1"));
        EntityWorld world = _rig.Start();
        FuncDoor door = EntityRuntime.Live<FuncDoor>(world, doorNode);
        Vector3 closed = hum.WorldPosition;

        // What an undo of a delete does: a new node under the old id.
        doorNode.RemoveChild(hum);
        var restored = new SceneNode("hum", hum.Id) { Entity = hum.Entity };
        doorNode.AddChild(restored);

        EntityRuntime.Send(door, "Open");
        Movers.Run(world, door.TravelTicks);

        world.Sounds.Count.ShouldBe(1);
        SoundEmitter emitter = world.Sounds.Playing[0];
        emitter.Node.ShouldBeSameAs(restored);
        emitter.Node.ShouldBeSameAs(EntityRuntime.Live<PointSound>(world, restored).Node);
        Vector3.Distance(emitter.Node.WorldPosition, closed + new Vector3(0f, 1.95f, 0f)).ShouldBeLessThan(1e-5f);
    }

    [Fact]
    public void A_level_the_scene_manager_starts_has_its_sound_playing_from_the_spawn()
    {
        var manager = new SceneManager(NullLogger<SceneManager>.Instance)
        {
            Startup = StartupSceneKind.Baseplate,
            EntityCatalog = EntityRuntime.Catalog([]),
            SoundCatalog = _rig.Catalog,
        };
        manager.LoadStartupScene(new FakeRenderer(), new AssetManager(NullLogger<AssetManager>.Instance));
        SceneNode hum = _rig.SoundUnder(
            manager.ActiveScene.ShouldNotBeNull().Root, "hum", SoundRig.OneSecond, ("startplaying", "1"));

        manager.StartEntityWorld();

        EntityWorld world = manager.EntityWorld.ShouldNotBeNull();
        world.Sounds.Count.ShouldBe(1);
        world.Sounds.Playing[0].Node.ShouldBeSameAs(hum);
        world.Sounds.Playing[0].StartTick.ShouldBe(0L);

        manager.StopEntityWorld();
        world.Sounds.Count.ShouldBe(0);
    }

    [Fact]
    public void Playing_sounds_cost_the_runtime_no_allocation_per_tick()
    {
        // One loops and fires a marker at a relay four times a second. The
        // other plays itself again every time it ends.
        _rig.Catalog.Add(
            "Sounds/hum.wav",
            new SoundDescription(12_000, SoundRig.Rate, default, [new AudioMarker(6_000, "beat")]));
        _rig.Catalog.Add("Sounds/ping.wav", new SoundDescription(8_000, SoundRig.Rate));

        SceneNode hum = _rig.SoundUnder(
            _rig.Scene.Root, "hum", "Sounds/hum.wav", ("startplaying", "1"), ("looped", "1"));
        SceneNode ping = _rig.SoundUnder(_rig.Scene.Root, "ping", "Sounds/ping.wav", ("startplaying", "1"));
        SceneNode relayNode = EntityRuntime.Place(_rig.Scene.Root, "relay", "logic_relay");
        EntityRuntime.Wire(hum, PointSound.OnMarker, "relay", "Trigger");
        EntityRuntime.Wire(ping, PointSound.OnEnded, "ping", "Play");

        // No trace: the rig's keeps a string for every wire.
        var world = new EntityWorld(_rig.Scene, _rig.Logger, EntityRuntime.Catalog(_rig.Heard))
        {
            SoundCatalog = _rig.Catalog,
        };
        world.Activate();

        // Grows the event heap and the registry.
        Movers.Run(world, 400);

        // The least of several rounds: a one-off from the runtime is not a
        // cost per tick.
        long least = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            Movers.Run(world, 200);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);
        world.Sounds.Count.ShouldBe(2);
        EntityRuntime.Live<LogicRelay>(world, relayNode).TriggerCount.ShouldBeGreaterThan(20);
        world.Sounds.Playing[^1].StartTick.ShouldBeGreaterThan(1_300L);
    }

    // A 2 by 2 by 2 trigger at the origin, stamped the way the editor stamps one.
    private SceneNode Zone()
    {
        SceneNode node = _rig.Scene.Root.CreateChild("zone");
        node.LocalPosition = new Vector3(0f, 1f, 0f);
        node.Entity = new EntityData("trigger_once");

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        node.CanCollide = false;
        node.CanQuery = false;
        node.IsRendered = false;
        return node;
    }

    // 2 high, well clear of the trigger, and it stays open once it is.
    private SceneNode Door()
    {
        SceneNode node = Movers.Part(_rig.Scene.Root, "door", new Vector3(1f, 2f, 0.2f), new Vector3(0f, 1f, 8f));

        var data = new EntityData("func_door");
        data.SetValue("wait", "-1");
        node.Entity = data;
        return node;
    }
}
