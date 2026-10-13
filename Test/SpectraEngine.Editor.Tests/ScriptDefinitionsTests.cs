using System.Text.RegularExpressions;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Editor.Shell.Scripts;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The definitions file the editor puts into a project: the script API of
/// this build, made the way the demo's switch makes it.
/// </summary>
public sealed partial class ScriptDefinitionsTests
{
    [GeneratedRegex(@"ScriptApiDescription\s+api\s*=\s*(?<source>[^;]+);")]
    private static partial Regex DescriptionRead();

    [GeneratedRegex(@"LuauDefinitions\.Write\((?<arguments>[^()]*)\)")]
    private static partial Regex DefinitionsCall();

    [Fact]
    public void The_text_is_the_script_api_of_this_build()
    {
        string text = ScriptDefinitions.Read();

        text.ShouldBe(LuauDefinitions.Write(LuauScriptApi.Description));
        text.ShouldStartWith("-- The script API of Spectra Engine, for luau-lsp.\n");
        text.ShouldNotContain("\r");
        text.ShouldContain("declare game: DataModel\n");
    }

    // The demo's switch is in a project the editor cannot reference, so the
    // two calls are compared as text. When one changes, a project would get
    // another file than --export-script-types writes.
    [Fact]
    public void The_editor_makes_the_text_by_the_call_the_demos_switch_makes()
    {
        string demo = Source("SpectraEngine.Executable", "ScriptApiExport.cs");
        string editor = Source("SpectraEngine.Editor", "Shell", "Scripts", "ScriptDefinitions.cs");

        Only(DescriptionRead(), demo, "source").ShouldBe("LuauScriptApi.Description");
        Only(DescriptionRead(), editor, "source").ShouldBe(Only(DescriptionRead(), demo, "source"));
        Only(DefinitionsCall(), editor, "arguments").ShouldBe(Only(DefinitionsCall(), demo, "arguments"));
    }

    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine([IconFiles.SourceRoot(), .. path]));

    private static string Only(Regex pattern, string source, string group) =>
        pattern.Matches(source).ShouldHaveSingleItem().Groups[group].Value.Trim();
}
