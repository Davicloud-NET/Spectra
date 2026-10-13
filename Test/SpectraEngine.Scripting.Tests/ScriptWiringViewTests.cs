using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a wiring view is told about real scripts in a played level: which
/// script hears an output and sends an input, from which file and line, and
/// that a connection that ends is gone.
/// </summary>
public sealed class ScriptWiringViewTests
{
    private readonly WireActivityTrace _trace = new();

    [Fact]
    public void A_script_that_connects_and_sends_is_two_wires_at_its_own_lines()
    {
        using var level = Watched();
        SceneNode echo = Echo(level);
        SceneNode main = level.ScriptAt("Scripts/main.luau", """
            local echo = workspace.Echo
            echo.OnPing:Connect(function() end)

            echo:Ping()
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        LogicPlayInfo play = Captured(level);
        play.Scripts.Wires.Count.ShouldBe(2);

        LogicScriptWire hears = play.Scripts.Wires.Single(wire => !wire.IsSend);
        hears.ShouldBe(Wire(main, echo, "OnPing", isSend: false, "Scripts/main.luau", 2));
        LogicScriptWire sends = play.Scripts.Wires.Single(wire => wire.IsSend);
        sends.ShouldBe(Wire(main, echo, "Ping", isSend: true, "Scripts/main.luau", 4));

        play.ScriptActivity.ShouldAllBe(activity => activity.Fired == 1);
        play.Recent.Where(happened => happened.IsOfScript).Select(happened => (happened.Kind, happened.ScriptName))
            .ShouldBe([(EntityTraceKind.InputDelivered, "main"), (EntityTraceKind.OutputFired, "main")]);
    }

    [Fact]
    public void What_a_modules_code_connects_and_sends_counts_for_the_script_that_asked_and_names_the_modules_file()
    {
        using var level = Watched();
        SceneNode echo = Echo(level);
        level.FileAt("Scripts/lib/doors.luau", """
            local doors = {}
            workspace.Echo.OnTapped:Connect(function() end)
            function doors.watch(echo)
                echo.OnPing:Connect(function()
                    echo:Tap()
                end)
            end
            return doors
            """);
        SceneNode main = level.ScriptAt("Scripts/main.luau", """
            require('./lib/doors').watch(workspace.Echo)
            workspace.Echo:Ping()
            """);

        level.Play().Tick(3);

        level.Errors.ShouldBeEmpty();
        Captured(level).Scripts.Wires.OrderBy(wire => wire.Line).ShouldBe(
        [
            Wire(main, echo, "OnTapped", isSend: false, "Scripts/lib/doors.luau", 2),
            Wire(main, echo, "Ping", isSend: true, "Scripts/main.luau", 2),
            Wire(main, echo, "OnPing", isSend: false, "Scripts/lib/doors.luau", 4),
            Wire(main, echo, "Tap", isSend: true, "Scripts/lib/doors.luau", 5),
        ]);
    }

    [Fact]
    public void A_module_node_is_never_the_script_of_a_wire()
    {
        using var level = Watched();
        SceneNode echo = Echo(level);
        SceneNode main = level.ScriptAt("Scripts/main.luau", """
            require(script.Shared)
            """);
        level.ModuleAt("Scripts/shared.luau", """
            workspace.Echo.OnPing:Connect(function() end)
            return {}
            """, under: main).Name = "Shared";

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        Captured(level).Scripts.Wires.ShouldHaveSingleItem()
            .ShouldBe(Wire(main, echo, "OnPing", isSend: false, "Scripts/shared.luau", 1));
    }

    [Fact]
    public void A_connection_a_script_ends_leaves_the_picture()
    {
        using var level = Watched();
        Echo(level);
        level.ScriptAt("Scripts/main.luau", """
            local heard = workspace.Echo.OnPing:Connect(function() end)
            workspace.Echo.OnTapped:Once(function() end)
            task.wait(0.05)
            heard:Disconnect()
            workspace.Echo:Tap()
            """);

        level.Play().Tick();
        LogicPlayInfo connected = Captured(level);
        level.Tick(5);
        LogicPlayInfo ended = _trace.Capture(connected, [], level.Entities);

        level.Errors.ShouldBeEmpty();
        connected.Scripts.Wires.Select(wire => wire.Port).Order().ShouldBe(["OnPing", "OnTapped"]);
        ended.Scripts.Wires.ShouldHaveSingleItem().Port.ShouldBe("Tap");
    }

    [Fact]
    public void The_connections_of_a_script_that_was_switched_off_leave_the_picture()
    {
        using var level = Watched();
        Echo(level);
        level.ScriptAt("Scripts/hangs.luau", """
            workspace.Echo.OnPing:Connect(function() end)
            task.wait()
            while true do end
            """);
        level.ScriptAt("Scripts/stays.luau", "workspace.Echo.OnTapped:Connect(function() end)");
        level.Play().Tick();
        LogicPlayInfo both = Captured(level);

        level.StopTheNextHangSoon();
        level.Tick(2);

        level.Errors.ShouldHaveSingleItem().ShouldContain("switched off");
        both.Scripts.Wires.Count.ShouldBe(2);
        _trace.Capture(both, [], level.Entities).Scripts.Wires.ShouldHaveSingleItem().Port.ShouldBe("OnTapped");
    }

    [Fact]
    public void A_thread_cancelled_while_it_waits_for_an_output_leaves_the_picture()
    {
        using var level = Watched();
        Echo(level);
        level.ScriptAt("Scripts/main.luau", """
            local waiter = task.spawn(function() workspace.Echo.OnPing:Wait() end)
            task.wait()
            task.cancel(waiter)
            """);

        level.Play().Tick();
        LogicPlayInfo waiting = Captured(level);
        level.Tick(2);

        level.Errors.ShouldBeEmpty();
        waiting.Scripts.Wires.ShouldHaveSingleItem().Line.ShouldBe(1);
        _trace.Capture(waiting, [], level.Entities).Scripts.Wires.ShouldBeEmpty();
    }

    [Fact]
    public void A_script_that_sends_and_waits_every_tick_keeps_two_wires_and_one_list()
    {
        using var level = Watched();
        Echo(level);
        level.ScriptAt("Scripts/main.luau", """
            local echo = workspace.Echo
            game:GetService('RunService').Heartbeat:Connect(function()
                echo:Tap()
            end)
            while true do
                echo.OnTapped:Wait()
            end
            """);

        level.Play().Tick(3);
        LogicPlayInfo early = Captured(level);
        level.Tick(30);
        LogicPlayInfo late = _trace.Capture(early, [], level.Entities);

        level.Errors.ShouldBeEmpty();
        late.Scripts.ShouldBeSameAs(early.Scripts);
        late.Scripts.Wires.Select(wire => (wire.Port, wire.Line)).Order().ShouldBe([("OnTapped", 6), ("Tap", 3)]);
        late.ScriptActivity.ShouldAllBe(activity => activity.Fired >= 30);
        late.Recent.Count(happened => happened.IsOfScript).ShouldBe(2);
    }

    [Fact]
    public void A_connection_made_while_nobody_watched_still_knows_its_script_and_its_line()
    {
        using var level = new ScriptLevel();
        SceneNode echo = Echo(level);
        SceneNode main = level.ScriptAt("Scripts/main.luau", """

            workspace.Echo.OnPing:Connect(function() end)
            """);
        level.Play().Tick();

        var listeners = new List<ScriptListener>();
        level.Entities.Listeners.CopyTo(listeners);

        listeners.ShouldHaveSingleItem()
            .ShouldBe(new ScriptListener(echo.Id, "OnPing", new ScriptPlace(main.Id, "Scripts/main.luau", 2)));
    }

    private ScriptLevel Watched()
    {
        var level = new ScriptLevel();
        level.Manager.EntityTrace = _trace;
        return level;
    }

    private LogicPlayInfo Captured(ScriptLevel level) => _trace.Capture(null, [], level.Entities);

    private static SceneNode Echo(ScriptLevel level)
    {
        SceneNode node = level.Scene.Root.CreateChild("Echo");
        node.Entity = new EntityData(EchoEntity.Class);
        return node;
    }

    private static LogicScriptWire Wire(
        SceneNode script, SceneNode entity, string port, bool isSend, string file, int line) => new()
    {
        Script = script.Id,
        Entity = entity.Id,
        Port = port,
        IsSend = isSend,
        File = file,
        Line = line,
    };
}
