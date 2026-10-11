using System.Numerics;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A level's scripts reporting to the engine's console, as a host sets it up:
/// what the lines of a real print, a real error and a script that did not
/// start carry beside their text.
/// </summary>
public sealed class ScriptConsoleLineTests
{
    [Fact]
    public void A_print_and_an_error_reach_the_console_with_their_place_and_the_errors_callers()
    {
        using var level = new ScriptLevel();
        var console = new ConsoleOutput();
        level.Manager.Scripts.Sink = new ConsoleScriptSink(console);
        SceneNode script = level.AddScript(level.Part("Door", new Vector3(0f, 2f, 0f)), """
            local function open(door)
                error("no hinge on " .. door)
            end

            local function press()
                open("front")
            end

            print("pressing")
            press()
            """);
        string path = script.Script.ShouldNotBeNull().Path;

        level.Play().Tick();

        IReadOnlyList<ConsoleLine> lines = console.Drain();
        lines.Count.ShouldBe(2);

        lines[0].Severity.ShouldBe(LogLevel.Information);
        lines[0].Text.ShouldBe("pressing");
        lines[0].Place.ShouldBe(new ConsolePlace(path, 9));
        lines[0].Stack.ShouldBeEmpty();

        lines[1].Severity.ShouldBe(LogLevel.Error);
        lines[1].Text.ShouldBe("no hinge on front");
        lines[1].Place.ShouldBe(new ConsolePlace(path, 2));
        lines[1].Stack.ShouldBe([new ConsolePlace(path, 6, "press"), new ConsolePlace(path, 10)]);
        lines[1].ProblemNode.ShouldBe(Guid.Empty);
        lines[1].PlainText.ShouldBe($"{path}:2: no hinge on front");
    }

    [Fact]
    public void A_script_that_does_not_compile_and_one_with_no_file_name_their_nodes_on_the_console()
    {
        using var level = new ScriptLevel();
        var console = new ConsoleOutput();
        level.Manager.Scripts.Sink = new ConsoleScriptSink(console);
        SceneNode broken = level.AddScript(level.Part("Door", Vector3.Zero), "local a = 1\nlocal = 2", "Broken");
        SceneNode lamp = level.Scene.Root.CreateChild("Lamp");
        lamp.Script = new Script("Scripts/nowhere.luau");

        level.Play().Tick();

        IReadOnlyList<ConsoleLine> lines = console.Drain();
        lines.Select(line => (line.Severity, line.Place, line.ProblemNode)).ShouldBe(
        [
            (LogLevel.Error, new ConsolePlace(broken.Script.ShouldNotBeNull().Path, 2), broken.Id),
            (LogLevel.Error, new ConsolePlace("Scripts/nowhere.luau", 0), lamp.Id),
        ]);
        lines[0].Text.ShouldEndWith("The script on 'Broken' did not start.");
        lines[1].Text.ShouldBe("'Lamp' names a script file that is not in the level's content, so it did not start.");
    }

    [Fact]
    public void A_flood_of_prints_leaves_room_in_the_console_for_the_error_after_it()
    {
        using var level = new ScriptLevel();
        var console = new ConsoleOutput();
        level.Manager.Scripts.Sink = new ConsoleScriptSink(console);
        level.AddScript(level.Scene.Root, """
            for i = 1, 5000 do
                print("line " .. i)
            end
            error("after the flood")
            """);

        level.Play().Tick();

        IReadOnlyList<ConsoleLine> lines = console.Drain();
        console.Written.ShouldBe(lines.Count, "the console dropped nothing");
        lines.Count(line => line.Severity == LogLevel.Information).ShouldBe(100);
        lines.ShouldContain(line => line.Severity == LogLevel.Error && line.Text == "after the flood");
        lines[^1].Text.ShouldBe(
            "4900 more lines were printed in this tick and dropped. Scripts may print 100 lines a tick.");
        lines[^1].Place.ShouldNotBeNull().Line.ShouldBe(2);
    }
}
