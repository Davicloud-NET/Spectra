using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Threads that wait: a yield with values, a resume with values, a cancel.
/// This is what a scheduler sits on.
/// </summary>
public sealed class ThreadTests
{
    [Fact]
    public void A_script_yields_values_and_goes_on_with_the_values_it_is_resumed_with()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript script = host.LoadText("local answer = coroutine.yield('ready', 7)\nreturn answer * 2");

        LuauResume first = script.Start();
        int yielded = script.Thread.ValueCount;
        string? word = script.Thread.Values.GetText(1);
        script.Thread.Values.TryGetNumber(2, out double number).ShouldBeTrue();
        LuauResume second = script.Thread.Resume([21]);

        first.ShouldBe(LuauResume.Yielded);
        yielded.ShouldBe(2);
        word.ShouldBe("ready");
        number.ShouldBe(7);
        second.ShouldBe(LuauResume.Finished);
        script.Thread.Values.TryGetNumber(1, out double result).ShouldBeTrue();
        result.ShouldBe(42);
    }

    [Fact]
    public void A_host_function_yields_the_script_and_a_resume_gives_the_call_its_results()
    {
        LuauThread waiting = default;
        double seconds = 0;
        using LuauHost host = TestHost.Open(out _, fresh => fresh.SetGlobal("wait"u8, stack =>
        {
            stack.TryGetNumber(1, out seconds).ShouldBeTrue();
            waiting = fresh.ThreadOf(stack);
            return LuauStack.Yield();
        }));
        LuauScript script = host.LoadText("local waited, note = wait(0.5)\nreturn waited, note");

        LuauResume first = script.Start();
        LuauResume second = waiting.Resume([0.75, "late"]);

        first.ShouldBe(LuauResume.Yielded);
        seconds.ShouldBe(0.5);
        waiting.ShouldBe(script.Thread);
        second.ShouldBe(LuauResume.Finished);
        waiting.Values.TryGetNumber(1, out double waited).ShouldBeTrue();
        waited.ShouldBe(0.75);
        waiting.Values.GetText(2).ShouldBe("late");
    }

    [Fact]
    public void A_host_function_yields_values_to_whoever_resumes()
    {
        using LuauHost host = TestHost.Open(out _, fresh => fresh.SetGlobal("ask"u8, stack =>
        {
            stack.Push("question").ShouldBeTrue();
            return LuauStack.Yield(1);
        }));
        LuauScript script = host.LoadText("return ask()");

        LuauResume outcome = script.Start();

        outcome.ShouldBe(LuauResume.Yielded);
        script.Thread.ValueCount.ShouldBe(1);
        script.Thread.Values.GetText(1).ShouldBe("question");
    }

    [Fact]
    public void A_cancelled_thread_never_runs_again()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        LuauScript script = host.LoadText("coroutine.yield()\nprint('went on')");
        script.Start().ShouldBe(LuauResume.Yielded);

        bool isCancelled = script.Thread.Cancel();
        LuauResume outcome = script.Thread.Resume();

        isCancelled.ShouldBeTrue();
        outcome.ShouldBe(LuauResume.Refused);
        script.Thread.IsAlive.ShouldBeFalse();
        sink.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void A_thread_that_ended_is_gone_for_every_copy_of_its_handle()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript script = host.LoadText("return 1");
        LuauThread copy = script.Thread;

        script.Start().ShouldBe(LuauResume.Finished);

        copy.IsAlive.ShouldBeFalse();
        copy.Resume().ShouldBe(LuauResume.Refused);
        copy.Cancel().ShouldBeFalse();
    }

    [Fact]
    public void What_a_thread_returned_is_gone_once_another_thread_has_ended()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript first = host.LoadText("return 1", "first.luau");
        LuauScript second = host.LoadText("return 2", "second.luau");
        first.Start().ShouldBe(LuauResume.Finished);

        second.Start().ShouldBe(LuauResume.Finished);

        Should.Throw<InvalidOperationException>(() => first.Thread.ValueCount);
        second.Thread.Values.TryGetNumber(1, out double value).ShouldBeTrue();
        value.ShouldBe(2);
    }

    [Fact]
    public void A_handle_that_was_never_set_refuses_everything()
    {
        LuauThread none = default;

        none.IsAlive.ShouldBeFalse();
        none.Resume().ShouldBe(LuauResume.Refused);
        none.Cancel().ShouldBeFalse();
        none.Script.ShouldBeNull();
    }

    // A coroutine a script made itself yields into the host. The host keeps
    // it from then on, and resumes it like any other thread.
    [Fact]
    public void A_coroutine_a_script_made_waits_in_the_host_and_is_resumed_by_it()
    {
        LuauThread waiting = default;
        using LuauHost host = TestHost.Open(out RecordingSink sink, fresh => fresh.SetGlobal("wait"u8, stack =>
        {
            waiting = fresh.ThreadOf(stack);
            return LuauStack.Yield();
        }));
        LuauScript script = host.LoadText("coroutine.wrap(function()\n    print('woke with', wait())\nend)()\nreturn 'top level done'");

        LuauResume top = script.Start();
        LuauScript? owner = waiting.Script;
        LuauResume inner = waiting.Resume(["a nudge"]);

        top.ShouldBe(LuauResume.Finished);
        waiting.ShouldNotBe(script.Thread);
        owner.ShouldBe(script);
        inner.ShouldBe(LuauResume.Finished);
        sink.Texts(ScriptSeverity.Print).ShouldBe(["woke with a nudge"]);
    }

    [Fact]
    public void A_host_function_is_handed_a_coroutine_and_the_host_resumes_it()
    {
        LuauThread handed = default;
        LuauThread notOne = default;
        using LuauHost host = TestHost.Open(out RecordingSink sink, fresh => fresh.SetGlobal("later"u8, stack =>
        {
            handed = fresh.ThreadAt(stack, 1);
            notOne = fresh.ThreadAt(stack, 2);
            return 0;
        }));
        host.RunText("later(coroutine.create(function(word) print('ran with', word) end), 'not a thread')")
            .ShouldBe(LuauResume.Finished);

        LuauResume outcome = handed.Resume(["a word"]);

        outcome.ShouldBe(LuauResume.Finished);
        notOne.IsAlive.ShouldBeFalse();
        sink.Texts(ScriptSeverity.Print).ShouldBe(["ran with a word"]);
    }

    // What task.spawn will be: a function and its arguments, straight from
    // the caller's stack onto a thread of their own.
    [Fact]
    public void A_host_function_starts_a_function_with_the_arguments_on_its_stack()
    {
        LuauResume started = LuauResume.Refused;
        using LuauHost host = TestHost.Open(out RecordingSink sink, fresh => fresh.SetGlobal("spawn"u8, stack =>
        {
            started = fresh.Start(stack, stack.Count, out _);
            return 0;
        }));

        LuauResume outcome = host.RunText("spawn(function(a, b) print(a + b) end, 40, 2)\nprint('after')");

        outcome.ShouldBe(LuauResume.Finished);
        started.ShouldBe(LuauResume.Finished);
        sink.Texts(ScriptSeverity.Print).ShouldBe(["42", "after"]);
    }

    [Fact]
    public void A_host_function_hands_the_thread_it_started_back_to_the_script()
    {
        using LuauHost host = TestHost.Open(out _, fresh => fresh.SetGlobal("spawn"u8, stack =>
        {
            fresh.Start(stack, stack.Count, out LuauThread started);
            return started.Push(stack) ? 1 : LuauStack.Raise;
        }));
        LuauScript script = host.LoadText(
            "local waiting = spawn(function() coroutine.yield() end)\nlocal ended = spawn(function() end)\nreturn type(waiting), coroutine.status(waiting), ended");

        script.Start().ShouldBe(LuauResume.Finished);

        script.Thread.Values.GetText(1).ShouldBe("thread");
        script.Thread.Values.GetText(2).ShouldBe("suspended");
        script.Thread.Values.IsNil(3).ShouldBeTrue();
    }

    // What task.defer and task.delay will be: the thread is there at once,
    // with its function and arguments, and runs when the host says so.
    [Fact]
    public void A_host_function_prepares_a_thread_that_runs_later_with_its_arguments()
    {
        LuauThread deferred = default;
        using LuauHost host = TestHost.Open(out RecordingSink sink, fresh => fresh.SetGlobal("defer"u8, stack =>
        {
            fresh.Prepare(stack, stack.Count, out deferred).ShouldBeTrue();
            return deferred.Push(stack) ? 1 : LuauStack.Raise;
        }));
        host.RunText("local later = defer(function(a, b, c) print(a + b, c) end, 40, 2)\nprint(coroutine.status(later))")
            .ShouldBe(LuauResume.Finished);
        string[] before = sink.Texts(ScriptSeverity.Print);

        LuauResume outcome = deferred.Resume(["and one more"]);

        before.ShouldBe(["suspended"]);
        outcome.ShouldBe(LuauResume.Finished);
        sink.Texts(ScriptSeverity.Print).ShouldBe(["suspended", "42 and one more"]);
    }

    [Fact]
    public void A_thread_goes_to_another_script_as_a_value()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript waiting = host.LoadText("coroutine.yield()", "Scripts/waiting.luau");
        LuauScript asking = host.LoadText("local other = ...\nreturn coroutine.status(other)", "Scripts/asking.luau");
        waiting.Start().ShouldBe(LuauResume.Yielded);

        asking.Start([waiting.Thread]).ShouldBe(LuauResume.Finished);

        asking.Thread.Values.GetText(1).ShouldBe("suspended");
    }

    // Each start nests on the native stack. Luau counts the nesting and
    // answers with an error before the stack runs out.
    [Fact]
    public void A_function_that_starts_itself_without_end_gets_an_error_and_the_host_goes_on()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink, fresh => fresh.SetGlobal("spawn"u8, stack =>
        {
            fresh.Start(stack, stack.Count, out _);
            return 0;
        }));

        host.RunText("local function again() spawn(again) end\nagain()").ShouldBe(LuauResume.Finished);
        LuauScript after = host.LoadText("return 'fine'");

        after.Start().ShouldBe(LuauResume.Finished);
        sink.Texts(ScriptSeverity.Error).ShouldHaveSingleItem().ShouldContain("stack overflow");
    }
}
