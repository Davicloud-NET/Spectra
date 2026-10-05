using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// A mover counts ticks: it lands on its stored poses, and the way back takes
/// as long as the way out.
/// </summary>
public sealed class LinearMoverTests
{
    [Theory]
    [InlineData(2f, 2f, 60)]
    [InlineData(3f, 2f, 90)]
    [InlineData(1.9f, 2f, 57)]
    [InlineData(1f, 0.7f, 86)]
    [InlineData(0.01f, 2f, 1)]
    [InlineData(0f, 2f, 1)]
    public void The_trip_takes_the_distance_over_the_speed_rounded_up_to_whole_ticks(
        float distance, float speed, int ticks)
    {
        Rig rig = Start(distance, speed);

        rig.Mover.TravelTicks.ShouldBe(ticks);
    }

    [Fact]
    public void A_trip_that_divides_evenly_takes_no_extra_tick_at_fifty_ticks_a_second()
    {
        // 0.02f is a hair under a fiftieth, so 2 / (2 * 0.02f) is a hair over 50.
        const float fiftieth = 1f / 50f;
        var scene = new Scene("Mover");
        var world = new EntityWorld(scene, new CapturingLogger(), Movers.Catalog([]));
        world.Activate();
        world.Tick(fiftieth);

        SceneNode node = Movers.Part(scene.Root, "slab", Vector3.One, Vector3.Zero);
        node.Entity = new EntityData(MoverProbe.WireName);
        world.QueueSpawn(node);
        world.Tick(fiftieth);

        EntityRuntime.Live<MoverProbe>(world, node).Mover.TravelTicks.ShouldBe(50);
    }

    [Fact]
    public void The_node_reaches_the_open_pose_on_the_last_tick_of_the_trip_bit_for_bit()
    {
        Rig rig = Start(distance: 2f, speed: 2f);
        rig.Mover.MoveTo(rig.Mover.TravelTicks);

        for (int tick = 1; tick < 60; tick++)
            rig.Mover.Advance().ShouldBe(LinearMoverStep.Moving, $"tick {tick}");

        Movers.Bits(rig.Node.LocalTransform).ShouldNotBe(Movers.Bits(rig.Mover.Open));

        rig.Mover.Advance().ShouldBe(LinearMoverStep.ArrivedOpen);

        Movers.Bits(rig.Node.LocalTransform).ShouldBe(Movers.Bits(rig.Mover.Open));
        rig.Mover.IsFullyOpen.ShouldBeTrue();
        rig.Mover.Advance().ShouldBe(LinearMoverStep.Stopped);
    }

    [Fact]
    public void The_node_returns_to_the_authored_transform_bit_for_bit()
    {
        Rig rig = Start(distance: 2f, speed: 2f);
        Movers.Bits(rig.Mover.Closed).ShouldBe(rig.Authored);

        rig.Mover.MoveTo(rig.Mover.TravelTicks);
        Advance(rig.Mover, 60);
        Movers.Bits(rig.Node.LocalTransform).ShouldNotBe(rig.Authored);

        rig.Mover.MoveTo(0);
        for (int tick = 1; tick < 60; tick++)
            rig.Mover.Advance().ShouldBe(LinearMoverStep.Moving, $"tick {tick}");

        rig.Mover.Advance().ShouldBe(LinearMoverStep.ArrivedClosed);

        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
        rig.Mover.IsFullyClosed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(59)]
    public void A_reversal_part_way_takes_as_long_back_as_it_took_out(int ticksOut)
    {
        Rig rig = Start(distance: 2f, speed: 2f);
        rig.Mover.MoveTo(rig.Mover.TravelTicks);
        Advance(rig.Mover, ticksOut);

        rig.Mover.MoveTo(0);
        int ticksBack = 1;
        while (rig.Mover.Advance() != LinearMoverStep.ArrivedClosed)
        {
            ticksBack++;
            ticksBack.ShouldBeLessThanOrEqualTo(60);
        }

        ticksBack.ShouldBe(ticksOut);
        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
    }

    [Fact]
    public void A_tick_of_the_trip_is_the_same_pose_reached_from_either_end()
    {
        Rig rig = Start(distance: 2f, speed: 2f);

        rig.Mover.MoveTo(rig.Mover.TravelTicks);
        Advance(rig.Mover, 20);
        byte[] onTheWayOut = Movers.Bits(rig.Node.LocalTransform);

        Advance(rig.Mover, 40);
        rig.Mover.MoveTo(0);
        Advance(rig.Mover, 40);

        rig.Mover.TicksTravelled.ShouldBe(20);
        Movers.Bits(rig.Node.LocalTransform).ShouldBe(onTheWayOut);
    }

    [Fact]
    public void Between_the_ends_the_node_is_as_far_along_as_the_tick_count_says()
    {
        Rig rig = Start(distance: 2f, speed: 2f);
        Vector3 closed = rig.Mover.Closed.Position;
        Vector3 open = rig.Mover.Open.Position;

        rig.Mover.MoveTo(rig.Mover.TravelTicks);
        Advance(rig.Mover, 15);

        Vector3.Distance(rig.Node.LocalPosition, Vector3.Lerp(closed, open, 0.25f)).ShouldBeLessThan(1e-5f);
        rig.Node.LocalRotation.ShouldBe(rig.Mover.Closed.Rotation);
        rig.Node.LocalScale.ShouldBe(rig.Mover.Closed.Scale);
    }

    [Fact]
    public void Open_is_the_closed_pose_moved_the_distance_along_the_nodes_own_axis()
    {
        // A quarter turn about Z takes the node's up to the parent's -X.
        Quaternion quarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        Rig rig = Start(distance: 2f, speed: 2f, rotation: quarterTurn);

        Vector3 travel = rig.Mover.Open.Position - rig.Mover.Closed.Position;

        Vector3.Distance(travel, new Vector3(-2f, 0f, 0f)).ShouldBeLessThan(1e-5f);
        rig.Mover.Open.Rotation.ShouldBe(rig.Mover.Closed.Rotation);
        rig.Mover.Open.Scale.ShouldBe(rig.Mover.Closed.Scale);
    }

    [Fact]
    public void A_target_part_way_is_reached_and_reported_as_a_stop()
    {
        Rig rig = Start(distance: 2f, speed: 2f);
        rig.Mover.TicksAt(0.5f).ShouldBe(30);

        rig.Mover.MoveTo(30);
        for (int tick = 1; tick < 30; tick++)
            rig.Mover.Advance().ShouldBe(LinearMoverStep.Moving, $"tick {tick}");

        rig.Mover.Advance().ShouldBe(LinearMoverStep.Stopped);
        rig.Mover.TicksTravelled.ShouldBe(30);
        rig.Mover.IsMoving.ShouldBeFalse();
    }

    [Theory]
    [InlineData(-1f, 0)]
    [InlineData(0f, 0)]
    [InlineData(0.26f, 16)]
    [InlineData(1f, 60)]
    [InlineData(7f, 60)]
    [InlineData(float.NaN, 0)]
    public void A_fraction_of_the_trip_is_its_nearest_tick(float fraction, int ticks)
    {
        Rig rig = Start(distance: 2f, speed: 2f);

        rig.Mover.TicksAt(fraction).ShouldBe(ticks);
    }

    [Fact]
    public void A_refused_move_leaves_the_count_where_the_node_is()
    {
        var scene = new Scene("Mover");
        SceneNode lift = scene.Root.CreateChild("lift");
        lift.Entity = new EntityData(MoverProbe.WireName);
        lift.CreateChild("slab").Brush = Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f));
        byte[] authored = Movers.Bits(lift.LocalTransform);

        var world = new EntityWorld(scene, new CapturingLogger(), Movers.Catalog([]));
        world.Activate();
        LinearMover mover = EntityRuntime.Live<MoverProbe>(world, lift).Mover;

        mover.MoveTo(mover.TravelTicks);
        mover.Advance().ShouldBe(LinearMoverStep.Stopped);

        mover.TicksTravelled.ShouldBe(0);
        mover.IsMoving.ShouldBeFalse();
        mover.PlaceAt(mover.TravelTicks).ShouldBeFalse();
        mover.TicksTravelled.ShouldBe(0);
        world.RefusedMoveCount.ShouldBe(2);
        Movers.Bits(lift.LocalTransform).ShouldBe(authored);
    }

    [Fact]
    public void Placing_the_node_puts_it_there_at_once_and_at_rest()
    {
        Rig rig = Start(distance: 2f, speed: 2f);

        rig.Mover.PlaceAt(rig.Mover.TravelTicks).ShouldBeTrue();

        Movers.Bits(rig.Node.LocalTransform).ShouldBe(Movers.Bits(rig.Mover.Open));
        rig.Mover.IsMoving.ShouldBeFalse();
        rig.Mover.Advance().ShouldBe(LinearMoverStep.Stopped);
    }

    [Fact]
    public void Stopping_the_level_puts_a_node_left_open_back_where_it_was_authored()
    {
        Rig rig = Start(distance: 2f, speed: 2f);
        rig.Mover.PlaceAt(rig.Mover.TravelTicks);

        rig.World.Deactivate();

        Movers.Bits(rig.Node.LocalTransform).ShouldBe(rig.Authored);
    }

    [Fact]
    public void A_direction_is_brought_to_unit_length()
    {
        LinearMover.TryNormalize(new Vector3(0f, 0f, 3f), out Vector3 direction).ShouldBeTrue();

        direction.ShouldBe(Vector3.UnitZ);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    public void A_direction_with_no_length_is_refused_and_comes_back_as_up(float component)
    {
        LinearMover.TryNormalize(new Vector3(component), out Vector3 direction).ShouldBeFalse();

        direction.ShouldBe(Vector3.UnitY);
    }

    [Fact]
    public void The_extent_along_a_direction_spans_every_brush_given()
    {
        var scene = new Scene("Mover");
        SceneNode door = Movers.Part(scene.Root, "door", new Vector3(1f, 2f, 0.2f), new Vector3(40f, 3f, -9f));
        SceneNode panel = Movers.Part(door, "panel", new Vector3(0.5f, 1f, 0.2f), new Vector3(0f, 3f, 0f));
        SceneNode[] brushes = [door, panel];

        // The door spans -1 to 1 and the panel 2.5 to 3.5.
        LinearMover.ExtentAlong(door, brushes, Vector3.UnitY).ShouldBe(4.5f);
        LinearMover.ExtentAlong(door, brushes, Vector3.UnitX).ShouldBe(1f);
        LinearMover.ExtentAlong(door, [door], Vector3.UnitY).ShouldBe(2f);
        LinearMover.ExtentAlong(door, [], Vector3.UnitY).ShouldBe(0f);
    }

    [Fact]
    public void The_extent_is_measured_in_the_nodes_own_axes()
    {
        var scene = new Scene("Mover");
        SceneNode door = Movers.Part(scene.Root, "door", new Vector3(1f, 2f, 0.2f), new Vector3(40f, 3f, -9f));
        door.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);

        LinearMover.ExtentAlong(door, [door], Vector3.UnitY).ShouldBe(2f);
    }

    private sealed record Rig(EntityWorld World, SceneNode Node, LinearMover Mover, byte[] Authored);

    // An awkward pose, so a pose that is only near an end does not pass as it.
    private static Rig Start(float distance, float speed, Quaternion? rotation = null)
    {
        var scene = new Scene("Mover");
        SceneNode node = Movers.Part(scene.Root, "slab", new Vector3(1f, 2f, 0.2f), new Vector3(0.1f, 1f / 3f, -7.3f));
        node.LocalRotation = rotation ?? Quaternion.CreateFromYawPitchRoll(0.3f, 0.2f, 0.1f);
        node.Entity = new EntityData(MoverProbe.WireName);
        node.Entity.SetValue("distance", KeyvalueWire.Format(distance));
        node.Entity.SetValue("speed", KeyvalueWire.Format(speed));
        byte[] authored = Movers.Bits(node.LocalTransform);

        var world = new EntityWorld(scene, new CapturingLogger(), Movers.Catalog([]));
        world.Activate();
        return new Rig(world, node, EntityRuntime.Live<MoverProbe>(world, node).Mover, authored);
    }

    private static void Advance(LinearMover mover, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            mover.Advance();
    }
}
