using System;
using System.Collections.Generic;
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

    /// <summary>Creates a launcher.</summary>
    /// <param name="starter">Starts the program that was chosen.</param>
    /// <param name="survey">Says what this computer has, asked on every open.</param>
    /// <param name="fileExists">Whether a script is on disk. The real disk when null.</param>
    public ScriptLauncher(
        IProgramStarter starter, Func<ScriptEditorFacts> survey, Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(starter);
        ArgumentNullException.ThrowIfNull(survey);

        _starter = starter;
        _survey = survey;
        _fileExists = fileExists ?? File.Exists;
    }

    /// <summary>A launcher over this computer.</summary>
    public static ScriptLauncher ForThisComputer() => new(new ProcessStarter(), ScriptEditorSurvey.Take);

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

        if (!_fileExists(request.FilePath))
            return new ScriptOpenOutcome(null, OutputSeverity.Error, $"{name} is not on disk any more.", []);

        ScriptEditorFacts facts = _survey();
        var failures = new List<string>();
        var notes = new List<string>();

        foreach (ScriptOpenStep step in ScriptOpenPlan.For(request, facts))
        {
            if (step.Way == ScriptOpenWay.FileManager && !facts.HasDefaultProgram)
                notes.Add(NoProgramSet);

            if (_starter.TryStart(step.Start, out string failure))
                return Opened(step.Way, name, notes, failures);

            notes.Add(DidNotStart(step.Way));
            failures.Add($"{DidNotStart(step.Way).TrimEnd('.')} ({step.Start.FileName}): {failure}");
        }

        return Failed(name, failures);
    }

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
