using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a tick sends and what it leaves: the budget's worth of what was
/// waiting, the budget's worth of what it makes, and a queue that fills and
/// goes no further. An event that was waiting is sent late, never dropped.
/// </summary>
public sealed class EntityWorldBudgetTests
{
    private const float Tick = 1f / 60f;
    private const int Budget = 8;
    private const int QueueLimit = Budget * 4;

    private readonly List<string> _log = [];
    private readonly CapturingLogger _logger = new();
    private readonly Scene _scene = new("Budget");

    [Fact]
    public void More_waiting_events_than_the_budget_go_out_over_the_next_ticks_in_order_and_none_is_lost()
    {
        const int pings = (Budget * 2) + 3;
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started();

        world.QueueInput("bystander", "Later", delay: 2 * Tick);
        for (int i = 0; i < pings; i++)
            world.QueueInput("bystander", $"Ping{i}");

        world.Tick(Tick);
        _log.Count.ShouldBe(Budget);
        world.LastTickDispatchCount.ShouldBe(Budget);
        world.PendingEventCount.ShouldBe(Budget + 3 + 1);

        world.Tick(Tick);
        _log.Count.ShouldBe(Budget * 2);

        // Later was due a tick ago. It keeps its place behind the pings.
        world.Tick(Tick);
        world.LastTickDispatchCount.ShouldBe(3 + 1);
        world.PendingEventCount.ShouldBe(0);
        Heard().ShouldBe([.. Enumerable.Range(0, pings).Select(i => $"Ping{i}"), "Later"]);

        world.DiscardedEventCount.ShouldBe(0);
        world.DispatchBudgetTripCount.ShouldBe(2);
    }

    [Fact]
    public void Falling_behind_is_a_warning_said_once_a_level_and_names_the_first_event_left()
    {
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started();

        FallBehindAndCatchUp(world);
        FallBehindAndCatchUp(world);

        // Nothing was lost, so it is no error.
        world.DispatchBudgetTripCount.ShouldBe(2);
        _logger.MessagesAt(LogLevel.Error).ShouldBeEmpty();
        string warning = _logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain($"More than {Budget}");
        warning.ShouldContain($"input 'Ping{Budget}'");
        warning.ShouldContain("'bystander'");

        world.Deactivate();
        world.Activate();
        FallBehindAndCatchUp(world);

        _logger.MessagesAt(LogLevel.Warning).Count.ShouldBe(2);
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

        // Waiting from two ticks back, from before the loop starts and from
        // after. With the loop's own first input that is the budget's worth.
        world.QueueInput("bystander", "Delayed", delay: 2 * Tick);
        world.Tick(Tick);
        world.QueueInput("bystander", "Before");
        world.QueueInput("ping", "Trigger");
        for (int i = 0; i < Budget - 3; i++)
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
        string[] heard = Heard();
        heard.Length.ShouldBe(Budget - 1);
        heard[0].ShouldBe("Before");
        heard[1].ShouldBe("After0");
        heard[^1].ShouldBe("Delayed");

        // The waiting ones, the loop's first input, then the budget's worth of the loop.
        world.LastTickDispatchCount.ShouldBe(Budget + Budget);
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
        var spinner = (RethinkingEntity)EntityRuntime.Live(world, node);

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
    public void A_cascade_the_size_of_the_budget_runs_whole_in_a_tick_that_had_the_budget_waiting()
    {
        // A chain of relays, one hop for each event the budget allows.
        for (int i = 0; i < Budget; i++)
        {
            SceneNode relay = EntityRuntime.Place(_scene.Root, $"relay{i}", "relay");
            EntityRuntime.Wire(relay, "OnTrigger", i + 1 < Budget ? $"relay{i + 1}" : "bystander", "Trigger");
        }

        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started();

        for (int i = 0; i < Budget - 1; i++)
            world.QueueInput("bystander", "Ping");
        world.QueueInput("relay0", "Trigger");
        world.Tick(Tick);

        world.DispatchBudgetTripCount.ShouldBe(0);
        world.LastTickDispatchCount.ShouldBe(Budget + Budget);
        _log.Count.ShouldBe(Budget);
        _log[^1].ShouldStartWith("bystander:Trigger");
    }

    [Fact]
    public void A_loop_that_waits_a_tick_and_doubles_never_makes_a_tick_send_more_than_the_budget()
    {
        Doubler();
        EntityWorld world = Started();
        world.QueueInput("doubler", "Trigger");

        int most = 0;
        for (int i = 0; i < 16; i++)
        {
            world.Tick(Tick);
            most = Math.Max(most, world.LastTickDispatchCount);
        }

        most.ShouldBe(Budget);
    }

    [Fact]
    public void A_loop_that_waits_a_tick_and_doubles_fills_the_queue_and_no_further()
    {
        Doubler();
        EntityWorld world = Started();
        world.QueueInput("doubler", "Trigger");

        int most = 0;
        for (int i = 0; i < 100; i++)
        {
            world.Tick(Tick);
            most = Math.Max(most, world.PendingEventCount);
        }

        most.ShouldBe(QueueLimit);
        world.DiscardedEventCount.ShouldBeGreaterThan(0);

        // Said once, though the queue stays full.
        string error = _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem();
        error.ShouldContain($"full at {QueueLimit}");
        error.ShouldContain("output 'OnTrigger' sending 'Trigger'");
        error.ShouldContain("'doubler'");
    }

    [Fact]
    public void A_queue_that_fills_again_after_it_emptied_is_an_error_again()
    {
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started();

        for (int round = 0; round < 2; round++)
        {
            for (int i = 0; i < QueueLimit + 2; i++)
                world.QueueInput("bystander", "Ping", delay: 2 * Tick);
            Run(world, 2 + (QueueLimit / Budget));
            world.PendingEventCount.ShouldBe(0);
        }

        world.DiscardedEventCount.ShouldBe(4);
        _logger.MessagesAt(LogLevel.Error).Count.ShouldBe(2);
    }

    [Fact]
    public void An_entity_that_waits_again_each_time_it_is_woken_keeps_running_in_a_full_queue()
    {
        Doubler();
        SceneNode node = EntityRuntime.Place(_scene.Root, "pulse", "pulse");
        EntityWorld world = Started();
        var pulse = (RethinkingEntity)EntityRuntime.Live(world, node);
        pulse.SetNextThinkIn(Tick);
        world.QueueInput("doubler", "Trigger");

        Run(world, 100);
        world.PendingEventCount.ShouldBe(QueueLimit);
        int thinksByThen = pulse.Thinks;
        Run(world, 100);

        // Late, since the level is behind, and never lost.
        pulse.Thinks.ShouldBeGreaterThan(thinksByThen + 10);
        pulse.NextThinkTick.ShouldNotBe(Entity.NoThink);
    }

    [Fact]
    public void A_think_the_queue_has_no_room_for_does_not_come_and_reads_as_none()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "pulse", "pulse");
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started();
        var pulse = (RethinkingEntity)EntityRuntime.Live(world, node);
        for (int i = 0; i < QueueLimit; i++)
            world.QueueInput("bystander", "Ping", delay: 1f);

        pulse.SetNextThinkIn(Tick);

        pulse.NextThinkTick.ShouldBe(Entity.NoThink);
        world.PendingEventCount.ShouldBe(QueueLimit);
        world.DiscardedEventCount.ShouldBe(1);
        string error = _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem();
        error.ShouldContain("a think");
        error.ShouldContain("'pulse'");

        Run(world, 5);
        pulse.Thinks.ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_budget_or_a_queue_limit_below_one_is_refused(int limit)
    {
        var world = new EntityWorld(_scene, _logger, EntityRuntime.Catalog(_log));

        Should.Throw<ArgumentOutOfRangeException>(() => world.MaxDispatchesPerTick = limit);
        Should.Throw<ArgumentOutOfRangeException>(() => world.MaxPendingEvents = limit);
    }

    // Each Trigger sends itself two more, one tick on.
    private void Doubler()
    {
        SceneNode doubler = EntityRuntime.Place(_scene.Root, "doubler", "relay");
        EntityRuntime.Wire(doubler, "OnTrigger", "doubler", "Trigger", delay: Tick);
        EntityRuntime.Wire(doubler, "OnTrigger", "doubler", "Trigger", delay: Tick);
    }

    private EntityWorld Started()
    {
        EntityCatalog catalog = EntityRuntime.Catalog(_log);
        catalog.Add(new EntitySchema("spinner"), () => new RethinkingEntity(0f));
        catalog.Add(new EntitySchema("pulse"), () => new RethinkingEntity(Tick));

        var world = new EntityWorld(_scene, _logger, catalog)
        {
            MaxDispatchesPerTick = Budget,
            MaxPendingEvents = QueueLimit,
        };
        world.Activate();
        return world;
    }

    // One event more than a tick may send, then the tick that sends it.
    private static void FallBehindAndCatchUp(EntityWorld world)
    {
        for (int i = 0; i < Budget + 1; i++)
            world.QueueInput("bystander", $"Ping{i}");

        Run(world, 2);
        world.PendingEventCount.ShouldBe(0);
    }

    private string[] Heard() => [.. _log.Select(line => line.Split(':')[1])];

    private static void Run(EntityWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            world.Tick(Tick);
    }

    // Every think asks for another, this long on.
    private sealed class RethinkingEntity(float delay) : Entity
    {
        public int Thinks { get; private set; }

        protected internal override void Think()
        {
            Thinks++;
            SetNextThinkIn(delay);
        }
    }
}
