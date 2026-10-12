namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A path whose letters are in another case than its file's. A pack and a
/// Windows folder find the file, and a Linux folder does not. Either way a
/// file is one module, and the answer does not turn on who asked first.
/// </summary>
public sealed class ModuleCaseTests
{
    [Fact]
    public void Where_case_is_ignored_two_cases_of_one_path_are_one_module()
    {
        using var level = new ScriptLevel();
        level.Files.IgnoresCase = true;
        level.FileAt("Scripts/Lib/Util.luau", "print('ran')\nreturn {}");
        level.ScriptAt("Scripts/main.luau", """
            local util = require('./lib/util')
            print(util == require('./Lib/Util'), util == require('@assets/SCRIPTS/LIB/UTIL'))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["ran", "true true"]);
    }

    [Theory]
    [InlineData("./Lib/Util", "./lib/util")]
    [InlineData("./lib/util", "./Lib/Util")]
    public void Where_case_counts_a_path_in_another_case_is_no_module_whichever_was_asked_first(
        string first, string second)
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/Lib/Util.luau", "return 'util'");
        level.ScriptAt("Scripts/main.luau", $"""
            print(pcall(require, '{first}'))
            print(pcall(require, '{second}'))
            """);

        level.Play().Tick();

        const string Missing =
            "no module at './lib/util': looked for Scripts/lib/util.luau and Scripts/lib/util/init.luau";
        string[] printed = first == "./lib/util"
            ? [$"false Scripts/main.luau:1: {Missing}", "true util"]
            : ["true util", $"false Scripts/main.luau:2: {Missing}"];
        level.Printed.ShouldBe(printed);
    }

    // The file is asked for as an init file and as a file of its own name.
    [Fact]
    public void Where_case_is_ignored_an_init_file_asked_for_in_capitals_is_the_same_module()
    {
        using var level = new ScriptLevel();
        level.Files.IgnoresCase = true;
        level.FileAt("Scripts/kit/init.luau", "print('ran')\nreturn {}");
        level.ScriptAt("Scripts/main.luau", "print(require('./kit') == require('./kit/Init'))");

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["ran", "true"]);
    }
}
