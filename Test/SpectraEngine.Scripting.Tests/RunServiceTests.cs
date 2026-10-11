using System.Globalization;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// RunService's signals on the fixed tick, and what a script can do with a
/// signal: connect, connect once, wait and disconnect.
/// </summary>
public sealed class RunServiceTests
{
    private const string AllFour = """
        local RunService = game:GetService("RunService")
        RunService.Heartbeat:Connect(function() print('Heartbeat') end)
        RunService.PostSimulation:Connect(function() print('PostSimulation') end)
        RunService.Stepped:Connect(function() print('Stepped') end)
        RunService.PreSimulation:Connect(function() print('PreSimulation') end)
        """;

    [Fact]
    public void Stepped_comes_before_Heartbeat_in_every_tick()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, AllFour);

        level.Play().Tick(2);

        string[] oneTick = ["PreSimulation", "Stepped", "PostSimulation", "Heartbeat"];
        level.Printed.ShouldBe([.. oneTick, .. oneTick]);
    }

    [Fact]
    public void Both_come_in_a_tick_where_nothing_walks()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.AddScript(level.Scene.Root, AllFour);

        level.Play().Tick();

        level.Session.IsActive.ShouldBeFalse();
        level.Printed.ShouldBe(["PreSimulation", "Stepped", "PostSimulation", "Heartbeat"]);
    }

    [Fact]
    public void The_step_a_handler_is_given_is_the_fixed_one()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local RunService = game:GetService("RunService")
            RunService.Stepped:Connect(function(now, step) print(now, step) end)
            RunService.Heartbeat:Connect(function(step) print(step) end)
            """);

        level.Play().Tick(3);

        string step = Text(ScriptLevel.Dt);
        level.Printed.ShouldBe(
        [
            $"{Text(ScriptLevel.Dt)} {step}", step,
            $"{Text(2 * ScriptLevel.Dt)} {step}", step,
            $"{Text(3 * ScriptLevel.Dt)} {step}", step,
        ]);
    }

    [Fact]
    public void Handlers_run_in_the_order_they_connected_and_a_new_one_waits_for_the_next_fire()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local Heartbeat = game:GetService("RunService").Heartbeat
            Heartbeat:Once(function()
                print('first')
                Heartbeat:Once(function() print('made in a fire') end)
            end)
            Heartbeat:Once(function() print('second') end)
            """);

        level.Play().Tick();
        level.Printed.ShouldBe(["first", "second"]);

        level.Tick(2);
        level.Printed.ShouldBe(["first", "second", "made in a fire"]);
    }

    [Fact]
    public void A_disconnected_handler_is_not_called_again()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local beats = 0
            local connection
            connection = game:GetService("RunService").Heartbeat:Connect(function()
                beats += 1
                print(beats, connection.Connected)
                if beats == 2 then
                    connection:Disconnect()
                    print(connection.Connected)
                end
            end)
            """);

        level.Play().Tick(5);

        level.Printed.ShouldBe(["1 true", "2 true", "false"]);
        level.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Wait_on_a_signal_goes_on_at_the_next_fire_with_what_it_was_fired_with()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local RunService = game:GetService("RunService")
            print('waiting')
            local now, step = RunService.Stepped:Wait()
            print(now == step, step == RunService.Heartbeat:Wait())
            """);

        level.Play().Tick();

        level.Printed.ShouldBe(["waiting", "true true"]);
    }

    [Fact]
    public void A_handler_may_wait_and_each_fire_runs_it_on_a_thread_of_its_own()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local beat = 0
            game:GetService("RunService").Heartbeat:Connect(function()
                beat += 1
                local mine = beat
                task.wait(0.05)
                print(mine)
            end)
            """);

        level.Play().Tick(5);

        // Three ticks after each of the first two beats.
        level.Printed.ShouldBe(["1", "2"]);
    }

    [Fact]
    public void A_signal_and_a_service_print_as_their_names_and_have_no_other_members()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local RunService = game:GetService("RunService")
            print(RunService, RunService.Heartbeat, game)
            print(RunService.Heartbeat == RunService.Heartbeat, RunService == game:GetService("RunService"))
            print(pcall(function() return RunService.RenderStepped end))
            print(pcall(function() RunService.Heartbeat = nil end))
            print(pcall(function() return game:GetService("Players") end))
            print(pcall(function() return RunService.Heartbeat.Connect() end))
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            "RunService Signal Heartbeat game",
            "true true",
            $"false {here}:4: RenderStepped is not a valid member of RunService \"RunService\"",
            $"false {here}:5: Unable to assign property Heartbeat. Property is read only",
            $"false {here}:6: 'Players' is not a valid Service name",
            $"false {here}:7: Expected ':' not '.' calling member function Connect",
        ]);
    }

    // A number the way Luau prints it: the shortest text that reads back the same.
    private static string Text(float number) => ((double)number).ToString("R", CultureInfo.InvariantCulture);
}
