using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a wiring view is told about the scripts of a running level: the
/// outputs they hear, the inputs they have sent, how often each carried
/// something, and what they did last.
/// </summary>
public sealed class ScriptWiringTraceTests
{
    private const float Tick = 1f / 60f;
    private const string File = "Scripts/main.luau";

    private readonly Scene _scene = new("Scripted");
    private readonly WireActivityTrace _trace = new();

    [Fact]
    public void A_listener_of_a_script_is_a_wire_from_the_output_to_the_script()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode main = Script("Main");
        EntityWorld world = Started();

        world.Listeners.Add(relay, "OnTrigger", 1, new ScriptPlace(main.Id, File, 42));

        LogicScriptWire wire = Captured(world).Scripts.Wires.ShouldHaveSingleItem();
        wire.Script.ShouldBe(main.Id);
        wire.Entity.ShouldBe(relay.Id);
        wire.Port.ShouldBe("OnTrigger");
        wire.IsSend.ShouldBeFalse();
        wire.File.ShouldBe(File);
        wire.Line.ShouldBe(42);
    }

    [Fact]
    public void A_listener_no_script_stands_behind_is_no_wire()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        EntityWorld world = Started();

        world.Listeners.Add(relay, "OnTrigger", 1);

        Captured(world).Scripts.Wires.ShouldBeEmpty();
    }

    [Fact]
    public void A_connection_that_ends_leaves_the_picture()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode main = Script("Main");
        EntityWorld world = Started();
        world.Listeners.Add(relay, "OnTrigger", 1, new ScriptPlace(main.Id, File, 3));
        world.Listeners.Add(relay, "OnTrigger", 2, new ScriptPlace(main.Id, File, 9));
        LogicPlayInfo both = Captured(world);

        world.Listeners.Remove(relay, "OnTrigger", 1).ShouldBeTrue();
        LogicPlayInfo one = _trace.Capture(both, [], world);

        both.Scripts.Wires.Count.ShouldBe(2);
        one.Scripts.Wires.ShouldHaveSingleItem().Line.ShouldBe(9);
    }

    [Fact]
    public void Two_functions_connected_on_one_line_are_one_wire()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode main = Script("Main");
        EntityWorld world = Started();

        world.Listeners.Add(relay, "OnTrigger", 1, new ScriptPlace(main.Id, File, 3));
        world.Listeners.Add(relay, "OnTrigger", 2, new ScriptPlace(main.Id, File, 3));

        Captured(world).Scripts.Wires.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_script_that_sends_every_tick_is_one_wire_that_counts_each_send()
    {
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        SceneNode main = Script("Main");
        EntityWorld world = Started();

        for (int i = 0; i < 5; i++)
        {
            world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", new ScriptPlace(main.Id, File, 57));
            world.Tick(Tick);
        }

        LogicPlayInfo play = Captured(world);
        LogicScriptWire wire = play.Scripts.Wires.ShouldHaveSingleItem();
        wire.Script.ShouldBe(main.Id);
        wire.Entity.ShouldBe(door.Id);
        wire.Port.ShouldBe("Open");
        wire.IsSend.ShouldBeTrue();
        wire.Line.ShouldBe(57);
        play.ScriptActivity.ShouldHaveSingleItem().ShouldBe(new LogicScriptActivity(5, 4));
    }

    [Fact]
    public void The_same_input_sent_from_two_lines_is_two_wires()
    {
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        SceneNode main = Script("Main");
        EntityWorld world = Started();
        Entity live = EntityRuntime.Live(world, door);

        world.QueueScriptInput(live, "Open", "", new ScriptPlace(main.Id, File, 3));
        world.QueueScriptInput(live, "Open", "", new ScriptPlace(main.Id, "Scripts/doors.luau", 3));
        world.Tick(Tick);

        Captured(world).Scripts.Wires.Select(wire => wire.File).ShouldBe(["Scripts/doors.luau", File]);
    }

    [Fact]
    public void A_fired_output_counts_on_the_wire_of_each_script_that_hears_it()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode main = Script("Main");
        SceneNode beep = Script("Beep");
        EntityWorld world = Started();
        world.Listeners.Add(relay, "OnTrigger", 1, new ScriptPlace(main.Id, File, 42));
        world.Listeners.Add(relay, "OnTrigger", 2, new ScriptPlace(beep.Id, "Scripts/beep.luau", 3));
        world.Listeners.Add(relay, "OnOther", 3, new ScriptPlace(beep.Id, "Scripts/beep.luau", 4));

        world.QueueInput("relay", "Trigger");
        world.Tick(Tick);
        world.QueueInput("relay", "Trigger");
        world.Tick(Tick);

        LogicPlayInfo play = Captured(world);
        var fired = play.Scripts.Wires
            .Select((wire, index) => (wire.Port, wire.Line, play.ScriptActivity[index].Fired))
            .OrderBy(wire => wire.Line)
            .ToArray();
        fired.ShouldBe([("OnTrigger", 3, 2), ("OnOther", 4, 0), ("OnTrigger", 42, 2)]);
        play.ScriptActivity.Max(activity => activity.LastFiredTick).ShouldBe(2);
    }

    [Fact]
    public void What_a_script_heard_and_sent_is_among_the_recent_events_with_its_name_and_its_line()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        SceneNode main = Script("Main");
        EntityWorld world = Started();
        world.Listeners.Add(relay, "OnTrigger", 1, new ScriptPlace(main.Id, "Scripts/doors.luau", 42));

        world.QueueInput("relay", "Trigger");
        world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "fast", new ScriptPlace(main.Id, File, 57));
        world.Tick(Tick);

        IReadOnlyList<LogicEventInfo> recent = Captured(world).Recent;
        recent.Count.ShouldBe(3);

        recent[0].Kind.ShouldBe(EntityTraceKind.InputDelivered);
        recent[0].IsOfScript.ShouldBeFalse();

        LogicEventInfo heard = recent[1];
        heard.Kind.ShouldBe(EntityTraceKind.OutputFired);
        heard.SourceName.ShouldBe("relay");
        heard.Output.ShouldBe("OnTrigger");
        heard.ScriptId.ShouldBe(main.Id);
        heard.ScriptName.ShouldBe("Main");
        heard.ScriptFile.ShouldBe("Scripts/doors.luau");
        heard.ScriptLine.ShouldBe(42);

        LogicEventInfo sent = recent[2];
        sent.Kind.ShouldBe(EntityTraceKind.InputDelivered);
        sent.TargetName.ShouldBe("door");
        sent.Input.ShouldBe("Open");
        sent.Parameter.ShouldBe("fast");
        sent.ScriptName.ShouldBe("Main");
        sent.ScriptFile.ShouldBe(File);
        sent.ScriptLine.ShouldBe(57);
    }

    [Fact]
    public void A_script_that_sends_every_tick_is_one_recent_event_and_leaves_the_wires_theirs()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        EntityRuntime.Wire(source, "OnGo", "door", "Ping");
        SceneNode main = Script("Main");
        EntityWorld world = Started();

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        for (int i = 0; i < 2 * WireActivityTrace.RecentCapacity; i++)
        {
            world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", new ScriptPlace(main.Id, File, 57));
            world.Tick(Tick);
        }

        IReadOnlyList<LogicEventInfo> recent = Captured(world).Recent;
        recent.Count.ShouldBe(2);
        recent[0].Input.ShouldBe("Ping");
        recent[1].Input.ShouldBe("Open");
        recent[1].Tick.ShouldBe(2 * WireActivityTrace.RecentCapacity);
        recent[1].Number.ShouldBeGreaterThan(recent[0].Number);
    }

    [Fact]
    public void While_scripts_do_nothing_new_a_view_gets_the_lists_it_had()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        SceneNode main = Script("Main");
        EntityWorld world = Started();
        world.Listeners.Add(relay, "OnTrigger", 1, new ScriptPlace(main.Id, File, 42));
        world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", new ScriptPlace(main.Id, File, 57));
        world.Tick(Tick);
        LogicPlayInfo before = Captured(world);

        world.Tick(Tick);
        LogicPlayInfo quiet = _trace.Capture(before, [], world);
        world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", new ScriptPlace(main.Id, File, 57));
        world.Tick(Tick);
        LogicPlayInfo sentAgain = _trace.Capture(quiet, [], world);

        quiet.Scripts.ShouldBeSameAs(before.Scripts);
        quiet.ScriptActivity.ShouldBeSameAs(before.ScriptActivity);

        // The same wires carried more, so only what they carried is new.
        sentAgain.Scripts.ShouldBeSameAs(before.Scripts);
        sentAgain.ScriptActivity.ShouldNotBeSameAs(before.ScriptActivity);
    }

    [Fact]
    public void A_thread_that_waits_for_an_output_in_a_loop_keeps_one_wire_and_the_same_list()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode main = Script("Main");
        EntityWorld world = Started();
        world.Listeners.Add(relay, "OnTrigger", 1, new ScriptPlace(main.Id, File, 8));
        LogicPlayInfo before = Captured(world);

        // What a wait in a loop does each time round: a new listener, and the old one gone.
        world.Listeners.Add(relay, "OnTrigger", 2, new ScriptPlace(main.Id, File, 8));
        world.Listeners.Remove(relay, "OnTrigger", 1);

        _trace.Capture(before, [], world).Scripts.ShouldBeSameAs(before.Scripts);
    }

    [Fact]
    public void A_capture_with_no_level_says_nothing_of_scripts()
    {
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        SceneNode main = Script("Main");
        EntityWorld world = Started();
        world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", new ScriptPlace(main.Id, File, 57));
        world.Tick(Tick);

        LogicPlayInfo play = _trace.Capture(null, []);

        play.Scripts.ShouldBeSameAs(LogicScriptWiring.None);
        play.ScriptActivity.ShouldBeEmpty();
    }

    [Fact]
    public void A_trace_that_is_cleared_forgets_what_scripts_sent_and_still_lists_who_listens()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        SceneNode main = Script("Main");
        EntityWorld world = Started();
        world.Listeners.Add(relay, "OnTrigger", 1, new ScriptPlace(main.Id, File, 42));
        world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", new ScriptPlace(main.Id, File, 57));
        world.Tick(Tick);
        LogicPlayInfo before = Captured(world);

        _trace.Clear();
        LogicPlayInfo after = _trace.Capture(before, [], world);

        before.Scripts.Wires.Count.ShouldBe(2);
        after.Scripts.Wires.ShouldHaveSingleItem().IsSend.ShouldBeFalse();
        after.Recent.ShouldBeEmpty();
    }

    [Fact]
    public void The_wires_come_in_one_order_whatever_order_the_scripts_connected_in()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode other = EntityRuntime.Place(_scene.Root, "other", "relay");
        SceneNode main = Script("Main");
        EntityWorld world = Started();
        var first = new ScriptPlace(main.Id, File, 1);
        var second = new ScriptPlace(main.Id, File, 2);

        world.Listeners.Add(relay, "OnTrigger", 1, first);
        world.Listeners.Add(other, "OnTrigger", 2, second);
        LogicPlayInfo oneWay = Captured(world);

        world.Listeners.Remove(relay, "OnTrigger", 1);
        world.Listeners.Remove(other, "OnTrigger", 2);
        world.Listeners.Add(other, "OnTrigger", 3, second);
        world.Listeners.Add(relay, "OnTrigger", 4, first);
        LogicPlayInfo otherWay = _trace.Capture(oneWay, [], world);

        otherWay.Scripts.ShouldBeSameAs(oneWay.Scripts);
    }

    private SceneNode Script(string name)
    {
        SceneNode node = _scene.Root.CreateChild(name);
        node.Script = new Script($"Scripts/{name.ToLowerInvariant()}.luau");
        return node;
    }

    private EntityWorld Started()
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), EntityRuntime.Catalog([]))
        {
            Trace = _trace,
            Scripts = new FakeScriptRuntime(),
        };

        world.Activate();
        return world;
    }

    private LogicPlayInfo Captured(EntityWorld world) => _trace.Capture(null, [], world);
}
