namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Modules that ask for each other in a circle. The one that closes the
/// circle gets an error that names every module on it.
/// </summary>
public sealed class ModuleCircleTests
{
    private const string Recursive = "Requested module was required recursively";
    private const string LoadFailed = "Requested module experienced an error while loading";

    [Fact]
    public void Two_modules_that_ask_for_each_other_fail_with_the_chain()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/a.luau", "return require('./b')");
        level.FileAt("Scripts/b.luau", "return require('./a')");
        level.ScriptAt("Scripts/main.luau", "print(pcall(require, './a'))");

        level.Play().Tick();

        const string Circle = $"{Recursive}: Scripts/a.luau -> Scripts/b.luau -> Scripts/a.luau";
        level.ErrorsWithPlace().ShouldBe(
        [
            $"Scripts/b.luau:1: {Circle}",
            $"Scripts/a.luau:1: {LoadFailed}: Scripts/b.luau:1: {Circle}",
        ]);
        level.Printed.ShouldBe(
        [
            $"false Scripts/main.luau:1: {LoadFailed}: Scripts/a.luau:1: {LoadFailed}: Scripts/b.luau:1: {Circle}",
        ]);
    }

    [Fact]
    public void Three_modules_in_a_circle_fail_with_all_three_in_the_chain()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/a.luau", "return require('./b')");
        level.FileAt("Scripts/b.luau", "return require('./c')");
        level.FileAt("Scripts/c.luau", "local a = 1\nreturn require('@assets/Scripts/a')");
        level.ScriptAt("Scripts/main.luau", "require('./a')");

        level.Play().Tick();

        string[] errors = level.ErrorsWithPlace();
        errors.Length.ShouldBe(4);
        errors[0].ShouldBe(
            $"Scripts/c.luau:2: {Recursive}: Scripts/a.luau -> Scripts/b.luau -> Scripts/c.luau -> Scripts/a.luau");
        errors[3].ShouldStartWith($"Scripts/main.luau:1: {LoadFailed}: Scripts/a.luau:1: ");
        errors[3].ShouldEndWith(errors[0]);
    }

    [Fact]
    public void A_module_that_asks_for_itself_is_a_circle_of_one()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/kit/init.luau", "return require('@self')");
        level.ScriptAt("Scripts/main.luau", "print(pcall(require, './kit'))");

        level.Play().Tick();

        level.ErrorsWithPlace().ShouldBe(
        [
            $"Scripts/kit/init.luau:1: {Recursive}: Scripts/kit/init.luau -> Scripts/kit/init.luau",
        ]);
    }

    // The chain starts at the module that was asked for twice. What asked
    // for that one first is not on the circle.
    [Fact]
    public void The_chain_names_the_circle_and_not_the_way_into_it()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/door.luau", "return require('./a')");
        level.FileAt("Scripts/a.luau", "return require('./b')");
        level.FileAt("Scripts/b.luau", "return require('./a')");
        level.ScriptAt("Scripts/main.luau", "require('./door')");

        level.Play().Tick();

        level.ErrorsWithPlace()[0].ShouldBe(
            $"Scripts/b.luau:1: {Recursive}: Scripts/a.luau -> Scripts/b.luau -> Scripts/a.luau");
    }

    [Fact]
    public void A_module_that_catches_the_circle_still_loads_and_so_does_the_one_that_asked_for_it()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/a.luau", "return { b = require('./b') }");
        level.FileAt("Scripts/b.luau", "return { sawCircle = not pcall(require, './a') }");
        level.ScriptAt("Scripts/main.luau", """
            local a = require('./a')
            print(a.b.sawCircle, a.b == require('./b'))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true true"]);
    }

    // Each module that runs sits on the native stack of its asker, so the
    // chain has an end before the stack does.
    [Theory]
    [InlineData(100, "true 99")]
    [InlineData(101, null)]
    [InlineData(400, null)]
    public void A_chain_of_modules_loads_a_hundred_deep_and_no_deeper(int modules, string? printed)
    {
        using var level = new ScriptLevel();
        for (int i = 0; i < modules; i++)
        {
            level.FileAt(
                $"Scripts/m{i}.luau", i == modules - 1 ? "return 0" : $"return require('./m{i + 1}') + 1");
        }

        level.ScriptAt("Scripts/main.luau", "print(pcall(require, './m0'))");

        level.Play().Tick();

        if (printed is not null)
        {
            level.Errors.ShouldBeEmpty();
            level.Printed.ShouldBe([printed]);
            return;
        }

        string[] errors = level.ErrorsWithPlace();
        errors.Length.ShouldBe(100);
        errors[0].ShouldBe(
            "Scripts/m99.luau:1: modules ask for one another more than 100 deep, from Scripts/m0.luau down");
        level.Printed.ShouldHaveSingleItem().ShouldStartWith("false Scripts/main.luau:1: ");
    }

    [Fact]
    public void Two_modules_that_ask_for_a_third_are_no_circle()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/base.luau", "print('base')\nreturn {}");
        level.FileAt("Scripts/left.luau", "return require('./base')");
        level.FileAt("Scripts/right.luau", "return require('./base')");
        level.ScriptAt("Scripts/main.luau", "print(require('./left') == require('./right'))");

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["base", "true"]);
    }
}
