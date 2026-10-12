using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The string a script hands require, and the files it names by Luau's
/// rules. Nothing here runs a script.
/// </summary>
public sealed class ModulePathTests
{
    private const string Shape = "A path starts with ./ or ../, from the file that asks, or with @self or @assets";

    [Theory]
    [InlineData("./util", "Scripts/main.luau", "Scripts/util")]
    [InlineData("./lib/util", "Scripts/main.luau", "Scripts/lib/util")]
    [InlineData("../Shared/util", "Scripts/main.luau", "Shared/util")]
    [InlineData("../util", "Scripts/doors/open.luau", "Scripts/util")]
    [InlineData("./util", "main.luau", "util")]
    [InlineData("./b/./c", "Scripts/main.luau", "Scripts/b/c")]
    [InlineData("./../Shared/util", "Scripts/main.luau", "Shared/util")]
    [InlineData("./lib//util/", "Scripts/main.luau", "Scripts/lib/util")]
    public void A_path_with_a_dot_is_found_from_the_folder_of_the_file_that_asks(string asked, string asker, string module)
    {
        Resolve(asked, asker).ShouldBe([$"{module}.luau", $"{module}/init.luau"]);
    }

    // An init file is its folder. So ./ is beside the folder, as it is for
    // any other module, and @self is inside it.
    [Theory]
    [InlineData("./util", "Scripts/kit/init.luau", "Scripts/util")]
    [InlineData("../util", "Scripts/kit/init.luau", "util")]
    [InlineData("@self/part", "Scripts/kit/init.luau", "Scripts/kit/part")]
    [InlineData("@self/parts/bolt", "Scripts/kit/init.luau", "Scripts/kit/parts/bolt")]
    [InlineData("@self/part", "init.luau", "part")]
    public void An_init_file_asks_from_beside_its_folder_and_reaches_into_it_with_self(string asked, string asker, string module)
    {
        Resolve(asked, asker).ShouldBe([$"{module}.luau", $"{module}/init.luau"]);
    }

    // By Luau's rule it would be the folder of the file's own name. Luau's
    // tools refuse a file beside a folder of its name, so nothing can be there.
    [Theory]
    [InlineData("@self/part", "Scripts/door.luau")]
    [InlineData("@self", "Scripts/door.luau")]
    [InlineData("@SELF/../hinge", "door.luau")]
    public void Self_is_refused_for_a_file_that_is_no_init_file(string asked, string asker)
    {
        Refusal(asked, asker).ShouldBe(
            $"'{asked}' is asked by {asker}, which is no init file. @self is the folder of the init.luau " +
            "that asks. A file beside this one is at ./");
    }

    // Luau finds an init file by the bytes of its name.
    [Fact]
    public void Only_a_file_named_init_in_small_letters_is_its_folder()
    {
        Resolve("./x", "Scripts/Kit/Init.luau").ShouldBe(["Scripts/Kit/x.luau", "Scripts/Kit/x/init.luau"]);
        Resolve("./Kit/Init", "Scripts/main.luau").ShouldBe(["Scripts/Kit/Init.luau", "Scripts/Kit/Init/init.luau"]);
        Refusal("@self/x", "Scripts/Kit/INIT.luau").ShouldContain("which is no init file");
    }

    [Theory]
    [InlineData("@assets/Shared/util", "Shared/util")]
    [InlineData("@assets/util", "util")]
    [InlineData("@ASSETS/Shared/util", "Shared/util")]
    [InlineData("@Assets/Shared/./util", "Shared/util")]
    public void The_assets_alias_is_the_content_root_wherever_the_asker_is(string asked, string module)
    {
        Resolve(asked, "Scripts/deep/down/main.luau").ShouldBe([$"{module}.luau", $"{module}/init.luau"]);
        Resolve(asked, "main.luau").ShouldBe([$"{module}.luau", $"{module}/init.luau"]);
    }

    [Fact]
    public void A_backslash_counts_as_a_slash()
    {
        Resolve(".\\lib\\util", "Scripts/main.luau").ShouldBe(["Scripts/lib/util.luau", "Scripts/lib/util/init.luau"]);
    }

    // The content root has no name, and init is the name of a folder's own
    // file. Neither can be a file x.luau.
    [Theory]
    [InlineData("@assets", "Scripts/main.luau", "init.luau")]
    [InlineData("./", "main.luau", "init.luau")]
    [InlineData("./kit/init", "Scripts/main.luau", "Scripts/kit/init/init.luau")]
    public void A_path_to_the_content_root_or_to_init_names_only_a_folders_init_file(string asked, string asker, string file)
    {
        Resolve(asked, asker).ShouldBe([file]);
    }

    [Theory]
    [InlineData("Scripts/util")]
    [InlineData("util")]
    [InlineData("")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(".util")]
    public void A_path_with_no_prefix_is_refused_and_told_what_a_path_looks_like(string asked)
    {
        Refusal(asked, "Scripts/main.luau").ShouldBe($"'{asked}' is no path to a module. {Shape}");
    }

    [Theory]
    [InlineData("/Scripts/util")]
    [InlineData("C:/Game/Assets/Scripts/util")]
    [InlineData("C:\\Game\\Assets\\Scripts\\util")]
    public void An_absolute_path_is_refused_and_told_what_a_path_looks_like(string asked)
    {
        Refusal(asked, "Scripts/main.luau").ShouldBe($"'{asked}' is an absolute path. {Shape}");
    }

    [Theory]
    [InlineData("../../util", "Scripts/main.luau")]
    [InlineData("../util", "main.luau")]
    [InlineData("./util", "init.luau")]
    [InlineData("./../../util", "Scripts/main.luau")]
    [InlineData("@assets/../util", "Scripts/main.luau")]
    [InlineData("@self/../../../util", "Scripts/kit/init.luau")]
    public void A_path_that_climbs_out_of_the_content_root_is_refused(string asked, string asker)
    {
        Refusal(asked, asker).ShouldBe($"'{asked}' climbs out of the content root. {Shape}");
    }

    [Theory]
    [InlineData("@game/ReplicatedStorage/Util", "@game")]
    [InlineData("@lune/fs", "@lune")]
    [InlineData("@", "@")]
    public void An_alias_the_engine_does_not_have_is_refused_by_name(string asked, string alias)
    {
        Refusal(asked, "Scripts/main.luau").ShouldBe($"'{alias}' is not a valid alias. {Shape}");
    }

    [Fact]
    public void A_path_with_a_drive_in_it_names_no_file_under_the_content_root()
    {
        Refusal("@assets/C:/Windows/util", "Scripts/main.luau")
            .ShouldBe($"'@assets/C:/Windows/util' names no file under the content root. {Shape}");
    }

    [Theory]
    [InlineData("./util")]
    [InlineData("@self/util")]
    public void Code_with_no_file_can_only_ask_from_the_content_root(string asked)
    {
        Refusal(asked, asker: "").ShouldBe(
            $"'{asked}' is found from the file of the function that calls require, and here no script " +
            "function does. Start the path with @assets");
        Resolve("@assets/util", asker: "").ShouldBe(["util.luau", "util/init.luau"]);
    }

    // Luau walks the folders and fails at one that is not there. A script
    // source has no folders to ask, so a path may not go down and up again.
    [Theory]
    [InlineData("./a/../b", "Scripts/main.luau")]
    [InlineData("../lib/../util", "Scripts/main.luau")]
    [InlineData("@assets/Shared/x/../util", "Scripts/main.luau")]
    [InlineData("@self/part/../other", "Scripts/kit/init.luau")]
    public void A_path_that_climbs_after_it_named_a_folder_is_refused(string asked, string asker)
    {
        Refusal(asked, asker).ShouldBe($"'{asked}' climbs with .. after it named a folder. Put every .. first");
    }

    // Windows opens "lib." as "lib", which would make one file two modules.
    [Theory]
    [InlineData("./lib./near")]
    [InlineData("@assets/Shared./near")]
    [InlineData("./near.")]
    [InlineData("./lib.../near")]
    public void A_name_that_ends_in_a_dot_is_refused(string asked)
    {
        Refusal(asked, "Scripts/main.luau").ShouldBe(
            $"'{asked}' has a name that ends in a dot, which Windows reads as the name without it. Leave the dot out");
    }

    [Fact]
    public void A_missing_module_names_the_path_and_every_file_looked_for()
    {
        ModulePath.Missing("./lib/util", ["Scripts/lib/util.luau", "Scripts/lib/util/init.luau"]).ShouldBe(
            "no module at './lib/util': looked for Scripts/lib/util.luau and Scripts/lib/util/init.luau");
        ModulePath.Missing("@assets", ["init.luau"]).ShouldBe("no module at '@assets': looked for init.luau");
    }

    [Theory]
    [InlineData("./util.luau", ". A path leaves the extension out")]
    [InlineData("./util.lua", ". A path leaves the extension out")]
    [InlineData("./kit/init", ". An init file is asked for by its folder's name")]
    public void A_missing_module_says_what_is_likely_wrong_with_the_path(string asked, string hint)
    {
        ModulePath.Missing(asked, ["x.luau"]).ShouldBe($"no module at '{asked}': looked for x.luau{hint}");
    }

    [Fact]
    public void Two_files_for_one_path_and_a_file_that_cannot_be_read_are_named()
    {
        ModulePath.TwoFiles("./kit", "Scripts/kit.luau", "Scripts/kit/init.luau").ShouldBe(
            "'./kit' names two files, Scripts/kit.luau and Scripts/kit/init.luau. Remove one");
        ModulePath.Unreadable("./kit", "Scripts/kit.luau", "It is of another version.").ShouldBe(
            "'./kit' is Scripts/kit.luau, which cannot be read. It is of another version.");
    }

    private static string[] Resolve(string asked, string asker)
    {
        ModulePath.TryResolve(asked, asker, out string[] files, out string? refusal).ShouldBeTrue(refusal);
        return files;
    }

    private static string Refusal(string asked, string asker)
    {
        ModulePath.TryResolve(asked, asker, out string[] files, out string? refusal).ShouldBeFalse();
        files.ShouldBeEmpty();
        return refusal.ShouldNotBeNull();
    }
}
