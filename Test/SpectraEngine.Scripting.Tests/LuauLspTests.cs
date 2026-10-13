namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// luau-lsp reads the definitions file in a project set up the way the
/// editor sets one up: the scripts a person would write pass with nothing
/// said, and every marked mistake is named. Each under both type solvers.
/// </summary>
// The one place a test starts a program that is no part of the build. It
// runs only where SPECTRA_LUAU_LSP names the program.
[Trait("Suite", LuauLsp.Suite)]
public sealed class LuauLspTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_clean_samples_pass_with_nothing_said(bool isNewSolver)
    {
        string program = LuauLsp.Require();
        using var project = new SampleProject();

        LuauLspRun run = await LuauLsp.Analyze(program, project, ScriptSamples.Clean, isNewSolver);

        run.Diagnostics.ShouldBeEmpty(
            "A script that is right is reported. Either the definitions file types something wrongly, " +
            "or the sample is wrong.");
        run.ExitCode.ShouldBe(0, $"luau-lsp named no line and still failed. It said:\n{run.Said}");
    }

    [Fact]
    public async Task A_run_that_cannot_read_a_script_names_no_line_and_ends_with_an_error()
    {
        string program = LuauLsp.Require();
        using var project = new SampleProject();
        var missing = new ScriptSample("Scripts/not_in_the_project.luau", string.Empty);

        LuauLspRun run = await LuauLsp.Analyze(program, project, [missing], isNewSolver: false);

        // Which is why the clean samples are held to the exit code as well.
        run.Diagnostics.ShouldBeEmpty();
        run.ExitCode.ShouldNotBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_marked_line_of_a_mistake_is_reported_with_its_words_and_no_other_line_is(bool isNewSolver)
    {
        string program = LuauLsp.Require();
        using var project = new SampleProject();

        LuauLspDiagnostic[] said =
            (await LuauLsp.Analyze(program, project, ScriptSamples.Mistakes, isNewSolver)).Diagnostics;

        List<string> wrong = [];
        foreach (ScriptSample sample in ScriptSamples.Mistakes)
        {
            string file = SampleProject.AssetsFolder + "/" + sample.ContentPath;
            ILookup<int, LuauLspDiagnostic> byLine = said.Where(one => one.File == file).ToLookup(one => one.Line);
            Dictionary<int, string> expected = sample.Expected(isNewSolver);

            foreach ((int line, string words) in expected)
            {
                if (!byLine[line].Any(one => one.Text.Contains(words, StringComparison.Ordinal)))
                    wrong.Add($"{file}({line}): expected \"{words}\", got {Listed(byLine[line])}");
            }

            foreach (IGrouping<int, LuauLspDiagnostic> group in byLine.Where(group => !expected.ContainsKey(group.Key)))
                wrong.Add($"{file}({group.Key}): expected nothing, got {Listed(group)}");
        }

        wrong.ShouldBeEmpty(
            "A mistake that passes means the definitions file types too little. " +
            $"The {(isNewSolver ? "new" : "old")} solver said:\n{string.Join('\n', said.AsEnumerable())}");
        said.ShouldNotBeEmpty();
    }

    private static string Listed(IEnumerable<LuauLspDiagnostic> said)
    {
        string[] texts = [.. said.Select(one => $"\"{one.Text}\"")];
        return texts.Length == 0 ? "nothing" : string.Join(" and ", texts);
    }
}
