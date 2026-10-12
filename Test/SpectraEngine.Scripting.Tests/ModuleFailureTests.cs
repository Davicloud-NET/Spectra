using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A module that fails because one it asked for failed. The first failure
/// is reported once, and every asker on the way gets it as it was.
/// </summary>
public sealed class ModuleFailureTests
{
    private const string LoadFailed = "Requested module experienced an error while loading";

    [Fact]
    public void A_failure_is_reported_once_and_every_asker_up_the_chain_gets_it_unchanged()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/deep.luau", "local a = 1\nerror('boom')");
        level.FileAt("Scripts/middle.luau", "return require('./deep')");
        level.FileAt("Scripts/outer.luau", "return require('./middle')");
        level.ScriptAt("Scripts/first.luau", """
            print(pcall(require, './outer'))
            print(pcall(require, './middle'))
            """);
        level.ScriptAt("Scripts/second.luau", "require('./outer')");

        level.Play().Tick();

        const string Boom = $"{LoadFailed}: Scripts/deep.luau:2: boom";
        level.Printed.ShouldBe([$"false Scripts/first.luau:1: {Boom}", $"false Scripts/first.luau:2: {Boom}"]);
        level.ErrorsWithPlace().ShouldBe(["Scripts/deep.luau:2: boom", $"Scripts/second.luau:1: {Boom}"]);
    }

    [Fact]
    public void A_module_that_catches_a_failed_module_and_then_fails_itself_is_reported_itself()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/deep.luau", "error('boom')");
        level.FileAt("Scripts/middle.luau", "pcall(require, './deep')\nerror('my own')");
        level.ScriptAt("Scripts/main.luau", "print(pcall(require, './middle'))");

        level.Play().Tick();

        level.ErrorsWithPlace().ShouldBe(["Scripts/deep.luau:1: boom", "Scripts/middle.luau:2: my own"]);
        level.Printed.ShouldBe([$"false Scripts/main.luau:1: {LoadFailed}: Scripts/middle.luau:2: my own"]);
    }

    // Only the module's own end is left untold. A thread it started fails
    // like any other, and the module goes on.
    [Fact]
    public void A_thread_a_module_started_that_fails_for_a_failed_module_is_still_reported()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/deep.luau", "error('boom')");
        level.FileAt("Scripts/middle.luau", """
            task.spawn(function() require('./deep') end)
            print('went on')
            return 'middle'
            """);
        level.ScriptAt("Scripts/main.luau", "print(require('./middle'))");

        level.Play().Tick();

        level.ErrorsWithPlace().ShouldBe(
        [
            "Scripts/deep.luau:1: boom",
            $"Scripts/middle.luau:1: {LoadFailed}: Scripts/deep.luau:1: boom",
        ]);
        level.Printed.ShouldBe(["went on", "middle"]);
    }

    // What a module prints after one of its threads failed comes after that
    // failure, as it happened.
    [Fact]
    public void An_error_and_a_print_of_a_module_that_loads_keep_their_order()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/loud.luau", """
            task.spawn(function() error('first') end)
            print('second')
            task.spawn(function() error('third') end)
            return 1
            """);
        level.ScriptAt("Scripts/main.luau", "require('./loud')\nprint('fourth')");

        level.Play().Tick();

        level.Sink.Messages.Select(message => message.Text).ShouldBe(["first", "second", "third", "fourth"]);
    }

    [Fact]
    public void A_module_node_that_fails_for_a_module_it_asked_for_hands_the_first_failure_on()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/deep.luau", "error('boom')");
        SceneNode rules = level.ModuleAt("Scripts/Rules.luau", "return require('./deep')");
        level.ScriptAt("Scripts/main.luau", """
            print(pcall(require, script.Parent))
            print(pcall(require, script.Parent))
            """, under: rules);

        level.Play().Tick();

        const string Boom = $"{LoadFailed}: Scripts/deep.luau:1: boom";
        level.ErrorsWithPlace().ShouldBe(["Scripts/deep.luau:1: boom"]);
        level.Printed.ShouldBe([$"false Scripts/main.luau:1: {Boom}", $"false Scripts/main.luau:2: {Boom}"]);
    }
}
