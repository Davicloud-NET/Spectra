using System.Diagnostics;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A script that will not come back is stopped at the line it was on and
/// switched off, and the host goes on with the others.
/// </summary>
public sealed class WatchdogTests
{
    private static readonly TimeSpan _shortLimit = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void A_host_gives_a_call_three_seconds_unless_told_otherwise()
    {
        using LuauHost host = TestHost.Open(out _);

        host.CallLimit.ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void A_runaway_script_is_stopped_at_its_line_and_switched_off()
    {
        using LuauHost host = OpenWithShortLimit(out RecordingSink sink);
        LuauScript script = host.LoadText("local turns = 0\nwhile true do\n    turns += 1\nend", "Scripts/spinner.luau");

        Stopwatch watch = Stopwatch.StartNew();
        LuauResume outcome = script.Start();
        watch.Stop();

        outcome.ShouldBe(LuauResume.Failed);
        script.IsSwitchedOff.ShouldBeTrue();
        watch.Elapsed.ShouldBeGreaterThanOrEqualTo(_shortLimit);
        ScriptMessage message = sink.Single;
        message.Severity.ShouldBe(ScriptSeverity.Error);
        message.Script.ShouldBe("Scripts/spinner.luau");
        message.Line.ShouldBeInRange(2, 4);
        message.Text.ShouldBe("the script ran for more than 0.1 s without a pause and was switched off");
        message.Stack.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_host_runs_other_scripts_after_one_was_stopped()
    {
        using LuauHost host = OpenWithShortLimit(out _);
        host.RunText("while true do end", "Scripts/spinner.luau").ShouldBe(LuauResume.Failed);

        LuauScript other = host.LoadText("local sum = 0\nfor i = 1, 1000 do sum += i end\nreturn sum", "Scripts/other.luau");

        other.Start().ShouldBe(LuauResume.Finished);
        other.Thread.Values.TryGetNumber(1, out double sum).ShouldBeTrue();
        sum.ShouldBe(500500);
        other.IsSwitchedOff.ShouldBeFalse();
    }

    [Fact]
    public void A_script_that_catches_the_stop_is_stopped_all_the_same()
    {
        using LuauHost host = OpenWithShortLimit(out RecordingSink sink);
        LuauScript script = host.LoadText("while true do\n    pcall(function() while true do end end)\nend");

        LuauResume outcome = script.Start();

        outcome.ShouldBe(LuauResume.Failed);
        script.IsSwitchedOff.ShouldBeTrue();
        sink.Single.Text.ShouldContain("switched off");
    }

    [Fact]
    public void Nothing_of_a_switched_off_script_runs_again()
    {
        LuauFunction? kept = null;
        using LuauHost host = OpenWithShortLimit(out RecordingSink sink, fresh => fresh.SetGlobal("keep"u8, stack =>
        {
            kept = fresh.KeepFunction(stack, 1);
            return 0;
        }));
        LuauScript script = host.LoadText("keep(function() print('called') end)\ncoroutine.yield()\nwhile true do end");
        script.Start().ShouldBe(LuauResume.Yielded);
        script.Thread.Resume().ShouldBe(LuauResume.Failed);
        sink.Messages.Clear();

        LuauResume call = kept.ShouldNotBeNull().Start();

        call.ShouldBe(LuauResume.Refused);
        script.Thread.Resume().ShouldBe(LuauResume.Refused);
        sink.Messages.ShouldBeEmpty();
    }

    // The limit is for one call. A script that does a little every tick for
    // a long time never meets it.
    [Fact]
    public void A_script_that_runs_in_many_short_calls_is_not_stopped()
    {
        using LuauHost host = OpenWithShortLimit(out RecordingSink sink);
        LuauScript script = host.LoadText("while true do\n    for i = 1, 1000 do end\n    coroutine.yield()\nend");
        script.Start().ShouldBe(LuauResume.Yielded);

        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed < _shortLimit * 4)
            script.Thread.Resume().ShouldBe(LuauResume.Yielded);

        script.IsSwitchedOff.ShouldBeFalse();
        sink.Messages.ShouldBeEmpty();
    }

    // The script that hangs is the one that runs when the stop lands. The
    // script whose call led there goes on, with a new allowance.
    [Fact]
    public void Only_the_script_that_hangs_is_switched_off()
    {
        LuauFunction? hanging = null;
        LuauResume started = LuauResume.Refused;
        using LuauHost host = OpenWithShortLimit(out RecordingSink sink, fresh =>
        {
            fresh.SetGlobal("keep"u8, stack =>
            {
                hanging = fresh.KeepFunction(stack, 1);
                return 0;
            });
            fresh.SetGlobal("call_kept"u8, _ =>
            {
                started = hanging.ShouldNotBeNull().Start();
                return 0;
            });
        });
        LuauScript hanger = host.LoadText("keep(function() while true do end end)", "Scripts/hanger.luau");
        LuauScript caller = host.LoadText("call_kept()\nreturn 'went on'", "Scripts/caller.luau");
        hanger.Start().ShouldBe(LuauResume.Finished);

        LuauResume outcome = caller.Start();

        outcome.ShouldBe(LuauResume.Finished);
        caller.Thread.Values.GetText(1).ShouldBe("went on");
        started.ShouldBe(LuauResume.Failed);
        hanger.IsSwitchedOff.ShouldBeTrue();
        caller.IsSwitchedOff.ShouldBeFalse();
        sink.Single.Script.ShouldBe("Scripts/hanger.luau");
    }

    // The limit is for the whole call into Luau, so a slow host function
    // uses up the allowance of the script that called it.
    [Fact]
    public void The_time_a_host_function_takes_counts_for_the_script_that_called_it()
    {
        using LuauHost host = OpenWithShortLimit(out RecordingSink sink, fresh => fresh.SetGlobal("work"u8, _ =>
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.Elapsed < _shortLimit * 3)
                Thread.SpinWait(100);
            return 0;
        }));
        LuauScript script = host.LoadText("work()\nreturn 'back'");

        LuauResume outcome = script.Start();

        // The stop lands at the script's next line, once the host function is back.
        outcome.ShouldBe(LuauResume.Failed);
        script.IsSwitchedOff.ShouldBeTrue();
        sink.Single.Line.ShouldBe(2);
    }

    [Fact]
    public void A_limit_of_zero_turns_the_watchdog_off()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        host.CallLimit = TimeSpan.Zero;
        LuauScript script = host.LoadText("local deadline = os.clock() + 0.3\nwhile os.clock() < deadline do end\nreturn 'done'");

        LuauResume outcome = script.Start();

        outcome.ShouldBe(LuauResume.Finished);
        sink.Messages.ShouldBeEmpty();
    }

    private static LuauHost OpenWithShortLimit(out RecordingSink sink, Action<LuauHost>? setup = null)
    {
        LuauHost host = TestHost.Open(out sink, setup);
        host.CallLimit = _shortLimit;
        return host;
    }
}
