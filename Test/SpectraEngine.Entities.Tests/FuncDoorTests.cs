using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// A door run through a real <see cref="EntityWorld"/>: what it fires, when it
/// arrives, and that the level is as authored once it stops.
/// </summary>
public sealed class FuncDoorTests
{
    // The fixture door is 2 high: 1.95 of travel at 2 a second.
    private const int TravelTicks = 59;

    private const string OnOpen = "sink:OnOpen:";
    private const string OnClose = "sink:OnClose:";
    private const string OnFullyOpen = "sink:OnFullyOpen:";
    private const string OnFullyClosed = "sink:OnFullyClosed:";

    [Fact]
    public void Open_fires_OnOpen_once_and_OnFullyOpen_once()
    {
        Rig rig = Start(door => door.SetValue("wait", "-1"));

        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks + 30);

        rig.Log.ShouldBe([OnOpen, OnFullyOpen]);
        rig.Door.IsFullyOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_door_with_no_keyvalues_slides_up_by_its_height_less_the_lip()
    {
        Rig rig = Start();
        Vector3 closed = rig.Node.LocalPosition;

        rig.Door.TravelTicks.ShouldBe(TravelTicks);
        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks);

        Vector3.Distance(rig.Node.LocalPosition, closed + new Vector3(0f, 1.95f, 0f)).ShouldBeLessThan(1e-5f);
    }

    [Fact]
    public void The_door_is_open_on_the_tick_its_travel_predicts()
    {
        Rig rig = Start(door => door.SetValue("wait", "-1"));

        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks - 1);
        rig.Door.IsFullyOpen.ShouldBeFalse();

        rig.World.Tick(Movers.Dt);

        rig.Door.IsFullyOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_stated_distance_is_used_as_it_is()
    {
        Rig rig = Start(door => door.SetValue("distance", "3"));
        Vector3 closed = rig.Node.LocalPosition;

        rig.Door.TravelTicks.ShouldBe(90);
        rig.Send("Open");
        Movers.Run(rig.World, 90);

        rig.Node.LocalPosition.ShouldBe(closed + new Vector3(0f, 3f, 0f));
    }

    [Theory]
    [InlineData("0.5", 30)]
    [InlineData("4", 240)]
    [InlineData("0", 1)]
    public void The_door_stays_open_for_its_wait_counted_in_ticks(string wait, int waitTicks)
    {
        Rig rig = Start(door => door.SetValue("wait", wait));

        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks + waitTicks - 1);
        rig.Door.IsFullyOpen.ShouldBeTrue();

        rig.World.Tick(Movers.Dt);

        rig.Door.TicksTravelled.ShouldBe(TravelTicks - 1);
    }

    [Fact]
    public void After_its_wait_the_door_closes_itself_onto_the_authored_transform()
    {
        Rig rig = Start(door => door.SetValue("wait", "0.5"));

        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks + 30 + TravelTicks - 1);
        rig.Door.IsFullyClosed.ShouldBeTrue();
        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);

        // The last output is delivered a tick after it is fired.
        rig.World.Tick(Movers.Dt);
        rig.Log.ShouldBe([OnOpen, OnFullyOpen, OnClose, OnFullyClosed]);

        Movers.Run(rig.World, 600);
        rig.Log.Count.ShouldBe(4);
        rig.World.TickingEntityCount.ShouldBe(0);
    }

    [Fact]
    public void A_wait_of_minus_one_stays_open()
    {
        Rig rig = Start(door => door.SetValue("wait", "-1"));

        rig.Send("Open");
        Movers.Run(rig.World, 2000);

        rig.Door.IsFullyOpen.ShouldBeTrue();
        rig.Log.ShouldBe([OnOpen, OnFullyOpen]);
        rig.World.TickingEntityCount.ShouldBe(0);
    }

    [Fact]
    public void Open_while_opening_does_nothing()
    {
        Rig rig = Start(door => door.SetValue("wait", "-1"));

        rig.Send("Open");
        Movers.Run(rig.World, 20);
        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks - 20);

        rig.Door.IsFullyOpen.ShouldBeTrue();
        Movers.Run(rig.World, 2);
        rig.Log.ShouldBe([OnOpen, OnFullyOpen]);
    }

    [Fact]
    public void Open_while_open_does_not_restart_the_wait()
    {
        Rig rig = Start(door => door.SetValue("wait", "0.5"));

        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks + 10);
        rig.Send("Open");
        Movers.Run(rig.World, 20);

        rig.Door.TicksTravelled.ShouldBe(TravelTicks - 1);
    }

    [Fact]
    public void Close_while_opening_turns_the_door_round_where_it_is()
    {
        Rig rig = Start();

        rig.Send("Open");
        Movers.Run(rig.World, 20);
        rig.Door.TicksTravelled.ShouldBe(20);

        rig.Send("Close");
        Movers.Run(rig.World, 19);
        rig.Door.IsFullyClosed.ShouldBeFalse();
        rig.World.Tick(Movers.Dt);

        rig.Door.IsFullyClosed.ShouldBeTrue();
        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
        rig.World.Tick(Movers.Dt);
        rig.Log.ShouldBe([OnOpen, OnClose, OnFullyClosed]);
    }

    [Fact]
    public void Open_while_closing_turns_the_door_round_too()
    {
        Rig rig = Start(door => door.SetValue("wait", "-1"));

        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks);
        rig.Send("Close");
        Movers.Run(rig.World, 15);
        rig.Send("Open");
        Movers.Run(rig.World, 15);

        rig.Door.IsFullyOpen.ShouldBeTrue();
        rig.World.Tick(Movers.Dt);
        rig.Log.ShouldBe([OnOpen, OnFullyOpen, OnClose, OnOpen, OnFullyOpen]);
    }

    [Fact]
    public void Close_cuts_the_wait_short()
    {
        Rig rig = Start(door => door.SetValue("wait", "4"));

        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks + 5);
        rig.Send("Close");
        Movers.Run(rig.World, TravelTicks);

        rig.Door.IsFullyClosed.ShouldBeTrue();
        Movers.Run(rig.World, 600);
        rig.Log.ShouldBe([OnOpen, OnFullyOpen, OnClose, OnFullyClosed]);
    }

    [Fact]
    public void Close_on_a_closed_door_does_nothing()
    {
        Rig rig = Start();

        rig.Send("Close");
        Movers.Run(rig.World, 5);

        rig.Log.ShouldBeEmpty();
        rig.World.MovedNodeCount.ShouldBe(0);
    }

    [Fact]
    public void Toggle_opens_a_closed_door_and_closes_one_that_is_open_or_opening()
    {
        Rig rig = Start(door => door.SetValue("wait", "-1"));

        rig.Send("Toggle");
        Movers.Run(rig.World, 10);
        rig.Door.TicksTravelled.ShouldBe(10);

        rig.Send("Toggle");
        Movers.Run(rig.World, 10);
        rig.Door.IsFullyClosed.ShouldBeTrue();

        rig.Send("Toggle");
        Movers.Run(rig.World, TravelTicks);
        rig.Door.IsFullyOpen.ShouldBeTrue();

        rig.Send("Toggle");
        Movers.Run(rig.World, TravelTicks);
        rig.Door.IsFullyClosed.ShouldBeTrue();
    }

    [Fact]
    public void Open_and_Close_on_one_tick_leave_the_door_shut_and_at_rest()
    {
        Rig rig = Start();

        rig.Send("Open");
        rig.Send("Close");
        Movers.Run(rig.World, 3);

        rig.Door.IsFullyClosed.ShouldBeTrue();
        rig.Log.ShouldBe([OnOpen, OnClose]);
        rig.World.TickingEntityCount.ShouldBe(0);
    }

    [Fact]
    public void An_output_carries_the_activator_of_the_input_that_caused_it()
    {
        var log = new List<string>();
        var scene = new Scene("Door");
        SceneNode door = Movers.Part(scene.Root, "door", new Vector3(1f, 2f, 0.2f), Vector3.Zero);
        door.Entity = new EntityData("func_door");
        SceneNode pusher = EntityRuntime.Place(scene.Root, "pusher", "test_recorder");
        EntityRuntime.Wire(door, FuncDoor.OnOpen, "!activator", "Opening");
        EntityRuntime.Wire(door, FuncDoor.OnFullyOpen, "!activator", "Arrived");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        world.QueueInput(
            EntityRuntime.Live<FuncDoor>(world, door), "Open",
            activator: EntityRuntime.Live<RecordingEntity>(world, pusher));
        Movers.Run(world, TravelTicks + 1);

        // The arrival starts with the door, which records nothing.
        log.ShouldBe(["pusher:Opening:"]);
    }

    [Fact]
    public void A_door_that_starts_open_is_open_before_the_first_tick_and_fires_nothing()
    {
        Rig rig = Start(door =>
        {
            door.SetValue("startopen", "1");
            door.SetValue("wait", "0.5");
        });

        rig.Door.IsFullyOpen.ShouldBeTrue();
        Movers.Bits(rig.Node.LocalTransform).ShouldNotBe(rig.Authored);

        // Longer than the wait: it runs from an arrival, and there was none.
        Movers.Run(rig.World, 120);
        rig.Door.IsFullyOpen.ShouldBeTrue();
        rig.Log.ShouldBeEmpty();

        rig.Send("Close");
        Movers.Run(rig.World, TravelTicks);
        rig.Door.IsFullyClosed.ShouldBeTrue();
        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
    }

    [Fact]
    public void Stopping_while_a_door_that_starts_open_is_open_restores_the_authored_transform()
    {
        Rig rig = Start(door => door.SetValue("startopen", "1"));

        rig.World.Deactivate();

        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
    }

    [Theory]
    [InlineData("0 0 0")]
    [InlineData("sideways")]
    public void A_zero_or_unreadable_movedir_is_refused_and_up_is_used(string movedir)
    {
        Rig rig = Start(door => door.SetValue("movedir", movedir));
        Vector3 closed = rig.Node.LocalPosition;

        rig.Logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem().ShouldContain($"'movedir' = '{movedir}'");

        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks);

        Vector3.Distance(rig.Node.LocalPosition, closed + new Vector3(0f, 1.95f, 0f)).ShouldBeLessThan(1e-5f);
    }

    [Fact]
    public void Movedir_is_in_the_doors_own_axes_and_need_not_be_unit_length()
    {
        // A quarter turn about Y takes the door's +X to the parent's -Z.
        Rig rig = Start(
            door =>
            {
                door.SetValue("movedir", "5 0 0");
                door.SetValue("distance", "2");
            },
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f));
        Vector3 closed = rig.Node.LocalPosition;

        rig.Send("Open");
        Movers.Run(rig.World, 60);

        Vector3.Distance(rig.Node.LocalPosition, closed + new Vector3(0f, 0f, -2f)).ShouldBeLessThan(1e-5f);
    }

    [Fact]
    public void The_distance_from_size_spans_the_brushes_below_the_door_too()
    {
        var scene = new Scene("Door");
        SceneNode door = Movers.Part(scene.Root, "door", new Vector3(1f, 2f, 0.2f), Vector3.Zero);
        Movers.Part(door, "window", new Vector3(0.5f, 1f, 0.2f), new Vector3(0f, 1.5f, 0f));
        door.Entity = new EntityData("func_door");
        door.Entity.SetValue("lip", "0");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();

        // -1 to 2 at 2 a second.
        EntityRuntime.Live<FuncDoor>(world, door).TravelTicks.ShouldBe(90);
    }

    [Fact]
    public void A_door_with_a_world_brush_stays_shut_and_comes_to_rest()
    {
        var logger = new CapturingLogger();
        var scene = new Scene("Door");
        SceneNode door = scene.Root.CreateChild("door");
        door.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(new Vector3(-0.5f, -1f, -0.1f), new Vector3(0.5f, 1f, 0.1f));
        door.Entity = new EntityData("func_door");
        byte[] authored = Movers.Bits(door.LocalTransform);

        var world = new EntityWorld(scene, logger, EntityRuntime.Catalog([]));
        world.Activate();
        world.QueueInput(EntityRuntime.Live<FuncDoor>(world, door), "Open");
        Movers.Run(world, 10);

        Movers.Bits(door.LocalTransform).ShouldBe(authored);
        world.RefusedMoveCount.ShouldBe(1);
        world.TickingEntityCount.ShouldBe(0);
        logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem().ShouldContain("'door'");
    }

    [Fact]
    public void The_static_world_stays_clean_for_the_whole_run()
    {
        Rig rig = Start(door => door.SetValue("wait", "0.5"));
        rig.Scene.StaticWorldDirty.ShouldBeFalse();

        rig.Send("Open");
        for (int tick = 1; tick <= TravelTicks + 30 + TravelTicks + 10; tick++)
        {
            rig.World.Tick(Movers.Dt);
            rig.Scene.StaticWorldDirty.ShouldBeFalse($"tick {tick}");
        }

        rig.Door.IsFullyClosed.ShouldBeTrue();
        rig.Log.Count.ShouldBe(4);
        rig.World.RefusedMoveCount.ShouldBe(0);
        rig.Scene.StaticWorldCompileCount.ShouldBe(0);

        rig.World.Deactivate();
        rig.Scene.StaticWorldDirty.ShouldBeFalse();
    }

    [Fact]
    public void The_schema_describes_what_the_class_declares()
    {
        EntitySchema schema = FuncDoor.SpectraSchema;

        schema.ClassName.ShouldBe("func_door");
        schema.DisplayName.ShouldBe("Door");
        schema.Group.ShouldBe("Movers");
        schema.Placement.ShouldBe(EntityPlacement.Brush);
        schema.Inputs.ShouldBe(["Open", "Close", "Toggle"]);
        schema.Outputs.ShouldBe(["OnOpen", "OnClose", "OnFullyOpen", "OnFullyClosed"]);

        schema.Keyvalues.Select(keyvalue => keyvalue.Name)
            .ShouldBe(["movedir", "distance", "lip", "speed", "wait", "startopen"]);
        schema.Keyvalues.Select(keyvalue => keyvalue.Default)
            .ShouldBe(["0 1 0", "0", "0.05", "2", "4", "0"]);
        schema.Keyvalues.Select(keyvalue => keyvalue.Type).ShouldBe(
        [
            KeyvalueType.Vec3, KeyvalueType.Float, KeyvalueType.Float,
            KeyvalueType.Float, KeyvalueType.Float, KeyvalueType.Bool,
        ]);
        schema.Keyvalues[3].Min.ShouldBe(LinearMover.MinimumSpeed);
    }

    [Fact]
    public void The_schema_defaults_are_the_values_a_bare_door_has()
    {
        var bare = new FuncDoor();
        var parsed = new FuncDoor();
        foreach (KeyvalueDescriptor keyvalue in FuncDoor.SpectraSchema.Keyvalues)
            parsed.ParseKeyValue(keyvalue.Name, keyvalue.Default).ShouldBeTrue(keyvalue.Name);

        parsed.MoveDirection.ShouldBe(bare.MoveDirection);
        parsed.Distance.ShouldBe(bare.Distance);
        parsed.Lip.ShouldBe(bare.Lip);
        parsed.Speed.ShouldBe(bare.Speed);
        parsed.Wait.ShouldBe(bare.Wait);
        parsed.StartOpen.ShouldBe(bare.StartOpen);
    }

    private sealed record Rig(
        Scene Scene, SceneNode Node, EntityWorld World, FuncDoor Door,
        List<string> Log, CapturingLogger Logger, byte[] Authored)
    {
        public void Send(string input) => World.QueueInput(Door, input);
    }

    // One door, 1 by 2 by 0.2, every output wired to a recorder. Parts only,
    // so the scene starts with a clean static world.
    private static Rig Start(Action<EntityData>? author = null, Quaternion? rotation = null)
    {
        var log = new List<string>();
        var logger = new CapturingLogger();
        var scene = new Scene("Door");

        SceneNode node = Movers.Part(scene.Root, "door", new Vector3(1f, 2f, 0.2f), new Vector3(3.3f, 1.1f, -2.7f));
        node.LocalRotation = rotation ?? Quaternion.Identity;
        node.Entity = new EntityData("func_door");
        author?.Invoke(node.Entity);
        Movers.WireToSink(node, FuncDoor.OnOpen, FuncDoor.OnClose, FuncDoor.OnFullyOpen, FuncDoor.OnFullyClosed);
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        byte[] authored = Movers.Bits(node.LocalTransform);

        var world = new EntityWorld(scene, logger, EntityRuntime.Catalog(log));
        world.Activate();
        return new Rig(scene, node, world, EntityRuntime.Live<FuncDoor>(world, node), log, logger, authored);
    }
}
