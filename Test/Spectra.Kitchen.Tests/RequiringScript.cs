namespace Spectra.Kitchen.Tests;

// A script that asks for a module in every way there is and prints what each
// gave, with the modules it asks for. Each module prints a line when it runs.
// Every line begins with the same word, so a test finds them among the rest.
internal static class RequiringScript
{
    public const string Path = "Scripts/requires.luau";

    // The module node the script sits under, and its file.
    public const string ModuleNode = "Answers";
    public const string ModulePath = "Scripts/answers.luau";

    // What every line of the script and of its modules begins with.
    public const string Mark = "Requires | ";

    public const string Text = """
        local function say(what, answer)
        	print("Requires | " .. what .. " | " .. tostring(answer))
        end

        local near = require("./lib/near")
        say("by a relative path", near.name)
        local far = require("@assets/Shared/far")
        say("by @assets", far.name)
        local kit = require("./kit")
        say("an init module", kit.name .. " with " .. kit.part .. ", beside " .. kit.near.name)
        say("one table for everyone", kit.near == near and far == require("../Shared/far"))

        local answers = require(script.Parent)
        say("a module node", answers.name .. " whose script is " .. answers.script .. ", a " .. answers.class)
        say("the same node again", answers == require(script.Parent))

        local found, why = pcall(require, "./lib/lost")
        say("a module that is not there", why)
        """;

    // The modules, by content path. None is named by a node.
    public static readonly (string Path, string Text)[] Modules =
    [
        ("Scripts/lib/near.luau", """
            print("Requires | near ran")
            return { name = "near" }
            """),
        ("Shared/far.luau", """
            print("Requires | far ran")
            return { name = "far" }
            """),

        // An init file is its folder: ./ is beside the folder and @self is inside it.
        ("Scripts/kit/init.luau", """
            print("Requires | kit ran")
            return { name = "kit", part = require("@self/part"), near = require("./lib/near") }
            """),
        ("Scripts/kit/part.luau", """
            print("Requires | part ran")
            return "a part of the kit"
            """),
    ];

    // The file of the module node. Its script is the node.
    public const string ModuleText = """
        print("Requires | answers ran under " .. script.Parent.Name)
        return { name = "answers", script = script.Name, class = script.ClassName }
        """;

    // What a level with the script prints, each line with the file and line it came from.
    public static readonly string[] Lines =
    [
        "Print Scripts/lib/near.luau:1: Requires | near ran",
        "Print Scripts/requires.luau:2: Requires | by a relative path | near",
        "Print Shared/far.luau:1: Requires | far ran",
        "Print Scripts/requires.luau:2: Requires | by @assets | far",
        "Print Scripts/kit/init.luau:1: Requires | kit ran",
        "Print Scripts/kit/part.luau:1: Requires | part ran",
        "Print Scripts/requires.luau:2: Requires | an init module | kit with a part of the kit, beside near",
        "Print Scripts/requires.luau:2: Requires | one table for everyone | true",
        "Print Scripts/answers.luau:1: Requires | answers ran under Root",
        "Print Scripts/requires.luau:2: Requires | a module node | answers whose script is Answers, a ModuleScript",
        "Print Scripts/requires.luau:2: Requires | the same node again | true",
        "Print Scripts/requires.luau:2: Requires | a module that is not there | Scripts/requires.luau:17: " +
            "no module at './lib/lost': looked for Scripts/lib/lost.luau and Scripts/lib/lost/init.luau",
    ];
}
