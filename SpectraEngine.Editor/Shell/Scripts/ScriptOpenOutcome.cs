using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>What became of one request to open a script.</summary>
/// <param name="Way">How the file got in front of somebody, or null when nothing worked.</param>
/// <param name="Severity">How loud <paramref name="Message"/> is.</param>
/// <param name="Message">One line for the status bar.</param>
/// <param name="Failures">One line for each thing that was tried and did not start.</param>
public sealed record ScriptOpenOutcome(
    ScriptOpenWay? Way,
    OutputSeverity Severity,
    string Message,
    IReadOnlyList<string> Failures)
{
    /// <summary>Whether the file is open in a program that edits it.</summary>
    public bool IsOpenInEditor => Way is ScriptOpenWay.VisualStudioCode or ScriptOpenWay.DefaultProgram;
}
