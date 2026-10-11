using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What the dispatch budget counts and what it drops: only events made in the
/// drain that trips it. An event that was waiting when the drain began runs.
/// </summary>
public sealed class EntityWorldBudgetTests
{
    private const float Tick = 1f / 60f;
    private const int Budget = 8;

    private readonly List<string> _log = [];
    private readonly CapturingLogger _logger = new();
    private readonly Scene _scene = new("Budget");

    [Fact]
    public void More_waiting_events_than_the_budget_all_run_and_trip_nothing()
    {
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started();

        for (int i = 0; i < Budget * 3; i++)
            world.QueueInput("bystander", $"Ping{i}");
        world.Tick(Tick);

        _log.Count.ShouldBe(Budget * 3);
        world.LastTickDispatchCount.ShouldBe(Budget * 3);
        world.DispatchBudgetTripCount.ShouldBe(0);
        world.DiscardedEventCount.ShouldBe(0);
        _logger.MessagesAt(LogLevel.Error).ShouldBeEmpty();
    }

    [Fact]
    public void A_zero_delay_loop_trips_the_budget_and_everything_that_was_waiting_still_runs_that_tick()
    {
        SceneNode ping = EntityRuntime.Place(_scene.Root, "ping", "relay");
        SceneNode pong = EntityRuntime.Place(_scene.Root, "pong", "relay");
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityRuntime.Wire(ping, "OnTrigger", "pong", "Trigger");
        EntityRuntime.Wire(pong, "OnTrigger", "ping", "Trigger");
        EntityWorld world = Started();

        // Waiting from two ticks back, from before the loop starts and from after.
        world.QueueInput("bystander", "Delayed", delay: 2 * Tick);
        world.Tick(Tick);
        world.QueueInput("bystander", "Before");
        world.QueueInput("ping", "Trigger");
        for (int i = 0; i < Budget * 2; i++)
            world.QueueInput("bystander", $"After{i}");

        world.Tick(Tick);

        // The loop is named and what it had made is gone. Its hops alternate,
        // and the one past an even budget is aimed at pong.
        world.DispatchBudgetTripCount.ShouldBe(1);
        world.DiscardedEventCount.ShouldBe(1);
        string error = _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem();
        error.ShouldContain("OnTrigger");
        error.ShouldContain("'pong'");

        // Every bystander's input arrived, in the order it was queued.
        string[] heard = [.. _log.Select(line => line.Split(':')[1])];
        heard.Length.ShouldBe(2 + (Budget * 2));
        heard[0].ShouldBe("Before");
        heard[1].ShouldBe("After0");
        heard[^1].ShouldBe("Delayed");

        // The waiting ones, the loop's first input, then the budget's worth of the loop.
        world.LastTickDispatchCount.ShouldBe(heard.Length + 1 + Budget);
        world.PendingEventCount.ShouldBe(0);
    }

    [Fact]
    public void An_event_waiting_for_a_later_tick_outlives_a_trip_and_arrives_on_its_tick()
    {
        SceneNode ping = EntityRuntime.Place(_scene.Root, "ping", "relay");
        SceneNode pong = EntityRuntime.Place(_scene.Root, "pong", "relay");
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityRuntime.Wire(ping, "OnTrigger", "pong", "Trigger");
        EntityRuntime.Wire(pong, "OnTrigger", "ping", "Trigger");
        EntityWorld world = Started();
        world.QueueInput("bystander", "Later", delay: 3 * Tick);
        world.QueueInput("ping", "Trigger");

        world.Tick(Tick);

        world.DispatchBudgetTripCount.ShouldBe(1);
        world.PendingEventCount.ShouldBe(1);
        _log.ShouldBeEmpty();

        world.Tick(Tick);
        _log.ShouldBeEmpty();
        world.Tick(Tick);

        _log.ShouldHaveSingleItem().ShouldStartWith("bystander:Later");
        world.DispatchBudgetTripCount.ShouldBe(1);
    }

    [Fact]
    public void A_think_that_asks_to_think_again_at_once_trips_the_budget_and_is_named()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "spinner", "spinner");
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started();
        var spinner = (SpinningEntity)EntityRuntime.Live(world, node);

        spinner.SetNextThinkIn(0f);
        world.QueueInput("bystander", "Ping");
        world.Tick(Tick);

        // The think that was waiting, then the budget's worth of what it made.
        spinner.Thinks.ShouldBe(1 + Budget);
        world.DispatchBudgetTripCount.ShouldBe(1);
        string error = _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem();
        error.ShouldContain("a think");
        error.ShouldContain("'spinner'");
        _log.ShouldHaveSingleItem().ShouldStartWith("bystander:Ping");

        // Dropped, so the next tick is quiet.
        world.Tick(Tick);
        spinner.Thinks.ShouldBe(1 + Budget);
        world.DispatchBudgetTripCount.ShouldBe(1);
    }

    [Fact]
    public void A_cascade_the_size_of_the_budget_runs_whole_however_much_was_waiting()
    {
        // A chain of relays, one hop for each event the budget allows.
        for (int i = 0; i < Budget; i++)
        {
            SceneNode relay = EntityRuntime.Place(_scene.Root, $"relay{i}", "relay");
            EntityRuntime.Wire(relay, "OnTrigger", i + 1 < Budget ? $"relay{i + 1}" : "bystander", "Trigger");
        }

        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started();

        for (int i = 0; i < Budget * 2; i++)
            world.QueueInput("bystander", "Ping");
        world.QueueInput("relay0", "Trigger");
        world.Tick(Tick);

        world.DispatchBudgetTripCount.ShouldBe(0);
        _log.Count.ShouldBe((Budget * 2) + 1);
        _log[^1].ShouldStartWith("bystander:Trigger");
    }

    private EntityWorld Started()
    {
        EntityCatalog catalog = EntityRuntime.Catalog(_log);
        catalog.Add(new EntitySchema("spinner"), () => new SpinningEntity());

        var world = new EntityWorld(_scene, _logger, catalog) { MaxDispatchesPerTick = Budget };
        world.Activate();
        return world;
    }

    // Every think asks for another with no delay.
    private sealed class SpinningEntity : Entity
    {
        public int Thinks { get; private set; }

        protected internal override void Think()
        {
            Thinks++;
            SetNextThinkIn(0f);
        }
    }
}
