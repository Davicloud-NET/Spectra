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

        if (facts.VisualStudioCode is { Length: > 0 } code)
            steps.Add(new ScriptOpenStep(ScriptOpenWay.VisualStudioCode, VisualStudioCode(code, request)));

        if (facts.HasDefaultProgram)
        {
            steps.Add(new ScriptOpenStep(
                ScriptOpenWay.DefaultProgram, new ProgramStart(request.FilePath, [], UseShell: true)));
        }

        if (FileManager(facts.FileManager, request.FilePath) is { } reveal)
            steps.Add(new ScriptOpenStep(ScriptOpenWay.FileManager, reveal));

        return steps;
    }

    // The folder first, so the window is the project's and its language
    // server reads every script. A window already open on it is used again.
    private static ProgramStart VisualStudioCode(string program, ScriptOpenRequest request)
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

        return new ProgramStart(program, arguments);
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
