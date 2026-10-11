using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Hosting;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// Puts what the engine's console printed into the output, and says so when
/// lines went missing on the way. UI thread only.
/// </summary>
// Console lines are sent once. A snapshot the shell dropped, or a burst the
// engine could not hold, leaves only a jump in the line numbers.
public sealed class ConsoleLineFeed
{
    private long _next;

    /// <summary>Starts the count again. Line numbers restart with each engine.</summary>
    public void Reset() => _next = 0;

    /// <summary>
    /// Appends one snapshot's lines to <paramref name="output"/>, in order.
    /// Says how many the engine wrote and never sent, and how many of these
    /// the output could not keep.
    /// </summary>
    public void Feed(FrameSnapshot snapshot, OutputLog output)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(output);

        int rowsBefore = output.Entries.Count;
        long fallenBefore = output.FallenOff;

        Feed(snapshot.ConsoleLines, output);

        // Lines dropped at the end of a burst have no later line to show the
        // jump. The engine's own count does.
        SayMissing(snapshot.ConsoleLinesWritten - _next, output);
        _next = Math.Max(_next, snapshot.ConsoleLinesWritten);

        // More came than the output holds, so some went by unread.
        long unread = output.FallenOff - fallenBefore - rowsBefore;
        if (unread > 0)
        {
            output.Append(
                OutputSeverity.Warning,
                $"{unread} line(s) came and went before they could be read: the output keeps {OutputLog.Capacity}. " +
                "The run log has them all.");
        }
    }

    /// <summary>Appends lines to <paramref name="output"/>, in order.</summary>
    public void Feed(IReadOnlyList<ConsoleLine> lines, OutputLog output)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            ConsoleLine line = lines[i];

            SayMissing(line.Sequence - _next, output);
            output.Append(SeverityOf(line.Severity), line.Text, line.Place, line.Stack);
            _next = line.Sequence + 1;
        }
    }

    private static void SayMissing(long missing, OutputLog output)
    {
        if (missing <= 0)
            return;

        output.Append(
            OutputSeverity.Warning,
            $"{missing} console line(s) are missing here: more were printed than the editor kept up with.");
    }

    private static OutputSeverity SeverityOf(LogLevel level) => level switch
    {
        >= LogLevel.Error => OutputSeverity.Error,
        LogLevel.Warning => OutputSeverity.Warning,
        _ => OutputSeverity.Info,
    };
}
