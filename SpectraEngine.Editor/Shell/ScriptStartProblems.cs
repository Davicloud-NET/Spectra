using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Hosting;
using System;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Keeps the problems about scripts that did not start: a file that is not
/// there, or one that does not compile. A row selects the node the script is
/// on, and goes when the level is played again and the script starts.
/// </summary>
// The engine says it once, as a console line that names the node. So every
// snapshot has to come here, in the order they were published.
public sealed class ScriptStartProblems
{
    /// <summary>What the rows are grouped by.</summary>
    public const string Template = "A script did not start";

    private readonly ProblemList _problems;
    private bool _wasPlaying;

    /// <summary>Creates the feed over the list it writes to.</summary>
    public ScriptStartProblems(ProblemList problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        _problems = problems;
    }

    /// <summary>Forgets whether a level was playing. A new engine starts stopped.</summary>
    public void Reset() => _wasPlaying = false;

    /// <summary>Takes what one snapshot says about scripts that did not start.</summary>
    public void Apply(FrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // A level that starts says again which of its scripts did not. What
        // was fixed since the last time is not said, and so its row goes.
        if (snapshot.IsPlaying && !_wasPlaying)
            _problems.Replace(OutputSeverity.Error, Template, ProblemScope.Map, []);

        _wasPlaying = snapshot.IsPlaying;

        for (int i = 0; i < snapshot.ConsoleLines.Count; i++)
        {
            ConsoleLine line = snapshot.ConsoleLines[i];
            if (line.ProblemNode == Guid.Empty)
                continue;

            // One row for a file: the same mistake on ten nodes is one thing
            // to fix. A script with no file is told apart by its sentence.
            _problems.Report(
                OutputSeverity.Error, Template, line.PlainText, line.Place?.File ?? line.Text,
                ProblemScope.Map, line.ProblemNode);
        }
    }
}
