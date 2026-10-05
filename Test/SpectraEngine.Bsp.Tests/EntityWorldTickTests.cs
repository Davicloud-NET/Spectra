using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Physics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The tick counter, per-tick entities, inputs queued by hand and which entity
/// owns a node.
/// </summary>
public sealed class EntityWorldTickTests
{
    private const float Dt = EntityMotion.Dt;

    [Fact]
    public void The_tick_number_is_zero_while_spawning_and_counts_from_the_first_tick()
    {
        var log = new List<string>();
        var scene = new Scene("Ticks");
        Ticker(scene.Root, "clock", ticking: true);
        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog(log));

        world.Activate();

        world.TickNumber.ShouldBe(0);
        log.ShouldBe(new[] { "spawn:clock:0" });

        for (int i = 0; i < 3; i++)
            world.Tick(Dt);

        world.TickNumber.ShouldBe(3);
        log.ShouldBe(new[] { "spawn:clock:0", "tick:clock:1", "tick:clock:2", "tick:clock:3" });

        world.Deactivate();
        world.Activate();

        world.TickNumber.ShouldBe(0);
    }

    [Fact]
    public void The_fixed_step_is_the_engines_until_a_tick_says_otherwise()
    {
        var world = new EntityWorld(new Scene("Ticks"), new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();

        world.FixedDeltaTime.ShouldBe(PhysicsDefaults.FixedDeltaTime);

        world.Tick(1f / 30f);

        world.FixedDeltaTime.ShouldBe(1f / 30f);

        world.Deactivate();
        world.Activate();

        world.FixedDeltaTime.ShouldBe(PhysicsDefaults.FixedDeltaTime);
    }

    [Fact]
    public void Ticking_entities_run_in_entity_order()
    {
        // Placed c, a, b so name order cannot pass by accident.
        var log = new List<string>();
        var scene = new Scene("Ticks");
        Ticker(scene.Root, "c", ticking: true);
        SceneNode group = scene.Root.CreateChild("group");
        Ticker(group, "a", ticking: true);
        Ticker(scene.Root, "idle", ticking: false);
        Ticker(scene.Root, "b", ticking: true);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog(log));
        world.Activate();
        log.Clear();

        world.Tick(Dt);
        world.Tick(Dt);

        world.TickingEntityCount.ShouldBe(3);
        log.ShouldBe(new[]
        {
            "tick:c:1", "tick:a:1", "tick:b:1",
            "tick:c:2", "tick:a:2", "tick:b:2",
        });
    }

    [Fact]
    public void An_entity_told_to_tick_by_an_input_ticks_in_that_same_tick()
    {
        var log = new List<string>();
        var scene = new Scene("Ticks");
        SceneNode door = Ticker(scene.Root, "door", ticking: false);
        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog(log));
        world.Activate();
        log.Clear();

        world.Tick(Dt);
        log.ShouldBeEmpty();

        world.QueueInput(EntityRuntime.Live(world, door), "Start");
        world.Tick(Dt);

        // The input first, then the tick it asked for.
        log.ShouldBe(new[] { "input:door:Start:2", "tick:door:2" });
    }

    [Fact]
    public void An_entity_that_stops_ticking_is_not_called_again()
    {
        var log = new List<string>();
        var scene = new Scene("Ticks");
        SceneNode door = Ticker(scene.Root, "door", ticking: true);
        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog(log));
        world.Activate();
        world.Tick(Dt);
        log.Clear();

        world.QueueInput(EntityRuntime.Live(world, door), "Stop");
        world.Tick(Dt);
        world.Tick(Dt);

        log.ShouldBe(new[] { "input:door:Stop:2" });
        world.TickingEntityCount.ShouldBe(0);
    }

    [Fact]
    public void A_despawned_entity_stops_ticking()
    {
        var log = new List<string>();
        var scene = new Scene("Ticks");
        SceneNode door = Ticker(scene.Root, "door", ticking: true);
        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog(log));
        world.Activate();
        log.Clear();

        world.QueueDespawn(EntityRuntime.Live(world, door));
        world.Tick(Dt);
        world.Tick(Dt);

        // Despawns run at the end of a tick, so it ticked once more.
        log.ShouldBe(new[] { "tick:door:1" });
        world.TickingEntityCount.ShouldBe(0);
    }

    [Fact]
    public void A_queued_input_arrives_on_the_next_tick()
    {
        var log = new List<string>();
        var scene = new Scene("Inputs");
        SceneNode button = EntityRuntime.Place(scene.Root, "button", "recorder");
        SceneNode player = EntityRuntime.Place(scene.Root, "stand_in", "recorder");
        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog(log));
        world.Activate();

        world.QueueInput(
            EntityRuntime.Live(world, button), "Use", "hard", EntityRuntime.Live(world, player));

        log.ShouldBeEmpty();
        world.PendingEventCount.ShouldBe(1);

        world.Tick(Dt);

        // Input, parameter, activator, and no caller.
        log.ShouldBe(new[] { "button:Use:hard:stand_in:-" });
    }

    [Fact]
    public void An_input_queued_with_nothing_else_has_no_activator_and_no_parameter()
    {
        var log = new List<string>();
        var scene = new Scene("Inputs");
        SceneNode button = EntityRuntime.Place(scene.Root, "button", "recorder");
        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog(log));
        world.Activate();

        world.QueueInput(EntityRuntime.Live(world, button), "Use");
        world.Tick(Dt);

        log.ShouldBe(new[] { "button:Use::-:-" });
    }

    [Fact]
    public void Queued_inputs_and_fired_outputs_arrive_in_the_order_they_were_made()
    {
        var log = new List<string>();
        var scene = new Scene("Inputs");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        SceneNode alpha = EntityRuntime.Place(scene.Root, "alpha", "recorder");
        SceneNode beta = EntityRuntime.Place(scene.Root, "beta", "recorder");
        EntityRuntime.Wire(source, "OnGo", "alpha", "Wired");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog(log));
        world.Activate();
        Entity target = EntityRuntime.Live(world, beta);

        world.QueueInput(target, "First");
        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.QueueInput(target, "Last");
        world.Tick(Dt);

        log.Select(line => line.Split(':')[1]).ShouldBe(new[] { "First", "Wired", "Last" });
    }

    [Fact]
    public void An_input_queued_for_an_entity_that_has_gone_is_dropped()
    {
        var log = new List<string>();
        var scene = new Scene("Inputs");
        SceneNode button = EntityRuntime.Place(scene.Root, "button", "recorder");
        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog(log));
        world.Activate();
        Entity gone = EntityRuntime.Live(world, button);

        world.QueueDespawn(gone);
        world.Tick(Dt);
        world.QueueInput(gone, "Use");
        world.Tick(Dt);

        log.ShouldBeEmpty();
        world.PendingEventCount.ShouldBe(0);
    }

    [Fact]
    public void An_input_cannot_be_queued_on_a_world_that_is_not_running()
    {
        var scene = new Scene("Inputs");
        SceneNode button = EntityRuntime.Place(scene.Root, "button", "recorder");
        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();
        Entity entity = EntityRuntime.Live(world, button);

        world.Deactivate();

        Should.Throw<InvalidOperationException>(() => world.QueueInput(entity, "Use"));
    }

    [Fact]
    public void An_input_cannot_be_queued_for_another_worlds_entity()
    {
        var scene = new Scene("Inputs");
        SceneNode button = EntityRuntime.Place(scene.Root, "button", "recorder");
        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        var other = new EntityWorld(new Scene("Elsewhere"), new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();
        other.Activate();

        Should.Throw<ArgumentException>(() => other.QueueInput(EntityRuntime.Live(world, button), "Use"));
    }

    [Fact]
    public void A_node_is_owned_by_the_nearest_entity_at_or_above_it()
    {
        var scene = new Scene("Owners");
        SceneNode outer = EntityRuntime.Place(scene.Root, "outer", "owner");
        SceneNode panel = EntityMotion.BrushNode(outer, "panel", BrushKind.Part, Vector3.Zero);
        SceneNode inner = EntityRuntime.Place(outer, "inner", "owner");
        SceneNode frame = inner.CreateChild("frame");
        SceneNode glass = EntityMotion.BrushNode(frame, "glass", BrushKind.Part, Vector3.Zero);
        SceneNode loose = EntityMotion.BrushNode(scene.Root, "loose", BrushKind.Part, Vector3.Zero);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();

        Owner(world, outer).ShouldBe("outer");
        Owner(world, panel).ShouldBe("outer");
        Owner(world, glass).ShouldBe("inner");
        Owner(world, loose).ShouldBeNull();

        world.Deactivate();

        Owner(world, panel).ShouldBeNull();
    }

    [Fact]
    public void An_entity_that_is_not_running_still_keeps_its_brushes_from_the_one_above()
    {
        var scene = new Scene("Owners");
        SceneNode outer = EntityRuntime.Place(scene.Root, "outer", "owner");
        SceneNode late = outer.CreateChild("late");
        SceneNode glass = EntityMotion.BrushNode(late, "glass", BrushKind.Part, Vector3.Zero);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();
        Owner(world, glass).ShouldBe("outer");

        // Authored after the world started and never spawned.
        late.Entity = new EntityData("owner");

        Owner(world, glass).ShouldBeNull();
    }

    [Fact]
    public void An_entity_owns_the_brushes_below_it_down_to_the_next_entity()
    {
        var scene = new Scene("Owners");
        SceneNode door = EntityRuntime.Place(scene.Root, "door", "owner");
        door.BrushKind = BrushKind.Part;
        door.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        EntityMotion.BrushNode(door, "panel", BrushKind.Part, Vector3.Zero);
        SceneNode frame = door.CreateChild("frame");
        EntityMotion.BrushNode(frame, "hinge", BrushKind.Part, Vector3.Zero);
        door.CreateChild("empty");

        // A button on the door owns its own brush.
        SceneNode button = EntityRuntime.Place(door, "button", "owner");
        EntityMotion.BrushNode(button, "cap", BrushKind.Part, Vector3.Zero);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();

        Owned(world, door).ShouldBe(new[] { "door", "panel", "hinge" });
        Owned(world, button).ShouldBe(new[] { "cap" });
    }

    private static SceneNode Ticker(SceneNode parent, string name, bool ticking)
    {
        SceneNode node = EntityRuntime.Place(parent, name, "ticker");
        node.Entity!.SetValue("ticking", KeyvalueWire.Format(ticking));
        return node;
    }

    private static string? Owner(EntityWorld world, SceneNode node) =>
        world.TryFindOwner(node, out Entity? owner) ? owner.TargetName : null;

    private static string[] Owned(EntityWorld world, SceneNode node) =>
        [.. ((OwningEntity)EntityRuntime.Live(world, node)).OwnedBrushes().Select(n => n.Name)];
}
