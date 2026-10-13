using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SpectraEngine.Scripting.Tests;

// The luau-lsp program, for the tests that check the definitions file with
// it. It is no part of the build, so those tests run only where the
// environment names it, and skip everywhere else. They must not pass.
internal static partial class LuauLsp
{
    // The trait of every test that calls Require. CI runs the tests that
    // have it in a step where a skip fails.
    public const string Suite = "LuauLsp";

    // Holds the path of the program.
    public const string Variable = "SPECTRA_LUAU_LSP";

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    // file(line,column): Kind: text
    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\): (?<kind>\w+): (?<text>.*)$")]
    private static partial Regex Diagnostic();

    public static string Require()
    {
        RequireTheTrait();

        string program = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
        Assert.SkipWhen(
            program.Length == 0,
            $"{Variable} does not name the luau-lsp program, so the definitions file is not checked against it here.");

        File.Exists(program).ShouldBeTrue($"{Variable} names '{program}', and there is no such file");
        return program;
    }

    // Runs "luau-lsp analyze" in the project's folder on the scripts named,
    // and hands back how it ended and everything it said of them.
    public static async Task<LuauLspRun> Analyze(
        string program, SampleProject project, IEnumerable<ScriptSample> samples, bool isNewSolver)
    {
        var start = new ProcessStartInfo(program)
        {
            WorkingDirectory = project.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add("analyze");
        foreach (string argument in project.AnalyzeArguments())
            start.ArgumentList.Add(argument);

        // Said for the old solver too, so a new default cannot make both runs the same.
        start.ArgumentList.Add("--flag:LuauSolverV2=" + (isNewSolver ? "true" : "false"));
        foreach (ScriptSample sample in samples)
            start.ArgumentList.Add(SampleProject.AssetsFolder + "/" + sample.ContentPath);

        using Process process = Process.Start(start).ShouldNotBeNull();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();

        using var deadline = new CancellationTokenSource(Patience);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"luau-lsp did not end within {Patience.TotalMinutes} minutes.");
        }

        string said = await output + "\n" + await errors;
        return new LuauLspRun(process.ExitCode, Parse(said), said);
    }

    // A message can run over several lines. Those that follow its first
    // line belong to it, up to the next message or the next log line.
    internal static LuauLspDiagnostic[] Parse(string said)
    {
        List<LuauLspDiagnostic> diagnostics = [];
        bool isInMessage = false;
        foreach (string line in said.ReplaceLineEndings("\n").Split('\n'))
        {
            Match match = Diagnostic().Match(line);
            if (match.Success)
            {
                diagnostics.Add(new LuauLspDiagnostic(
                    match.Groups["file"].Value.Replace('\\', '/'),
                    int.Parse(match.Groups["line"].Value),
                    int.Parse(match.Groups["column"].Value),
                    match.Groups["kind"].Value,
                    match.Groups["text"].Value));
                isInMessage = true;
            }
            else if (line.StartsWith('['))
            {
                isInMessage = false;
            }
            else if (isInMessage && line.Length > 0)
            {
                LuauLspDiagnostic last = diagnostics[^1];
                diagnostics[^1] = last with { Text = last.Text + "\n" + line };
            }
        }

        return [.. diagnostics];
    }

    // A failure and not a skip: a test without the trait is in no step that
    // counts skips, and a missing program would go unseen there.
    private static void RequireTheTrait()
    {
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? traits = TestContext.Current.TestCase?.Traits;

        bool has = traits is not null
            && traits.TryGetValue("Suite", out IReadOnlyCollection<string>? suites)
            && suites.Contains(Suite);

        Assert.True(
            has,
            "This test needs the luau-lsp program, so it carries [Trait(\"Suite\", LuauLsp.Suite)], on itself " +
            "or on its class.");
    }
}
