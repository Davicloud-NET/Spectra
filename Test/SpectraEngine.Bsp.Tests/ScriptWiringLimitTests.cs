using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a wiring view is told of scripts where there is a great deal of it,
/// and of scripts that were switched off: the wires keep their events, a cut
/// is said, ended connections use up no room, and a stopped script leaves.
/// </summary>
public sealed class ScriptWiringLimitTests
{
    private const float Tick = 1f / 60f;
    private const string File = "Scripts/main.luau";

    private readonly Scene _scene = new("Scripted");
    private readonly WireActivityTrace _trace = new();

    [Fact]
    public void Many_lines_of_code_busy_every_tick_leave_what_the_wires_did_in_the_list()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        EntityRuntime.Wire(source, "OnGo", "door", "Ping");
        SceneNode main = Script("Main");
        EntityWorld world = Started();
        const int lines = 12;

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        for (int tick = 0; tick < 10; tick++)
        {
            for (int line = 1; line <= lines; line++)
                world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", new ScriptPlace(main.Id, File, line));

            world.Tick(Tick);
        }

        IReadOnlyList<LogicEventInfo> recent = Captured(world).Recent;
        recent.Where(happened => !happened.IsOfScript).ShouldHaveSingleItem().Input.ShouldBe("Ping");
        recent.Count(happened => happened.IsOfScript).ShouldBe(lines);
        recent.Select(happened => happened.Number).ShouldBeInOrder();
    }

    [Fact]
    public void More_lines_of_code_than_the_list_holds_push_out_their_own_and_never_a_wires()
    {
        SceneNode source = EntityRuntime.Place(_scene.Root, "source", "recorder");
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        EntityRuntime.Wire(source, "OnGo", "door", "Ping");
        SceneNode main = Script("Main");
        EntityWorld world = Started();

        EntityRuntime.Live(world, source).FireOutput("OnGo");
        for (int line = 1; line <= 3 * WireActivityTrace.RecentCapacity; line++)
        {
            world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", new ScriptPlace(main.Id, File, line));
            world.Tick(Tick);
        }

        IReadOnlyList<LogicEventInfo> recent = Captured(world).Recent;
        recent.Count(happened => happened.IsOfScript).ShouldBe(WireActivityTrace.RecentCapacity);
        recent[0].Input.ShouldBe("Ping");
        recent[^1].ScriptLine.ShouldBe(3 * WireActivityTrace.RecentCapacity);
    }

    [Fact]
    public void What_a_script_that_was_switched_off_has_sent_leaves_the_picture()
    {
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        SceneNode hangs = Script("Hangs");
        SceneNode stays = Script("Stays");
        EntityWorld world = Started();
        world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", new ScriptPlace(hangs.Id, File, 3));
        world.QueueScriptInput(EntityRuntime.Live(world, door), "Close", "", new ScriptPlace(stays.Id, File, 4));
        world.Tick(Tick);
        LogicPlayInfo both = Captured(world);

        world.StoppedScripts.Add(hangs.Id);
        LogicPlayInfo one = _trace.Capture(both, [], world);

        both.Scripts.Wires.Count.ShouldBe(2);
        one.Scripts.Wires.ShouldHaveSingleItem().Script.ShouldBe(stays.Id);
        one.ScriptActivity.ShouldHaveSingleItem().Fired.ShouldBe(1);
    }

    [Fact]
    public void A_script_that_was_switched_off_makes_no_wire_of_what_its_last_thread_still_sends()
    {
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        SceneNode hangs = Script("Hangs");
        EntityWorld world = Started();
        world.StoppedScripts.Add(hangs.Id);

        world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", new ScriptPlace(hangs.Id, File, 3));
        world.Tick(Tick);

        Captured(world).Scripts.Wires.ShouldBeEmpty();
    }

    [Fact]
    public void A_level_that_starts_again_has_no_stopped_scripts()
    {
        SceneNode hangs = Script("Hangs");
        EntityWorld world = Started();
        world.StoppedScripts.Add(hangs.Id);

        world.Deactivate();

        world.StoppedScripts.Count.ShouldBe(0);
        world.StoppedScripts.Contains(hangs.Id).ShouldBeFalse();
    }

    [Fact]
    public void A_listener_that_is_hidden_is_no_wire_and_is_still_called()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode main = Script("Main");
        var heard = new List<long>();
        EntityWorld world = Started(new FakeScriptRuntime { OnHear = heard.Add });
        world.Listeners.Add(relay, "OnTrigger", 7, new ScriptPlace(main.Id, File, 42));
        LogicPlayInfo listed = Captured(world);

        world.Listeners.Hide(relay, "OnTrigger", 7).ShouldBeTrue();
        world.Listeners.Hide(relay, "OnTrigger", 7).ShouldBeFalse();
        world.QueueInput("relay", "Trigger");
        world.Tick(Tick);
        LogicPlayInfo hidden = _trace.Capture(listed, [], world);

        listed.Scripts.Wires.ShouldHaveSingleItem();
        hidden.Scripts.Wires.ShouldBeEmpty();
        hidden.Recent.ShouldAllBe(happened => !happened.IsOfScript);
        world.Listeners.CountOf(relay, "OnTrigger").ShouldBe(1);
        heard.ShouldBe([7]);
    }

    [Fact]
    public void More_wires_than_one_capture_lists_are_cut_and_the_capture_says_so()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode main = Script("Main");
        EntityWorld world = Started();

        for (int line = 1; line <= LogicScriptWiring.MaxWires; line++)
            world.Listeners.Add(relay, "OnTrigger", line, new ScriptPlace(main.Id, File, line));

        LogicPlayInfo full = Captured(world);
        world.Listeners.Add(relay, "OnTrigger", 9000, new ScriptPlace(main.Id, File, 9000));
        LogicPlayInfo cut = _trace.Capture(full, [], world);

        full.Scripts.Wires.Count.ShouldBe(LogicScriptWiring.MaxWires);
        full.Scripts.IsTruncated.ShouldBeFalse();
        cut.Scripts.Wires.Count.ShouldBe(LogicScriptWiring.MaxWires);
        cut.Scripts.IsTruncated.ShouldBeTrue();
        cut.ScriptActivity.Count.ShouldBe(LogicScriptWiring.MaxWires);
    }

    [Fact]
    public void Connections_that_ended_leave_room_for_a_new_wire_to_be_drawn_and_to_light()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "recorder");
        SceneNode main = Script("Main");
        EntityWorld world = Started();
        LogicPlayInfo? play = null;

        // Three rounds of connections that each fire once and end: more
        // than the trace counts at one time.
        long listener = 0;
        for (int round = 0; round < 3; round++)
        {
            long first = listener + 1;
            for (int i = 0; i < LogicScriptWiring.MaxWires; i++)
                world.Listeners.Add(relay, "OnTrigger", ++listener, new ScriptPlace(main.Id, File, (int)listener));

            world.QueueInput("relay", "Trigger");
            world.Tick(Tick);
            play = _trace.Capture(play, [], world);

            for (long ended = first; ended <= listener; ended++)
                world.Listeners.Remove(relay, "OnTrigger", ended);

            play = _trace.Capture(play, [], world);
        }

        world.QueueScriptInput(EntityRuntime.Live(world, door), "Open", "", new ScriptPlace(main.Id, File, 57));
        world.Tick(Tick);
        play = _trace.Capture(play, [], world);

        play.Scripts.Wires.ShouldHaveSingleItem().Port.ShouldBe("Open");
        play.ScriptActivity.ShouldHaveSingleItem().ShouldBe(new LogicScriptActivity(1, 3));
    }

    private SceneNode Script(string name)
    {
        SceneNode node = _scene.Root.CreateChild(name);
        node.Script = new Script($"Scripts/{name.ToLowerInvariant()}.luau");
        return node;
    }

    private EntityWorld Started(FakeScriptRuntime? scripts = null)
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), EntityRuntime.Catalog([]))
        {
            Trace = _trace,
            Scripts = scripts ?? new FakeScriptRuntime(),
        };

        world.Activate();
        return world;
    }

    private LogicPlayInfo Captured(EntityWorld world) => _trace.Capture(null, [], world);
}
