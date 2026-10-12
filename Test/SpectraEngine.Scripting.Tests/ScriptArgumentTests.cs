using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What generated glue says about an argument it cannot take. An object that
/// has left the level is no wrong kind of value, and the words say so.
/// </summary>
public sealed class ScriptArgumentTests
{
    private const string TwoErrors = """
        local value = ...
        coroutine.yield()
        local _, wrong = pcall(touch, value)
        local _, dotted = pcall(open, value)
        return wrong, dotted
        """;

    [Fact]
    public void An_argument_of_another_kind_is_named_by_its_kind()
    {
        (string wrong, string dotted) = ErrorsFor(3.0, release: null);

        wrong.ShouldBe("Scripts/test.luau:3: invalid argument #1 to 'Touch' (Instance expected, got number)");
        dotted.ShouldBe("Scripts/test.luau:4: Expected ':' not '.' calling member function Open");
    }

    [Fact]
    public void An_argument_that_has_left_the_level_is_not_called_a_wrong_kind()
    {
        object door = new();

        (string wrong, _) = ErrorsFor(LuauValue.Object(ScriptTags.Object, door), release: door);

        wrong.ShouldBe(
            "Scripts/test.luau:3: invalid argument #1 to 'Touch' (Instance expected, got an object that has left the level)");
    }

    // A call with a colon says the same of such an object.
    [Fact]
    public void A_method_called_with_a_dot_on_an_object_that_has_left_the_level_says_that_it_left()
    {
        object door = new();

        (_, string dotted) = ErrorsFor(LuauValue.Object(ScriptTags.Object, door), release: door);

        dotted.ShouldBe("Scripts/test.luau:4: this object has left the level");
    }

    // Hands the value to a script, lets the object go while the script
    // waits, and gives back what the two refusals then say.
    private static (string Wrong, string Dotted) ErrorsFor(LuauValue value, object? release)
    {
        using LuauHost host = TestHost.Open(out _, fresh =>
        {
            LuauStack stack = fresh.Stack;
            stack.NewTable().ShouldBeTrue();
            fresh.Objects.SetMetatable(stack, ScriptTags.Object).ShouldBeTrue();

            fresh.SetGlobal("touch"u8, call => ScriptArguments.Wrong(call, 1, "Touch", "Instance"));
            fresh.SetGlobal("open"u8, call => ScriptArguments.NoSelf(call, "Open"));
        });

        LuauScript script = host.LoadText(TwoErrors);
        script.Start([value]).ShouldBe(LuauResume.Yielded);
        if (release is not null)
            host.Objects.Release(release).ShouldBeTrue();

        script.Thread.Resume().ShouldBe(LuauResume.Finished);
        return (script.Thread.Values.GetText(1).ShouldNotBeNull(), script.Thread.Values.GetText(2).ShouldNotBeNull());
    }
}
