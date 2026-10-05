using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a wiring view is told about a running level: how often each wire
/// fired, what is still waiting, what was lost, and the newest events.
/// </summary>
public sealed class WireActivityTraceTests
{
    private const float Tick = 1f / 60f;

    private readonly Scene _scene = new("Activity");
    private readonly WireActivityTrace _trace = new();

    [Fact]
    public void A_wire_counts_each_time_its_output_fires_it()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityRuntime.Wire(source, "OnOther", "target", "Ping");
        EntityRuntime.Wire(source, "OnGo", "target", "Ping");
        EntityWorld world = Started();
        Entity live = EntityRuntime.Live(world, source);

        live.FireOutput("OnGo");
        world.Tick(Tick);
        live.FireOutput("OnGo");
        world.Tick(Tick);

        LogicWireActivity wire = Captured().Wires.ShouldHaveSingleItem();
        wire.NodeId.ShouldBe(source.Id);
        wire.Wire.ShouldBe(1);
        wire.Fired.ShouldBe(2);
        wire.LastFiredTick.ShouldBe(1);
        wire.Missed.ShouldBe(0);
        wire.Waiting.ShouldBe(0);
    }

    [Fact]
    public void A_wire_that_waits_says_until_when_and_stops_once_it_is_delivered()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityRuntime.Wire(source, "OnGo", "target", "Ping", delay: 0.05f);
        EntityWorld world = Started();
        world.Tick(Tick);

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        float queuedAt = world.Time;

        LogicWireActivity waiting = Captured().Wires.ShouldHaveSingleItem();
        waiting.Waiting.ShouldBe(1);
        waiting.WaitingSince.ShouldBe(queuedAt);
        waiting.WaitingDue.ShouldBe(queuedAt + 0.05f);

        for (int i = 0; i < 4; i++)
            world.Tick(Tick);

        LogicWireActivity delivered = Captured().Wires.ShouldHaveSingleItem();
        delivered.Waiting.ShouldBe(0);
        delivered.WaitingDue.ShouldBe(0f);
        delivered.Fired.ShouldBe(1);
    }

    [Fact]
    public void A_wire_to_a_name_nothing_has_counts_a_miss()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        EntityRuntime.Wire(source, "OnGo", "nobody", "Ping");
        EntityWorld world = Started();

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.Tick(Tick);

        LogicPlayInfo play = Captured();
        play.Wires.ShouldHaveSingleItem().Missed.ShouldBe(1);

        LogicEventInfo lost = play.Recent.ShouldHaveSingleItem();
        lost.Kind.ShouldBe(EntityTraceKind.TargetMissing);
        lost.SourceName.ShouldBe("source");
        lost.Output.ShouldBe("OnGo");
        lost.TargetName.ShouldBe("nobody");
        lost.TargetId.ShouldBe(Guid.Empty);
        lost.Input.ShouldBe("Ping");
        lost.Wire.ShouldBe(0);
    }

    [Fact]
    public void An_input_the_target_does_not_take_counts_as_refused()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        EntityRuntime.Wire(source, "OnGo", "relay", "Open");
        EntityWorld world = Started();

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.Tick(Tick);

        LogicPlayInfo play = Captured();
        play.Wires.ShouldHaveSingleItem().Refused.ShouldBe(1);
        play.Recent.Select(seen => seen.Kind).ShouldBe(
            [EntityTraceKind.InputDelivered, EntityTraceKind.InputRefused]);
        play.Recent[0].TargetId.ShouldBe(relay.Id);
    }

    [Fact]
    public void An_input_no_wire_sent_is_remembered_and_counted_on_no_wire()
    {
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityWorld world = Started();

        world.QueueInput("target", "Ping");
        world.Tick(Tick);

        LogicPlayInfo play = Captured();
        play.Wires.ShouldBeEmpty();

        LogicEventInfo sent = play.Recent.ShouldHaveSingleItem();
        sent.SourceId.ShouldBe(Guid.Empty);
        sent.SourceName.ShouldBe("");
        sent.Wire.ShouldBe(-1);
        sent.TargetName.ShouldBe("target");
    }

    [Fact]
    public void Recent_keeps_the_newest_events_oldest_first()
    {
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityWorld world = Started();

        int sent = WireActivityTrace.RecentCapacity + 5;
        for (int i = 0; i < sent; i++)
        {
            world.QueueInput("target", "Ping");
            world.Tick(Tick);
        }

        LogicPlayInfo play = Captured();
        play.Recent.Count.ShouldBe(WireActivityTrace.RecentCapacity);
        play.Recent[0].Number.ShouldBe(5);
        play.Recent[^1].Number.ShouldBe(sent - 1);
        play.Recent[^1].Tick.ShouldBe(sent);
        play.Tick.ShouldBe(sent);
    }

    [Fact]
    public void While_no_wire_does_anything_a_capture_reuses_the_last_lists()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityRuntime.Wire(source, "OnGo", "target", "Ping");
        EntityWorld world = Started();
        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.Tick(Tick);

        LogicPlayInfo first = _trace.Capture(null, []);
        world.Tick(Tick);
        LogicPlayInfo second = _trace.Capture(first, []);

        second.Tick.ShouldBe(2);
        second.Wires.ShouldBeSameAs(first.Wires);
        second.Recent.ShouldBeSameAs(first.Recent);

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        _trace.Capture(second, []).Wires.ShouldNotBeSameAs(second.Wires);
    }

    [Fact]
    public void A_queued_input_the_world_dropped_stops_waiting()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityRuntime.Wire(source, "OnGo", "target", "Ping", delay: 0.05f);
        EntityWorld world = Started();
        EntityRuntime.Live(world, source).FireOutput("OnGo");

        // The world never says it delivered. Time passing is all the trace sees.
        _trace.EndTick(200, world.Time + 2f);

        Captured().Wires.ShouldHaveSingleItem().Waiting.ShouldBe(0);
    }

    [Fact]
    public void Clearing_forgets_everything()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        EntityRuntime.Wire(source, "OnGo", "nobody", "Ping");
        EntityWorld world = Started();
        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.Tick(Tick);
        LogicPlayInfo before = Captured();

        _trace.Clear();
        LogicPlayInfo after = _trace.Capture(before, []);

        after.Wires.ShouldBeEmpty();
        after.Recent.ShouldBeEmpty();
        after.Tick.ShouldBe(0);
    }

    [Fact]
    public void Two_watchers_share_the_worlds_one_trace_slot()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        EntityRuntime.Place(_scene.Root, "target", "recorder");
        EntityRuntime.Wire(source, "OnGo", "target", "Ping");
        var lines = new RecordingEntityTrace();
        var world = new EntityWorld(_scene, new CapturingLogger(), EntityRuntime.Catalog([]))
        {
            Trace = EntityTracePair.Join(lines, _trace),
        };
        world.Activate();

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.Tick(Tick);

        lines.Lines.ShouldContain("send source.OnGo -> target.Ping by=source");
        lines.EndedTicks.ShouldBe(new long[] { 0, 1 });
        Captured().Wires.ShouldHaveSingleItem().Fired.ShouldBe(1);
    }

    [Fact]
    public void Joining_a_watcher_with_nothing_is_that_watcher()
    {
        EntityTracePair.Join(_trace, null).ShouldBeSameAs(_trace);
        EntityTracePair.Join(null, _trace).ShouldBeSameAs(_trace);
        EntityTracePair.Join(null, null).ShouldBeNull();
    }

    private EntityWorld Started()
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), EntityRuntime.Catalog([])) { Trace = _trace };
        world.Activate();
        return world;
    }

    private LogicPlayInfo Captured() => _trace.Capture(null, []);
}
