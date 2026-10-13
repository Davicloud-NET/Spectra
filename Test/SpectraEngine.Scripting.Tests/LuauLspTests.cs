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
    public async Task The_clean_samples_pass_with_nothing_said(bool newSolver)
    {
        string program = LuauLsp.Require();
        using var project = new SampleProject();

        LuauLspDiagnostic[] said = await LuauLsp.Analyze(program, project, ScriptSamples.Clean, newSolver);

        said.ShouldBeEmpty(
            "A script that is right is reported. Either the definitions file types something wrongly, " +
            "or the sample is wrong.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_marked_line_of_a_mistake_is_reported_with_its_words_and_no_other_line_is(bool newSolver)
    {
        string program = LuauLsp.Require();
        using var project = new SampleProject();

        LuauLspDiagnostic[] said = await LuauLsp.Analyze(program, project, ScriptSamples.Mistakes, newSolver);

        List<string> wrong = [];
        foreach (ScriptSample sample in ScriptSamples.Mistakes)
        {
            string file = SampleProject.AssetsFolder + "/" + sample.ContentPath;
            ILookup<int, LuauLspDiagnostic> byLine = said.Where(one => one.File == file).ToLookup(one => one.Line);
            Dictionary<int, string> expected = sample.Expected(newSolver);

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
            $"The {(newSolver ? "new" : "old")} solver said:\n{string.Join('\n', said.AsEnumerable())}");
        said.ShouldNotBeEmpty();
    }

    private static string Listed(IEnumerable<LuauLspDiagnostic> said)
    {
        string[] texts = [.. said.Select(one => $"\"{one.Text}\"")];
        return texts.Length == 0 ? "nothing" : string.Join(" and ", texts);
    }
}
