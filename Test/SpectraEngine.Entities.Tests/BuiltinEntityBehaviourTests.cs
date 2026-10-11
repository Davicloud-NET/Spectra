using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

/// <summary>Generated entities run through a real <see cref="EntityWorld"/>.</summary>
public sealed class BuiltinEntityBehaviourTests
{
    private const float Tick = 1f / 60f;

    [Fact]
    public void A_relay_wired_to_a_counter_fires_OnHitMax_once_when_the_count_arrives_at_its_ceiling()
    {
        // Three triggers, ceiling of two: the third stays at the ceiling and
        // must not fire OnHitMax again.
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode relay = EntityRuntime.Place(scene.Root, "relay", "logic_relay");
        SceneNode counter = EntityRuntime.Place(scene.Root, "counter", "math_counter");
        counter.Entity!.SetValue("max", "2");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");

        EntityRuntime.Wire(relay, LogicRelay.OnTrigger, "counter", "Add", "1");
        EntityRuntime.Wire(counter, MathCounter.OnHitMax, "sink", "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        var live = EntityRuntime.Live<LogicRelay>(world, relay);
        var counted = EntityRuntime.Live<MathCounter>(world, counter);

        for (int i = 0; i < 3; i++)
        {
            EntityRuntime.Send(live, "Trigger").ShouldBeTrue();
            // Zero-delay wires: the whole cascade drains in one tick.
            world.Tick(Tick);
        }

        live.TriggerCount.ShouldBe(3);
        counted.Value.ShouldBe(2f);
        log.ShouldBe(["sink:Ping:"]);
    }

    [Fact]
    public void A_counter_that_leaves_its_ceiling_and_returns_announces_the_second_arrival()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode counter = EntityRuntime.Place(scene.Root, "counter", "math_counter");
        counter.Entity!.SetValue("max", "2");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(counter, MathCounter.OnHitMax, "sink", "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        var counted = EntityRuntime.Live<MathCounter>(world, counter);

        EntityRuntime.Send(counted, "Add", "2");
        world.Tick(Tick);
        EntityRuntime.Send(counted, "Subtract", "1");
        world.Tick(Tick);
        EntityRuntime.Send(counted, "Add", "1");
        world.Tick(Tick);

        log.Count.ShouldBe(2);
    }

    [Fact]
    public void An_unclamped_counter_announces_neither_bound()
    {
        // Min and max both zero means unclamped.
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode counter = EntityRuntime.Place(scene.Root, "counter", "math_counter");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(counter, MathCounter.OnHitMax, "sink", "Max");
        EntityRuntime.Wire(counter, MathCounter.OnHitMin, "sink", "Min");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        var counted = EntityRuntime.Live<MathCounter>(world, counter);
        counted.IsClamped.ShouldBeFalse();

        EntityRuntime.Send(counted, "Add", "5");
        EntityRuntime.Send(counted, "Subtract", "9");
        world.Tick(Tick);

        counted.Value.ShouldBe(-4f);
        log.ShouldBeEmpty();
    }

    [Fact]
    public void A_counter_reports_its_value_on_the_wire_as_the_parameter()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode counter = EntityRuntime.Place(scene.Root, "counter", "math_counter");
        counter.Entity!.SetValue("startvalue", "7");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(counter, MathCounter.OutValue, "sink", "Show");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        var counted = EntityRuntime.Live<MathCounter>(world, counter);
        counted.Value.ShouldBe(7f);

        EntityRuntime.Send(counted, "GetValue");
        world.Tick(Tick);

        log.ShouldBe(["sink:Show:7"]);
    }

    [Fact]
    public void An_Add_with_no_argument_adds_one()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode counter = EntityRuntime.Place(scene.Root, "counter", "math_counter");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        var counted = EntityRuntime.Live<MathCounter>(world, counter);
        EntityRuntime.Send(counted, "Add");
        world.Tick(Tick);

        counted.Value.ShouldBe(1f);
        counted.RefusedInputCount.ShouldBe(0);
    }

    [Fact]
    public void A_relay_that_starts_disabled_passes_nothing_until_it_is_enabled()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode relay = EntityRuntime.Place(scene.Root, "relay", "logic_relay");
        relay.Entity!.SetValue("startdisabled", "1");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(relay, LogicRelay.OnTrigger, "sink", "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        var live = EntityRuntime.Live<LogicRelay>(world, relay);
        live.StartDisabled.ShouldBeTrue();
        live.IsEnabled.ShouldBeFalse();

        EntityRuntime.Send(live, "Trigger");
        world.Tick(Tick);
        log.ShouldBeEmpty();

        EntityRuntime.Send(live, "Enable");
        EntityRuntime.Send(live, "Trigger");
        world.Tick(Tick);
        log.ShouldBe(["sink:Ping:"]);
    }

    [Fact]
    public void A_relay_refires_while_an_earlier_trigger_is_still_pending()
    {
        // The delay lives on the wire, so the relay has no pending state.
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode relay = EntityRuntime.Place(scene.Root, "relay", "logic_relay");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(relay, LogicRelay.OnTrigger, "sink", "Ping", delay: 0.5f);

        EntityWorld world = QuarterSecondWorld(scene, log);
        world.Activate();

        var live = EntityRuntime.Live<LogicRelay>(world, relay);
        EntityRuntime.Send(live, "Trigger");
        world.Tick(0.25f);
        EntityRuntime.Send(live, "Trigger");
        log.ShouldBeEmpty();

        world.Tick(0.25f);
        log.Count.ShouldBe(1);

        world.Tick(0.25f);
        log.Count.ShouldBe(2);
    }

    [Fact]
    public void Two_relays_that_share_a_name_and_fire_at_it_after_a_wait_are_held_to_the_budget_and_the_queue()
    {
        var logger = new CapturingLogger();
        var scene = new Scene("Entities");
        for (int i = 0; i < 2; i++)
        {
            SceneNode echo = EntityRuntime.Place(scene.Root, "echo", "logic_relay");
            EntityRuntime.Wire(echo, LogicRelay.OnTrigger, "echo", "Trigger", delay: 0.1f);
        }

        var world = new EntityWorld(scene, logger, EntityRuntime.Catalog([]));
        world.Activate();
        world.QueueInput("echo", "Trigger");

        // Each input reaches both relays, so the count doubles every tenth
        // of a second: a million in two seconds if nothing held it.
        int mostSent = 0;
        for (int i = 0; i < 180; i++)
        {
            world.Tick(Tick);
            mostSent = System.Math.Max(mostSent, world.LastTickDispatchCount);
        }

        mostSent.ShouldBe(world.MaxDispatchesPerTick);
        world.PendingEventCount.ShouldBe(world.MaxPendingEvents);
        logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem().ShouldContain("'echo'");
        logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem().ShouldContain("'echo'");
    }

    [Fact]
    public void A_timer_fires_on_its_interval_and_Enable_restarts_it()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode timer = EntityRuntime.Place(scene.Root, "timer", "logic_timer");
        timer.Entity!.SetValue("refiretime", "0.5");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(timer, LogicTimer.OnTimer, "sink", "Ping");

        EntityWorld world = QuarterSecondWorld(scene, log);
        world.Activate();

        var live = EntityRuntime.Live<LogicTimer>(world, timer);
        live.RefireInterval.ShouldBe(0.5f);
        live.IsEnabled.ShouldBeTrue();

        // Two ticks to an interval, so each line below is one tick.
        world.Tick(0.25f);
        log.ShouldBeEmpty();

        world.Tick(0.25f);
        log.Count.ShouldBe(1);

        world.Tick(0.25f);
        EntityRuntime.Send(live, "Enable");

        world.Tick(0.25f);
        log.Count.ShouldBe(1, "Enable restarts the interval, so the fire the old schedule held is dropped.");

        world.Tick(0.25f);
        log.Count.ShouldBe(2);
    }

    [Fact]
    public void A_timer_that_starts_disabled_runs_only_once_something_enables_it()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode timer = EntityRuntime.Place(scene.Root, "timer", "logic_timer");
        timer.Entity!.SetValue("startdisabled", "1");
        timer.Entity!.SetValue("refiretime", "0.5");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(timer, LogicTimer.OnTimer, "sink", "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log))
        {
            FixedDeltaTime = 0.5f,
        };
        world.Activate();

        var live = EntityRuntime.Live<LogicTimer>(world, timer);
        live.IsEnabled.ShouldBeFalse();

        world.Tick(0.5f);
        world.Tick(0.5f);
        log.ShouldBeEmpty();

        EntityRuntime.Send(live, "Enable");
        world.Tick(0.5f);
        log.Count.ShouldBe(1);
    }

    [Fact]
    public void A_timer_floors_a_refire_time_that_would_be_due_every_tick()
    {
        // A zero interval would be due forever and trip the dispatch budget.
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode timer = EntityRuntime.Place(scene.Root, "timer", "logic_timer");
        timer.Entity!.SetValue("refiretime", "0");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        EntityRuntime.Live<LogicTimer>(world, timer).RefireInterval.ShouldBe(LogicTimer.MinimumInterval);
    }

    [Fact]
    public void A_keyvalue_the_binder_cannot_read_keeps_the_default_and_is_reported()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");

        SceneNode timer = EntityRuntime.Place(scene.Root, "timer", "logic_timer");
        timer.Entity!.SetValue("refiretime", "half a second");

        var logger = new CapturingLogger();
        var world = new EntityWorld(scene, logger, EntityRuntime.Catalog(log));
        world.Activate();

        EntityRuntime.Live<LogicTimer>(world, timer).RefireInterval.ShouldBe(1f);
        logger.MessagesAt(LogLevel.Warning).ShouldContain(
            message => message.Contains("refiretime"),
            logger.Describe());
    }

    [Fact]
    public void An_input_no_built_in_class_declares_is_refused_rather_than_swallowed()
    {
        var log = new List<string>();
        var scene = new Scene("Entities");
        SceneNode relay = EntityRuntime.Place(scene.Root, "relay", "logic_relay");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log));
        world.Activate();

        EntityRuntime.Send(EntityRuntime.Live<LogicRelay>(world, relay), "Detonate").ShouldBeFalse();
    }

    // Half a second is two of its ticks, so a test can step through an interval.
    private static EntityWorld QuarterSecondWorld(Scene scene, List<string> log) =>
        new(scene, new CapturingLogger(), EntityRuntime.Catalog(log)) { FixedDeltaTime = 0.25f };
}
