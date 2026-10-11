using System.Numerics;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The entity world's side of scripting: when a runtime's scripts start, and
/// a waiting thread as one more event in the queue of wires and thinks.
/// </summary>
public sealed class EntityWorldScriptTests
{
    private const float Tick = 1f / 60f;

    private readonly List<string> _log = [];
    private readonly CapturingLogger _logger = new();
    private readonly Scene _scene = new("Scripted");

    [Fact]
    public void Scripts_start_once_at_the_head_of_the_first_tick_before_anything_is_delivered()
    {
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started(out _);
        world.QueueInput("bystander", "Ping");

        _log.ShouldBeEmpty();

        world.Tick(Tick);
        world.Tick(Tick);

        _log.ShouldBe(["start scripts", "bystander:Ping::-:-"]);
    }

    [Fact]
    public void A_world_that_is_activated_again_starts_its_scripts_again()
    {
        EntityWorld world = Started(out _);
        world.Tick(Tick);

        world.Deactivate();
        world.Activate();
        world.Tick(Tick);

        _log.ShouldBe(["start scripts", "start scripts"]);
    }

    [Fact]
    public void A_wake_and_an_input_due_on_one_tick_go_out_in_the_order_they_were_queued()
    {
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started(out _);

        world.QueueInput("bystander", "First", delay: 3 * Tick);
        world.QueueScriptWake(7, ticks: 3).ShouldBeTrue();
        world.QueueInput("bystander", "Last", delay: 3 * Tick);

        world.Tick(Tick);
        world.Tick(Tick);
        _log.ShouldBe(["start scripts"]);

        world.Tick(Tick);
        _log.Skip(1).ShouldBe(["bystander:First::-:-", "wake 7", "bystander:Last::-:-"]);
        world.PendingEventCount.ShouldBe(0);
    }

    [Fact]
    public void A_wake_with_no_wait_goes_out_in_the_drain_that_is_running_or_in_the_next_one()
    {
        EntityWorld world = Started(out FakeScriptRuntime runtime);

        // From the start of the level: the first tick's own drain takes it.
        runtime.OnStart = () => world.QueueScriptWake(1, ticks: 0);

        // From inside a drain: the same drain takes it.
        runtime.OnWake = token =>
        {
            if (token == 1)
                world.QueueScriptWake(2, ticks: 0);
        };

        world.Tick(Tick);
        _log.ShouldBe(["start scripts", "wake 1", "wake 2"]);

        // After a drain: the next tick's.
        world.QueueScriptWake(3, ticks: 0);
        _log.Count.ShouldBe(3);

        world.Tick(Tick);
        _log.Skip(3).ShouldBe(["wake 3"]);
    }

    [Fact]
    public void Waiting_wakes_past_the_budget_are_postponed_and_none_is_dropped()
    {
        EntityWorld world = Started(out _);
        world.MaxDispatchesPerTick = 2;
        for (int token = 1; token <= 5; token++)
            world.QueueScriptWake(token, ticks: 1);

        world.Tick(Tick);
        _log.Skip(1).ShouldBe(["wake 1", "wake 2"]);

        world.Tick(Tick);
        world.Tick(Tick);
        _log.Skip(1).ShouldBe(["wake 1", "wake 2", "wake 3", "wake 4", "wake 5"]);
        world.DiscardedEventCount.ShouldBe(0);
    }

    [Fact]
    public void A_wake_a_drain_made_past_its_budget_is_dropped_and_the_runtime_is_told()
    {
        EntityWorld world = Started(out FakeScriptRuntime runtime);
        world.MaxDispatchesPerTick = 3;

        // Every wake asks for the next one at once: a loop with no wait.
        runtime.OnWake = token => world.QueueScriptWake(token + 1, ticks: 0);
        world.QueueScriptWake(1, ticks: 1);

        world.Tick(Tick);

        // One was waiting and three more were made and sent. The fifth is the loop.
        _log.Skip(1).ShouldBe(["wake 1", "wake 2", "wake 3", "wake 4", "drop 5"]);
        world.DiscardedEventCount.ShouldBe(1);
        world.PendingEventCount.ShouldBe(0);

        // The line blames a script, not a wire between two entities.
        string error = _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem();
        error.ShouldContain("task.defer");
        error.ShouldNotContain("entities");
    }

    [Fact]
    public void More_waiting_wakes_than_the_budget_hold_up_nothing_an_entity_is_sent()
    {
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started(out _);
        world.MaxDispatchesPerTick = 2;
        for (int token = 1; token <= 5; token++)
            world.QueueScriptWake(token, ticks: 1);
        world.QueueInput("bystander", "Ping", delay: Tick);

        world.Tick(Tick);

        // The input was queued behind five wakes and still arrives on its tick.
        _log.Skip(1).ShouldBe(["wake 1", "wake 2", "bystander:Ping::-:-"]);
        string warning = _logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain("More than 2");
        warning.ShouldContain("script threads");

        world.Tick(Tick);
        world.Tick(Tick);
        _log.Skip(4).ShouldBe(["wake 3", "wake 4", "wake 5"]);
        world.DiscardedEventCount.ShouldBe(0);
    }

    [Fact]
    public void More_waiting_inputs_than_the_budget_hold_up_no_wake()
    {
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started(out _);
        world.MaxDispatchesPerTick = 2;
        for (int i = 1; i <= 3; i++)
            world.QueueInput("bystander", $"Ping{i}");
        world.QueueScriptWake(7, ticks: 1);

        world.Tick(Tick);

        _log.Skip(1).ShouldBe(["bystander:Ping1::-:-", "bystander:Ping2::-:-", "wake 7"]);
    }

    [Fact]
    public void A_loop_of_wakes_with_no_wait_drops_nothing_an_entity_made_in_that_drain()
    {
        SceneNode ping = EntityRuntime.Place(_scene.Root, "ping", "relay");
        SceneNode pong = EntityRuntime.Place(_scene.Root, "pong", "relay");
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityRuntime.Wire(ping, "OnTrigger", "pong", "Trigger");
        EntityRuntime.Wire(pong, "OnTrigger", "bystander", "Heard");
        EntityWorld world = Started(out FakeScriptRuntime runtime);
        world.MaxDispatchesPerTick = 2;

        // The two wires fire one after the other in the drain, between the
        // wakes of a loop that never waits.
        runtime.OnWake = token => world.QueueScriptWake(token + 1, ticks: 0);
        world.QueueScriptWake(1, ticks: 1);
        world.QueueInput("ping", "Trigger");

        world.Tick(Tick);

        _log.Skip(1).ShouldBe(["wake 1", "wake 2", "bystander:Heard::ping:pong", "wake 3", "drop 4"]);
        world.DiscardedEventCount.ShouldBe(1);
    }

    [Fact]
    public void A_loop_between_two_entities_drops_no_wake_made_in_that_drain()
    {
        SceneNode ping = EntityRuntime.Place(_scene.Root, "ping", "relay");
        SceneNode pong = EntityRuntime.Place(_scene.Root, "pong", "relay");
        EntityRuntime.Wire(ping, "OnTrigger", "pong", "Trigger");
        EntityRuntime.Wire(pong, "OnTrigger", "ping", "Trigger");
        EntityWorld world = Started(out FakeScriptRuntime runtime);
        world.MaxDispatchesPerTick = 4;

        // Three wakes, each made by the one before with no wait, as a defer does.
        runtime.OnWake = token =>
        {
            if (token < 4)
                world.QueueScriptWake(token + 1, ticks: 0);
        };
        world.QueueInput("ping", "Trigger");
        world.QueueScriptWake(1, ticks: 1);

        world.Tick(Tick);

        world.DispatchBudgetTripCount.ShouldBe(1);
        _log.Skip(1).ShouldBe(["wake 1", "wake 2", "wake 3", "wake 4"]);
    }

    [Fact]
    public void A_wake_that_waits_longer_than_a_level_can_count_is_not_queued()
    {
        EntityWorld world = Started(out _);

        world.QueueScriptWake(1, WholeTicks.Never).ShouldBeFalse();
        world.QueueScriptWake(2, WholeTicks.Never - 1).ShouldBeTrue();

        world.PendingEventCount.ShouldBe(1);
    }

    [Fact]
    public void A_wake_is_refused_when_the_queue_is_full_and_on_a_world_that_has_stopped()
    {
        EntityWorld world = Started(out _);
        world.MaxPendingEvents = 2;

        world.QueueScriptWake(1, ticks: 5).ShouldBeTrue();
        world.QueueScriptWake(2, ticks: 5).ShouldBeTrue();
        world.QueueScriptWake(3, ticks: 5).ShouldBeFalse();
        _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem().ShouldContain("2 script threads are waiting");

        world.Deactivate();
        Should.Throw<InvalidOperationException>(() => world.QueueScriptWake(4, ticks: 5));
    }

    [Fact]
    public void Wakes_that_fill_their_queue_take_no_room_from_what_entities_send()
    {
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started(out _);
        world.MaxPendingEvents = 2;

        world.QueueScriptWake(1, ticks: 5).ShouldBeTrue();
        world.QueueScriptWake(2, ticks: 5).ShouldBeTrue();
        world.QueueScriptWake(3, ticks: 5).ShouldBeFalse();
        world.QueueInput("bystander", "First");
        world.QueueInput("bystander", "Second");
        world.QueueInput("bystander", "Third");

        world.Tick(Tick);

        _log.Skip(1).ShouldBe(["bystander:First::-:-", "bystander:Second::-:-"]);
        world.PendingEventCount.ShouldBe(2);
        world.DiscardedEventCount.ShouldBe(2);

        // The wakes still hold their two places, and the inputs have theirs back.
        world.QueueInput("bystander", "Fourth");
        world.QueueInput("bystander", "Fifth");
        world.QueueScriptWake(4, ticks: 5).ShouldBeFalse();
        world.Tick(Tick);

        _log.Skip(3).ShouldBe(["bystander:Fourth::-:-", "bystander:Fifth::-:-"]);
    }

    [Fact]
    public void A_move_that_is_refused_gives_its_reason_to_a_caller_that_asks_and_logs_nothing()
    {
        SceneNode wall = _scene.Root.CreateChild("Wall");
        wall.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(-Vector3.One, Vector3.One);
        EntityWorld world = Started(out _);
        Transform moved = wall.LocalTransform with { Position = new Vector3(0f, 5f, 0f) };

        world.TrySetLocalTransform(wall, in moved, out string? refusal).ShouldBeFalse();

        refusal.ShouldNotBeNull().ShouldStartWith("a world brush sits at or below it");
        wall.LocalPosition.ShouldBe(Vector3.Zero);
        world.RefusedMoveCount.ShouldBe(1);
        _logger.MessagesAt(LogLevel.Error).ShouldBeEmpty();

        // The entities' own call still logs, once a node.
        world.SetLocalTransform(wall, in moved).ShouldBeFalse();
        world.SetLocalTransform(wall, in moved).ShouldBeFalse();
        _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem().ShouldContain("'Wall'");
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
