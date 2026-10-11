namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A thread a script ended itself, with <c>coroutine.close</c> or by resuming
/// it to its end, while the scheduler still held a wait for it.
/// </summary>
public sealed class EndedThreadTests
{
    [Fact]
    public void A_thread_closed_during_its_wait_is_not_resumed_when_the_wait_is_over()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local waiting = task.spawn(function()
                task.wait(0.05)
                print('woke')
            end)
            local old = task.spawn(function()
                wait(0.05)
                print('woke')
            end)
            coroutine.close(waiting)
            coroutine.close(old)
            """);

        level.Play().Tick(6);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBeEmpty();
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void A_thread_closed_while_it_waits_for_a_signal_is_not_resumed_by_the_fire()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local waiting = task.spawn(function()
                game:GetService("RunService").Stepped:Wait()
                print('woke')
            end)
            coroutine.close(waiting)
            """);

        level.Play().Tick(3);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBeEmpty();
    }

    [Fact]
    public void A_thread_a_script_resumed_to_its_end_is_not_resumed_again_by_its_wait()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local waiting = task.spawn(function()
                print('got', task.wait(0.05))
            end)
            coroutine.resume(waiting, 'a word')
            """);

        level.Play().Tick(6);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["got a word"]);
    }

    [Fact]
    public void Spawn_defer_and_delay_refuse_a_thread_that_has_ended_and_run_nothing_on_it()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local ended = coroutine.create(function() end)
            coroutine.resume(ended)
            local function ran() print('ran') end
            print(pcall(function() task.spawn(ended, ran) end))
            print(pcall(function() task.defer(ended, ran) end))
            print(pcall(function() task.delay(0, ended, ran) end))
            """);

        level.Play().Tick(3);

        string here = level.Scene.ScriptNodes[0].Script.ShouldNotBeNull().Path;
        level.Printed.ShouldBe(
        [
            $"false {here}:4: task.spawn cannot resume a thread that has ended",
            $"false {here}:5: task.defer cannot resume a thread that has ended",
            $"false {here}:6: task.delay cannot resume a thread that has ended",
        ]);
        level.Errors.ShouldBeEmpty();
    }
}
