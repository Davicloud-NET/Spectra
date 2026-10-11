using System.Globalization;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A thread waits for one thing at a time. A wait that could not begin, and
/// one the thread has left since, wake nothing.
/// </summary>
public sealed class StaleWaitTests
{
    private const string CannotYield = "attempt to yield across metamethod/C-call boundary";

    [Fact]
    public void A_wait_where_a_thread_cannot_yield_is_an_error_and_leaves_no_wake_behind()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            task.spawn(function()
                local lazy = setmetatable({}, { __index = function() task.wait(0.05) end })
                print(pcall(function() return lazy.field end))
                print('resumed with', coroutine.yield())
            end)
            """);

        level.Play().Tick();

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe([$"false {here}:2: {CannotYield}"]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);

        level.Tick(6);
        level.Printed.Length.ShouldBe(1);
        level.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_signal_wait_where_a_thread_cannot_yield_is_an_error_and_the_next_fire_resumes_nothing()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local Heartbeat = game:GetService("RunService").Heartbeat
            task.spawn(function()
                local lazy = setmetatable({}, { __index = function() Heartbeat:Wait() end })
                print(pcall(function() return lazy.field end))
                print('resumed with', coroutine.yield())
            end)
            """);

        level.Play().Tick(3);

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe([$"false {here}:3: {CannotYield}"]);
        level.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_thread_resumed_by_hand_out_of_a_wait_is_not_woken_by_it_while_it_waits_for_a_signal()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local Heartbeat = game:GetService("RunService").Heartbeat
            local thread = task.spawn(function()
                task.wait(0.05)
                while true do
                    print(Heartbeat:Wait())
                end
            end)
            coroutine.resume(thread)
            """);

        level.Play().Tick(5);

        // One line a tick, each the step. The wait it left would hand it 0.05.
        string step = ((double)ScriptLevel.Dt).ToString("R", CultureInfo.InvariantCulture);
        level.Printed.ShouldBe([step, step, step, step, step]);
        level.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_thread_resumed_by_hand_out_of_a_signal_wait_is_not_resumed_by_the_fire_while_it_waits_for_a_tick()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local thread = task.spawn(function()
                game:GetService("RunService").Heartbeat:Wait()
                task.wait(0.1)
                print('waited')
            end)
            coroutine.resume(thread)
            """);

        level.Play().Tick(6);
        level.Printed.ShouldBeEmpty();

        level.Tick();
        level.Printed.ShouldBe(["waited"]);
        level.Errors.ShouldBeEmpty();
    }
}
