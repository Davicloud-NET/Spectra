using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Entities;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// An input sent and an output heard by a function that lives in a module.
/// It runs on a thread of the script that required it, and its file is the
/// module's, which is what a trace names as the sender.
/// </summary>
public sealed class ModuleWiringTests
{
    [Fact]
    public void A_modules_function_sends_an_input_and_the_watch_names_the_modules_file_and_line()
    {
        using var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Echo").Entity = new EntityData(EchoEntity.Class);
        level.FileAt("Scripts/lib/ringer.luau", """
            local ringer = {}
            function ringer.ring(echo)
                echo:SayNumber(7)
            end
            return ringer
            """);
        level.ScriptAt("Scripts/main.luau", """
            local ringer = require('./lib/ringer')
            ringer.ring(workspace.Echo)
            """);
        var output = new ConsoleOutput();
        var watch = new EntityWatch(output);
        watch.TurnOn();
        level.Manager.EntityTrace = watch.ActiveTrace;

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Echo got SayNumber '7'"]);
        output.Drain().Select(line => line.Text)
            .ShouldContain("ent 1 send Scripts/lib/ringer.luau:3 -> Echo.SayNumber(\"7\")");
    }

    [Fact]
    public void A_modules_function_connected_to_an_output_gets_its_value_and_sends_in_its_turn()
    {
        using var level = new ScriptLevel();
        level.Scene.Root.CreateChild("Echo").Entity = new EntityData(EchoEntity.Class);
        level.FileAt("Scripts/lib/counter.luau", """
            return function(number)
                print('the module heard', number)
                if number < 2 then
                    workspace.Echo:SayNumber(number + 1)
                end
            end
            """);
        level.ScriptAt("Scripts/main.luau", """
            workspace.Echo.OnNumber:Connect(require('./lib/counter'))
            workspace.Echo:SayNumber(1)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "Echo got SayNumber '1'", "the module heard 1", "Echo got SayNumber '2'", "the module heard 2",
        ]);
    }
}
