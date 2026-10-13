using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What watching a level does to it, which is nothing, and what a wiring
/// view is told of a script the host switched off.
/// </summary>
public sealed class ScriptWiringWatchTests
{
    private const string CancelsItsWait = """
        local waiter = task.spawn(function() workspace.Echo.OnPing:Wait() end)
        task.wait()
        task.cancel(waiter)
        """;

    private readonly WireActivityTrace _trace = new();

    [Fact]
    public void What_a_script_that_was_switched_off_has_sent_leaves_the_picture()
    {
        using var level = Watched();
        Echo(level);
        level.ScriptAt("Scripts/hangs.luau", """
            workspace.Echo:Ping()
            task.wait()
            while true do end
            """);
        level.ScriptAt("Scripts/stays.luau", "workspace.Echo:Tap()");
        level.Play().Tick();
        LogicPlayInfo both = _trace.Capture(null, [], level.Entities);

        level.StopTheNextHangSoon();
        level.Tick(2);

        level.Errors.ShouldHaveSingleItem().ShouldContain("switched off");
        both.Scripts.Wires.Select(wire => wire.Port).Order().ShouldBe(["Ping", "Tap"]);
        _trace.Capture(both, [], level.Entities).Scripts.Wires.ShouldHaveSingleItem().Port.ShouldBe("Tap");
    }

    [Fact]
    public void A_module_that_is_switched_off_does_not_take_the_script_that_asked_for_it_out_of_the_picture()
    {
        using var level = Watched();
        Echo(level);
        level.FileAt("Scripts/lib/spins.luau", """
            task.spawn(function()
                task.wait()
                while true do end
            end)
            return {}
            """);
        level.ScriptAt("Scripts/main.luau", """
            require('./lib/spins')
            workspace.Echo:Ping()
            """);
        level.Play().Tick();

        level.StopTheNextHangSoon();
        level.Tick(2);

        level.Errors.ShouldHaveSingleItem().ShouldContain("switched off");
        _trace.Capture(null, [], level.Entities).Scripts.Wires.ShouldHaveSingleItem().Port.ShouldBe("Ping");
    }

    [Fact]
    public void A_watched_level_keeps_the_listeners_an_unwatched_one_keeps()
    {
        using var watched = Watched();
        using var unwatched = new ScriptLevel();
        SceneNode seen = Echo(watched);
        SceneNode unseen = Echo(unwatched);
        watched.ScriptAt("Scripts/main.luau", CancelsItsWait);
        unwatched.ScriptAt("Scripts/main.luau", CancelsItsWait);

        watched.Play().Tick(3);
        unwatched.Play().Tick(3);

        watched.Errors.ShouldBeEmpty();
        unwatched.Entities.Listeners.CountOf(unseen, "OnPing").ShouldBe(1);
        watched.Entities.Listeners.CountOf(seen, "OnPing").ShouldBe(1);
        _trace.Capture(null, [], watched.Entities).Scripts.Wires.ShouldBeEmpty();
    }

    private ScriptLevel Watched()
    {
        var level = new ScriptLevel();
        level.Manager.EntityTrace = _trace;
        return level;
    }

    private static SceneNode Echo(ScriptLevel level)
    {
        SceneNode node = level.Scene.Root.CreateChild("Echo");
        node.Entity = new EntityData(EchoEntity.Class);
        return node;
    }
}
