using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The entity world's side of a script that listens and sends: a fired
/// output queues one event for each listener, an input a script sent is one
/// more event, and both wait with what scripts made, in the one order.
/// </summary>
public sealed class ScriptListenerTests
{
    private const float Tick = 1f / 60f;

    private readonly List<string> _log = [];
    private readonly CapturingLogger _logger = new();
    private readonly Scene _scene = new("Listened");

    [Fact]
    public void A_fire_calls_the_listeners_after_the_outputs_own_wires_in_the_order_they_were_added()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        EntityRuntime.Place(_scene.Root, "door", "recorder");
        EntityRuntime.Wire(relay, "OnTrigger", "door", "Open");
        EntityRuntime.Wire(relay, "OnTrigger", "door", "Light");
        EntityWorld world = Started(out _);
        world.Listeners.Add(relay, "OnTrigger", 7);
        world.Listeners.Add(relay, "OnTrigger", 3);

        world.QueueInput("relay", "Trigger");
        world.Tick(Tick);

        _log.Skip(1).ShouldBe(["door:Open::relay:relay", "door:Light::relay:relay", "hear 7", "hear 3"]);
        world.PendingEventCount.ShouldBe(0);
    }

    [Fact]
    public void What_a_wires_input_fires_in_answer_comes_after_the_listeners_of_the_first_output()
    {
        SceneNode first = EntityRuntime.Place(_scene.Root, "first", "relay");
        SceneNode second = EntityRuntime.Place(_scene.Root, "second", "relay");
        EntityRuntime.Wire(first, "OnTrigger", "second", "Trigger");
        EntityWorld world = Started(out _);
        world.Listeners.Add(first, "OnTrigger", 1);
        world.Listeners.Add(second, "OnTrigger", 2);
        world.Listeners.Add(first, "OnTrigger", 3);

        world.QueueInput("first", "Trigger");
        world.Tick(Tick);

        _log.Skip(1).ShouldBe(["hear 1", "hear 3", "hear 2"]);
    }

    [Fact]
    public void A_wire_with_a_delay_is_sent_after_the_listeners_have_heard()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        EntityRuntime.Place(_scene.Root, "door", "recorder");
        EntityRuntime.Wire(relay, "OnTrigger", "door", "Open", delay: 2 * Tick);
        EntityWorld world = Started(out _);
        world.Listeners.Add(relay, "OnTrigger", 1);

        world.QueueInput("relay", "Trigger");
        world.Tick(Tick);
        _log.Skip(1).ShouldBe(["hear 1"]);

        world.Tick(Tick);
        world.Tick(Tick);
        _log.Skip(2).ShouldBe(["door:Open::relay:relay"]);
    }

    [Fact]
    public void A_listener_is_handed_what_the_output_sent_and_nothing_for_an_output_that_sends_nothing()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        EntityWorld world = Started(out _);
        world.Listeners.Add(relay, "OnValue", 1);
        Entity live = EntityRuntime.Live(world, relay);

        live.FireOutput("OnValue", parameterOverride: "2.5");
        live.FireOutput("OnValue", ParameterType.Of(KeyvalueType.Vec3), ParameterValue.Of(new System.Numerics.Vector3(1f, 2f, 3f)));
        live.FireOutput("OnValue");
        world.Tick(Tick);

        _log.Skip(1).ShouldBe(["hear 1 '2.5'", "hear 1 '1 2 3'", "hear 1"]);
    }

    [Fact]
    public void A_fire_nobody_listens_to_queues_nothing_and_a_listener_hears_only_its_own_node_and_name()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode other = EntityRuntime.Place(_scene.Root, "other", "relay");
        EntityWorld world = Started(out _);
        world.Listeners.Add(other, "OnTrigger", 1);
        world.Listeners.Add(relay, "OnTriggered", 2);
        world.Listeners.Add(relay, "ontrigger", 3);

        EntityRuntime.Live(world, relay).FireOutput("OnTrigger");

        world.PendingEventCount.ShouldBe(0);
        world.Listeners.Count.ShouldBe(3);
        world.Listeners.CountOf(relay, "OnTrigger").ShouldBe(0);
        world.Listeners.CountOf(other, "OnTrigger").ShouldBe(1);
    }

    [Fact]
    public void A_removed_listener_hears_no_later_fire_and_the_runtime_still_gets_what_was_queued()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        EntityWorld world = Started(out _);
        world.Listeners.Add(relay, "OnTrigger", 1);
        world.Listeners.Add(relay, "OnTrigger", 2);
        Entity live = EntityRuntime.Live(world, relay);

        live.FireOutput("OnTrigger");
        world.Listeners.Remove(relay, "OnTrigger", 1).ShouldBeTrue();
        world.Listeners.Remove(relay, "OnTrigger", 1).ShouldBeFalse();
        live.FireOutput("OnTrigger");
        world.Tick(Tick);

        _log.Skip(1).ShouldBe(["hear 1", "hear 2", "hear 2"]);
        world.Listeners.Count.ShouldBe(1);
    }

    [Fact]
    public void An_input_a_script_sent_is_delivered_in_the_order_it_was_queued_with_no_caller_and_no_activator()
    {
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        EntityWorld world = Started(out _);
        Entity live = EntityRuntime.Live(world, door);

        world.QueueInput("door", "First");
        world.QueueScriptInput(live, "Open", "fast", "Scripts/opener.luau", 4).ShouldBeTrue();
        world.QueueScriptWake(9, ticks: 0).ShouldBeTrue();
        world.QueueInput("door", "Last");
        world.Tick(Tick);

        _log.Skip(1).ShouldBe(["door:First::-:-", "door:Open:fast:-:-", "wake 9", "door:Last::-:-"]);
    }

    [Fact]
    public void Listeners_and_sent_inputs_take_the_scripts_room_and_none_of_the_entities()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        EntityWorld world = Started(out _);
        world.MaxPendingEvents = 2;
        for (int listener = 1; listener <= 3; listener++)
            world.Listeners.Add(relay, "OnTrigger", listener);

        EntityRuntime.Live(world, relay).FireOutput("OnTrigger");
        world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", "Scripts/opener.luau", 4).ShouldBeFalse();
        world.QueueInput("door", "First");
        world.QueueInput("door", "Second");

        world.DiscardedEventCount.ShouldBe(2);
        _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem()
            .ShouldContain("a listener's call for output 'OnTrigger' at 'relay' was dropped");

        world.Tick(Tick);
        _log.Skip(1).ShouldBe(["hear 1", "hear 2", "door:First::-:-", "door:Second::-:-"]);
    }

    [Fact]
    public void A_listener_that_sets_off_its_own_output_is_cut_off_at_the_ticks_budget()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        EntityWorld world = Started(out FakeScriptRuntime runtime);
        world.MaxDispatchesPerTick = 6;
        world.Listeners.Add(relay, "OnTrigger", 1);
        Entity live = EntityRuntime.Live(world, relay);
        runtime.OnHear = _ => world.QueueScriptInput(live, "Trigger", "", "Scripts/loop.luau", 2);

        world.QueueInput("relay", "Trigger");
        world.Tick(Tick);

        // Three rounds of a call and the input it sends are the six.
        _log.Skip(1).ShouldBe(["hear 1", "hear 1", "hear 1"]);
        world.DispatchBudgetTripCount.ShouldBe(1);
        world.PendingEventCount.ShouldBe(0);
        _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem()
            .ShouldContain("a listener that sets off its own signal");

        world.Tick(Tick);
        _log.Count.ShouldBe(4);
    }

    [Fact]
    public void A_stop_empties_the_listeners_and_an_input_sent_after_it_is_refused()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        EntityWorld world = Started(out _);
        world.Listeners.Add(relay, "OnTrigger", 1);
        Entity live = EntityRuntime.Live(world, relay);

        world.Deactivate();

        world.Listeners.Count.ShouldBe(0);
        Should.Throw<InvalidOperationException>(() => world.QueueScriptInput(live, "Trigger", "", "Scripts/late.luau", 1));
    }

    [Fact]
    public void An_input_for_an_entity_of_another_world_is_refused()
    {
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        EntityWorld world = Started(out _);
        var elsewhere = new EntityWorld(new Scene("Elsewhere"), _logger, EntityRuntime.Catalog(_log));
        elsewhere.Activate();

        Should.Throw<ArgumentException>(
            () => elsewhere.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", "Scripts/opener.luau", 1));
    }

    [Fact]
    public void What_fired_while_the_level_spawned_is_a_missed_fire_once_and_what_fires_later_is_none()
    {
        SceneNode spawner = EntityRuntime.Place(_scene.Root, "spawner", "spawner");
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        EntityCatalog catalog = EntityRuntime.Catalog(_log);
        catalog.Add(new EntitySchema("spawner"), () => new SpawnFiringEntity());
        var world = new EntityWorld(_scene, _logger, catalog) { FixedDeltaTime = Tick };

        world.Activate();
        world.Tick(Tick);
        EntityRuntime.Live(world, relay).FireOutput("OnTrigger");
        EntityRuntime.Live(world, spawner).FireOutput("OnLater");

        world.Listeners.TakeMissedFire(relay, "OnTrigger").ShouldBeFalse();
        world.Listeners.TakeMissedFire(spawner, "OnLater").ShouldBeFalse();
        world.Listeners.TakeMissedFire(spawner, SpawnFiringEntity.OnSpawned).ShouldBeTrue();
        world.Listeners.TakeMissedFire(spawner, SpawnFiringEntity.OnSpawned).ShouldBeFalse();
    }

    [Fact]
    public void A_stop_forgets_what_fired_while_the_level_spawned()
    {
        SceneNode spawner = EntityRuntime.Place(_scene.Root, "spawner", "spawner");
        EntityCatalog catalog = EntityRuntime.Catalog(_log);
        catalog.Add(new EntitySchema("spawner"), () => new SpawnFiringEntity());
        var world = new EntityWorld(_scene, _logger, catalog) { FixedDeltaTime = Tick };
        world.Activate();

        world.Deactivate();

        world.Listeners.TakeMissedFire(spawner, SpawnFiringEntity.OnSpawned).ShouldBeFalse();
    }

    private EntityWorld Started(out FakeScriptRuntime runtime)
    {
        runtime = new FakeScriptRuntime(_log);
        var world = new EntityWorld(_scene, _logger, EntityRuntime.Catalog(_log))
        {
            FixedDeltaTime = Tick,
            Scripts = runtime,
        };

        world.Activate();
        return world;
    }
}
