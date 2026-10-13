using System.Numerics;
using System.Text.RegularExpressions;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Tooling;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The site's page on a first script, held to the code: every script on it
/// is a sample luau-lsp checks, and it quotes what the engine says.
/// </summary>
public sealed partial class FirstScriptGuideTests
{
    // The file Insert Script makes for a part that is called Part.
    private const string PartScript = "Scripts/Part.luau";

    private static readonly string Page = File.ReadAllText(Path.Combine(
            BindingSourceTests.RepositoryRoot(), "site", "src", "content", "docs", "guides",
            "write-your-first-script.mdx"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    // A fenced block with its language. Inside a list the fence is indented,
    // and so is every line between.
    [GeneratedRegex(
        @"^(?<indent>[ \t]*)```(?<language>\w+)\n(?<body>.*?)\n\k<indent>```[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Fence();

    private static string[] Blocks(string language) =>
    [
        .. Fence().Matches(Page)
            .Where(match => match.Groups["language"].Value == language)
            .Select(match => string.Join('\n', match.Groups["body"].Value.Split('\n')
                .Select(line => line.StartsWith(match.Groups["indent"].Value, StringComparison.Ordinal)
                    ? line[match.Groups["indent"].Length..]
                    : line)) + "\n"),
    ];

    private static string Code(string contentPath) => ScriptSamples.At(contentPath).Code;

    [Fact]
    public void Every_script_on_the_page_is_a_sample_that_luau_lsp_checks()
    {
        string[] shown = Blocks("luau");
        string[] samples = [.. ScriptSamples.All.Select(sample => sample.Code)];

        foreach (string script in shown)
            samples.ShouldContain(script, "a script on the page is no sample of this project, so nothing checks it");

        // What Insert Script writes, the part that spins, and the same with a slip in it.
        shown.ShouldBe(
        [
            Code(ScriptSamples.TemplatePath), Code(ScriptSamples.FirstSpinPath), Code(ScriptSamples.FirstMistakePath),
        ]);
    }

    [Fact]
    public void Every_settings_file_on_the_page_has_the_text_the_editor_writes()
    {
        string[] written =
        [
            ScriptProjectFiles.LuauRcText(), ScriptProjectFiles.CodeSettingsText(), ScriptProjectFiles.CodeExtensionsText(),
        ];

        string[] shown = Blocks("json");

        shown.ShouldNotBeEmpty();
        foreach (string file in shown)
            written.ShouldContain(file);
    }

    // The editor leaves a settings file and a .luaurc alone that were there,
    // so the page is where their owner learns what they lack.
    [Fact]
    public void The_page_shows_what_a_file_of_the_persons_own_has_to_hold()
    {
        string[] shown = Blocks("json");

        shown.ShouldContain(ScriptProjectFiles.CodeSettingsText());
        shown.ShouldContain(ScriptProjectFiles.LuauRcText());
    }

    // The sample marks its slip for the old solver alone. While that holds,
    // a reader who uses the new one sees no red line and has to be told.
    [Fact]
    public void The_page_names_the_solver_that_passes_its_mistake()
    {
        ScriptSample mistake = ScriptSamples.At(ScriptSamples.FirstMistakePath);

        mistake.Expected(isNewSolver: false).ShouldNotBeEmpty();
        mistake.Expected(isNewSolver: true).ShouldBeEmpty(
            "the new solver reports the page's mistake now, so the page's note on it is out of date");
        Page.ShouldContain("`luau-lsp.fflags.enableNewSolver`");
        Page.ShouldContain("`--!strict`");
    }

    [Fact]
    public void The_page_names_every_file_a_project_gets()
    {
        Page.ShouldContain($"`{ScriptProjectFiles.Definitions}`");
        Page.ShouldContain($"`{ScriptProjectFiles.LuauRc}`");
        Page.ShouldContain($"`{ScriptProjectFiles.CodeSettings}`");
        Page.ShouldContain($"`{ScriptProjectFiles.CodeExtensions}`");
        Page.ShouldContain($"`{ScriptProjectFiles.LuauLspExtension}`");
    }

    [Fact]
    public void The_script_insert_writes_prints_the_line_the_page_shows()
    {
        using var level = new ScriptLevel();
        level.ScriptAt(PartScript, Code(ScriptSamples.TemplatePath), under: level.Part("Part", new Vector3(0f, 2f, 0f)));

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        ScriptMessage printed = level.Sink.Messages.ShouldHaveSingleItem();
        printed.Text.ShouldBe("Hello world!");
        Page.ShouldContain($"`{printed.Text}`");
        Page.ShouldContain($"`Part.luau:{printed.Line}`");
    }

    [Fact]
    public void The_spin_on_the_page_turns_its_part_a_radian_a_second_and_stop_puts_it_back()
    {
        using var level = new ScriptLevel();
        SceneNode part = level.Part("Part", new Vector3(0f, 2f, 0f));
        Transform built = part.LocalTransform;
        level.ScriptAt(PartScript, Code(ScriptSamples.FirstSpinPath), under: part);

        level.Play().Tick(60);

        level.Errors.ShouldBeEmpty();
        CFrame turned = new CFrame(new Vector3(0f, 2f, 0f)) * CFrame.Angles(0f, 1f, 0f);
        part.CFrame.FuzzyEq(turned, 1e-4f).ShouldBeTrue($"the part is at {part.CFrame}");

        level.Stop();

        part.LocalPosition.ShouldBe(built.Position);
        part.LocalRotation.ShouldBe(built.Rotation);
    }

    [Fact]
    public void The_mistake_on_the_page_stops_its_script_with_the_line_the_page_quotes()
    {
        using var level = new ScriptLevel();
        SceneNode part = level.Part("Part", new Vector3(0f, 2f, 0f));
        level.ScriptAt(PartScript, Code(ScriptSamples.FirstMistakePath), under: part);

        level.Play().Tick(3);

        // Once, and not on every tick: the script stopped where it failed.
        ScriptMessage failure = level.Sink.Messages.ShouldHaveSingleItem();
        failure.Severity.ShouldBe(ScriptSeverity.Error);
        failure.Script.ShouldBe(PartScript);
        failure.Text.ShouldBe("'RunServise' is not a valid Service name");
        part.CFrame.ShouldBe(new CFrame(new Vector3(0f, 2f, 0f)));

        Page.ShouldContain(failure.Text);
        Page.ShouldContain($"`Part.luau:{failure.Line}`");
        Page.ShouldContain($"line {failure.Line} of `Part.luau`");
    }
}
