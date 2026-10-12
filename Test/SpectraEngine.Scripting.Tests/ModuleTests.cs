using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What a module is: a file that runs once in a play session, on a thread
/// and with globals of its own, and hands everyone who asks the one value
/// it returned.
/// </summary>
public sealed class ModuleTests
{
    private const string LoadFailed = "Requested module experienced an error while loading";

    [Fact]
    public void A_module_runs_once_and_two_scripts_get_the_same_table()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/count.luau", "print('counting')\nreturn { n = 0 }");
        level.ScriptAt("Scripts/first.luau", """
            local count = require('./count')
            count.n += 1
            print('first', count.n)
            """);
        level.ScriptAt("Scripts/second.luau", """
            local count = require('./count')
            count.n += 1
            print('second', count.n, count == require('@assets/Scripts/count'))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["counting", "first 1", "second 2 true"]);
    }

    [Fact]
    public void A_module_runs_again_in_the_next_play_session()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/count.luau", "print('counting')\nreturn { n = 0 }");
        level.ScriptAt("Scripts/main.luau", """
            local count = require('./count')
            count.n += 1
            print(count.n)
            """);

        level.Play().Tick();
        level.Stop();
        level.Play().Tick();

        level.Printed.ShouldBe(["counting", "1", "counting", "1"]);
    }

    [Fact]
    public void A_module_has_globals_of_its_own_and_no_script_unless_it_was_asked_for_by_node()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/own.luau", """
            speed = 20
            return { script = script, speed = function() return speed end }
            """);
        level.ScriptAt("Scripts/main.luau", """
            speed = 5
            local own = require('./own')
            print(own.script, own.speed(), speed)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["nil 20 5"]);
    }

    [Fact]
    public void A_module_may_return_any_one_value()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/double.luau", "return function(n) return n * 2 end");
        level.FileAt("Scripts/no.luau", "return false");
        level.FileAt("Scripts/none.luau", "print('ran')\nreturn nil");
        level.ScriptAt("Scripts/main.luau", """
            print(require('./double')(21), require('./no'), require('./none'), require('./none'))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["ran", "42 false nil nil"]);
    }

    [Theory]
    [InlineData("print('ran')")]
    [InlineData("print('ran')\nreturn 1, 2")]
    public void A_module_that_does_not_return_one_value_fails_in_Robloxs_words(string module)
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/odd.luau", module);
        level.ScriptAt("Scripts/main.luau", """
            print(pcall(require, './odd'))
            print(pcall(require, './odd'))
            """);

        level.Play().Tick();

        const string Failure = $"{LoadFailed}: Scripts/odd.luau: Module code did not return exactly one value";
        level.Printed.ShouldBe(
        [
            "ran",
            $"false Scripts/main.luau:1: {Failure}",
            $"false Scripts/main.luau:2: {Failure}",
        ]);
        level.ErrorsWithPlace().ShouldBe(["Scripts/odd.luau: Module code did not return exactly one value"]);
    }

    [Fact]
    public void A_module_that_fails_is_reported_once_and_everyone_who_asks_gets_its_error_with_its_file_and_line()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/broken.luau", "print('running')\nerror('boom')");
        level.ScriptAt("Scripts/first.luau", """
            print(pcall(require, './broken'))
            print(pcall(require, './broken'))
            """);
        level.ScriptAt("Scripts/second.luau", "local x = 1\nrequire('./broken')\nprint('not reached')");

        level.Play().Tick();

        level.Printed.ShouldBe(
        [
            "running",
            $"false Scripts/first.luau:1: {LoadFailed}: Scripts/broken.luau:2: boom",
            $"false Scripts/first.luau:2: {LoadFailed}: Scripts/broken.luau:2: boom",
        ]);
        level.ErrorsWithPlace().ShouldBe(
        [
            "Scripts/broken.luau:2: boom",
            $"Scripts/second.luau:2: {LoadFailed}: Scripts/broken.luau:2: boom",
        ]);
    }

    [Fact]
    public void A_module_that_does_not_compile_fails_with_the_line_of_its_mistake()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/typo.luau", "local a = 1\nlocal = 2");
        level.ScriptAt("Scripts/main.luau", "print(pcall(require, './typo'))");

        level.Play().Tick();

        ScriptMessage mistake = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        mistake.Position.ShouldBe("Scripts/typo.luau:2");
        mistake.ProblemNode.ShouldBe(Guid.Empty);
        level.Printed.ShouldBe([$"false Scripts/main.luau:1: {LoadFailed}: Scripts/typo.luau:2: {mistake.Text}"]);
    }

    [Fact]
    public void The_chunk_name_of_a_module_is_its_content_path_in_an_error_a_stack_and_a_print()
    {
        using var level = new ScriptLevel();
        level.FileAt("Shared/tools/saw.luau", """
            print('sharpening')
            return {
                cut = function(board)
                    return board.length / 2
                end,
            }
            """);
        level.ScriptAt("Scripts/main.luau", """
            local saw = require('@assets/Shared/tools/saw')
            saw.cut(nil)
            """);

        level.Play().Tick();

        level.Sink.Messages[0].Script.ShouldBe("Shared/tools/saw.luau");
        level.Sink.Messages[0].Line.ShouldBe(1);
        ScriptMessage failure = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        failure.Position.ShouldBe("Shared/tools/saw.luau:4");
        failure.Text.ShouldBe("attempt to index nil with 'length'");
        failure.Stack.ShouldBe(
        [
            new ScriptFrame("Shared/tools/saw.luau", 4, "cut"),
            new ScriptFrame("Scripts/main.luau", 2, null),
        ]);
    }

    [Fact]
    public void A_module_that_waits_before_it_returns_is_refused_at_the_line_that_waited()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/slow.luau", "print('before')\ntask.wait(0.1)\nprint('after')\nreturn 1");
        level.ScriptAt("Scripts/main.luau", """
            print(pcall(require, './slow'))
            print(pcall(require, './slow'))
            """);

        level.Play().Tick(30);

        const string Waited =
            "a module cannot wait before it returns. Start a thread with task.spawn for what has to wait";
        level.Printed.ShouldBe(
        [
            "before",
            $"false Scripts/main.luau:1: {LoadFailed}: Scripts/slow.luau:2: {Waited}",
            $"false Scripts/main.luau:2: {LoadFailed}: Scripts/slow.luau:2: {Waited}",
        ]);
        level.ErrorsWithPlace().ShouldBe([$"Scripts/slow.luau:2: {Waited}"]);
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("task.wait()")]
    [InlineData("wait()")]
    [InlineData("game:GetService('RunService').Heartbeat:Wait()")]
    [InlineData("coroutine.yield()")]
    public void Every_way_to_wait_is_refused_while_a_module_loads_and_nothing_wakes_it_later(string wait)
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/slow.luau", $"local a = 1\n\n{wait}\nprint('after')\nreturn a");
        level.ScriptAt("Scripts/main.luau", "print(pcall(require, './slow'))");

        level.Play().Tick(5);

        level.ErrorsWithPlace().ShouldHaveSingleItem().ShouldStartWith(
            "Scripts/slow.luau:3: a module cannot wait before it returns.");
        level.Printed.ShouldHaveSingleItem().ShouldStartWith("false Scripts/main.luau:1: ");
        level.Runtime.ShouldNotBeNull().WaitingThreadCount.ShouldBe(0);
    }

    [Fact]
    public void A_function_of_a_module_may_wait_and_the_script_that_calls_it_waits()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/timer.luau", """
            return { after = function(seconds) task.wait(seconds) return 'done' end }
            """);
        level.ScriptAt("Scripts/main.luau", """
            local timer = require('./timer')
            print(timer.after(0.05))
            """);

        level.Play().Tick();
        level.Printed.ShouldBeEmpty();
        level.Tick(5);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["done"]);
    }

    [Fact]
    public void A_module_may_start_a_thread_that_waits_and_what_it_connected_goes_on()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/clock.luau", """
            local clock = { ticks = 0, beats = 0 }
            task.spawn(function()
                while true do
                    task.wait()
                    clock.ticks += 1
                end
            end)
            game:GetService("RunService").Heartbeat:Connect(function() clock.beats += 1 end)
            return clock
            """);
        level.ScriptAt("Scripts/main.luau", """
            local clock = require('./clock')
            task.wait(0.1)
            print(clock.ticks > 3, clock.beats > 3)
            """);

        level.Play().Tick(10);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true true"]);
    }

    [Fact]
    public void A_module_that_never_returns_is_stopped_by_the_watchdog_and_the_asker_gets_an_error_and_goes_on()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/forever.luau", "local turns = 0\nwhile true do turns += 1 end");
        level.ScriptAt("Scripts/main.luau", """
            print(pcall(require, './forever'))
            print(pcall(require, './forever'))
            print('went on')
            """);

        level.Play();
        level.StopTheNextHangSoon();
        level.Tick();

        const string Stopped =
            "Scripts/forever.luau:2: the script ran for more than 0.1 s without a pause and was switched off";
        level.ErrorsWithPlace().ShouldBe([Stopped]);
        level.Printed.ShouldBe(
        [
            $"false Scripts/main.luau:1: {LoadFailed}: {Stopped}",
            $"false Scripts/main.luau:2: {LoadFailed}: {Stopped}",
            "went on",
        ]);
    }
}
