using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What becomes of a listener that goes wrong: one that fails, one whose
/// thread or script is gone, and one that is handed text its output's type
/// cannot read.
/// </summary>
public sealed class ListenerUpkeepTests
{
    [Fact]
    public void A_listener_that_fails_is_reported_and_the_others_of_its_output_are_called_all_the_same()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            echo.OnPing:Connect(function() print('first') end)
            echo.OnPing:Connect(function() error('the second broke') end)
            echo.OnPing:Connect(function() print('third') end)
            echo:Ping()
            """);

        level.Play().Tick();
        level.Entities.QueueInput("Echo", "Ping");
        level.Tick();

        // It stays connected, as in Roblox, and fails again at the next fire.
        level.Printed.Where(line => !line.StartsWith("Echo got", StringComparison.Ordinal))
            .ShouldBe(["first", "third", "first", "third"]);
        level.Errors.ShouldBe(["the second broke", "the second broke"]);
        level.Entities.Listeners.Count.ShouldBe(3);
    }

    [Fact]
    public void Threads_cancelled_while_they_wait_for_an_output_that_never_fires_do_not_pile_up()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            for _ = 1, 500 do
                task.cancel(task.spawn(function() echo.OnPing:Wait() end))
            end
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Entities.Listeners.Count.ShouldBeLessThan(100);
    }

    [Fact]
    public void Listeners_of_a_script_that_was_switched_off_are_let_go_as_others_connect()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            for _ = 1, 100 do
                workspace.Echo.OnPing:Connect(function() end)
            end
            task.wait()
            while true do end
            """, "Hangs");
        level.AddScript(level.Scene.Root, """
            task.wait(0.05)
            for _ = 1, 100 do
                workspace.Echo.OnNumber:Connect(function() end)
            end
            """, "Listens");
        level.Play().Tick();
        level.Entities.Listeners.Count.ShouldBe(100);

        level.StopTheNextHangSoon();
        level.Tick(4);

        level.Errors.ShouldHaveSingleItem().ShouldContain("switched off");
        level.Entities.Listeners.Count.ShouldBe(100);
        level.Entities.Listeners.CountOf(level.Scene.Root.Children.Single(node => node.Name == "Echo"), "OnPing").ShouldBe(0);
    }

    [Fact]
    public void Text_an_outputs_type_cannot_read_is_warned_of_once_for_the_output()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            workspace.Echo.OnNumber:Connect(function(...) print('heard', select('#', ...)) end)
            """);
        level.Play();

        level.Entities.QueueInput("Echo", "SayNumber", "fast");
        level.Tick();
        level.Entities.QueueInput("Echo", "SayNumber", "slow");
        level.Tick();

        level.Printed.Count(line => line == "heard 0").ShouldBe(2);
        ScriptMessage warned = level.Sink.Messages
            .Where(message => message.Severity == ScriptSeverity.Warning).ShouldHaveSingleItem();
        warned.Text.ShouldBe(
            "OnNumber of \"Echo\" fired with 'fast', where its class says number. " +
            "A listener gets no value for such a fire.");
        warned.Script.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Ping", "")]
    [InlineData("SayMaybe", "")]
    [InlineData("SayNode", "")]
    [InlineData("SayAny", "")]
    [InlineData("SayAny", "anything at all")]
    [InlineData("SayNumber", "2.5")]
    public void Text_an_outputs_type_reads_is_warned_of_never(string input, string sent)
    {
        string output = EchoEntity.Ports.Single(port => port.Input == input).Output;
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, $"workspace.Echo.{output}:Connect(function() print('heard') end)");
        level.Play();

        level.Entities.QueueInput("Echo", input, sent);
        level.Tick();

        level.Printed.ShouldContain("heard");
        level.Sink.Messages.ShouldAllBe(message => message.Severity == ScriptSeverity.Print);
    }

    private static void Echo(ScriptLevel level) =>
        level.Scene.Root.CreateChild("Echo").Entity = new EntityData(EchoEntity.Class);
}
