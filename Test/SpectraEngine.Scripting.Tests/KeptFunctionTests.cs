using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The host keeps a function or a table of a script past the call that
/// handed it over, and calls the function later with arguments.
/// </summary>
public sealed class KeptFunctionTests
{
    [Fact]
    public void A_kept_function_is_called_with_arguments_and_its_results_are_read()
    {
        using LuauHost host = OpenWithKeep(out _, out Kept kept);
        host.RunText("keep(function(name, count) return name .. count, count + 1 end)").ShouldBe(LuauResume.Finished);
        LuauFunction function = kept.Function.ShouldNotBeNull();

        LuauResume outcome = function.Start(["door", 2], out LuauThread thread);

        outcome.ShouldBe(LuauResume.Finished);
        thread.ValueCount.ShouldBe(2);
        thread.Values.GetText(1).ShouldBe("door2");
        thread.Values.TryGetNumber(2, out double next).ShouldBeTrue();
        next.ShouldBe(3);
    }

    [Fact]
    public void A_kept_function_is_called_again_and_again()
    {
        using LuauHost host = OpenWithKeep(out _, out Kept kept);
        host.RunText("local calls = 0\nkeep(function() calls += 1 return calls end)").ShouldBe(LuauResume.Finished);
        LuauFunction function = kept.Function.ShouldNotBeNull();

        function.Start().ShouldBe(LuauResume.Finished);
        function.Start().ShouldBe(LuauResume.Finished);
        function.Start([], out LuauThread third).ShouldBe(LuauResume.Finished);

        third.Values.TryGetNumber(1, out double calls).ShouldBeTrue();
        calls.ShouldBe(3);
    }

    [Fact]
    public void A_kept_function_runs_on_a_thread_of_its_own_and_may_yield()
    {
        using LuauHost host = OpenWithKeep(out _, out Kept kept);
        host.RunText("keep(function(first) local second = coroutine.yield(first) return first + second end)")
            .ShouldBe(LuauResume.Finished);
        LuauFunction function = kept.Function.ShouldNotBeNull();

        LuauResume started = function.Start([40], out LuauThread thread);
        thread.Values.TryGetNumber(1, out double yielded).ShouldBeTrue();
        LuauResume resumed = thread.Resume([2]);

        started.ShouldBe(LuauResume.Yielded);
        yielded.ShouldBe(40);
        resumed.ShouldBe(LuauResume.Finished);
        thread.Values.TryGetNumber(1, out double sum).ShouldBeTrue();
        sum.ShouldBe(42);
    }

    [Fact]
    public void A_kept_function_belongs_to_its_script_and_sees_its_globals()
    {
        using LuauHost host = OpenWithKeep(out RecordingSink sink, out Kept kept);
        LuauScript script = host.LoadText("greeting = 'hello from the lamp'\nkeep(function() print(getfenv(0).greeting) end)", "Scripts/lamp.luau");
        script.Start().ShouldBe(LuauResume.Finished);
        LuauFunction function = kept.Function.ShouldNotBeNull();

        function.Start([], out LuauThread thread).ShouldBe(LuauResume.Finished);

        function.Script.ShouldBe(script);
        sink.Texts(ScriptSeverity.Print).ShouldBe(["hello from the lamp"]);
    }

    [Fact]
    public void An_error_in_a_kept_function_goes_to_the_sink_and_the_function_can_be_called_again()
    {
        using LuauHost host = OpenWithKeep(out RecordingSink sink, out Kept kept);
        host.RunText("keep(function(speed)\n    if speed < 0 then error('no negative speed') end\n    return speed\nend)")
            .ShouldBe(LuauResume.Finished);
        LuauFunction function = kept.Function.ShouldNotBeNull();

        LuauResume failed = function.Start([-1]);
        LuauResume worked = function.Start([5]);

        failed.ShouldBe(LuauResume.Failed);
        worked.ShouldBe(LuauResume.Finished);
        ScriptMessage message = sink.Single;
        message.Text.ShouldBe("no negative speed");
        message.Line.ShouldBe(2);
    }

    [Fact]
    public void A_function_that_was_let_go_is_not_called()
    {
        using LuauHost host = OpenWithKeep(out RecordingSink sink, out Kept kept);
        host.RunText("keep(function() print('called') end)").ShouldBe(LuauResume.Finished);
        LuauFunction function = kept.Function.ShouldNotBeNull();

        function.Dispose();
        LuauResume outcome = function.Start();

        outcome.ShouldBe(LuauResume.Refused);
        sink.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void Something_that_is_not_a_function_is_not_kept_as_one()
    {
        using LuauHost host = OpenWithKeep(out _, out Kept kept);

        host.RunText("keep('not a function')").ShouldBe(LuauResume.Finished);

        kept.Function.ShouldBeNull();
    }

    [Fact]
    public void A_kept_table_is_handed_back_to_a_script_as_the_same_table()
    {
        LuauReference? table = null;
        using LuauHost host = TestHost.Open(out _, fresh => fresh.SetGlobal("keep"u8, stack =>
        {
            table = fresh.Keep(stack, 1);
            return 0;
        }));
        LuauScript script = host.LoadText("local settings = { speed = 20 }\nkeep(settings)\nlocal back = coroutine.yield()\nreturn back == settings, back.speed");
        script.Start().ShouldBe(LuauResume.Yielded);
        host.Collect();

        script.Thread.Resume([table]).ShouldBe(LuauResume.Finished);

        script.Thread.Values.GetBoolean(1).ShouldBeTrue();
        script.Thread.Values.TryGetNumber(2, out double speed).ShouldBeTrue();
        speed.ShouldBe(20);
    }

    // A host with keep(value), which keeps a function for the test to call.
    private static LuauHost OpenWithKeep(out RecordingSink sink, out Kept kept)
    {
        Kept made = new();
        kept = made;
        return TestHost.Open(out sink, fresh => fresh.SetGlobal("keep"u8, stack =>
        {
            made.Function = fresh.KeepFunction(stack, 1);
            return 0;
        }));
    }

    private sealed class Kept
    {
        internal LuauFunction? Function { get; set; }
    }
}
