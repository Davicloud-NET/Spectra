using SpectraEngine.Core.ConsoleSystem;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The editor's diagnostic history. Bounded, oldest lines dropped first.
/// UI thread only.
/// </summary>
public sealed class OutputLog : ObservableObject
{
    /// <summary>How many lines are kept.</summary>
    public const int Capacity = 500;

    // One delegate for every line with a place, not one for each.
    private readonly Action<ConsolePlace> _requestPlace;
    private int _errorCount;
    private int _warningCount;

    /// <summary>An empty log.</summary>
    public OutputLog() => _requestPlace = place => PlaceRequested?.Invoke(place);

    /// <summary>The lines, oldest first.</summary>
    public ObservableCollection<OutputEntry> Entries { get; } = [];

    /// <summary>How many errors are currently in the log.</summary>
    public int ErrorCount
    {
        get => _errorCount;
        private set
        {
            Set(ref _errorCount, value);
        }
    }

    /// <summary>How many warnings are currently in the log.</summary>
    public int WarningCount
    {
        get => _warningCount;
        private set
        {
            Set(ref _warningCount, value);
        }
    }

    /// <summary>What this panel is, and how full it is.</summary>
    // Not a verdict: errors scroll out of a bounded log. ProblemList answers
    // "is anything wrong". An error with calls under it is one line.
    public string HistoryLabel => $"Shell output, {Entries.Count} of {Capacity} lines";

    /// <summary>How many lines have fallen off the top since the log was made.</summary>
    public long FallenOff { get; private set; }

    /// <summary>Raised after an entry is appended, so a view can scroll to it.</summary>
    public event Action<OutputEntry>? Appended;

    /// <summary>
    /// Raised when somebody asks to see the place a line or a call under it
    /// names. Whoever opens scripts listens.
    /// </summary>
    public event Action<ConsolePlace>? PlaceRequested;

    /// <summary>Appends one line.</summary>
    public void Append(OutputSeverity severity, string text) =>
        Append(severity, text, null, Array.Empty<ConsolePlace>());

    /// <summary>
    /// Appends one line that came from a place in a file, with the calls
    /// under it when it is an error.
    /// </summary>
    public void Append(
        OutputSeverity severity, string text, ConsolePlace? place, IReadOnlyList<ConsolePlace> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        if (string.IsNullOrWhiteSpace(text))
            return;

        string time = DateTime.Now.ToString("HH:mm:ss");

        // A repeat of the last line grows its count. Last entry only:
        // two alternating lines are two conditions.
        if (Entries.Count > 0 && Entries[^1] is { } last && last.IsSame(severity, text, place, calls))
        {
            last.Repeat(time);
            Appended?.Invoke(last);
            return;
        }

        OutputEntry entry = place is null && calls.Count == 0
            ? new OutputEntry(severity, text, time)
            : new OutputEntry(severity, text, time, place, calls, _requestPlace);

        while (Entries.Count >= Capacity)
        {
            OutputEntry dropped = Entries[0];
            Entries.RemoveAt(0);
            FallenOff++;

            if (dropped.Severity == OutputSeverity.Error)
                ErrorCount--;
            else if (dropped.Severity == OutputSeverity.Warning)
                WarningCount--;
        }

        Entries.Add(entry);
        Raise(nameof(HistoryLabel));

        if (severity == OutputSeverity.Error)
            ErrorCount++;
        else if (severity == OutputSeverity.Warning)
            WarningCount++;

        Appended?.Invoke(entry);
    }

    /// <summary>Empties the log.</summary>
    public void Clear()
    {
        Entries.Clear();
        ErrorCount = 0;
        WarningCount = 0;
        Raise(nameof(HistoryLabel));
    }
}
