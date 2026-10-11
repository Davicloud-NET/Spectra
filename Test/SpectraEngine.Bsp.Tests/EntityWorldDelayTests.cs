using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Where a wait lands: a wire's delay, a delayed input and a think are all
/// counted in whole ticks when they are queued, and events due on one tick
/// run in the order they were queued.
/// </summary>
public sealed class EntityWorldDelayTests
{
    private const float Tick = 1f / 60f;
    private const int TicksInAnHour = 60 * 60 * 60;

    private readonly List<string> _log = [];
    private readonly Scene _scene = new("Delays");
    private readonly RecordingEntityTrace _trace = new();

    [Fact]
    public void A_wire_that_waits_a_tenth_of_a_second_lands_six_ticks_on_after_an_hour_of_ticks()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityRuntime.Wire(source, "OnGo", "target", "Ping", delay: 0.1f);
        EntityWorld world = Started();
        Run(world, TicksInAnHour);

        EntityRuntime.Live(world, source).FireOutput("OnGo");

        Run(world, 5);
        _log.ShouldBeEmpty();

        world.Tick(Tick);
        _log.ShouldHaveSingleItem().ShouldStartWith("target:Ping");
        world.TickNumber.ShouldBe(TicksInAnHour + 6);
    }

    [Fact]
    public void An_input_queued_by_hand_a_tenth_of_a_second_ahead_lands_six_ticks_on_after_an_hour_of_ticks()
    {
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityWorld world = Started();
        Run(world, TicksInAnHour);

        world.QueueInput("target", "Ping", delay: 0.1f);

        Run(world, 5);
        _log.ShouldBeEmpty();

        world.Tick(Tick);
        _log.ShouldHaveSingleItem().ShouldStartWith("target:Ping");
    }

    [Fact]
    public void A_think_asked_for_in_a_tenth_of_a_second_comes_six_ticks_on_after_an_hour_of_ticks()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "thinker", "thinker");
        EntityWorld world = Started();
        Run(world, TicksInAnHour);
        Entity thinker = EntityRuntime.Live(world, node);

        thinker.SetNextThinkIn(0.1f);

        thinker.NextThinkTick.ShouldBe(TicksInAnHour + 6);
        Run(world, 5);
        _log.ShouldBeEmpty();

        world.Tick(Tick);
        _log.ShouldBe([$"think:thinker:{TicksInAnHour + 6}"]);
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 1)]
    [InlineData(1f, 1)]
    [InlineData(1.5f, 2)]
    [InlineData(2f, 2)]
    public void A_wire_fired_while_a_tick_drains_lands_its_delay_later_in_whole_ticks(float steps, int ticksOn)
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityRuntime.Wire(relay, "OnTrigger", "target", "Ping", delay: steps * Tick);
        EntityWorld world = Started();
        Run(world, 3);

        // Delivered on tick 4, and the relay fires its wire in that drain.
        world.QueueInput("relay", "Trigger");
        Run(world, 4);

        RecordingEntityTrace.Entry sent = _trace.Entries.Single(
            entry => entry.Kind == EntityTraceKind.InputDelivered && entry.Line.Contains("target.Ping"));
        sent.Tick.ShouldBe(4 + ticksOn);
    }

    [Fact]
    public void A_think_asked_for_with_no_delay_while_a_tick_drains_comes_in_that_drain()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "thinker", "thinker");
        EntityWorld world = Started();
        world.Tick(Tick);

        world.QueueInput(EntityRuntime.Live(world, node), "ThinkNow");
        world.Tick(Tick);

        _log.ShouldBe(["think:thinker:2"]);
    }

    [Fact]
    public void A_think_asked_for_at_a_world_time_comes_on_the_first_tick_at_or_after_it()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "thinker", "thinker");
        EntityWorld world = Started();
        Entity thinker = EntityRuntime.Live(world, node);
        Run(world, 10);

        thinker.SetNextThink(world.Time + 0.1f);

        thinker.NextThinkTick.ShouldBe(16);
        thinker.NextThinkTime.ShouldBe((float)(16 * (double)Tick));

        // A time already past is the next drain.
        thinker.SetNextThink(0f);
        thinker.NextThinkTick.ShouldBe(10);
        world.Tick(Tick);
        _log.ShouldBe(["think:thinker:11"]);
    }

    [Fact]
    public void A_think_asked_for_at_a_world_time_lands_where_the_same_delay_would_for_half_an_hour()
    {
        const int ticksInHalfAnHour = 30 * 60 * 60;
        float[] delays = [0.04f, 0.1f, 0.5f, 1f, 3f];
        SceneNode node = EntityRuntime.Place(_scene.Root, "thinker", "thinker");
        EntityWorld world = Started();
        world.Trace = null;
        Entity thinker = EntityRuntime.Live(world, node);

        string? firstMiss = null;
        for (int i = 0; i < ticksInHalfAnHour && firstMiss is null; i++)
        {
            world.Tick(Tick);
            foreach (float delay in delays)
            {
                thinker.SetNextThink(world.Time + delay);
                long landed = thinker.NextThinkTick - world.TickNumber;
                if (landed != world.TicksIn(delay))
                    firstMiss ??= $"{delay} s at tick {world.TickNumber} took {landed} ticks";
            }

            thinker.CancelThink();
        }

        firstMiss.ShouldBeNull();
    }

    [Fact]
    public void An_entity_with_no_think_pending_says_so()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "thinker", "thinker");
        EntityWorld world = Started();
        Entity thinker = EntityRuntime.Live(world, node);

        thinker.NextThinkTick.ShouldBe(Entity.NoThink);
        thinker.NextThinkTime.ShouldBe(float.PositiveInfinity);

        thinker.SetNextThinkIn(1f);
        thinker.NextThinkTick.ShouldBe(60);
        thinker.CancelThink();

        thinker.NextThinkTick.ShouldBe(Entity.NoThink);
        Run(world, 70);
        _log.ShouldBeEmpty();
    }

    [Fact]
    public void A_think_asked_for_after_a_wait_too_long_to_count_never_comes_and_waits_in_no_queue()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "thinker", "thinker");
        EntityWorld world = Started();
        Entity thinker = EntityRuntime.Live(world, node);

        thinker.SetNextThinkIn(float.PositiveInfinity);

        thinker.NextThinkTick.ShouldBe(Entity.NoThink);
        world.PendingEventCount.ShouldBe(0);
    }

    [Fact]
    public void Events_due_on_one_tick_run_in_the_order_they_were_queued_whichever_tick_that_was()
    {
        SceneNode node = EntityRuntime.Place(_scene.Root, "thinker", "thinker");
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityWorld world = Started();
        Entity thinker = EntityRuntime.Live(world, node);

        // All four are due on tick 3.
        world.QueueInput("target", "First", delay: 3 * Tick);
        world.Tick(Tick);
        thinker.SetNextThinkIn(2 * Tick);
        world.QueueInput("target", "Third", delay: 2 * Tick);
        world.Tick(Tick);
        world.QueueInput("target", "Fourth", delay: Tick);
        _log.ShouldBeEmpty();

        world.Tick(Tick);

        _log.Select(line => line.Split(':')[1]).ShouldBe(["First", "thinker", "Third", "Fourth"]);
    }

    [Fact]
    public void An_input_queued_between_ticks_with_no_delay_goes_ahead_of_what_waited_for_the_next_tick()
    {
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityWorld world = Started();
        world.QueueInput("target", "Waited", delay: 2 * Tick);
        world.Tick(Tick);

        // Due on tick 1, the one just run, so tick 2 takes it first.
        world.QueueInput("target", "Late");
        world.Tick(Tick);

        _log.Select(line => line.Split(':')[1]).ShouldBe(["Late", "Waited"]);
    }

    private EntityWorld Started()
    {
        EntityCatalog catalog = EntityRuntime.Catalog(_log);
        catalog.Add(new EntitySchema("thinker"), () => new ThinkingEntity(_log));

        var world = new EntityWorld(_scene, new CapturingLogger(), catalog) { Trace = _trace };
        world.Activate();
        return world;
    }

    private static void Run(EntityWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            world.Tick(Tick);
    }

    // Logs "think:name:tick" each time it thinks. ThinkNow asks for a think
    // with no delay.
    private sealed class ThinkingEntity(List<string> log) : Entity
    {
        protected internal override void Think() => log.Add($"think:{TargetName}:{World.TickNumber}");

        public override bool AcceptInput(string input, ref EntityInputContext context)
        {
            if (input != "ThinkNow")
                return false;

            SetNextThinkIn(0f);
            return true;
        }
    }
}
