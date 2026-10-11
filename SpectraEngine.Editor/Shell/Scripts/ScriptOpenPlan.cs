using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>
/// Decides how a script is opened from what the computer has: Visual Studio
/// Code first, then the system's program for the file, then the file manager.
/// </summary>
// Pure, so a test can ask about a computer it is not running on. Nothing
// here looks at the disk or starts a program.
public static class ScriptOpenPlan
{
    /// <summary>What to try, best first. A step that fails to start hands over to the next.</summary>
    public static IReadOnlyList<ScriptOpenStep> For(ScriptOpenRequest request, ScriptEditorFacts facts)
    {
        ArgumentNullException.ThrowIfNull(request);

        var steps = new List<ScriptOpenStep>(3);
        bool isScript = IsScriptFile(request.FilePath);

        if (isScript
            && facts.VisualStudioCode is { Length: > 0 } code
            && VisualStudioCode(code, request) is { } start)
        {
            steps.Add(new ScriptOpenStep(ScriptOpenWay.VisualStudioCode, start));
        }

        if (isScript && facts.HasDefaultProgram)
        {
            steps.Add(new ScriptOpenStep(
                ScriptOpenWay.DefaultProgram, new ProgramStart(request.FilePath, [], UseShell: true)));
        }

        if (FileManager(facts.FileManager, request.FilePath) is { } reveal)
            steps.Add(new ScriptOpenStep(ScriptOpenWay.FileManager, reveal));

        return steps;
    }

    /// <summary>
    /// Whether a path names a script file. Only such a file is handed to a
    /// program: a level can name any file as its script, and the system would run it.
    /// </summary>
    public static bool IsScriptFile(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        string name = filePath[(filePath.LastIndexOfAny(['/', '\\']) + 1)..];

        // A colon in a name is a stream of another file on Windows.
        return name.Length > ContentClassifier.ScriptExtension.Length
            && name.EndsWith(ContentClassifier.ScriptExtension, StringComparison.OrdinalIgnoreCase)
            && !name.Contains(':');
    }

    // The folder first, so the window is the project's and its language
    // server reads every script. A window already open on it is used again.
    private static ProgramStart? VisualStudioCode(string program, ScriptOpenRequest request)
    {
        var arguments = new List<string>(3);

        if (request.ProjectFolder is { Length: > 0 } folder)
            arguments.Add(folder);

        if (request.Line is int line and > 0)
        {
            arguments.Add("--goto");
            arguments.Add(request.FilePath + ":" + line.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            arguments.Add(request.FilePath);
        }

        return IsBatchFile(program) ? ThroughCommandPrompt(program, arguments) : new ProgramStart(program, arguments);
    }

    private static bool IsBatchFile(string program) =>
        program.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
        || program.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    // A batch file is run by the command prompt, which reads its line by its
    // own rules: with /s the outer quotes go and what is inside is kept, each
    // part in quotes of its own. No console window: the program has its own.
    private static ProgramStart? ThroughCommandPrompt(string batchFile, List<string> arguments)
    {
        string line = "\"" + batchFile + "\"";
        foreach (string argument in arguments)
            line += " \"" + argument + "\"";

        // It swaps %NAME% for a variable inside quotes too, and nothing on a
        // command line stops it. Such a path goes to the next way.
        if (line.Contains('%'))
            return null;

        // It starts whatever the batch file does, so its exit code is asked.
        return new ProgramStart(
            "cmd.exe", [], VerbatimArguments: "/d /s /c \"" + line + "\"", NoWindow: true, ChecksExitCode: true);
    }

    private static ProgramStart? FileManager(FileManagerKind kind, string filePath) => kind switch
    {
        // Explorer reads its own command line: the switch, a comma, the path in quotes.
        FileManagerKind.Explorer => new ProgramStart(
            "explorer.exe", [], VerbatimArguments: "/select,\"" + filePath + "\""),

        FileManagerKind.Finder => new ProgramStart("open", ["-R", filePath]),

        FileManagerKind.FolderOpener when FolderOf(filePath) is { Length: > 0 } folder =>
            new ProgramStart(folder, [], UseShell: true),

        _ => null,
    };

    // By hand, not Path.GetDirectoryName: that reads a path by the rules of
    // the system it runs on, and the plan is asked about others.
    private static string FolderOf(string filePath)
    {
        int cut = filePath.LastIndexOfAny(['/', '\\']);
        return cut > 0 ? filePath[..cut] : string.Empty;
    }
}
