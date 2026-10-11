using SpectraEngine.Core.Scripting;
using SpectraEngine.Scripting.Luau;

namespace SpectraEngine.Scripting.Tests;

/// <summary>What reaches the sink: prints, warnings and the errors nothing caught.</summary>
public sealed class ScriptReportTests
{
    [Fact]
    public void A_print_arrives_with_its_script_and_its_line()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);

        host.RunText("local first = 1\nprint('the door opens')", "Scripts/door.luau").ShouldBe(LuauResume.Finished);

        ScriptMessage message = sink.Single;
        message.Severity.ShouldBe(ScriptSeverity.Print);
        message.Text.ShouldBe("the door opens");
        message.Script.ShouldBe("Scripts/door.luau");
        message.Line.ShouldBe(2);
        message.Stack.ShouldBeEmpty();
    }

    [Fact]
    public void A_warning_arrives_as_a_warning()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);

        host.RunText("warn('no key')").ShouldBe(LuauResume.Finished);

        ScriptMessage message = sink.Single;
        message.Severity.ShouldBe(ScriptSeverity.Warning);
        message.Text.ShouldBe("no key");
        message.Line.ShouldBe(1);
    }

    [Fact]
    public void A_print_joins_its_values_with_spaces_and_turns_each_into_text()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);

        host.RunText("print('speed', 20, true, nil, vector.create(1, 2, 3))").ShouldBe(LuauResume.Finished);

        sink.Single.Text.ShouldBe("speed 20 true nil 1, 2, 3");
    }

    [Fact]
    public void A_print_of_nothing_is_an_empty_line()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);

        host.RunText("print()").ShouldBe(LuauResume.Finished);

        sink.Single.Text.ShouldBe(string.Empty);
    }

    [Fact]
    public void An_error_nothing_caught_arrives_with_its_script_its_line_and_its_stack()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        const string source = """
            local function open(door)
                return door.hinge.angle
            end

            local function press()
                open({})
            end

            press()
            """;

        LuauResume outcome = host.RunText(source, "Scripts/button.luau");

        outcome.ShouldBe(LuauResume.Failed);
        ScriptMessage message = sink.Single;
        message.Severity.ShouldBe(ScriptSeverity.Error);
        message.Text.ShouldBe("attempt to index nil with 'angle'");
        message.Script.ShouldBe("Scripts/button.luau");
        message.Line.ShouldBe(2);
        message.Stack.ShouldBe(
        [
            new ScriptFrame("Scripts/button.luau", 2, "open"),
            new ScriptFrame("Scripts/button.luau", 6, "press"),
            new ScriptFrame("Scripts/button.luau", 9, null),
        ]);
    }

    [Fact]
    public void An_error_a_script_raises_arrives_with_its_text()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);

        host.RunText("local first = 1\nerror('the door is stuck')").ShouldBe(LuauResume.Failed);

        ScriptMessage message = sink.Single;
        message.Text.ShouldBe("the door is stuck");
        message.Line.ShouldBe(2);
    }

    // error(message, 2) blames the caller, and so does the report.
    [Fact]
    public void An_error_raised_for_the_caller_names_the_callers_line()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        const string source = """
            local function check(speed)
                error('speed must be a number', 2)
            end

            check('fast')
            """;

        host.RunText(source).ShouldBe(LuauResume.Failed);

        ScriptMessage message = sink.Single;
        message.Text.ShouldBe("speed must be a number");
        message.Line.ShouldBe(5);
        message.Stack[0].Line.ShouldBe(2);
    }

    [Fact]
    public void An_error_value_that_is_not_text_is_described()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);

        host.RunText("error(setmetatable({}, { __tostring = function() return 'a broken hinge' end }))")
            .ShouldBe(LuauResume.Failed);

        ScriptMessage message = sink.Single;
        message.Text.ShouldBe("a broken hinge");
        message.Line.ShouldBe(1);
    }

    [Fact]
    public void A_script_that_fails_ends_its_own_thread_and_nothing_else()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        LuauScript broken = host.LoadText("error('first')", "Scripts/broken.luau");
        LuauScript sound = host.LoadText("return 'still here'", "Scripts/sound.luau");

        broken.Start().ShouldBe(LuauResume.Failed);
        LuauResume outcome = sound.Start();

        outcome.ShouldBe(LuauResume.Finished);
        sound.Thread.Values.GetText(1).ShouldBe("still here");
        broken.Thread.IsAlive.ShouldBeFalse();
        broken.IsSwitchedOff.ShouldBeFalse();
        sink.Single.Script.ShouldBe("Scripts/broken.luau");
    }

    // A recursion without end fills the stack. The report keeps the frames
    // nearest to the error.
    [Fact]
    public void A_stack_that_ran_out_is_reported_with_a_short_stack()
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);

        host.RunText("local function fall() return 1 + fall() end\nfall()").ShouldBe(LuauResume.Failed);

        ScriptMessage message = sink.Single;
        message.Text.ShouldContain("stack overflow");
        message.Stack.Count.ShouldBeInRange(2, 64);
    }
}
