using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A script calls into the host, by the two ways in: a static function
/// pointer and a delegate.
/// </summary>
public sealed unsafe class HostFunctionTests
{
    [Fact]
    public void A_script_calls_a_function_given_as_a_static_pointer()
    {
        using LuauHost host = TestHost.Open(out _, SetHalve);

        LuauScript script = host.LoadText("return halve(84)");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.TryGetNumber(1, out double half).ShouldBeTrue();
        half.ShouldBe(42);
    }

    [Fact]
    public void A_script_calls_a_function_given_as_a_delegate()
    {
        int calls = 0;
        using LuauHost host = TestHost.Open(out _, fresh => fresh.SetGlobal("count"u8, stack =>
        {
            calls++;
            stack.Push(calls).ShouldBeTrue();
            return 1;
        }));

        LuauScript script = host.LoadText("count()\ncount()\nreturn count()");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.TryGetNumber(1, out double third).ShouldBeTrue();
        third.ShouldBe(3);
        calls.ShouldBe(3);
    }

    [Fact]
    public void A_host_function_returns_several_values()
    {
        using LuauHost host = TestHost.Open(out _, fresh => fresh.SetGlobal("pair"u8, stack =>
        {
            stack.Push("left").ShouldBeTrue();
            stack.Push(false).ShouldBeTrue();
            return 2;
        }));

        LuauScript script = host.LoadText("local a, b = pair()\nreturn b, a");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.TypeOf(1).ShouldBe(LuauType.Boolean);
        script.Thread.Values.GetBoolean(1).ShouldBeFalse();
        script.Thread.Values.GetText(2).ShouldBe("left");
    }

    [Fact]
    public void A_host_function_finds_the_script_that_called_it()
    {
        string? caller = null;
        using LuauHost host = TestHost.Open(out _, fresh => fresh.SetGlobal("who"u8, stack =>
        {
            caller = fresh.ScriptOf(stack)?.Name;
            return 0;
        }));

        host.RunText("who()", "Scripts/lamp.luau").ShouldBe(LuauResume.Finished);

        caller.ShouldBe("Scripts/lamp.luau");
    }

    // A coroutine belongs to the script that made it.
    [Fact]
    public void A_host_function_called_from_a_coroutine_finds_the_script_that_made_it()
    {
        string? caller = null;
        using LuauHost host = TestHost.Open(out _, fresh => fresh.SetGlobal("who"u8, stack =>
        {
            caller = fresh.ScriptOf(stack)?.Name;
            return 0;
        }));

        host.RunText("coroutine.wrap(function() who() end)()", "Scripts/lamp.luau").ShouldBe(LuauResume.Finished);

        caller.ShouldBe("Scripts/lamp.luau");
    }

    [Fact]
    public void A_failure_of_a_host_function_is_an_error_at_the_line_of_the_call()
    {
        using LuauHost host = TestHost.Open(out _, SetHalve);

        LuauScript script = host.LoadText("local first = 1\nlocal ok, message = pcall(halve, 'door')\nreturn ok, message");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.GetBoolean(1).ShouldBeFalse();
        script.Thread.Values.GetText(2).ShouldBe("Scripts/test.luau:2: halve takes a number");
    }

    [Fact]
    public void An_exception_in_a_delegate_reaches_the_script_as_an_error_it_can_catch()
    {
        using LuauHost host = TestHost.Open(out _, fresh => fresh.SetGlobal("explode"u8, _ =>
            throw new InvalidOperationException("the door is locked")));

        LuauScript script = host.LoadText("local ok, message = pcall(explode)\nreturn ok, message");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.GetBoolean(1).ShouldBeFalse();
        script.Thread.Values.GetText(2).ShouldBe("Scripts/test.luau:1: the door is locked");
    }

    [Fact]
    public void An_exception_in_a_static_function_reaches_the_script_as_an_error_it_can_catch()
    {
        using LuauHost host = TestHost.Open(out _, SetHalve);

        LuauScript script = host.LoadText("local ok, message = pcall(halve, -1)\nreturn ok, message");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.GetBoolean(1).ShouldBeFalse();
        script.Thread.Values.GetText(2).ShouldBe("Scripts/test.luau:1: halve was given a negative number");
    }

    [Fact]
    public void An_exception_nothing_catches_goes_to_the_sink_with_the_line_of_the_call()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink, fresh => fresh.SetGlobal("explode"u8, _ =>
            throw new InvalidOperationException("the door is locked")));

        LuauResume outcome = host.RunText("local first = 1\n\nexplode()");

        outcome.ShouldBe(LuauResume.Failed);
        ScriptMessage message = sink.Single;
        message.Severity.ShouldBe(ScriptSeverity.Error);
        message.Text.ShouldBe("the door is locked");
        message.Script.ShouldBe(TestHost.ScriptName);
        message.Line.ShouldBe(3);
    }

    [Fact]
    public void A_host_function_calls_back_into_the_script()
    {
        using LuauHost host = TestHost.Open(out _, fresh => fresh.SetGlobal("twice"u8, stack =>
        {
            // The function is argument 1. It is called with a copy of argument 2.
            for (int round = 0; round < 2; round++)
            {
                stack.PushCopy(1).ShouldBeTrue();
                stack.PushCopy(2).ShouldBeTrue();
                if (!stack.Call(arguments: 1, results: 0))
                    return LuauStack.Raise;
            }

            return 0;
        }));

        LuauScript script = host.LoadText("local sum = 0\ntwice(function(n) sum += n end, 21)\nreturn sum");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.TryGetNumber(1, out double sum).ShouldBeTrue();
        sum.ShouldBe(42);
    }

    private static void SetHalve(LuauHost host)
    {
        host.Stack.PushFunction(&Halve, "halve"u8).ShouldBeTrue();
        host.Stack.SetGlobal("halve"u8).ShouldBeTrue();
    }

    // The shape of a function on the fast way in: it reads the stack it was
    // called with, and its last catch turns an exception into an error.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Halve(nint state)
    {
        LuauStack stack = new(state);
        try
        {
            if (!stack.TryGetNumber(1, out double value))
                return stack.Fail("halve takes a number");

            if (value < 0)
                throw new ArgumentException("halve was given a negative number");
            return stack.Push(value / 2) ? 1 : LuauStack.Raise;
        }
        catch (Exception e)
        {
            return stack.Fail(e);
        }
    }
}
