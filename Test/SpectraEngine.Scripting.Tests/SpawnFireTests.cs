using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Entities;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What fires while a level spawns is past when its scripts start, on the
/// first tick. A wire still carries it. A script that listens for it is told
/// that it came too late.
/// </summary>
public sealed class SpawnFireTests
{
    private const string Missed =
        "OnMapSpawn of \"Auto\" fired while the level started, before any script ran. " +
        "A script hears what fires after that.";

    [Fact]
    public void A_wire_carries_what_fired_while_the_level_spawned_and_no_script_hears_it()
    {
        using var level = Level();
        level.AddScript(level.Scene.Root, """
            workspace.Auto.OnMapSpawn:Connect(function() print('a script heard') end)
            """);

        level.Play().Tick(3);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["a wire heard"]);
    }

    [Fact]
    public void The_first_script_that_listens_for_such_a_fire_is_warned_at_its_line_and_no_other_is()
    {
        using var level = Level();
        SceneNode first = level.AddScript(level.Scene.Root, """
            local auto = workspace.Auto
            auto.OnMapSpawn:Connect(function() end)
            auto.OnMapSpawn:Once(function() end)
            """, "First");
        level.AddScript(level.Scene.Root, "workspace.Auto.OnMapSpawn:Connect(function() end)", "Second");

        level.Play().Tick(2);

        ScriptMessage warned = Warnings(level).ShouldHaveSingleItem();
        warned.Text.ShouldBe(Missed);
        warned.Position.ShouldBe($"{first.Script.ShouldNotBeNull().Path}:2");
    }

    [Fact]
    public void A_thread_that_waits_for_such_a_fire_is_warned_and_waits_on()
    {
        using var level = Level();
        SceneNode waiter = level.AddScript(level.Scene.Root, """
            print('waiting')
            workspace.Auto.OnMapSpawn:Wait()
            print('went on')
            """);

        level.Play().Tick(3);

        level.Printed.ShouldBe(["waiting", "a wire heard"]);
        ScriptMessage warned = Warnings(level).ShouldHaveSingleItem();
        warned.Text.ShouldBe(Missed);
        warned.Position.ShouldBe($"{waiter.Script.ShouldNotBeNull().Path}:2");
    }

    [Fact]
    public void An_output_that_fires_once_the_scripts_run_is_heard_and_nothing_is_warned_of()
    {
        using var level = Level();
        level.Scene.Root.CreateChild("Echo").Entity = new EntityData(EchoEntity.Class);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            echo.OnPing:Connect(function() print('a script heard') end)
            echo:Ping()
            """);

        level.Play().Tick();

        level.Printed.ShouldBe(["a wire heard", "Echo got Ping ''", "a script heard"]);
        Warnings(level).ShouldBeEmpty();
    }

    private static ScriptMessage[] Warnings(ScriptLevel level) =>
        [.. level.Sink.Messages.Where(message => message.Severity == ScriptSeverity.Warning)];

    // A logic_auto with one wire to an entity that prints what it is sent.
    private static ScriptLevel Level()
    {
        var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Sayer").Entity = new EntityData(SayingEntity.Class);

        var auto = new EntityData("logic_auto");
        auto.Connections.Add(new EntityConnection(
            LogicAuto.OnMapSpawn, "Sayer", "Say", "a wire heard", 0f, EntityConnection.Infinite));
        level.Scene.Root.CreateChild("Auto").Entity = auto;
        return level;
    }
}
