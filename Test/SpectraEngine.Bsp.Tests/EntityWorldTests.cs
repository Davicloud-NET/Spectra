using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Tick ordering, inputs queued by name, the dispatch budget, and authored
/// data staying untouched.
/// </summary>
public sealed class EntityWorldTests
{
    private const float Tick = 1f / 60f;

    [Fact]
    public void Two_events_due_at_the_same_time_dispatch_in_the_order_they_were_scheduled()
    {
        // Wired beta then alpha so alphabetical order can't pass by accident.
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        EntityRuntime.Place(scene.Root, "alpha", "recorder");
        EntityRuntime.Place(scene.Root, "beta", "recorder");
        EntityRuntime.Wire(source, "OnGo", "beta", "Ping");
        EntityRuntime.Wire(source, "OnGo", "alpha", "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.Tick(Tick);

        log.Count.ShouldBe(2);
        log[0].ShouldStartWith("beta:Ping");
        log[1].ShouldStartWith("alpha:Ping");
    }

    [Fact]
    public void A_later_authored_wire_with_no_delay_still_arrives_before_an_earlier_one_that_waits()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        EntityRuntime.Place(scene.Root, "slow", "recorder");
        EntityRuntime.Place(scene.Root, "fast", "recorder");
        EntityRuntime.Wire(source, "OnGo", "slow", "Ping", delay: 0.5f);
        EntityRuntime.Wire(source, "OnGo", "fast", "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        EntityRuntime.Live(world, source).FireOutput("OnGo");

        world.Tick(Tick);
        log.Count.ShouldBe(1);
        log[0].ShouldStartWith("fast:Ping");

        for (int i = 0; i < 60; i++)
            world.Tick(Tick);

        log.Count.ShouldBe(2);
        log[1].ShouldStartWith("slow:Ping");
    }

    [Fact]
    public void A_zero_delay_mutual_relay_trips_the_dispatch_budget_and_the_error_names_the_target()
    {
        var log = new List<string>();
        var logger = new CapturingLogger();
        var scene = new Scene("Entities");
        SceneNode a = EntityRuntime.Place(scene.Root, "relay_a", "relay");
        SceneNode b = EntityRuntime.Place(scene.Root, "relay_b", "relay");
        EntityRuntime.Wire(a, "OnTrigger", "relay_b", "Trigger");
        EntityRuntime.Wire(b, "OnTrigger", "relay_a", "Trigger");

        var world = new EntityWorld(scene, logger, EntityRuntime.Catalog(log))
        {
            MaxDispatchesPerTick = 64,
        };
        world.Activate();
        EntityRuntime.Live(world, a).FireOutput("OnTrigger");

        world.Tick(Tick);

        world.DispatchBudgetTripCount.ShouldBe(1);
        world.LastTickDispatchCount.ShouldBe(64);

        string error = logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem();
        // The cascade alternates, so the 65th event is aimed at relay_b.
        error.ShouldContain("relay_b");
        error.ShouldContain("OnTrigger");

        // The runaway is dropped, not requeued: no repeat error next tick.
        world.DiscardedEventCount.ShouldBeGreaterThan(0);
        world.Tick(Tick);
        world.DispatchBudgetTripCount.ShouldBe(1);
        logger.MessagesAt(LogLevel.Error).Count.ShouldBe(1);
    }

    [Fact]
    public void An_unknown_classname_becomes_a_placeholder_that_keeps_every_keyvalue_and_wire()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "mystery", "func_nothing_here");
        node.Entity!.SetValue("speed", "100");
        node.Entity.SetValue("wait", "4");
        EntityRuntime.Wire(node, "OnFullyOpen", "hall_light", "TurnOn", delay: 0.5f);

        var logger = new CapturingLogger();
        var world = new EntityWorld(scene, logger, new EntityCatalog());
        world.Activate();

        world.Entities.ShouldHaveSingleItem().ShouldBeOfType<PlaceholderEntity>();
        node.Entity.ClassName.ShouldBe("func_nothing_here");
        node.Entity.Keyvalues.Count.ShouldBe(2);
        node.Entity.TryGetValue("speed", out string speed).ShouldBeTrue();
        speed.ShouldBe("100");
        node.Entity.Connections.Count.ShouldBe(1);
        node.Entity.Connections[0].Input.ShouldBe("TurnOn");
    }

    [Fact]
    public void A_placeholder_refuses_inputs_and_says_so_once_per_classname()
    {
        var scene = new Scene("Entities");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "func_nothing_here");
        SceneNode target = EntityRuntime.Place(scene.Root, "target", "func_nothing_here");
        EntityRuntime.Wire(source, "OnGo", "target", "Open");

        var logger = new CapturingLogger();
        var world = new EntityWorld(scene, logger, new EntityCatalog());
        world.Activate();

        Entity live = EntityRuntime.Live(world, source);
        live.FireOutput("OnGo");
        world.Tick(Tick);
        live.FireOutput("OnGo");
        world.Tick(Tick);

        IReadOnlyList<string> warnings = logger.MessagesAt(LogLevel.Warning);
        warnings.Count(w => w.Contains("cannot accept")).ShouldBe(1, logger.Describe());
    }

    [Fact]
    public void A_keyvalue_nothing_can_parse_warns_and_leaves_the_default_standing()
    {
        var log = new List<string>();
        var logger = new CapturingLogger();
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "mover", "speedster");
        node.Entity!.SetValue("speed", "as fast as it goes");

        var world = new EntityWorld(scene, logger, EntityRuntime.Catalog(log));
        Should.NotThrow(world.Activate);

        var entity = world.Entities.ShouldHaveSingleItem().ShouldBeOfType<SpeedEntity>();
        entity.Speed.ShouldBe(100f);

        string warning = logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain("speed");
        warning.ShouldContain("as fast as it goes");
    }

    [Fact]
    public void Exhausting_a_wires_fire_count_never_touches_the_authored_data()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        EntityRuntime.Place(scene.Root, "target", "recorder");
        EntityRuntime.Wire(source, "OnGo", "target", "Ping", timesToFire: 1);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        Entity live = EntityRuntime.Live(world, source);

        live.FireOutput("OnGo");
        world.Tick(Tick);
        live.FireOutput("OnGo");
        world.Tick(Tick);

        log.Count.ShouldBe(1);

        EntityOutput output = live.FindOutput("OnGo").ShouldNotBeNull();
        output.FiresLeftAt(0).ShouldBe(0);
        output.LiveWireCount.ShouldBe(0);

        source.Entity!.Connections[0].TimesToFire.ShouldBe(1);
    }

    [Fact]
    public void An_infinite_wire_is_never_decremented()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        EntityRuntime.Place(scene.Root, "target", "recorder");
        EntityRuntime.Wire(source, "OnGo", "target", "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        Entity live = EntityRuntime.Live(world, source);

        for (int i = 0; i < 5; i++)
        {
            live.FireOutput("OnGo");
            world.Tick(Tick);
        }

        log.Count.ShouldBe(5);
        live.FindOutput("OnGo")!.FiresLeftAt(0).ShouldBe(EntityConnection.Infinite);
    }

    [Fact]
    public void Activation_wires_every_target_before_the_first_spawn_runs()
    {
        // The target comes later in traversal order than the entity firing at it.
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "spawner");
        EntityRuntime.Place(scene.Root, "later", "recorder");
        EntityRuntime.Wire(source, "OnSpawned", "later", "Ping");

        EntityCatalog catalog = EntityRuntime.Catalog(log);
        catalog.Add(new EntitySchema("spawner"), () => new SpawnFiringEntity());

        var world = new EntityWorld(scene, new CapturingLogger(), catalog);
        world.Activate();
        world.Tick(Tick);

        log.ShouldHaveSingleItem().ShouldStartWith("later:Ping");
    }

    [Fact]
    public void A_think_runs_at_the_time_it_asked_for_and_a_reschedule_supersedes_the_old_one()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "ticker", "ticker");

        var catalog = new EntityCatalog();
        catalog.Add(new EntitySchema("ticker"), () => new CountingThinkEntity());
        var world = new EntityWorld(scene, new CapturingLogger(), catalog);
        world.Activate();

        var entity = world.Entities.ShouldHaveSingleItem().ShouldBeOfType<CountingThinkEntity>();
        entity.SetNextThink(1f);
        // The first entry stays in the heap; it must not think twice.
        entity.SetNextThink(0.5f);

        for (int i = 0; i < 35; i++)
            world.Tick(Tick);

        entity.Thinks.ShouldBe(1);

        for (int i = 0; i < 60; i++)
            world.Tick(Tick);

        entity.Thinks.ShouldBe(1);
    }

    [Fact]
    public void An_input_queued_by_hand_reaches_every_entity_the_name_resolves_to()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        EntityRuntime.Place(scene.Root, "door_north", "recorder");
        EntityRuntime.Place(scene.Root, "hall", "recorder");
        EntityRuntime.Place(scene.Root, "door_south", "recorder");
        EntityRuntime.Place(scene.Root, "lamp", "recorder").Entity!.SetValue("tag", "lamp_a");
        EntityRuntime.Place(scene.Root, "lamp", "recorder").Entity!.SetValue("tag", "lamp_b");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        world.QueueInput("door*", "Open");
        world.QueueInput("lamp", "TurnOn");
        world.QueueInput("nobody", "Open");

        log.ShouldBeEmpty();
        world.PendingEventCount.ShouldBe(3);

        world.Tick(Tick);

        log.ShouldBe(new[]
        {
            "door_north:Open::-:-", "door_south:Open::-:-", "lamp_a:TurnOn::-:-", "lamp_b:TurnOn::-:-",
        });
    }

    [Fact]
    public void An_input_queued_by_hand_has_no_caller_and_no_activator()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        EntityRuntime.Place(scene.Root, "button", "recorder");
        SceneNode relay = EntityRuntime.Place(scene.Root, "relay", "relay");
        EntityRuntime.Wire(relay, "OnTrigger", "button", "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        world.QueueInput("button", "Use", "hard");
        world.QueueInput("relay", "Trigger");
        world.Tick(Tick);

        // Nobody sent the first. The relay had no activator to pass on, so
        // what it fired names the relay as both.
        log.ShouldBe(new[] { "button:Use:hard:-:-", "button:Ping::relay:relay" });
    }

    [Fact]
    public void A_delayed_input_queued_by_hand_arrives_after_its_delay()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        EntityRuntime.Place(scene.Root, "button", "recorder");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        world.QueueInput("button", "Late", delay: 0.5f);
        world.QueueInput("button", "Now");

        for (int i = 0; i < 28; i++)
            world.Tick(Tick);

        log.ShouldHaveSingleItem().ShouldStartWith("button:Now");

        for (int i = 0; i < 4; i++)
            world.Tick(Tick);

        log.Count.ShouldBe(2);
        log[1].ShouldStartWith("button:Late");
    }

    [Fact]
    public void A_name_queued_by_hand_is_resolved_when_the_input_comes_due()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "gate", "recorder");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        world.QueueInput("door", "Open");
        node.Name = "door";
        world.Tick(Tick);

        log.ShouldHaveSingleItem().ShouldStartWith("door:Open");
    }

    [Fact]
    public void An_input_cannot_be_queued_by_name_on_a_world_that_is_not_running()
    {
        var scene = new Scene("Entities");
        EntityRuntime.Place(scene.Root, "button", "recorder");
        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));

        Should.Throw<InvalidOperationException>(() => world.QueueInput("button", "Use"));

        world.Activate();
        world.Deactivate();

        Should.Throw<InvalidOperationException>(() => world.QueueInput("button", "Use"));
    }

    [Fact]
    public void Deactivating_removes_every_entity_and_lets_go_of_the_scene()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "one", "recorder");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();
        world.Index.ShouldNotBeNull();

        world.Deactivate();

        world.IsActive.ShouldBeFalse();
        world.Entities.ShouldBeEmpty();
        world.Index.ShouldBeNull();

        // A rename after deactivation must reach nothing.
        Should.NotThrow(() => node.Name = "renamed after");
    }

    private sealed class CountingThinkEntity : Entity
    {
        public int Thinks { get; private set; }

        protected internal override void Think() => Thinks++;
    }
}
