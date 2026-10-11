using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A sealed host: no script changes what all scripts share, and no script
/// sees another script's globals. It stops mistakes, not attackers.
/// </summary>
public sealed class SandboxTests
{
    [Fact]
    public void A_script_cannot_replace_a_library_function()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);

        LuauResume outcome = host.RunText("string.upper = function() return 'gone' end");

        outcome.ShouldBe(LuauResume.Failed);
        sink.Single.Text.ShouldContain("readonly");
        host.RunText("print(string.upper('still here'))", "Scripts/other.luau").ShouldBe(LuauResume.Finished);
        sink.Texts(ScriptSeverity.Print).ShouldBe(["STILL HERE"]);
    }

    [Fact]
    public void A_script_cannot_change_a_table_the_host_shares()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink, fresh =>
        {
            LuauStack stack = fresh.Stack;
            stack.NewTable().ShouldBeTrue();
            stack.Push(20).ShouldBeTrue();
            stack.SetField(-2, "speed"u8).ShouldBeTrue();
            stack.SetGlobal("settings"u8).ShouldBeTrue();
        });

        LuauResume outcome = host.RunText("settings.speed = 0");

        outcome.ShouldBe(LuauResume.Failed);
        sink.Single.Text.ShouldContain("readonly");
    }

    // Luau's own sandbox call freezes the tables that are globals, and not
    // what they hold. Sealing goes all the way down.
    [Fact]
    public void A_script_cannot_change_a_table_inside_a_table_the_host_shares()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink, fresh =>
        {
            LuauStack stack = fresh.Stack;
            stack.NewTable().ShouldBeTrue();
            stack.NewTable().ShouldBeTrue();
            stack.Push(20).ShouldBeTrue();
            stack.SetField(-2, "speed"u8).ShouldBeTrue();
            stack.SetField(-2, "door"u8).ShouldBeTrue();
            stack.SetGlobal("settings"u8).ShouldBeTrue();
        });

        LuauResume outcome = host.RunText("settings.door.speed = 0");

        outcome.ShouldBe(LuauResume.Failed);
        sink.Single.Text.ShouldContain("readonly");
    }

    [Fact]
    public void A_shared_table_that_holds_itself_is_sealed_all_the_same()
    {
        using LuauHost host = TestHost.Open(out _, fresh =>
        {
            LuauStack stack = fresh.Stack;
            stack.NewTable().ShouldBeTrue();
            stack.PushCopy(-1).ShouldBeTrue();
            stack.SetField(-2, "self"u8).ShouldBeTrue();
            stack.SetGlobal("ring"u8).ShouldBeTrue();
        });

        LuauScript script = host.LoadText("return pcall(function() ring.self.self.added = 1 end)");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.GetBoolean(1).ShouldBeFalse();
    }

    // An assignment to a global lands in the script's own table. The shared
    // one keeps what the host put there, for every other script.
    [Fact]
    public void A_script_that_sets_a_shared_global_changes_it_for_itself_only()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        host.RunText("print = function() end\nprint('silenced')\n_G.print = print", "Scripts/first.luau")
            .ShouldBe(LuauResume.Failed);
        sink.Messages.Clear();

        host.RunText("print('still loud')", "Scripts/second.luau").ShouldBe(LuauResume.Finished);

        sink.Texts(ScriptSeverity.Print).ShouldBe(["still loud"]);
    }

    [Fact]
    public void Two_scripts_do_not_see_each_others_globals()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript first = host.LoadText("secret = 'the first one'\ncoroutine.yield()\nreturn secret", "Scripts/first.luau");
        LuauScript second = host.LoadText("local seen = secret\nsecret = 'the second one'\nreturn seen", "Scripts/second.luau");
        first.Start().ShouldBe(LuauResume.Yielded);

        second.Start().ShouldBe(LuauResume.Finished);
        bool wasHidden = second.Thread.Values.IsNil(1);
        first.Thread.Resume().ShouldBe(LuauResume.Finished);

        wasHidden.ShouldBeTrue();
        first.Thread.Values.GetText(1).ShouldBe("the first one");
    }

    [Fact]
    public void The_host_cannot_set_a_shared_global_once_it_is_sealed()
    {
        using LuauHost host = TestHost.Open(out _);
        host.Stack.Push(1).ShouldBeTrue();

        bool isSet = host.Stack.SetGlobal("late"u8);

        isSet.ShouldBeFalse();
        host.Stack.GetText(-1).ShouldNotBeNull().ShouldContain("readonly");
    }

    [Fact]
    public void A_script_cannot_reach_the_process()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript script = host.LoadText("return type(io), type(require), type(loadstring), type(os.execute), type(dofile)");

        script.Start().ShouldBe(LuauResume.Finished);

        for (int index = 1; index <= 5; index++)
            script.Thread.Values.GetText(index).ShouldBe("nil");
    }
}
