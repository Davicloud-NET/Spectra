using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Hosting;
using System;
using System.Collections.Generic;
using System.Globalization;

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

    // What the first node said about each file, as its row was made.
    private readonly Dictionary<string, string> _firstSaid = new(StringComparer.Ordinal);
    private bool _wasPlaying;

    /// <summary>Creates the feed over the list it writes to.</summary>
    public ScriptStartProblems(ProblemList problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        _problems = problems;
    }

    /// <summary>Forgets whether a level was playing. A new engine starts stopped.</summary>
    public void Reset() => _wasPlaying = false;

    /// <summary>
    /// What has to arrive of a snapshot the shell has no room for: its lines
    /// about scripts that did not start, in a snapshot that holds nothing
    /// else. Null when it has none. Safe on any thread.
    /// </summary>
    public static FrameSnapshot? KeptOf(FrameSnapshot dropped)
    {
        ArgumentNullException.ThrowIfNull(dropped);

        List<ConsoleLine>? kept = null;
        for (int i = 0; i < dropped.ConsoleLines.Count; i++)
        {
            if (dropped.ConsoleLines[i].ProblemNode != Guid.Empty)
                (kept ??= []).Add(dropped.ConsoleLines[i]);
        }

        if (kept is null)
            return null;

        return new FrameSnapshot
        {
            FrameNumber = dropped.FrameNumber,
            IsPlaying = dropped.IsPlaying,
            ConsoleLines = kept,
            ConsoleLinesWritten = dropped.ConsoleLinesWritten,
        };
    }

    /// <summary>Takes what one snapshot says about scripts that did not start.</summary>
    public void Apply(FrameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // A level that starts says again which of its scripts did not. What
        // was fixed since the last time is not said, and so its row goes.
        if (snapshot.IsPlaying && !_wasPlaying)
        {
            _problems.Replace(OutputSeverity.Error, Template, ProblemScope.Map, []);
            _firstSaid.Clear();
        }

        _wasPlaying = snapshot.IsPlaying;

        for (int i = 0; i < snapshot.ConsoleLines.Count; i++)
        {
            ConsoleLine line = snapshot.ConsoleLines[i];
            if (line.ProblemNode == Guid.Empty)
                continue;

            // One row for a file: the same mistake on ten nodes is one thing
            // to fix. A script with no file is told apart by its sentence.
            string subject = line.Place?.File ?? line.Text;
            _problems.Report(
                OutputSeverity.Error, Template, WordsFor(subject, line.PlainText), subject,
                ProblemScope.Map, line.ProblemNode);
        }
    }

    // A row selects the first node that said it, so it keeps that node's
    // sentence. The nodes that follow are counted after it.
    private string WordsFor(string subject, string said)
    {
        if (_problems.Find(OutputSeverity.Error, Template, subject) is not { } standing)
        {
            _firstSaid[subject] = said;
            return said;
        }

        string first = _firstSaid.GetValueOrDefault(subject, standing.Message);
        return standing.Count == 1
            ? $"{first} The same goes for 1 more node."
            : $"{first} The same goes for {standing.Count.ToString(CultureInfo.InvariantCulture)} more nodes.";
    }
}
