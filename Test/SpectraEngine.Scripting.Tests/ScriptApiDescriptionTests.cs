using System.Text.RegularExpressions;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The description of the script API against what a script finds when it
/// looks. A member the binding offers and the description does not have
/// would be missing from the definitions file and the reference, and the
/// other way round a script would be promised what is not there.
/// </summary>
public sealed partial class ScriptApiDescriptionTests
{
    // An object of every class, by the class's name. A new class gets a line here.
    private const string Holders = """
        local run = game:GetService("RunService")
        local holders = {
            Instance = script.Parent,
            DataModel = game,
            RunService = run,
            RBXScriptSignal = run.Heartbeat,
            RBXScriptConnection = run.Heartbeat:Connect(function() end),
            CFrame = CFrame.new(1, 2, 3),
            Vector3 = Vector3.new(1, 2, 3),
        }
        """;

    // Names a Roblox script would try, which no class here may answer by accident.
    private static readonly string[] RobloxNames =
    [
        "Anchored", "Clone", "Destroy", "FindFirstChild", "GetChildren", "IsA", "Size", "Changed", "new", "Fire",
    ];

    // A name the binding's own sources compare or declare.
    [GeneratedRegex(@"""(?<name>[A-Za-z_]\w*)""(?:u8)?")]
    private static partial Regex NameLiteral();

    private static ScriptApiDescription Api => LuauScriptApi.Description;

    [Fact]
    public void An_object_of_every_class_has_the_members_the_description_gives_it_and_no_other()
    {
        string[] names = [.. Candidates()];
        string[] printed = ScriptLevel.Run($$"""
            {{Holders}}
            local names = { {{string.Join(", ", names.Select(name => $"\"{name}\""))}} }
            for class, holder in holders do
                local found = {}
                for _, name in names do
                    local isThere, value = pcall(function() return holder[name] end)
                    if isThere then
                        table.insert(found, name .. (type(value) == "function" and "()" or ""))
                    end
                end
                print(class .. ": " .. table.concat(found, " "))
            end
            """);

        Dictionary<string, string> found = ByLabel(printed);
        found.Keys.Order(StringComparer.Ordinal).ShouldBe(
            Api.Classes.Select(described => described.Name).Order(StringComparer.Ordinal),
            "the test holds no object of a class the description has. Give it one in Holders");

        foreach (ScriptClassDescription described in Api.Classes)
        {
            found[described.Name].ShouldBe(
                string.Join(' ', MembersOf(described).Order(StringComparer.Ordinal)),
                $"what a script finds on a {described.Name}, against what the description says it has. " +
                "A name with () is a method");
        }
    }

    [Fact]
    public void Every_library_holds_the_functions_and_constants_the_description_gives_it_and_no_other()
    {
        string[] printed = ScriptLevel.Run($$"""
            for _, library in { {{string.Join(", ", Api.Libraries.Select(library => $"\"{library.Name}\""))}} } do
                local found = {}
                for name, value in _G[library] do
                    table.insert(found, name .. (type(value) == "function" and "()" or ""))
                end
                table.sort(found)
                print(library .. ": " .. table.concat(found, " "))
            end
            """);

        Dictionary<string, string> found = ByLabel(printed);
        Api.Libraries.ShouldNotBeEmpty();
        foreach (ScriptLibraryDescription library in Api.Libraries)
        {
            IEnumerable<string> described = library.Constants.Select(constant => constant.Name)
                .Concat(library.Functions.Select(function => function.Name + "()"));

            found[library.Name].ShouldBe(
                string.Join(' ', described.Order(StringComparer.Ordinal)),
                $"what the table {library.Name} holds, against what the description says it holds");
        }
    }

    [Fact]
    public void Every_global_the_runtime_adds_is_described_and_every_described_global_is_there()
    {
        const string ListGlobals = """
            local found = {}
            for name in _G do table.insert(found, name) end
            print(table.concat(found, " "))
            """;

        string[] described =
        [
            .. Api.Libraries.Select(library => library.Name),
            .. Api.Globals.Select(global => global.Name),
            .. Api.Functions.Select(function => function.Name),
        ];

        string[] inALevel = ScriptLevel.Run(ListGlobals)[0].Split(' ');
        string[] isThere = ScriptLevel.Run(
            string.Join('\n', described.Select(name => $"print('{name}', {name} ~= nil)")));

        inALevel.Except(GlobalsOfABareHost(ListGlobals)).Order(StringComparer.Ordinal).ShouldBeSubsetOf(
            described,
            "the runtime sets a global the description does not have");
        isThere.ShouldBe(
            described.Select(name => $"{name} true"),
            "the description has a global a script does not find");
        described.ShouldBeUnique();
    }

    [Fact]
    public void Every_member_and_every_class_has_words_for_the_reference()
    {
        foreach (ScriptClassDescription described in Api.Classes)
        {
            described.Summary.ShouldNotBeNullOrWhiteSpace(described.Name);
            described.Properties.ShouldAllBe(property => property.Summary.Length > 0 && property.Type.Length > 0);
            described.Methods.ShouldAllBe(method => method.Summary.Length > 0 && method.Signatures.Count > 0);
            described.Signals.ShouldAllBe(signal => signal.Summary.Length > 0);
        }

        foreach (ScriptLibraryDescription library in Api.Libraries)
        {
            library.Summary.ShouldNotBeNullOrWhiteSpace(library.Name);
            library.Constants.ShouldAllBe(constant => constant.Summary.Length > 0);
            library.Functions.ShouldAllBe(function => function.Summary.Length > 0 && function.Signatures.Count > 0);
        }

        Api.Globals.ShouldAllBe(global => global.Summary.Length > 0);
        Api.Functions.ShouldAllBe(function => function.Summary.Length > 0);
    }

    [Fact]
    public void The_old_spellings_are_marked_and_say_what_to_write_instead()
    {
        Dictionary<string, string> deprecated = Api.Functions
            .Where(function => function.Deprecated.Length > 0)
            .ToDictionary(function => function.Name, function => function.Deprecated);

        deprecated.ShouldBe(new Dictionary<string, string>
        {
            ["wait"] = "task.wait",
            ["spawn"] = "task.spawn",
            ["delay"] = "task.delay",
        });
    }

    // What the description says an object of the class has. A method ends in ().
    private static IEnumerable<string> MembersOf(ScriptClassDescription described)
    {
        var methods = new HashSet<string>(StringComparer.Ordinal);
        for (ScriptClassDescription? at = described; at is not null; at = Api.FindClass(at.Extends))
            methods.UnionWith(at.Methods.Select(method => method.Name));

        return Api.MemberNamesOf(described.Name).Select(name => methods.Contains(name) ? name + "()" : name);
    }

    // Every name a class might answer: what the description has, what the
    // binding's sources name in a string, and what Roblox has.
    private static SortedSet<string> Candidates()
    {
        var names = new SortedSet<string>(RobloxNames, StringComparer.Ordinal);
        foreach (ScriptClassDescription described in Api.Classes)
            names.UnionWith(Api.MemberNamesOf(described.Name));

        string binding = Path.Combine(BindingSourceTests.RepositoryRoot(), BindingSourceTests.Binding);
        foreach (string file in BindingSourceTests.Sources(binding))
            names.UnionWith(NameLiteral().Matches(File.ReadAllText(file)).Select(match => match.Groups["name"].Value));

        names.Count.ShouldBeGreaterThan(50, "the description or the binding's sources were not found");
        return names;
    }

    private static Dictionary<string, string> ByLabel(string[] printed) =>
        printed.ToDictionary(line => line[..line.IndexOf(':')], line => line[(line.IndexOf(':') + 1)..].Trim());

    // What a host has before a level gives it anything: Luau's own globals,
    // and the host's print and warn.
    private static string[] GlobalsOfABareHost(string listGlobals)
    {
        using LuauHost host = TestHost.Open(out RecordingSink sink);
        host.RunText(listGlobals).ShouldBe(LuauResume.Finished);
        return sink.Single.Text.Split(' ');
    }
}
