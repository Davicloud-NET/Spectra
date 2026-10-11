using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The task library over the entity queue: which tick a waiting thread goes
/// on at, and the order of what runs on one tick.
/// </summary>
public sealed class TaskLibraryTests
{
    [Fact]
    public void A_wait_in_a_loop_resumes_on_the_ticks_whole_ticks_gives()
    {
        using var level = new ScriptLevel();
        SceneNode part = level.Part("Mover", new Vector3(0f, 2f, 0f));
        level.AddScript(part, """
            local part = script.Parent
            for step = 1, 3 do
                print(task.wait(0.1))
                part.Position += Vector3.new(1, 0, 0)
            end
            """);

        var movedOn = new List<long>();
        level.Play();
        for (int tick = 0; tick < 30; tick++)
        {
            float before = part.LocalPosition.X;
            level.Tick();
            if (part.LocalPosition.X != before)
                movedOn.Add(level.Entities.TickNumber);
        }

        // The script starts on tick 1, and a tenth of a second is six ticks.
        long wait = WholeTicks.In(0.1, ScriptLevel.Dt);
        wait.ShouldBe(6);
        movedOn.ShouldBe([1 + wait, 1 + (2 * wait), 1 + (3 * wait)]);
        part.LocalPosition.ShouldBe(new Vector3(3f, 2f, 0f));

        // What wait returns is the time that passed, in whole ticks.
        double passed = wait * (double)ScriptLevel.Dt;
        level.Printed.Select(Number).ShouldBe([passed, passed, passed]);
    }

    [Theory]
    [InlineData("task.wait()")]
    [InlineData("task.wait(0)")]
    [InlineData("task.wait(-1)")]
    [InlineData("task.wait(0.001)")]
    [InlineData("wait()")]
    public void A_wait_of_no_time_still_waits_for_the_next_tick(string wait)
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $"print('before') {wait} print('after')");

        level.Play().Tick();
        level.Printed.ShouldBe(["before"]);

        level.Tick();
        level.Printed.ShouldBe(["before", "after"]);
    }

    [Fact]
    public void Spawn_runs_at_once_defer_in_the_same_drain_and_delay_on_a_later_tick()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            print('top')
            task.spawn(function(word)
                print('spawn ' .. word)
                task.wait()
                print('spawn again')
            end, 'now')
            task.defer(function(word) print('defer ' .. word) end, 'soon')
            task.delay(0, function(word) print('delay ' .. word) end, 'later')
            print('bottom')
            """);

        level.Play().Tick();
        level.Printed.ShouldBe(["top", "spawn now", "bottom", "defer soon"]);

        // The spawned thread asked to wait before the delay was made.
        level.Tick();
        level.Printed.Skip(4).ShouldBe(["spawn again", "delay later"]);
    }

    [Fact]
    public void A_defer_from_a_Heartbeat_handler_runs_in_the_next_ticks_drain()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local RunService = game:GetService("RunService")
            RunService.Heartbeat:Once(function()
                print('beat')
                task.defer(function() print('deferred') end)
            end)
            RunService.Stepped:Connect(function() print('stepped') end)
            """);

        level.Play().Tick(2);

        level.Printed.ShouldBe(["stepped", "beat", "deferred", "stepped"]);
    }

    [Fact]
    public void A_cancelled_thread_never_runs()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local delayed = task.delay(0.05, function() print('delayed') end)
            local deferred = task.defer(function() print('deferred') end)
            local waiting = task.spawn(function()
                task.wait(0.05)
                print('waited')
            end)
            task.cancel(delayed)
            task.cancel(deferred)
            task.cancel(waiting)
            task.delay(0.05, function() print('kept') end)
            """);

        level.Play().Tick(10);

        level.Printed.ShouldBe(["kept"]);
        level.Errors.ShouldBeEmpty();
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void Spawn_and_defer_resume_a_thread_a_script_holds_with_the_values_given()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local thread = coroutine.create(function(first)
                print('started with ' .. first)
                print('resumed with ' .. coroutine.yield())
                print('deferred with ' .. select('#', coroutine.yield()) .. ' values')
            end)
            task.spawn(thread, 'one')
            task.spawn(thread, 'two')
            task.defer(thread, nil, 'three')
            print('end')
            """);

        level.Play().Tick();

        level.Printed.ShouldBe(["started with one", "resumed with two", "end", "deferred with 2 values"]);
        level.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void The_old_wait_spawn_and_delay_work_and_hand_over_the_time_that_passed()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            spawn(function(passed, now) print('spawn', passed, now) end)
            delay(0.05, function(passed, now) print('delay', passed, now) end)
            local passed, now = wait(0.1)
            print('wait', passed, now)
            """);

        level.Play().Tick(10);

        double step = ScriptLevel.Dt;
        level.Printed.ShouldBe(
        [
            $"spawn {Text(step)} {Text(2 * step)}",
            $"delay {Text(3 * step)} {Text((float)(4 * step))}",
            $"wait {Text(6 * step)} {Text((float)(7 * step))}",
        ]);
    }

    [Fact]
    public void A_script_and_a_wire_due_on_one_tick_run_in_the_order_they_were_queued()
    {
        using var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Speaker").Entity = new EntityData(SayingEntity.Class);
        level.AddScript(level.Scene.Root, "task.wait(0.5) print('script')");

        // Thirty ticks from the first one is tick 31 for all three: one wire
        // queued before the script waits and one after.
        level.Play();
        level.Entities.QueueInput("Speaker", "Say", "wire before", delay: 31 * ScriptLevel.Dt);
        level.Tick();
        level.Entities.QueueInput("Speaker", "Say", "wire after", delay: 30 * ScriptLevel.Dt);

        level.Tick(29);
        level.Printed.ShouldBeEmpty();

        level.Tick();
        level.Entities.TickNumber.ShouldBe(31);
        level.Printed.ShouldBe(["wire before", "script", "wire after"]);
    }

    [Fact]
    public void Waiting_threads_past_a_ticks_budget_are_postponed_and_none_is_lost()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            for index = 1, 10 do
                task.spawn(function()
                    task.wait()
                    print(index)
                end)
            end
            """);

        level.Play();
        level.Entities.MaxDispatchesPerTick = 4;
        level.Tick(2);
        level.Printed.ShouldBe(["1", "2", "3", "4"]);

        level.Tick(2);
        level.Printed.ShouldBe(["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"]);
    }

    [Fact]
    public void A_defer_loop_with_no_wait_is_cut_off_and_the_level_plays_on()
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, """
            local function again() task.defer(again) end
            again()
            task.wait(0.05)
            print('still here')
            """);

        level.Play();
        level.Entities.MaxDispatchesPerTick = 64;
        level.Tick(10);

        level.Printed.ShouldBe(["still here"]);
        level.Entities.DispatchBudgetTripCount.ShouldBe(1);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void A_wait_handed_something_else_than_a_number_is_an_error_at_its_line()
    {
        using var level = new ScriptLevel();
        SceneNode script = level.AddScript(level.Scene.Root, "print('a')\ntask.wait({})");

        level.Play().Tick(2);

        level.Sink.Messages[^1].Position.ShouldBe($"{script.Script.ShouldNotBeNull().Path}:2");
        level.Errors.ShouldBe(["invalid argument #1 to 'wait' (number expected, got table)"]);
    }

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    // A number the way Luau prints it: the shortest text that reads back the same.
    private static string Text(double number) => number.ToString("R", CultureInfo.InvariantCulture);
}
