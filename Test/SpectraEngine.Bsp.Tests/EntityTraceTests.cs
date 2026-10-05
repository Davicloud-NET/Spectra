using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What an <see cref="IEntityTrace"/> is told and in what order, and that
/// being watched changes nothing about a world.
/// </summary>
public sealed class EntityTraceTests
{
    private const float Tick = 1f / 60f;

    [Fact]
    public void A_fired_output_is_traced_even_with_nothing_wired_to_it()
    {
        var scene = new Scene("Trace");
        EntityRuntime.Place(scene.Root, "relay", "relay");
        var trace = new RecordingEntityTrace();
        EntityWorld world = Started(scene, trace);

        world.QueueInput("relay", "Trigger");
        world.Tick(Tick);

        RecordingEntityTrace.Entry fired =
            trace.Entries.Single(entry => entry.Kind == EntityTraceKind.OutputFired);
        fired.Line.ShouldBe("fire relay.OnTrigger by=relay");
        fired.WiresQueued.ShouldBe(0);
        fired.WiresSpent.ShouldBe(0);
    }

    [Fact]
    public void A_delivery_is_traced_before_the_outputs_it_causes()
    {
        var scene = new Scene("Trace");
        SceneNode relay = EntityRuntime.Place(scene.Root, "relay", "relay");
        EntityRuntime.Place(scene.Root, "target", "recorder");
        EntityRuntime.Wire(relay, "OnTrigger", "target", "Ping", "loud");
        var trace = new RecordingEntityTrace();
        EntityWorld world = Started(scene, trace);

        world.QueueInput("relay", "Trigger");
        world.Tick(Tick);

        trace.Lines.ShouldBe(new[]
        {
            "queue - -> relay.Trigger",
            "send - -> relay.Trigger",
            "fire relay.OnTrigger by=relay",
            "queue relay.OnTrigger -> target.Ping(loud) by=relay",
            "send relay.OnTrigger -> target.Ping(loud) by=relay",
        });
    }

    [Fact]
    public void A_target_that_matches_nothing_is_traced_as_missing()
    {
        var scene = new Scene("Trace");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        EntityRuntime.Wire(source, "OnGo", "nobody", "Ping");
        var trace = new RecordingEntityTrace();
        EntityWorld world = Started(scene, trace);

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.QueueInput("dor*", "Open");
        world.Tick(Tick);

        trace.Lines.ShouldBe(new[]
        {
            "fire source.OnGo by=source",
            "queue source.OnGo -> nobody.Ping by=source",
            "queue - -> dor*.Open",
            "miss source.OnGo -> nobody.Ping by=source",
            "miss - -> dor*.Open",
        });
    }

    [Fact]
    public void An_input_for_an_entity_that_has_gone_is_traced_as_missing()
    {
        var scene = new Scene("Trace");
        SceneNode button = EntityRuntime.Place(scene.Root, "button", "recorder");
        var trace = new RecordingEntityTrace();
        EntityWorld world = Started(scene, trace);
        Entity gone = EntityRuntime.Live(world, button);

        world.QueueDespawn(gone);
        world.Tick(Tick);
        world.QueueInput(gone, "Use");
        world.Tick(Tick);

        trace.Lines.ShouldBe(new[] { "queue - -> button.Use", "miss - -> button.Use" });
    }

    [Fact]
    public void An_input_the_class_does_not_accept_is_traced_as_refused()
    {
        var scene = new Scene("Trace");
        EntityRuntime.Place(scene.Root, "relay", "relay");
        var trace = new RecordingEntityTrace();
        EntityWorld world = Started(scene, trace);

        world.QueueInput("relay", "Open");
        world.Tick(Tick);

        // Sent first, then refused: the entity was asked.
        trace.Lines.ShouldBe(new[]
        {
            "queue - -> relay.Open",
            "send - -> relay.Open",
            "deny - -> relay.Open",
        });
    }

    [Fact]
    public void A_spent_wire_is_counted_and_not_queued()
    {
        var scene = new Scene("Trace");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        EntityRuntime.Place(scene.Root, "once", "recorder");
        EntityRuntime.Place(scene.Root, "always", "recorder");
        EntityRuntime.Wire(source, "OnGo", "once", "Ping", timesToFire: 1);
        EntityRuntime.Wire(source, "OnGo", "always", "Ping");
        var trace = new RecordingEntityTrace();
        EntityWorld world = Started(scene, trace);
        Entity live = EntityRuntime.Live(world, source);

        live.FireOutput("OnGo");
        world.Tick(Tick);
        trace.Entries.Clear();

        live.FireOutput("OnGo");

        trace.Lines.ShouldBe(new[]
        {
            "fire source.OnGo by=source",
            "queue source.OnGo -> always.Ping by=source",
        });
        trace.Entries[0].WiresQueued.ShouldBe(1);
        trace.Entries[0].WiresSpent.ShouldBe(1);
    }

    [Fact]
    public void A_wire_that_waits_is_traced_with_the_time_it_is_due()
    {
        var scene = new Scene("Trace");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        EntityRuntime.Place(scene.Root, "target", "recorder");
        EntityRuntime.Wire(source, "OnGo", "target", "Ping", delay: 0.5f);
        var trace = new RecordingEntityTrace();
        EntityWorld world = Started(scene, trace);
        world.Tick(Tick);

        EntityRuntime.Live(world, source).FireOutput("OnGo");

        RecordingEntityTrace.Entry queued =
            trace.Entries.Single(entry => entry.Kind == EntityTraceKind.InputQueued);
        queued.Tick.ShouldBe(1);
        queued.Time.ShouldBe(world.Time);
        queued.DueTime.ShouldBe(world.Time + 0.5f);
    }

    [Fact]
    public void A_queued_input_names_its_wire_by_its_place_in_the_authored_list()
    {
        // The runtime groups wires by output. The trace still counts them the
        // way the map lists them.
        var scene = new Scene("Trace");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        EntityRuntime.Place(scene.Root, "target", "recorder");
        EntityRuntime.Wire(source, "OnGo", "target", "First");
        EntityRuntime.Wire(source, "OnOther", "target", "Second");
        EntityRuntime.Wire(source, "OnGo", "target", "Third");
        var trace = new RecordingEntityTrace();
        EntityWorld world = Started(scene, trace);

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.Tick(Tick);

        trace.Entries
            .Where(entry => entry.Kind == EntityTraceKind.InputQueued)
            .Select(entry => entry.Wire)
            .ShouldBe([0, 2]);
        trace.Entries
            .Where(entry => entry.Kind == EntityTraceKind.InputDelivered)
            .Select(entry => entry.Wire)
            .ShouldBe([0, 2]);
    }

    [Fact]
    public void A_fired_output_and_an_input_no_wire_sent_name_no_wire()
    {
        var scene = new Scene("Trace");
        EntityRuntime.Place(scene.Root, "relay", "relay");
        var trace = new RecordingEntityTrace();
        EntityWorld world = Started(scene, trace);

        world.QueueInput("relay", "Trigger");
        world.Tick(Tick);

        trace.Entries.ShouldAllBe(entry => entry.Wire == -1);
        trace.Entries.Single(entry => entry.Kind == EntityTraceKind.OutputFired).Sequence.ShouldBe(-1);
    }

    [Fact]
    public void Everything_that_happens_to_one_queued_input_carries_its_sequence()
    {
        var scene = new Scene("Trace");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "recorder");
        EntityRuntime.Place(scene.Root, "lamp", "recorder");
        EntityRuntime.Place(scene.Root, "lamp", "relay");
        EntityRuntime.Wire(source, "OnGo", "lamp", "Ping");
        EntityRuntime.Wire(source, "OnGo", "nobody", "Ping");
        var trace = new RecordingEntityTrace();
        EntityWorld world = Started(scene, trace);

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        world.Tick(Tick);

        // Two lamps share the name: one takes Ping, the relay refuses it.
        long toLamps = trace.Entries.First(entry => entry.Kind == EntityTraceKind.InputQueued).Sequence;
        trace.Entries.Where(entry => entry.Sequence == toLamps).Select(entry => entry.Kind).ShouldBe(
        [
            EntityTraceKind.InputQueued,
            EntityTraceKind.InputDelivered,
            EntityTraceKind.InputDelivered,
            EntityTraceKind.InputRefused,
        ]);

        long toNobody = trace.Entries.Single(entry => entry.Kind == EntityTraceKind.TargetMissing).Sequence;
        toNobody.ShouldNotBe(toLamps);
    }

    [Fact]
    public void Delivery_order_is_the_same_with_and_without_a_trace()
    {
        List<string> unwatched = RunEveryPath(trace: null);

        var trace = new RecordingEntityTrace();
        List<string> watched = RunEveryPath(trace);

        // The run is only a fair comparison if it went through every trace site.
        trace.Entries.Select(entry => entry.Kind).Distinct().Count()
            .ShouldBe(Enum.GetValues<EntityTraceKind>().Length);
        watched.ShouldBe(unwatched);
    }

    [Fact]
    public void A_trace_set_before_activation_sees_the_outputs_fired_while_spawning()
    {
        var log = new List<string>();
        var scene = new Scene("Trace");
        SceneNode source = EntityRuntime.Place(scene.Root, "source", "spawner");
        EntityRuntime.Place(scene.Root, "later", "recorder");
        EntityRuntime.Wire(source, SpawnFiringEntity.OnSpawned, "later", "Ping");

        EntityCatalog catalog = EntityRuntime.Catalog(log);
        catalog.Add(new EntitySchema("spawner"), () => new SpawnFiringEntity());
        var trace = new RecordingEntityTrace();
        var world = new EntityWorld(scene, new CapturingLogger(), catalog) { Trace = trace };

        world.Activate();

        trace.Lines.ShouldBe(new[]
        {
            "fire source.OnSpawned by=source",
            "queue source.OnSpawned -> later.Ping by=source",
        });
        trace.Entries.ShouldAllBe(entry => entry.Tick == 0);

        world.Tick(Tick);

        trace.Entries[^1].Line.ShouldBe("send source.OnSpawned -> later.Ping by=source");
        trace.Entries[^1].Tick.ShouldBe(1);
    }

    [Fact]
    public void Spawning_and_every_tick_tell_the_trace_when_they_are_done()
    {
        var scene = new Scene("Trace");
        EntityRuntime.Place(scene.Root, "relay", "relay");
        var trace = new RecordingEntityTrace();
        EntityWorld world = Started(scene, trace);

        trace.EndedTicks.ShouldBe(new long[] { 0 });

        world.Tick(Tick);
        world.Tick(Tick);

        trace.EndedTicks.ShouldBe(new long[] { 0, 1, 2 });
    }

    [Fact]
    public void A_warmed_up_cascade_with_no_trace_allocates_nothing()
    {
        LeastAllocatedByACascade(trace: null).ShouldBe(0L);
    }

    [Fact]
    public void A_trace_that_keeps_nothing_costs_the_world_no_allocation()
    {
        // The world hands over a struct. Any text is the trace's to build.
        var trace = new CountingTrace();

        LeastAllocatedByACascade(trace).ShouldBe(0L);
        trace.Events.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Counting_wire_activity_costs_a_warmed_up_cascade_no_allocation()
    {
        var trace = new WireActivityTrace();

        LeastAllocatedByACascade(trace).ShouldBe(0L);
        trace.Capture(null, []).Wires.Count.ShouldBeGreaterThan(0);
    }

    // Eight relays in a chain, each also firing at two more by prefix. Returns
    // the least any round of a hundred cascades allocated: a one-off from the
    // runtime is not the dispatch path, and a cost per dispatch shows in
    // every round.
    private static long LeastAllocatedByACascade(IEntityTrace? trace)
    {
        const int links = 8;
        var scene = new Scene("Trace");
        for (int i = 0; i < links; i++)
        {
            SceneNode relay = EntityRuntime.Place(scene.Root, $"relay{i}", "relay");
            if (i + 1 < links)
                EntityRuntime.Wire(relay, "OnTrigger", $"relay{i + 1}", "Trigger");
            EntityRuntime.Wire(relay, "OnTrigger", "sink*", "Trigger");
        }

        EntityRuntime.Place(scene.Root, "sink_a", "relay");
        EntityRuntime.Place(scene.Root, "sink_b", "relay");
        EntityWorld world = Started(scene, trace);

        // Grows the event heap and the resolve list.
        RunCascades(world, 200);

        long least = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            RunCascades(world, 100);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        world.LastTickDispatchCount.ShouldBe(links * 2);
        return least;
    }

    private static void RunCascades(EntityWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            world.QueueInput("relay0", "Trigger");
            world.Tick(Tick);
        }
    }

    private static EntityWorld Started(Scene scene, IEntityTrace? trace)
    {
        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([])) { Trace = trace };
        world.Activate();
        return world;
    }

    // A level with every path in it: a prefix, two entities that share a
    // name, a wire that waits, one that runs out, a name nothing has, an
    // input a class refuses, a loop that feeds itself and inputs from a host.
    private static List<string> RunEveryPath(IEntityTrace? trace)
    {
        var log = new List<string>();
        var scene = new Scene("Trace");
        SceneNode start = EntityRuntime.Place(scene.Root, "start", "relay");
        SceneNode echo = EntityRuntime.Place(scene.Root, "echo", "relay");
        SceneNode north = EntityRuntime.Place(scene.Root, "door_north", "recorder");
        EntityRuntime.Place(scene.Root, "door_south", "recorder");
        SceneNode lamp = EntityRuntime.Place(scene.Root, "lamp", "recorder");
        lamp.Entity!.SetValue("tag", "lamp_a");
        EntityRuntime.Place(scene.Root, "lamp", "recorder").Entity!.SetValue("tag", "lamp_b");

        EntityRuntime.Wire(start, "OnTrigger", "door*", "Open");
        EntityRuntime.Wire(start, "OnTrigger", "echo", "Trigger", delay: 0.05f);
        EntityRuntime.Wire(start, "OnTrigger", "nobody", "Open");
        EntityRuntime.Wire(start, "OnTrigger", "lamp", "TurnOn", "bright", timesToFire: 1);
        EntityRuntime.Wire(start, "OnTrigger", "echo", "Open");
        EntityRuntime.Wire(echo, "OnTrigger", "lamp", "Flicker");
        EntityRuntime.Wire(echo, "OnTrigger", TargetNameIndex.ActivatorToken, "Ping");
        EntityRuntime.Wire(echo, "OnTrigger", "start", "Trigger", delay: 0.1f);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(log)) { Trace = trace };
        world.Activate();

        world.QueueInput("start", "Trigger");
        world.QueueInput(EntityRuntime.Live(world, lamp), "Use", "hard", EntityRuntime.Live(world, north));

        for (int tick = 1; tick <= 40; tick++)
        {
            if (tick == 10)
                world.QueueInput("door*", "Knock", delay: 0.05f);

            world.Tick(Tick);
            log.Add($"tick {tick}: {world.LastTickDispatchCount} sent, {world.PendingEventCount} waiting");
        }

        return log;
    }

    private sealed class CountingTrace : IEntityTrace
    {
        public int Events { get; private set; }

        public void Record(in EntityTraceEvent traced) => Events++;

        public void EndTick(long tick, float time)
        {
        }
    }
}
