using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A script that asks for a module by path, in a level that plays: which
/// file it gets, and what it is told when it gets none.
/// </summary>
public sealed class RequirePathTests
{
    private const string Shape = "A path starts with ./ or ../, from the file that asks, or with @self or @assets";

    [Fact]
    public void A_script_gets_a_module_by_a_path_from_its_own_file_and_from_the_content_root()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/lib/near.luau", "return 'near'");
        level.FileAt("Shared/far.luau", "return 'far'");
        level.ScriptAt("Scripts/main.luau", """
            print(require('./lib/near'), require('../Shared/far'), require('@assets/Shared/far'))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["near far far"]);
    }

    [Fact]
    public void A_folder_with_an_init_file_is_a_module_that_reaches_its_own_files_with_self()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/kit/init.luau", """
            return { part = require('@self/part'), beside = require('./near') }
            """);
        level.FileAt("Scripts/kit/part.luau", "return 'a part of the kit'");
        level.FileAt("Scripts/near.luau", "return 'beside the kit'");
        level.ScriptAt("Scripts/main.luau", """
            local kit = require('./kit')
            print(kit.part, kit.beside)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["a part of the kit beside the kit"]);
    }

    // The path is found from the file of the function that asks, which is
    // the module's, and not from the script whose thread runs it.
    [Fact]
    public void A_function_of_a_module_asks_from_the_modules_own_file()
    {
        using var level = new ScriptLevel();
        level.FileAt("Shared/tools/init.luau", """
            return { later = function() return require('@self/saw') end }
            """);
        level.FileAt("Shared/tools/saw.luau", "return 'a saw'");
        level.ScriptAt("Scripts/main.luau", """
            local tools = require('@assets/Shared/tools')
            print(tools.later())
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["a saw"]);
    }

    [Fact]
    public void A_path_that_names_a_file_and_a_folder_with_an_init_file_is_an_error_that_names_the_two()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/kit.luau", "return 'the file'");
        level.FileAt("Scripts/kit/init.luau", "return 'the folder'");
        level.ScriptAt("Scripts/main.luau", "require('./kit')");

        level.Play().Tick();

        level.ErrorsWithPlace().ShouldBe(
        [
            "Scripts/main.luau:1: './kit' names two files, Scripts/kit.luau and Scripts/kit/init.luau. Remove one",
        ]);
    }

    [Fact]
    public void A_missing_module_is_an_error_at_the_askers_line_with_the_path_and_the_files_looked_for()
    {
        using var level = new ScriptLevel();
        level.ScriptAt("Scripts/main.luau", """
            print('before')
            local lost = require('./lib/lost')
            print('after')
            """);

        level.Play().Tick();

        ScriptMessage failure = level.Sink.Messages.Single(message => message.Severity == ScriptSeverity.Error);
        failure.Position.ShouldBe("Scripts/main.luau:2");
        failure.Text.ShouldBe(
            "no module at './lib/lost': looked for Scripts/lib/lost.luau and Scripts/lib/lost/init.luau");
        level.Printed.ShouldBe(["before"]);
    }

    [Fact]
    public void A_module_file_the_source_cannot_read_is_an_error_at_the_askers_line_that_says_why()
    {
        using var level = new ScriptLevel();
        level.Files.AddUnreadable("Scripts/old.luau", "'Scripts/old.luau' is a packed script of version 9.");
        level.ScriptAt("Scripts/main.luau", "\nrequire('./old')");

        level.Play().Tick();

        level.ErrorsWithPlace().ShouldBe(
        [
            "Scripts/main.luau:2: './old' is Scripts/old.luau, which cannot be read. " +
            "'Scripts/old.luau' is a packed script of version 9.",
        ]);
    }

    [Fact]
    public void A_module_that_was_missing_is_looked_for_again_and_one_that_is_found_is_not()
    {
        using var level = new ScriptLevel();
        level.ScriptAt("Scripts/main.luau", """
            print(pcall(require, './late'))
            task.wait()
            print(require('./late'), require('./late'))
            """);

        level.Play().Tick();
        level.FileAt("Scripts/late.luau", "print('ran')\nreturn 'late'");
        level.Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "false Scripts/main.luau:1: no module at './late': looked for Scripts/late.luau and Scripts/late/init.luau",
            "ran",
            "late late",
        ]);
    }

    [Theory]
    [InlineData("Scripts/util", "'Scripts/util' is no path to a module. " + Shape)]
    [InlineData("/Scripts/util", "'/Scripts/util' is an absolute path. " + Shape)]
    [InlineData("../../util", "'../../util' climbs out of the content root. " + Shape)]
    [InlineData("@game/util", "'@game' is not a valid alias. " + Shape)]
    [InlineData("./util.luau", "no module at './util.luau': looked for Scripts/util.luau.luau and " +
        "Scripts/util.luau/init.luau. A path leaves the extension out")]
    public void A_path_require_cannot_follow_is_refused_at_the_askers_line(string asked, string refusal)
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/util.luau", "return 1");
        level.ScriptAt("Scripts/main.luau", $"require('{asked}')");

        level.Play().Tick();

        level.ErrorsWithPlace().ShouldBe([$"Scripts/main.luau:1: {refusal}"]);
    }

    [Fact]
    public void One_string_asked_from_two_folders_names_two_modules_however_often_it_is_asked()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/doors/words.luau", "return 'of the doors'");
        level.FileAt("Scripts/lamps/words.luau", "return 'of the lamps'");
        const string Ask = "print(require('./words'), require('./words'), require('./words'))";
        level.ScriptAt("Scripts/doors/main.luau", Ask);
        level.ScriptAt("Scripts/lamps/main.luau", Ask);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "of the doors of the doors of the doors",
            "of the lamps of the lamps of the lamps",
        ]);
    }

    // A handler that asks on every tick is common in a Roblox script.
    [Theory]
    [InlineData("./lib/near")]
    [InlineData("@assets/Scripts/lib/near")]
    public void Asking_again_for_a_module_by_its_path_allocates_nothing(string path)
    {
        using var level = new ScriptLevel(hasCharacter: false);
        level.FileAt("Scripts/lib/near.luau", "return {}");
        level.ScriptAt("Scripts/main.luau", $$"""
            game:GetService("RunService").Heartbeat:Connect(function()
                for i = 1, 1000 do
                    local _ = require('{{path}}')
                end
            end)
            """);

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
    public void Two_spellings_of_one_path_are_one_module()
    {
        using var level = new ScriptLevel();
        level.FileAt("Scripts/Lib/Util.luau", "print('ran')\nreturn {}");
        level.ScriptAt("Scripts/main.luau", """
            local util = require('./Lib/Util')
            print(util == require('./Lib/../Lib/Util'), util == require('@ASSETS/Scripts/Lib/Util'))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["ran", "true true"]);
    }
}
