using System;
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

    private int _errorCount;
    private int _warningCount;

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
    // "is anything wrong".
    public string HistoryLabel => $"Shell output, {Entries.Count} of {Capacity} lines";

    /// <summary>Raised after an entry is appended, so a view can scroll to it.</summary>
    public event Action<OutputEntry>? Appended;

    /// <summary>Appends one line.</summary>
    public void Append(OutputSeverity severity, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        string time = DateTime.Now.ToString("HH:mm:ss");

        // A repeat of the last line grows its count. Last entry only:
        // two alternating lines are two conditions.
        if (Entries.Count > 0)
        {
            OutputEntry last = Entries[^1];
            if (last.Severity == severity && string.Equals(last.Text, text, StringComparison.Ordinal))
            {
                last.Repeat(time);
                Appended?.Invoke(last);
                return;
            }
        }

        var entry = new OutputEntry(severity, text, time);

        while (Entries.Count >= Capacity)
        {
            OutputEntry dropped = Entries[0];
            Entries.RemoveAt(0);

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
