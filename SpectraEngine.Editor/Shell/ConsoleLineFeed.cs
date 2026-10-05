using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
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

    /// <summary>Appends one snapshot's lines to <paramref name="output"/>, in order.</summary>
    public void Feed(IReadOnlyList<ConsoleLine> lines, OutputLog output)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            ConsoleLine line = lines[i];

            long missing = line.Sequence - _next;
            if (missing > 0)
            {
                output.Append(
                    OutputSeverity.Warning,
                    $"{missing} console line(s) are missing here: more were printed than the editor kept up with.");
            }

            output.Append(SeverityOf(line.Severity), line.Text);
            _next = line.Sequence + 1;
        }
    }

    private static OutputSeverity SeverityOf(LogLevel level) => level switch
    {
        >= LogLevel.Error => OutputSeverity.Error,
        LogLevel.Warning => OutputSeverity.Warning,
        _ => OutputSeverity.Info,
    };
}
