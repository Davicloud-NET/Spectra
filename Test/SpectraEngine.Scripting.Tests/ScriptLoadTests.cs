using System.Text;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>A script from its source to its first run.</summary>
public sealed class ScriptLoadTests
{
    [Fact]
    public void A_script_compiles_runs_and_returns_its_values()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        LuauScript script = host.LoadText("local speed = 20\nreturn speed * 2, 'done'");

        LuauResume outcome = script.Start();

        outcome.ShouldBe(LuauResume.Finished);
        script.Thread.ValueCount.ShouldBe(2);
        script.Thread.Values.TryGetNumber(1, out double doubled).ShouldBeTrue();
        doubled.ShouldBe(40);
        script.Thread.Values.GetText(2).ShouldBe("done");
        sink.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void A_script_gets_the_values_it_is_started_with()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript script = host.LoadText("local name, count = ...\nreturn name .. count");

        LuauResume outcome = script.Start(["door", 3]);

        outcome.ShouldBe(LuauResume.Finished);
        script.Thread.Values.GetText(1).ShouldBe("door3");
    }

    [Fact]
    public void A_script_with_a_mistake_in_it_is_reported_with_its_name_and_line()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);

        LuauScript? script = host.Load("Scripts/door.luau", Encoding.UTF8.GetBytes("local door = 1\nlocal = 5"));

        script.ShouldBeNull();
        ScriptMessage message = sink.Single;
        message.Severity.ShouldBe(ScriptSeverity.Error);
        message.Script.ShouldBe("Scripts/door.luau");
        message.Line.ShouldBe(2);
        message.Position.ShouldBe("Scripts/door.luau:2");
        message.Text.ShouldStartWith("Expected identifier");
    }

    [Fact]
    public void A_host_runs_scripts_after_one_did_not_compile()
    {
        using LuauHost host = TestHost.Open(out _);
        host.Load("broken.luau", "return return"u8).ShouldBeNull();

        LuauScript script = host.LoadText("return 7");

        script.Start().ShouldBe(LuauResume.Finished);
        script.Thread.Values.TryGetNumber(1, out double value).ShouldBeTrue();
        value.ShouldBe(7);
    }

    [Fact]
    public void A_script_saved_with_a_byte_order_mark_loads_and_keeps_its_line_numbers()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        byte[] source = [.. Encoding.UTF8.Preamble, .. "local first = 1\nerror('second line')"u8];

        LuauScript? script = host.Load("Scripts/marked.luau", source);

        script.ShouldNotBeNull();
        script.Start().ShouldBe(LuauResume.Failed);
        sink.Single.Line.ShouldBe(2);
    }

    [Fact]
    public void A_script_is_not_loaded_before_the_host_is_sealed()
    {
        NativeLuau.Require();
        using LuauHost host = new(new RecordingSink());

        Should.Throw<InvalidOperationException>(() => host.Load("early.luau", "return 1"u8));
    }

    [Fact]
    public void A_global_set_for_a_script_is_seen_by_that_script_only()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript first = host.LoadText("return owner", "first.luau");
        LuauScript second = host.LoadText("return owner", "second.luau");

        first.SetGlobal("owner"u8, "the first").ShouldBeTrue();
        first.Start().ShouldBe(LuauResume.Finished);
        string? seenByFirst = first.Thread.Values.GetText(1);
        second.Start().ShouldBe(LuauResume.Finished);

        seenByFirst.ShouldBe("the first");
        second.Thread.Values.IsNil(1).ShouldBeTrue();
    }

    [Fact]
    public void A_script_runs_once()
    {
        using LuauHost host = TestHost.Open(out _);
        LuauScript script = host.LoadText("return 1");
        script.Start().ShouldBe(LuauResume.Finished);

        LuauResume again = script.Start();

        again.ShouldBe(LuauResume.Refused);
    }
}
