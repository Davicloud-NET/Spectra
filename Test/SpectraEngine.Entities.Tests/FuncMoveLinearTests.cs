using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// A linear mover run through a real <see cref="EntityWorld"/>: where it goes
/// when it is sent, and what it fires on arriving.
/// </summary>
public sealed class FuncMoveLinearTests
{
    // The default: 4 units at 2 a second.
    private const int TravelTicks = 120;

    private const string OnFullyOpen = "sink:OnFullyOpen:";
    private const string OnFullyClosed = "sink:OnFullyClosed:";

    [Fact]
    public void Open_slides_the_brush_its_distance_and_fires_OnFullyOpen_once()
    {
        Rig rig = Start();
        Vector3 closed = rig.Node.LocalPosition;

        rig.Mover.TravelTicks.ShouldBe(TravelTicks);
        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks - 1);
        rig.Mover.TicksTravelled.ShouldBe(TravelTicks - 1);

        rig.World.Tick(Movers.Dt);
        rig.Node.LocalPosition.ShouldBe(closed + new Vector3(0f, 4f, 0f));

        Movers.Run(rig.World, 600);
        rig.Log.ShouldBe([OnFullyOpen]);
        rig.World.TickingEntityCount.ShouldBe(0);
    }

    [Fact]
    public void Close_brings_the_brush_back_to_the_authored_transform_bit_for_bit()
    {
        Rig rig = Start();

        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks);
        rig.Send("Close");
        Movers.Run(rig.World, TravelTicks);

        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
        rig.World.Tick(Movers.Dt);
        rig.Log.ShouldBe([OnFullyOpen, OnFullyClosed]);
    }

    [Fact]
    public void Open_while_opening_does_nothing()
    {
        Rig rig = Start();

        rig.Send("Open");
        Movers.Run(rig.World, 50);
        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks - 50);

        rig.Mover.TicksTravelled.ShouldBe(TravelTicks);
        rig.World.Tick(Movers.Dt);
        rig.Log.ShouldBe([OnFullyOpen]);
    }

    [Fact]
    public void Close_while_opening_turns_the_brush_round_where_it_is()
    {
        Rig rig = Start();

        rig.Send("Open");
        Movers.Run(rig.World, 50);
        rig.Send("Close");
        Movers.Run(rig.World, 50);

        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
        rig.World.Tick(Movers.Dt);
        rig.Log.ShouldBe([OnFullyClosed]);
    }

    [Fact]
    public void SetPosition_heads_for_a_fraction_of_the_way_and_fires_nothing_there()
    {
        Rig rig = Start();
        Vector3 closed = rig.Node.LocalPosition;

        rig.Send("SetPosition", "0.25");
        Movers.Run(rig.World, 200);

        rig.Mover.TicksTravelled.ShouldBe(30);
        Vector3.Distance(rig.Node.LocalPosition, closed + new Vector3(0f, 1f, 0f)).ShouldBeLessThan(1e-5f);
        rig.Log.ShouldBeEmpty();
        rig.World.TickingEntityCount.ShouldBe(0);
    }

    [Fact]
    public void SetPosition_at_either_end_arrives_as_Open_and_Close_do()
    {
        Rig rig = Start();

        rig.Send("SetPosition", "1");
        Movers.Run(rig.World, TravelTicks + 1);
        rig.Log.ShouldBe([OnFullyOpen]);

        rig.Send("SetPosition", "0");
        Movers.Run(rig.World, TravelTicks + 1);
        rig.Log.ShouldBe([OnFullyOpen, OnFullyClosed]);
        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
    }

    [Fact]
    public void SetPosition_to_where_the_brush_is_fires_nothing()
    {
        Rig rig = Start();

        rig.Send("SetPosition", "0");
        Movers.Run(rig.World, 5);

        rig.Log.ShouldBeEmpty();
        rig.World.MovedNodeCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("7", TravelTicks)]
    [InlineData("-3", 0)]
    public void A_position_outside_0_to_1_goes_to_the_nearer_end(string position, int ticks)
    {
        Rig rig = Start(mover => mover.SetValue("startposition", "0.5"));

        rig.Send("SetPosition", position);
        Movers.Run(rig.World, TravelTicks);

        rig.Mover.TicksTravelled.ShouldBe(ticks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("half")]
    public void A_position_that_is_not_a_number_leaves_the_brush_on_its_course(string position)
    {
        Rig rig = Start();

        rig.Send("Open");
        Movers.Run(rig.World, 10);
        rig.Send("SetPosition", position);
        Movers.Run(rig.World, TravelTicks - 10);

        rig.Mover.RefusedInputCount.ShouldBe(1);
        rig.Mover.TicksTravelled.ShouldBe(TravelTicks);
    }

    [Fact]
    public void A_start_position_places_the_brush_before_the_first_tick_and_fires_nothing()
    {
        Rig rig = Start(mover => mover.SetValue("startposition", "0.5"));
        Vector3 start = rig.Node.LocalPosition;

        rig.Mover.TicksTravelled.ShouldBe(60);
        Movers.Run(rig.World, 5);
        rig.Node.LocalPosition.ShouldBe(start);
        rig.Log.ShouldBeEmpty();

        rig.Send("Close");
        Movers.Run(rig.World, 60);
        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
    }

    [Fact]
    public void Stopping_puts_a_brush_with_a_start_position_back_where_it_was_authored()
    {
        Rig rig = Start(mover => mover.SetValue("startposition", "1"));
        Movers.Bits(rig.Node.LocalTransform).ShouldNotBe(rig.Authored);

        rig.World.Deactivate();

        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
    }

    [Fact]
    public void Movedir_is_in_the_nodes_own_axes()
    {
        // A quarter turn about Y takes the node's +X to the parent's -Z.
        Rig rig = Start(
            mover => mover.SetValue("movedir", "1 0 0"),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f));
        Vector3 closed = rig.Node.LocalPosition;

        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks);

        Vector3.Distance(rig.Node.LocalPosition, closed + new Vector3(0f, 0f, -4f)).ShouldBeLessThan(1e-5f);
    }

    [Fact]
    public void A_zero_movedir_is_refused_and_up_is_used()
    {
        Rig rig = Start(mover => mover.SetValue("movedir", "0 0 0"));
        Vector3 closed = rig.Node.LocalPosition;

        rig.Logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem().ShouldContain("'movedir' = '0 0 0'");

        rig.Send("Open");
        Movers.Run(rig.World, TravelTicks);

        rig.Node.LocalPosition.ShouldBe(closed + new Vector3(0f, 4f, 0f));
    }

    [Fact]
    public void The_static_world_stays_clean_while_the_brush_travels()
    {
        Rig rig = Start();

        rig.Send("Open");
        for (int tick = 1; tick <= TravelTicks; tick++)
        {
            rig.World.Tick(Movers.Dt);
            rig.Scene.StaticWorldDirty.ShouldBeFalse($"tick {tick}");
        }

        rig.World.RefusedMoveCount.ShouldBe(0);
        rig.Scene.StaticWorldCompileCount.ShouldBe(0);
    }

    [Fact]
    public void The_schema_describes_what_the_class_declares()
    {
        EntitySchema schema = FuncMoveLinear.SpectraSchema;

        schema.ClassName.ShouldBe("func_movelinear");
        schema.DisplayName.ShouldBe("Linear Mover");
        schema.Group.ShouldBe("Movers");
        schema.Placement.ShouldBe(EntityPlacement.Brush);
        schema.Inputs.ShouldBe(["Open", "Close", "SetPosition"]);
        schema.Outputs.ShouldBe(["OnFullyOpen", "OnFullyClosed"]);

        schema.Keyvalues.Select(keyvalue => keyvalue.Name)
            .ShouldBe(["movedir", "distance", "speed", "startposition"]);
        schema.Keyvalues.Select(keyvalue => keyvalue.Default).ShouldBe(["0 1 0", "4", "2", "0"]);
        schema.Keyvalues.Select(keyvalue => keyvalue.Type)
            .ShouldBe([KeyvalueType.Vec3, KeyvalueType.Float, KeyvalueType.Float, KeyvalueType.Float]);

        schema.Keyvalues[3].Min.ShouldBe(0f);
        schema.Keyvalues[3].Max.ShouldBe(1f);
    }

    [Fact]
    public void The_schema_defaults_are_the_values_a_bare_mover_has()
    {
        var bare = new FuncMoveLinear();
        var parsed = new FuncMoveLinear();
        foreach (KeyvalueDescriptor keyvalue in FuncMoveLinear.SpectraSchema.Keyvalues)
            parsed.ParseKeyValue(keyvalue.Name, keyvalue.Default).ShouldBeTrue(keyvalue.Name);

        parsed.MoveDirection.ShouldBe(bare.MoveDirection);
        parsed.Distance.ShouldBe(bare.Distance);
        parsed.Speed.ShouldBe(bare.Speed);
        parsed.StartPosition.ShouldBe(bare.StartPosition);
    }

    private sealed record Rig(
        Scene Scene, SceneNode Node, EntityWorld World, FuncMoveLinear Mover,
        List<string> Log, CapturingLogger Logger, byte[] Authored)
    {
        public void Send(string input, string parameter = "") => World.QueueInput(Mover, input, parameter);
    }

    // One platform with both outputs wired to a recorder. Parts only, so the
    // scene starts with a clean static world.
    private static Rig Start(Action<EntityData>? author = null, Quaternion? rotation = null)
    {
        var log = new List<string>();
        var logger = new CapturingLogger();
        var scene = new Scene("Platform");

        SceneNode node = Movers.Part(
            scene.Root, "platform", new Vector3(2f, 0.25f, 2f), new Vector3(-4.4f, 0.3f, 6.1f));
        node.LocalRotation = rotation ?? Quaternion.Identity;
        node.Entity = new EntityData("func_movelinear");
        author?.Invoke(node.Entity);
        Movers.WireToSink(node, FuncMoveLinear.OnFullyOpen, FuncMoveLinear.OnFullyClosed);
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        byte[] authored = Movers.Bits(node.LocalTransform);

        var world = new EntityWorld(scene, logger, EntityRuntime.Catalog(log));
        world.Activate();
        return new Rig(
            scene, node, world, EntityRuntime.Live<FuncMoveLinear>(world, node), log, logger, authored);
    }
}
