using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// An output of an entity class as a signal of its node: a connected
/// function is called from the level's queue with the output's value, never
/// inside the call that fired it.
/// </summary>
public sealed class EntityOutputTests
{
    // Prints how many values a listener got, and each with its type.
    private const string Show = """
        local function show(...)
            local shown = { tostring(select("#", ...)) }
            for _, value in { ... } do
                table.insert(shown, typeof(value) .. " " .. tostring(value))
            end
            print(table.concat(shown, " | "))
        end
        """;

    [Fact]
    public void A_listener_is_called_from_the_queue_after_the_call_that_fired_the_output_has_returned()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            echo.OnPing:Connect(function() print('heard') end)
            echo:Ping()
            print('called')
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["called", "Echo got Ping ''", "heard"]);
    }

    [Theory]
    [InlineData("OnPing", "SayNothing", "", "0")]
    [InlineData("OnFlag", "SayFlag", "1", "1 | boolean true")]
    [InlineData("OnFlag", "SayFlag", "0", "1 | boolean false")]
    [InlineData("OnWhole", "SayWhole", "-3", "1 | number -3")]
    [InlineData("OnNumber", "SayNumber", "0.1", "1 | number 0.1")]
    [InlineData("OnText", "SayText", "hello there", "1 | string hello there")]
    [InlineData("OnName", "SayName", "door1", "1 | string door1")]
    [InlineData("OnPair", "SayPair", "1 2.5", "2 | number 1 | number 2.5")]
    [InlineData("OnPoint", "SayPoint", "1 2 3", "1 | Vector3 1, 2, 3")]
    [InlineData("OnFour", "SayFour", "1 2 3 4", "4 | number 1 | number 2 | number 3 | number 4")]
    [InlineData("OnColor", "SayColor", "1 0.5 0", "1 | Vector3 1, 0.5, 0")]
    [InlineData("OnAngles", "SayAngles", "0 90 0", "1 | Vector3 0, 90, 0")]
    [InlineData("OnNode", "SayNode", "", "1")]
    [InlineData("OnMaybe", "SayMaybe", "4", "1 | number 4")]
    [InlineData("OnMaybe", "SayMaybe", "", "0")]
    [InlineData("OnAny", "SayAny", "as typed", "1 | string as typed")]
    [InlineData("OnAny", "SayAny", "", "0")]
    public void A_listener_gets_the_outputs_value_by_its_type(string output, string input, string sent, string heard)
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, $"""
            {Show}
            workspace.Echo.{output}:Connect(show)
            """);
        level.Play();

        // As a wire sends it: text, which the entity fires on as it came.
        level.Entities.QueueInput("Echo", input == "SayNothing" ? "Ping" : input, sent);
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.Last().ShouldBe(heard);
    }

    [Fact]
    public void A_node_an_output_sends_is_the_node_itself_and_nil_once_it_is_no_node_of_the_level()
    {
        using var level = new ScriptLevel();
        Echo(level);
        SceneNode crate = level.Part("Crate", default);
        level.AddScript(level.Scene.Root, """
            workspace.Echo.OnNode:Connect(function(node)
                print(node == workspace.Crate, node)
            end)
            """);
        level.Play();

        level.Entities.QueueInput("Echo", "SayNode", KeyvalueWire.Format(crate.Id));
        level.Entities.QueueInput("Echo", "SayNode", KeyvalueWire.Format(Guid.NewGuid()));
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.Where(line => !line.StartsWith("Echo got", StringComparison.Ordinal))
            .ShouldBe(["true Crate", "false nil"]);
    }

    [Fact]
    public void Text_the_outputs_type_cannot_read_calls_the_listener_with_nothing()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, $"""
            {Show}
            workspace.Echo.OnNumber:Connect(show)
            """);
        level.Play();

        level.Entities.QueueInput("Echo", "SayNumber", "fast");
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Echo got SayNumber 'fast'", "0"]);
    }

    [Fact]
    public void An_output_is_the_same_signal_on_every_read_and_another_on_another_node()
    {
        using var level = new ScriptLevel();
        Echo(level);
        Echo(level, "Other");
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            local kept = echo.OnPing
            print(kept, typeof(kept))
            print(kept == echo.OnPing, rawequal(kept, workspace.Echo.OnPing))
            print(kept == echo.OnNumber, kept == workspace.Other.OnPing)
            print(select(2, pcall(function() echo:OnPing() end)))
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            "Signal OnPing RBXScriptSignal",
            "true true",
            "false false",
            $"{here}:6: attempt to call a RBXScriptSignal value",
        ]);
    }

    [Fact]
    public void Listeners_are_called_in_the_order_they_connected()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            echo.OnPing:Connect(function() print('first') end)
            echo.OnPing:Connect(function() print('second') end)
            echo.OnPing:Connect(function() print('third') end)
            echo:Ping()
            """);

        level.Play().Tick();

        level.Printed.ShouldBe(["Echo got Ping ''", "first", "second", "third"]);
    }

    [Fact]
    public void A_listener_that_disconnects_itself_is_not_called_for_a_fire_that_was_already_queued()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            local connection
            connection = echo.OnPing:Connect(function()
                print('heard', connection.Connected)
                connection:Disconnect()
                print('after', connection.Connected)
            end)
            echo:Ping()
            echo:Ping()
            """);

        level.Play().Tick();
        level.Entities.QueueInput("Echo", "Ping");
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.Where(line => !line.StartsWith("Echo got", StringComparison.Ordinal))
            .ShouldBe(["heard true", "after false"]);
        level.Entities.Listeners.Count.ShouldBe(0);
    }

    [Fact]
    public void A_listener_connected_inside_a_call_hears_the_next_fire_and_not_the_one_being_called()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            echo.OnPing:Once(function()
                print('first')
                echo.OnPing:Connect(function() print('made in a call') end)
            end)
            echo:Ping()
            """);

        level.Play().Tick();
        level.Printed.ShouldBe(["Echo got Ping ''", "first"]);

        level.Entities.QueueInput("Echo", "Ping");
        level.Tick();
        level.Printed.Skip(2).ShouldBe(["Echo got Ping ''", "made in a call"]);
    }

    [Fact]
    public void Once_is_called_for_one_fire_and_not_for_a_second_one_queued_in_the_same_tick()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            local connection = echo.OnNumber:Once(function(number) print('once', number) end)
            echo:SayNumber(1)
            echo:SayNumber(2)
            task.wait()
            print(connection.Connected)
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.Where(line => !line.StartsWith("Echo got", StringComparison.Ordinal))
            .ShouldBe(["once 1", "false"]);
        level.Entities.Listeners.Count.ShouldBe(0);
    }

    [Fact]
    public void Wait_goes_on_when_the_output_fires_next_with_its_value()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            task.defer(function() echo:SayPair(3, 4) end)
            print('waiting')
            print('went on with', echo.OnPair:Wait())
            echo:SayPair(5, 6)
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "waiting", "Echo got SayPair '3 4'", "went on with 3 4", "Echo got SayPair '5 6'",
        ]);
        level.Entities.Listeners.Count.ShouldBe(0);
    }

    [Fact]
    public void A_thread_that_was_cancelled_while_it_waited_is_let_go_at_the_next_fire()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            local waiter = task.spawn(function()
                echo.OnPing:Wait()
                print('never')
            end)
            task.cancel(waiter)
            echo:Ping()
            """);

        level.Play();
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Echo got Ping ''"]);
        level.Entities.Listeners.Count.ShouldBe(0);
    }

    [Fact]
    public void A_listener_may_wait_and_each_fire_runs_it_on_a_thread_of_its_own()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            echo.OnNumber:Connect(function(number)
                task.wait(0.05)
                print('later', number)
            end)
            echo:SayNumber(1)
            echo:SayNumber(2)
            """);

        level.Play().Tick(5);

        level.Errors.ShouldBeEmpty();
        level.Printed.Skip(2).ShouldBe(["later 1", "later 2"]);
    }

    [Fact]
    public void A_listener_of_a_script_that_was_switched_off_is_not_called_and_is_let_go()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            workspace.Echo.OnPing:Connect(function() print('the hung script heard') end)
            task.wait()
            while true do end
            """, "Hangs");
        level.AddScript(level.Scene.Root, """
            workspace.Echo.OnPing:Connect(function() print('the other heard') end)
            """, "Listens");
        level.Play().Tick();
        level.Entities.Listeners.Count.ShouldBe(2);

        StopTheNextHangSoon(level);
        level.Tick();
        level.Errors.ShouldHaveSingleItem().ShouldContain("switched off");

        level.Entities.QueueInput("Echo", "Ping");
        level.Tick();

        level.Printed.ShouldBe(["Echo got Ping ''", "the other heard"]);
        level.Errors.Length.ShouldBe(1);
        level.Entities.Listeners.Count.ShouldBe(1);
    }

    [Fact]
    public void A_fire_with_a_full_queue_reaches_the_listeners_there_is_room_for_and_nothing_fails()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            for index = 1, 6 do
                workspace.Echo.OnPing:Connect(function() print('heard', index) end)
            end
            """);
        level.Play().Tick();
        level.Entities.MaxPendingEvents = 4;

        level.Entities.QueueInput("Echo", "Ping");
        level.Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Echo got Ping ''", "heard 1", "heard 2", "heard 3", "heard 4"]);
        level.Entities.DiscardedEventCount.ShouldBe(2);

        // The queue has room again, and every listener is still connected.
        level.Entities.QueueInput("Echo", "Ping");
        level.Tick();
        level.Printed.Length.ShouldBe(10);
    }

    [Fact]
    public void An_input_sent_with_a_full_queue_is_an_error_at_the_scripts_line()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            task.wait()
            local sent = 0
            local ok, why = pcall(function()
                for _ = 1, 10 do
                    workspace.Echo:Ping()
                    sent += 1
                end
            end)
            print(sent, ok, why)
            """);
        level.Play().Tick();
        level.Entities.MaxPendingEvents = 3;

        level.Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Errors.ShouldBeEmpty();
        level.Printed[0].ShouldBe(
            $"3 false {here}:5: Ping was not sent: the level's scripts have too much waiting in its queue");
        level.Printed.Length.ShouldBe(4);
    }

    [Fact]
    public void A_listener_that_sets_off_its_own_output_is_stopped_by_the_ticks_budget_and_the_level_goes_on()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            local echo = workspace.Echo
            local heard = 0
            echo.OnPing:Connect(function()
                heard += 1
                echo:Ping()
            end)
            echo.OnNumber:Connect(function() print('heard', heard) end)
            echo:Ping()
            """);
        level.Play();
        level.Entities.MaxDispatchesPerTick = 20;

        level.Tick();
        level.Entities.DispatchBudgetTripCount.ShouldBe(1);
        level.Entities.PendingEventCount.ShouldBe(0);

        level.Entities.QueueInput("Echo", "SayNumber", "1");
        level.Tick();

        // Ten rounds of an input and a listener's call are the twenty.
        level.Errors.ShouldBeEmpty();
        level.Printed.Last().ShouldBe("heard 10");
    }

    [Fact]
    public void A_stop_lets_every_listener_go()
    {
        using var level = new ScriptLevel();
        Echo(level);
        level.AddScript(level.Scene.Root, """
            workspace.Echo.OnPing:Connect(function() end)
            task.spawn(function() workspace.Echo.OnNumber:Wait() end)
            """);
        level.Play().Tick();
        EntityWorld world = level.Entities;
        world.Listeners.Count.ShouldBe(2);

        level.Stop();

        world.Listeners.Count.ShouldBe(0);
    }

    // The watchdog goes by the wall clock. The short limit holds for the one
    // call that hangs, and the default is back before any other script runs.
    private static void StopTheNextHangSoon(ScriptLevel level)
    {
        LuauScriptRuntime runtime = level.Runtime.ShouldNotBeNull();
        runtime.CallLimit = TimeSpan.FromMilliseconds(100);
        level.Sink.Heard = message =>
        {
            if (message.Severity == ScriptSeverity.Error)
                runtime.CallLimit = LuauHost.DefaultCallLimit;
        };
    }

    private static SceneNode Echo(ScriptLevel level, string name = "Echo")
    {
        SceneNode node = level.Scene.Root.CreateChild(name);
        node.Entity = new EntityData(EchoEntity.Class);
        return node;
    }
}
