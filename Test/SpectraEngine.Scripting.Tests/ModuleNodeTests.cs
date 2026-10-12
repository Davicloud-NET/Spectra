using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A module asked for by its node, as Roblox asks for one, and what
/// require says to anything that is neither a path nor a module script.
/// </summary>
public sealed class ModuleNodeTests
{
    private const string Invalid = "Attempted to call require with invalid argument(s).";

    [Fact]
    public void A_module_node_gives_its_module_and_the_module_has_its_node_as_script()
    {
        using var level = new ScriptLevel();
        SceneNode rules = level.ModuleAt("Scripts/Rules.luau", """
            print(script.Name, script.ClassName, script.Parent.Name)
            return { speed = 16 }
            """);
        level.ScriptAt("Scripts/main.luau", """
            local rules = require(script.Parent)
            print(rules.speed, rules == require(script.Parent))
            """, under: rules);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Rules ModuleScript Root", "16 true"]);
    }

    // Each has its own script, so they cannot be one module.
    [Fact]
    public void A_module_node_and_its_file_asked_for_by_path_are_two_modules()
    {
        using var level = new ScriptLevel();
        SceneNode rules = level.ModuleAt("Scripts/Rules.luau", "print(script)\nreturn {}");
        level.ScriptAt("Scripts/main.luau", """
            local byNode = require(script.Parent)
            local byPath = require('./Rules')
            print(byNode == byPath, byNode == require(script.Parent), byPath == require('./Rules'))
            """, under: rules);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Rules", "nil", "false true true"]);
    }

    [Fact]
    public void A_module_node_asks_by_path_from_its_file()
    {
        using var level = new ScriptLevel();
        level.FileAt("Shared/lib/words.luau", "return 'from beside the module'");
        SceneNode rules = level.ModuleAt("Shared/lib/Rules.luau", "return require('./words')");
        level.ScriptAt("Scripts/main.luau", "print(require(script.Parent))", under: rules);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["from beside the module"]);
    }

    [Fact]
    public void A_node_that_is_no_module_script_is_refused_in_Robloxs_words()
    {
        using var level = new ScriptLevel();
        SceneNode door = level.Part("Door", new Vector3(0f, 2f, 0f));
        level.ScriptAt("Scripts/main.luau", """
            print(pcall(require, script.Parent))
            print(pcall(require, script))
            print(pcall(require, workspace))
            """, under: door);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            $"false Scripts/main.luau:1: {Invalid} 'Door' (Part) is not a ModuleScript",
            $"false Scripts/main.luau:2: {Invalid} 'main' (Script) is not a ModuleScript",
            $"false Scripts/main.luau:3: {Invalid} 'Root' (Folder) is not a ModuleScript",
        ]);
    }

    [Theory]
    [InlineData("5", "number")]
    [InlineData("", "nil")]
    [InlineData("nil", "nil")]
    [InlineData("{}", "table")]
    [InlineData("game", "DataModel")]
    [InlineData("CFrame.identity", "CFrame")]
    public void What_is_neither_a_path_nor_a_node_is_refused_in_Robloxs_words(string argument, string got)
    {
        using var level = new ScriptLevel();
        level.ScriptAt("Scripts/main.luau", $"require({argument})");

        level.Play().Tick();

        level.ErrorsWithPlace().ShouldBe(
            [$"Scripts/main.luau:1: {Invalid} A path or a ModuleScript was expected, got {got}"]);
    }

    [Fact]
    public void A_module_node_whose_file_is_missing_is_an_error_at_the_askers_line_that_names_both()
    {
        using var level = new ScriptLevel();
        SceneNode rules = level.Scene.Root.CreateChild("Rules");
        rules.Script = new Script("Scripts/nowhere.luau") { IsModule = true };
        level.ScriptAt("Scripts/main.luau", "\nrequire(script.Parent)", under: rules);

        level.Play().Tick();

        ScriptMessage failure = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        failure.Position.ShouldBe("Scripts/main.luau:2");
        failure.Text.ShouldBe("the module 'Rules' names Scripts/nowhere.luau, and the level's content has no such file");
        failure.ProblemNode.ShouldBe(Guid.Empty);
    }

    [Fact]
    public void A_module_node_whose_file_cannot_be_read_says_why()
    {
        using var level = new ScriptLevel();
        level.Files.AddUnreadable("Scripts/old.luau", "It is of another version.");
        SceneNode rules = level.Scene.Root.CreateChild("Rules");
        rules.Script = new Script("Scripts/old.luau") { IsModule = true };
        level.ScriptAt("Scripts/main.luau", "require(script.Parent)", under: rules);

        level.Play().Tick();

        level.ErrorsWithPlace().ShouldBe(
        [
            "Scripts/main.luau:1: the module 'Rules' names Scripts/old.luau, which cannot be read. " +
            "It is of another version.",
        ]);
    }

    [Fact]
    public void A_module_node_that_is_switched_off_is_not_run()
    {
        using var level = new ScriptLevel();
        SceneNode rules = level.ModuleAt("Scripts/Rules.luau", "print('ran')\nreturn {}");
        rules.Script = rules.Script.ShouldNotBeNull() with { IsEnabled = false };
        level.ScriptAt("Scripts/main.luau", "require(script.Parent)", under: rules);

        level.Play().Tick();

        level.Printed.ShouldBeEmpty();
        level.ErrorsWithPlace().ShouldBe(["Scripts/main.luau:1: the module 'Rules' is switched off"]);
    }

    // A script that does not compile is a standing problem of its node, and
    // so is a module that was asked for by its node.
    [Fact]
    public void A_module_node_that_does_not_compile_is_a_standing_problem_of_its_node()
    {
        using var level = new ScriptLevel();
        SceneNode rules = level.ModuleAt("Scripts/Rules.luau", "local = 2");
        level.ScriptAt("Scripts/main.luau", "print(pcall(require, script.Parent))", under: rules);

        level.Play().Tick();

        ScriptMessage mistake = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        mistake.Position.ShouldBe("Scripts/Rules.luau:1");
        mistake.Text.ShouldEndWith(". The script on 'Rules' did not start.");
        mistake.ProblemNode.ShouldBe(rules.Id);
        level.Printed.ShouldHaveSingleItem().ShouldStartWith(
            "false Scripts/main.luau:1: Requested module experienced an error while loading: Scripts/Rules.luau:1: ");
    }

    // A handler that asks on every tick is common in a Roblox script.
    [Fact]
    public void Asking_again_for_a_module_by_its_node_allocates_nothing()
    {
        using var level = new ScriptLevel(hasCharacter: false);
        SceneNode rules = level.ModuleAt("Scripts/Rules.luau", "return {}");
        level.ScriptAt("Scripts/main.luau", """
            local module = script.Parent
            game:GetService("RunService").Heartbeat:Connect(function()
                for i = 1, 1000 do
                    local _ = require(module)
                end
            end)
            """, under: rules);

        level.Play().Tick(30);

        // The best of a few runs, as the runtime compiles a method again once it has run often.
        long allocated = long.MaxValue;
        for (int run = 0; run < 3; run++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            level.Tick(50);
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        level.Errors.ShouldBeEmpty();
        allocated.ShouldBe(0);
    }

    [Fact]
    public void A_module_node_is_run_again_in_the_next_play_session()
    {
        using var level = new ScriptLevel();
        SceneNode rules = level.ModuleAt("Scripts/Rules.luau", "print('ran')\nreturn {}");
        level.ScriptAt("Scripts/main.luau", "require(script.Parent)", under: rules);

        level.Play().Tick();
        level.Stop();
        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["ran", "ran"]);
    }
}
