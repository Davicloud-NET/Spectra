using System.Numerics;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The touch pass: which sensing entities the player is inside, and when
/// each one hears a touch start and end.
/// </summary>
public sealed class TouchTrackerTests
{
    private const float Dt = Touching.Dt;

    // A 2 by 2 by 2 zone standing on the ground at the origin. A capsule
    // coming in along +X first touches it with its feet at Edge.
    private const float Edge = 1f + FakePlayerPresence.Radius;

    private static readonly Vector3 ZoneCenter = new(0f, 1f, 0f);
    private static readonly Vector3 ZoneHalf = new(1f, 1f, 1f);
    private static readonly Vector3 Away = new(10f, 1f, 0f);

    private readonly List<string> _log = [];

    [Fact]
    public void Entering_and_leaving_fire_once_each()
    {
        var scene = new Scene("Touch");
        Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        var player = new FakePlayerPresence { Feet = new Vector3(4f, 0f, 0f) };
        EntityWorld world = Touching.Play(scene, _log, player);

        // Through the zone and out the far side, a tenth of a unit a tick.
        for (int i = 0; i <= 80; i++)
        {
            player.Feet = new Vector3(4f - i * 0.1f, 0f, 0f);
            world.Tick(Dt);
        }

        _log.ShouldBe(["start:zone", "end:zone"]);
        world.Touches.TouchCount.ShouldBe(0);
    }

    [Fact]
    public void A_capsule_resting_on_the_boundary_does_not_chatter()
    {
        var scene = new Scene("Touch");
        Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        var player = new FakePlayerPresence { Feet = new Vector3(4f, 0f, 0f) };
        EntityWorld world = Touching.Play(scene, _log, player);

        for (int i = 0; i < 600; i++)
        {
            // A millimetre inside, then a millimetre outside.
            float offset = i % 2 == 0 ? -0.001f : 0.001f;
            player.Feet = new Vector3(Edge + offset, 0f, 0f);
            world.Tick(Dt);
        }

        _log.ShouldBe(["start:zone"]);
    }

    [Fact]
    public void A_touch_ends_only_once_the_capsule_is_clear_by_the_exit_margin()
    {
        var scene = new Scene("Touch");
        Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        var player = new FakePlayerPresence { Feet = new Vector3(Edge - 0.01f, 0f, 0f) };
        EntityWorld world = Touching.Play(scene, _log, player);
        world.Tick(Dt);

        player.Feet = new Vector3(Edge + TouchTracker.ExitMargin - 0.01f, 0f, 0f);
        world.Tick(Dt);
        _log.ShouldBe(["start:zone"]);

        player.Feet = new Vector3(Edge + TouchTracker.ExitMargin + 0.01f, 0f, 0f);
        world.Tick(Dt);
        _log.ShouldBe(["start:zone", "end:zone"]);

        // Inside the margin again is not a touch: one starts at contact.
        player.Feet = new Vector3(Edge + TouchTracker.ExitMargin - 0.01f, 0f, 0f);
        world.Tick(Dt);
        _log.ShouldBe(["start:zone", "end:zone"]);
    }

    [Fact]
    public void A_trigger_moved_over_a_still_capsule_fires_that_tick()
    {
        var scene = new Scene("Touch");
        SceneNode zone = Touching.Volume(scene.Root, "zone", Away, ZoneHalf);
        var player = new FakePlayerPresence();
        EntityWorld world = Touching.Play(scene, _log, player);

        for (int i = 0; i < 5; i++)
            world.Tick(Dt);
        _log.ShouldBeEmpty();

        Move(world, zone, ZoneCenter);
        world.Tick(Dt);
        _log.ShouldBe(["start:zone"]);

        Move(world, zone, Away);
        world.Tick(Dt);
        _log.ShouldBe(["start:zone", "end:zone"]);
    }

    [Fact]
    public void A_trigger_carried_by_a_moving_group_fires_when_it_arrives()
    {
        var scene = new Scene("Touch");

        // The group slides 6 along +X in 12 ticks, then back. The zone under
        // it starts 6 short of the capsule.
        SceneNode lift = EntityMotion.Slider(scene.Root.CreateChild("lift"), new Vector3(6f, 0f, 0f), ticks: 12);
        Touching.Volume(lift, "zone", new Vector3(-6f, 1f, 0f), ZoneHalf);
        var player = new FakePlayerPresence();
        EntityWorld world = Touching.Play(scene, _log, player);

        // Tick 10 moves the zone's face past the capsule's side. The pass of
        // tick 11 is the first to see it there.
        for (int i = 0; i < 10; i++)
            world.Tick(Dt);
        _log.ShouldBeEmpty();

        world.Tick(Dt);
        _log.ShouldBe(["start:zone"]);

        // Tick 15 takes it clear again, on the way back.
        for (int i = 0; i < 4; i++)
            world.Tick(Dt);
        _log.ShouldBe(["start:zone"]);

        world.Tick(Dt);
        _log.ShouldBe(["start:zone", "end:zone"]);
    }

    [Fact]
    public void A_trigger_despawned_while_touched_still_ends_the_touch()
    {
        var scene = new Scene("Touch");
        SceneNode zone = Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        EntityRuntime.Place(scene.Root, "door", "recorder");
        EntityRuntime.Wire(zone, "OnEndTouch", "door", "Close");
        EntityWorld world = Touching.Play(scene, _log, new FakePlayerPresence());
        world.Tick(Dt);
        _log.ShouldBe(["start:zone"]);

        world.QueueDespawn(EntityRuntime.Live(world, zone));
        world.Tick(Dt);

        _log.ShouldBe(["start:zone", "end:zone"]);
        world.Touches.TouchCount.ShouldBe(0);
        world.Touches.SensorCount.ShouldBe(0);

        // The capsule has not moved, and nothing is left to sense it. The
        // door still hears the touch end.
        for (int i = 0; i < 5; i++)
            world.Tick(Dt);
        _log.ShouldBe(["start:zone", "end:zone", "door:Close::zone:zone"]);
    }

    [Fact]
    public void Two_overlapping_triggers_fire_in_entity_order_whatever_the_insertion_order()
    {
        List<string> forwards = EnterAndLeaveBoth(stampFirst: "first", out string[] forwardsReported);
        List<string> backwards = EnterAndLeaveBoth(stampFirst: "second", out string[] backwardsReported);

        // The spatial index reports the pair in the order it was built in.
        forwardsReported.Order().ShouldBe(["first", "second"]);
        backwardsReported.ShouldBe(Enumerable.Reverse(forwardsReported));

        forwards.ShouldBe(["start:first", "start:second", "end:first", "end:second"]);
        backwards.ShouldBe(forwards);
    }

    [Fact]
    public void A_rotated_trigger_fires_inside_its_shape_and_not_inside_its_bounds()
    {
        var scene = new Scene("Touch");

        // A long thin slab turned 45 degrees: its bounds are a square, and
        // two corners of that square are nowhere near the slab.
        SceneNode zone = Touching.Volume(scene.Root, "zone", ZoneCenter, new Vector3(3f, 1f, 0.25f));
        zone.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);

        Vector3 onTheSlab = Vector3.Transform(new Vector3(2.5f, -1f, 0f), zone.WorldMatrix);
        var inAnEmptyCorner = new Vector3(onTheSlab.X, 0f, -onTheSlab.Z);

        var player = new FakePlayerPresence { Feet = inAnEmptyCorner };
        EntityWorld world = Touching.Play(scene, _log, player);

        scene.TryGetWorldBounds(zone, out Aabb bounds).ShouldBeTrue();
        bounds.Intersects(new Aabb(player.Capsule.Center1, player.Capsule.Center2)).ShouldBeTrue();

        for (int i = 0; i < 5; i++)
            world.Tick(Dt);
        _log.ShouldBeEmpty();

        player.Feet = onTheSlab;
        world.Tick(Dt);
        _log.ShouldBe(["start:zone"]);
    }

    [Fact]
    public void A_volume_with_touch_off_is_ignored()
    {
        var scene = new Scene("Touch");
        SceneNode zone = Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        zone.CanTouch = false;
        EntityWorld world = Touching.Play(scene, _log, new FakePlayerPresence());

        for (int i = 0; i < 5; i++)
            world.Tick(Dt);

        _log.ShouldBeEmpty();
        world.Touches.TouchCount.ShouldBe(0);
    }

    [Fact]
    public void A_world_brush_never_senses_and_the_entity_is_named_in_a_warning()
    {
        var scene = new Scene("Touch");
        SceneNode zone = scene.Root.CreateChild("zone");
        zone.LocalPosition = ZoneCenter;
        zone.Entity = new EntityData("sensor");
        zone.Brush = Brush.CreateBox(-ZoneHalf, ZoneHalf);
        var logger = new CapturingLogger();
        EntityWorld world = Touching.Play(scene, _log, new FakePlayerPresence(), logger);

        for (int i = 0; i < 5; i++)
            world.Tick(Dt);

        _log.ShouldBeEmpty();
        logger.MessagesAt(LogLevel.Warning)
            .ShouldContain(message => message.Contains("'zone'") && message.Contains("no part brush"));
    }

    [Fact]
    public void Stopping_mid_touch_delivers_nothing()
    {
        var scene = new Scene("Touch");
        SceneNode zone = Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        EntityRuntime.Place(scene.Root, "door", "recorder");
        EntityRuntime.Wire(zone, "OnEndTouch", "door", "Close");
        EntityWorld world = Touching.Play(scene, _log, new FakePlayerPresence());
        world.Tick(Dt);

        world.Deactivate();

        _log.ShouldBe(["start:zone"]);
        world.Touches.TouchCount.ShouldBe(0);
        world.Touches.SensorCount.ShouldBe(0);
    }

    [Fact]
    public void Playing_again_after_a_stop_senses_from_scratch()
    {
        var scene = new Scene("Touch");
        Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        var player = new FakePlayerPresence();
        EntityWorld world = Touching.Play(scene, _log, player);
        world.Tick(Dt);
        world.Deactivate();

        world.Activate();
        world.Player = player;
        world.Tick(Dt);

        _log.ShouldBe(["start:zone", "start:zone"]);
        world.Touches.TouchCount.ShouldBe(1);
    }

    [Fact]
    public void A_tick_with_nothing_touching_allocates_nothing()
    {
        var scene = new Scene("Touch");
        Touching.Volume(scene.Root, "far", new Vector3(40f, 1f, 0f), ZoneHalf);

        // Three centimetres clear of the capsule: measured, not touched.
        Touching.Volume(scene.Root, "near", new Vector3(Edge + 0.03f, 1f, 0f), ZoneHalf);

        // Bounds that hold the capsule around a shape that does not.
        SceneNode slab = Touching.Volume(scene.Root, "slab", new Vector3(1.5f, 1f, 1.5f), new Vector3(3f, 1f, 0.25f));
        slab.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);

        var player = new FakePlayerPresence();
        EntityWorld world = Touching.Play(scene, _log, player);

        var reported = new List<SceneNode>();
        scene.GetPartBoundsInBox(
            new Aabb(player.Capsule.Center1, player.Capsule.Center2)
                .Expanded(FakePlayerPresence.Radius + TouchTracker.ExitMargin),
            reported,
            new SceneQueryFilter { IgnoreQueryFlags = true });
        reported.Select(node => node.Name).Order().ShouldBe(["near", "slab"]);

        for (int i = 0; i < 60; i++)
            world.Tick(Dt);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
            world.Tick(Dt);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0L);
        _log.ShouldBeEmpty();
    }

    [Fact]
    public void A_touch_output_reaches_its_target_in_the_tick_the_touch_starts()
    {
        var scene = new Scene("Touch");
        SceneNode zone = Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        EntityRuntime.Place(scene.Root, "door", "recorder");
        EntityRuntime.Wire(zone, "OnStartTouch", "door", "Open");
        var player = new FakePlayerPresence { Feet = new Vector3(4f, 0f, 0f) };
        EntityWorld world = Touching.Play(scene, _log, player);
        world.Tick(Dt);
        _log.ShouldBeEmpty();

        player.Feet = Vector3.Zero;
        world.Tick(Dt);

        // The sensor passes no activator, so it is its own.
        _log.ShouldBe(["start:zone", "door:Open::zone:zone"]);
    }

    [Fact]
    public void Switching_a_sensor_off_while_touched_ends_the_touch_and_on_starts_another()
    {
        var scene = new Scene("Touch");
        SceneNode zone = Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        EntityWorld world = Touching.Play(scene, _log, new FakePlayerPresence());
        Entity sensor = EntityRuntime.Live(world, zone);
        world.Tick(Dt);

        world.QueueInput(sensor, "Disable");
        world.Tick(Dt);
        _log.ShouldBe(["start:zone", "end:zone"]);

        // Still inside, and not sensed.
        for (int i = 0; i < 5; i++)
            world.Tick(Dt);
        _log.ShouldBe(["start:zone", "end:zone"]);

        // The input lands after this tick's pass, so the next one starts the touch.
        world.QueueInput(sensor, "Enable");
        world.Tick(Dt);
        _log.ShouldBe(["start:zone", "end:zone"]);

        world.Tick(Dt);
        _log.ShouldBe(["start:zone", "end:zone", "start:zone"]);
    }

    [Fact]
    public void A_sensor_that_switches_itself_off_as_a_touch_starts_hears_it_end_once()
    {
        var scene = new Scene("Touch");
        SceneNode zone = Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        zone.Entity!.SetValue("once", "1");
        EntityWorld world = Touching.Play(scene, _log, new FakePlayerPresence());

        for (int i = 0; i < 5; i++)
            world.Tick(Dt);

        _log.ShouldBe(["start:zone", "end:zone"]);
        world.Touches.TouchCount.ShouldBe(0);
    }

    [Fact]
    public void An_entity_with_two_brushes_is_touched_once_as_the_capsule_crosses_between_them()
    {
        var scene = new Scene("Touch");
        SceneNode group = scene.Root.CreateChild("zone");
        group.Entity = new EntityData("sensor");

        // Side by side, so the pair spans -2 to 2 along X.
        foreach (float x in new[] { -1f, 1f })
        {
            SceneNode half = group.CreateChild($"half{x}");
            half.LocalPosition = new Vector3(x, 1f, 0f);
            Touching.Stamp(half, ZoneHalf);
        }

        var player = new FakePlayerPresence { Feet = new Vector3(5f, 0f, 0f) };
        EntityWorld world = Touching.Play(scene, _log, player);

        for (int i = 0; i <= 100; i++)
        {
            player.Feet = new Vector3(5f - i * 0.1f, 0f, 0f);
            world.Tick(Dt);
        }

        _log.ShouldBe(["start:zone", "end:zone"]);
    }

    [Fact]
    public void A_node_restored_under_its_old_id_keeps_its_touch()
    {
        var scene = new Scene("Touch");
        SceneNode zone = Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        var player = new FakePlayerPresence();
        EntityWorld world = Touching.Play(scene, _log, player);
        world.Tick(Dt);

        // What an undo of a delete does: a new object, the old id.
        scene.Root.RemoveChild(zone);
        var restored = new SceneNode("zone", zone.Id) { LocalPosition = ZoneCenter, Entity = zone.Entity };
        Touching.Stamp(restored, ZoneHalf);
        scene.Root.AddChild(restored);

        world.Tick(Dt);
        _log.ShouldBe(["start:zone"]);

        player.Feet = new Vector3(4f, 0f, 0f);
        world.Tick(Dt);
        _log.ShouldBe(["start:zone", "end:zone"]);
    }

    [Fact]
    public void A_player_that_leaves_the_level_ends_its_touches()
    {
        var scene = new Scene("Touch");
        Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        var player = new FakePlayerPresence();
        EntityWorld world = Touching.Play(scene, _log, player);
        world.Tick(Dt);

        player.IsPresent = false;
        world.Tick(Dt);

        _log.ShouldBe(["start:zone", "end:zone"]);
    }

    [Fact]
    public void A_world_with_no_player_senses_nothing()
    {
        var scene = new Scene("Touch");
        Touching.Volume(scene.Root, "zone", ZoneCenter, ZoneHalf);
        EntityWorld world = Touching.Play(scene, _log, player: null);

        for (int i = 0; i < 5; i++)
            world.Tick(Dt);

        _log.ShouldBeEmpty();
    }

    [Fact]
    public void An_entity_that_cannot_listen_cannot_register()
    {
        var scene = new Scene("Touch");
        SceneNode node = EntityRuntime.Place(scene.Root, "door", "recorder");
        EntityWorld world = Touching.Play(scene, _log, new FakePlayerPresence());

        Should.Throw<ArgumentException>(
            () => world.Touches.Register(EntityRuntime.Live(world, node), [node]));
    }

    // Two zones that both hold the origin. The nodes are created first and
    // second every time; only the order they join the spatial index changes.
    private static List<string> EnterAndLeaveBoth(string stampFirst, out string[] reported)
    {
        var log = new List<string>();
        var scene = new Scene("Touch");

        var nodes = new Dictionary<string, SceneNode>();
        foreach ((string name, float x) in new[] { ("first", -0.5f), ("second", 0.5f) })
        {
            SceneNode node = scene.Root.CreateChild(name);
            node.LocalPosition = new Vector3(x, 1f, 0f);
            node.Entity = new EntityData("sensor");
            nodes.Add(name, node);
        }

        Touching.Stamp(nodes[stampFirst], ZoneHalf);
        foreach (SceneNode node in nodes.Values)
        {
            if (node.Brush is null)
                Touching.Stamp(node, ZoneHalf);
        }

        var player = new FakePlayerPresence();
        EntityWorld world = Touching.Play(scene, log, player);

        var found = new List<SceneNode>();
        scene.GetPartBoundsInBox(
            new Aabb(player.Capsule.Center1, player.Capsule.Center2),
            found,
            new SceneQueryFilter { IgnoreQueryFlags = true });
        reported = [.. found.Select(node => node.Name)];

        world.Tick(Dt);
        player.Feet = new Vector3(8f, 0f, 0f);
        world.Tick(Dt);
        return log;
    }

    private static void Move(EntityWorld world, SceneNode node, Vector3 position)
    {
        Transform moved = node.LocalTransform;
        moved.Position = position;
        world.SetLocalTransform(node, in moved).ShouldBeTrue();
    }
}
