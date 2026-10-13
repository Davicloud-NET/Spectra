using SpectraEngine.Core.Scripting.Tooling;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>
/// Opens a script in a code editor outside this one. Everything in the shell
/// that opens a script asks here.
/// </summary>
public sealed class ScriptLauncher
{
    private const string NoProgramSet = "No program is set for .luau files.";

    private readonly IProgramStarter _starter;
    private readonly Func<ScriptEditorFacts> _survey;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, int?> _countLines;

    /// <summary>Creates a launcher.</summary>
    /// <param name="starter">Starts the program that was chosen.</param>
    /// <param name="survey">Says what this computer has, asked on every open.</param>
    /// <param name="fileExists">Whether a script is on disk. The real disk when null.</param>
    /// <param name="countLines">How many lines a script has, or null when that is not known. The real disk when null.</param>
    /// <param name="setup">Writes the project's files for a code editor. Nothing is written when null.</param>
    public ScriptLauncher(
        IProgramStarter starter, Func<ScriptEditorFacts> survey,
        Func<string, bool>? fileExists = null, Func<string, int?>? countLines = null,
        ScriptEditorSetup? setup = null)
    {
        ArgumentNullException.ThrowIfNull(starter);
        ArgumentNullException.ThrowIfNull(survey);

        _starter = starter;
        _survey = survey;
        _fileExists = fileExists ?? File.Exists;
        _countLines = countLines ?? ScriptDetails.CountLines;
        Setup = setup ?? ScriptEditorSetup.None;
    }

    /// <summary>What sets a script's project up before the script opens.</summary>
    public ScriptEditorSetup Setup { get; }

    /// <summary>A launcher over this computer.</summary>
    public static ScriptLauncher ForThisComputer() =>
        new(new ProcessStarter(), ScriptEditorSurvey.Take, setup: ScriptEditorSetup.OfThisBuild());

    /// <summary>
    /// Opens the script off the calling thread: looking for a code editor
    /// reads the disk, and starting one can take a moment.
    /// </summary>
    public Task<ScriptOpenOutcome> OpenAsync(ScriptOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(() =>
        {
            try
            {
                return Open(request);
            }
            catch (Exception ex)
            {
                // The boundary: whatever went wrong becomes a line, not a crash.
                return Failed(NameOf(request), [ex.Message]);
            }
        });
    }

    /// <summary>Opens the script on the calling thread.</summary>
    public ScriptOpenOutcome Open(ScriptOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string name = NameOf(request);

        // Gone, or picked before it was written. The sentence fits both.
        if (!_fileExists(request.FilePath))
            return new ScriptOpenOutcome(null, OutputSeverity.Error, $"{name} is not on disk.", []);

        // First, so a code editor that starts below finds its files there.
        // Only for a script: any other file is shown and never opened.
        ScriptProjectReport project =
            ScriptOpenPlan.IsScriptFile(request.FilePath) && request.ProjectFolder is { Length: > 0 } folder
                ? Setup.Prepare(folder)
                : ScriptProjectReport.Nothing;

        return Start(request, name) with { Project = project };
    }

    private ScriptOpenOutcome Start(ScriptOpenRequest request, string name)
    {
        int? lines = request.Line is null ? null : _countLines(request.FilePath);
        ScriptOpenRequest inFile = KeepLineInFile(request, lines);
        ScriptEditorFacts facts = _survey();
        bool isScript = ScriptOpenPlan.IsScriptFile(request.FilePath);
        var failures = new List<string>();
        var notes = new List<string>();

        string? whyOnlyShown =
            !isScript ? NotAScript(name)
            : !facts.HasDefaultProgram ? NoProgramSet
            : null;

        foreach (ScriptOpenStep step in ScriptOpenPlan.For(inFile, facts))
        {
            if (step.Way == ScriptOpenWay.FileManager && whyOnlyShown is not null)
                notes.Add(whyOnlyShown);

            if (_starter.TryStart(step.Start, out string failure))
                return AboutTheLine(Opened(step.Way, name, notes, failures), request, lines);

            notes.Add(DidNotStart(step.Way));
            failures.Add($"{DidNotStart(step.Way).TrimEnd('.')} ({step.Start.FileName}): {failure}");
        }

        return isScript
            ? Failed(name, failures)
            : new ScriptOpenOutcome(null, OutputSeverity.Error, NotAScript(name), failures);
    }

    private static string NotAScript(string name) =>
        $"{name} is not a .luau file, so no program was started for it.";

    // A message can name a line the file no longer has, when the file was
    // edited since. One past the count is the empty line after the last line
    // break, which is where a script that ends too soon fails.
    private static bool IsPastEnd(int line, int? lines) => lines is int count && line > count + 1;

    private static ScriptOpenRequest KeepLineInFile(ScriptOpenRequest request, int? lines) =>
        request.Line is int line && lines is int count && IsPastEnd(line, lines)
            ? request with { Line = Math.Max(count, 1) }
            : request;

    // Says what became of the line that was asked for, when the caret is not on it.
    private static ScriptOpenOutcome AboutTheLine(ScriptOpenOutcome opened, ScriptOpenRequest asked, int? lines)
    {
        if (asked.Line is not int line || line <= 0)
            return opened;

        if (lines is int count && IsPastEnd(line, lines))
        {
            string has = count == 1 ? "1 line" : $"{Number(count)} lines";
            return opened with
            {
                Severity = OutputSeverity.Warning,
                Message = $"{opened.Message} Line {Number(line)} is past its end: it has {has} now.",
            };
        }

        // Only Visual Studio Code is handed a line. With anything else the
        // person has to go there, so they are told where.
        return opened.Way == ScriptOpenWay.VisualStudioCode
            ? opened
            : opened with { Message = $"{opened.Message} Look at line {Number(line)}." };
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static ScriptOpenOutcome Opened(
        ScriptOpenWay way, string name, List<string> notes, List<string> failures)
    {
        notes.Add(way switch
        {
            ScriptOpenWay.VisualStudioCode => $"Opened {name} in Visual Studio Code.",
            ScriptOpenWay.DefaultProgram => $"Opened {name} in the program your system has for .luau files.",
            _ => $"{name} is shown in its folder.",
        });

        // Shown in a folder is not opened, and a fallback is worth a second look.
        bool plain = way != ScriptOpenWay.FileManager && failures.Count == 0;

        return new ScriptOpenOutcome(
            way, plain ? OutputSeverity.Info : OutputSeverity.Warning, string.Join(' ', notes), failures);
    }

    private static ScriptOpenOutcome Failed(string name, IReadOnlyList<string> failures) => new(
        null, OutputSeverity.Error, $"{name} could not be opened or shown in its folder.", failures);

    private static string DidNotStart(ScriptOpenWay way) => way switch
    {
        ScriptOpenWay.VisualStudioCode => "Visual Studio Code did not start.",
        ScriptOpenWay.DefaultProgram => "The system's program for .luau files did not start.",
        _ => "The file manager did not start.",
    };

    // By hand: Path.GetFileName reads a path by the rules of the system it runs on.
    private static string NameOf(ScriptOpenRequest request)
    {
        int cut = request.FilePath.LastIndexOfAny(['/', '\\']);
        return cut >= 0 ? request.FilePath[(cut + 1)..] : request.FilePath;
    }
}
