using System.Globalization;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The engine's CFrame against Roblox Studio's own answers. The probe in
/// <c>Data/cframe-probe.luau</c> asks Studio, and its output is kept beside it
/// as <c>cframe-studio.json</c>. Until that file is there, the comparisons skip.
/// </summary>
public sealed class CFrameStudioProbeTests
{
    private const float Tolerance = 1e-4f;
    private const string WriteSwitch = "SPECTRA_WRITE_CFRAME_PROBE";

    private const string NoAnswersYet =
        "Roblox Studio has not been asked yet. Paste Test/SpectraEngine.Bsp.Tests/Data/cframe-probe.luau " +
        "into Studio's command bar and save what it prints as cframe-studio.json beside the probe.";

    private static string ProbePath => Path.Combine(DataFolder(), "cframe-probe.luau");

    private static string AnswersPath => Path.Combine(DataFolder(), "cframe-studio.json");

    [Fact]
    public void Every_case_has_its_own_plain_name()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (CFrameProbeCase probeCase in CFrameProbeCases.All)
        {
            probeCase.Name.ShouldMatch("^[a-z0-9_]+$");
            names.Add(probeCase.Name).ShouldBeTrue($"'{probeCase.Name}' is used twice");
        }
    }

    [Fact]
    public void The_probe_file_asks_Roblox_for_the_cases_the_engine_answers()
    {
        string script = File.ReadAllText(ProbePath).ReplaceLineEndings("\n");
        string expected = CFrameProbeScript.CaseLines(CFrameProbeCases.All);

        if (Environment.GetEnvironmentVariable(WriteSwitch) == "1")
            File.WriteAllText(ProbePath, CFrameProbeScript.WithCases(script, expected));
        else
            CFrameProbeScript.CasesIn(script).ShouldBe(
                expected,
                $"cframe-probe.luau is behind CFrameProbeCases. Run the suite once with {WriteSwitch}=1 " +
                "to write it again, then ask Studio again: its old answers are for the old cases.");
    }

    [Fact]
    public void Every_case_Roblox_documents_matches_what_Studio_answers()
    {
        Dictionary<string, float[]> answers = StudioAnswers();
        var faults = new List<string>();

        foreach (CFrameProbeCase probeCase in CFrameProbeCases.All)
        {
            if (!probeCase.IsOurChoice && Fault(probeCase, answers) is { } fault)
                faults.Add(fault);
        }

        foreach (string name in answers.Keys.Except(CFrameProbeCases.All.Select(c => c.Name)))
            faults.Add($"{name}: Studio answered a case this probe does not ask. The file is from another probe.");

        faults.ShouldBeEmpty();
    }

    // Where the documentation is silent Roblox may still answer. A finite
    // answer that differs from ours is a decision to make: follow it, or keep
    // the difference and say so on the differences page.
    [Fact]
    public void Where_the_engine_chose_an_answer_Studio_gives_the_same_or_none()
    {
        Dictionary<string, float[]> answers = StudioAnswers();
        var faults = new List<string>();

        foreach (CFrameProbeCase probeCase in CFrameProbeCases.All)
        {
            if (!probeCase.IsOurChoice)
                continue;

            bool studioHasNone =
                answers.TryGetValue(probeCase.Name, out float[]? theirs) &&
                (theirs.Length == 0 || theirs.Any(float.IsNaN));
            if (!studioHasNone && Fault(probeCase, answers) is { } fault)
                faults.Add(fault);
        }

        faults.ShouldBeEmpty();
    }

    private static Dictionary<string, float[]> StudioAnswers()
    {
        Assert.SkipUnless(File.Exists(AnswersPath), NoAnswersYet);
        return StudioProbeAnswers.Parse(File.ReadAllText(AnswersPath));
    }

    // Null when Studio's answer is the engine's.
    private static string? Fault(CFrameProbeCase probeCase, Dictionary<string, float[]> answers)
    {
        if (!answers.TryGetValue(probeCase.Name, out float[]? theirs))
            return $"{probeCase.Name}: not in Studio's answers. Was the print cut short?";
        if (theirs.Length == 0)
            return $"{probeCase.Name}: Studio could not work out {probeCase.Luau}";
        if (theirs.Length != probeCase.Ours.Length)
            return $"{probeCase.Name}: Studio gave {theirs.Length} numbers where {probeCase.Ours.Length} were asked for";

        for (int i = 0; i < theirs.Length; i++)
        {
            float difference = probeCase.Ours[i] - theirs[i];
            if (probeCase.Reader == CFrameProbeCase.Angles)
                difference = MathF.IEEERemainder(difference, 2f * MathF.PI);

            // Written this way round so a NaN is a fault.
            if (!(MathF.Abs(difference) <= Tolerance * MathF.Max(1f, MathF.Abs(theirs[i]))))
            {
                return $"{probeCase.Name}: {probeCase.Luau}\n" +
                    $"    Studio: {Format(theirs)}\n" +
                    $"    engine: {Format(probeCase.Ours)}";
            }
        }

        return null;
    }

    private static string Format(float[] values) =>
        string.Join(", ", values.Select(v => v.ToString("0.#####", CultureInfo.InvariantCulture)));

    // The test project's own Data folder in the source tree.
    private static string DataFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return Path.Combine(dir.FullName, "Test", "SpectraEngine.Bsp.Tests", "Data");
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"No solution file above {AppContext.BaseDirectory}; the probe test needs the repo.");
    }
}
