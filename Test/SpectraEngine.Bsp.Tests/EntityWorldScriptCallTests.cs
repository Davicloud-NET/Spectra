using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A listener's call for a signal a script set off, as one more event in the
/// queue. It goes the way of a wake, and no log line takes it for a thread
/// that waits.
/// </summary>
public sealed class EntityWorldScriptCallTests
{
    private const float Tick = 1f / 60f;
    private const string Signal = "Signal AttributeChanged";

    private readonly List<string> _log = [];
    private readonly CapturingLogger _logger = new();
    private readonly Scene _scene = new("Scripted");

    [Fact]
    public void A_call_is_handed_back_as_a_wake_is_in_the_order_it_was_queued_in()
    {
        EntityRuntime.Place(_scene.Root, "bystander", "recorder");
        EntityWorld world = Started(out FakeScriptRuntime runtime);

        // One made inside the drain goes out in the same drain.
        runtime.OnWake = token =>
        {
            if (token == 7)
                world.QueueScriptCall(8, Signal).ShouldBeTrue();
        };
        world.QueueInput("bystander", "First");
        world.QueueScriptCall(7, Signal).ShouldBeTrue();
        world.QueueInput("bystander", "Last");

        world.Tick(Tick);

        _log.Skip(1).ShouldBe(["bystander:First::-:-", "wake 7", "bystander:Last::-:-", "wake 8"]);
        world.PendingEventCount.ShouldBe(0);
    }

    [Fact]
    public void A_call_the_queue_has_no_room_for_is_not_said_to_be_a_thread_that_waits()
    {
        EntityWorld world = Started(out _);
        world.MaxPendingEvents = 2;

        world.QueueScriptCall(1, Signal).ShouldBeTrue();
        world.QueueScriptCall(2, Signal).ShouldBeTrue();
        world.QueueScriptCall(3, Signal).ShouldBeFalse();

        string error = _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem();
        error.ShouldContain("a listener's call for 'Signal AttributeChanged'");
        error.ShouldContain("everything a script sends or is to hear");
        error.ShouldNotContain("threads are waiting");
        error.ShouldNotContain("The script that asked gets an error");
        world.DiscardedEventCount.ShouldBe(1);
    }

    [Fact]
    public void More_waiting_calls_than_the_budget_run_late_and_are_not_said_to_be_threads()
    {
        EntityWorld world = Started(out _);
        world.MaxDispatchesPerTick = 2;
        for (int token = 1; token <= 5; token++)
            world.QueueScriptCall(token, Signal);

        world.Tick(Tick);

        _log.Skip(1).ShouldBe(["wake 1", "wake 2"]);
        string warning = _logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain("More than 2 events of scripts");
        warning.ShouldContain("a listener's call for 'Signal AttributeChanged'");
        warning.ShouldNotContain("script threads");

        world.Tick(Tick);
        world.Tick(Tick);
        _log.Skip(1).ShouldBe(["wake 1", "wake 2", "wake 3", "wake 4", "wake 5"]);
        world.DiscardedEventCount.ShouldBe(0);
    }

    [Fact]
    public void A_call_a_drain_made_past_its_budget_is_dropped_and_the_runtime_is_told()
    {
        EntityWorld world = Started(out FakeScriptRuntime runtime);
        world.MaxDispatchesPerTick = 3;

        // Every call sets the signal off again: a listener that hears itself.
        runtime.OnWake = token => world.QueueScriptCall(token + 1, Signal);
        world.QueueScriptCall(1, Signal);

        world.Tick(Tick);

        _log.Skip(1).ShouldBe(["wake 1", "wake 2", "wake 3", "wake 4", "drop 5"]);
        world.DiscardedEventCount.ShouldBe(1);
        string error = _logger.MessagesAt(LogLevel.Error).ShouldHaveSingleItem();
        error.ShouldContain("a listener's call for 'Signal AttributeChanged'");
        error.ShouldContain("a listener that sets off its own signal");
    }

    [Fact]
    public void A_call_is_refused_on_a_world_that_has_stopped()
    {
        EntityWorld world = Started(out _);

        world.Deactivate();

        Should.Throw<InvalidOperationException>(() => world.QueueScriptCall(1, Signal));
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
