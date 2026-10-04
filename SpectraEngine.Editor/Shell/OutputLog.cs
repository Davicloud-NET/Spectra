using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.ObjectModel;

namespace SpectraEngine.Editor.Shell;

/// <summary>How loud one line in the output is.</summary>
public enum OutputSeverity
{
    /// <summary>Something happened and it worked.</summary>
    Info,

    /// <summary>Something is not right but the editor carried on.</summary>
    Warning,

    /// <summary>Something the user asked for did not happen.</summary>
    Error,

    /// <summary>A line the user typed into the console.</summary>
    Command,
}

/// <summary>One line in the output, and how many times it has been said.</summary>
public sealed class OutputEntry : ObservableObject
{
    private int _count = 1;
    private string _timeLabel;

    internal OutputEntry(OutputSeverity severity, string text, string timeLabel)
    {
        Severity = severity;
        Text = text;
        _timeLabel = timeLabel;
    }

    /// <summary>How loud it is.</summary>
    public OutputSeverity Severity { get; }

    /// <summary>What happened.</summary>
    public string Text { get; }

    /// <summary>When it was last said, as <c>HH:mm:ss</c>.</summary>
    public string TimeLabel
    {
        get => _timeLabel;
        private set => Set(ref _timeLabel, value);
    }

    /// <summary>How many times this line has been said in a row.</summary>
    public int Count
    {
        get => _count;
        private set
        {
            if (Set(ref _count, value)) Raise(nameof(CountLabel));
        }
    }

    /// <summary>"x12" once it has repeated, else empty.</summary>
    public string CountLabel =>
        _count > 1 ? "x" + _count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    internal void Repeat(string timeLabel)
    {
        TimeLabel = timeLabel;
        Count++;
    }

    /// <summary>Whether this line should carry the error colour.</summary>
    public bool IsError => Severity == OutputSeverity.Error;

    /// <summary>Whether this line should carry the warning colour.</summary>
    public bool IsWarning => Severity == OutputSeverity.Warning;

    /// <summary>Whether this line is something the user typed.</summary>
    public bool IsCommand => Severity == OutputSeverity.Command;

    /// <summary>The colour this line is set in, from the token dictionary.</summary>
    // Errors use TextDanger: the accent as text is only 4.0:1 on the panel.
    public IBrush? SeverityBrush => Resource(Severity switch
    {
        OutputSeverity.Error => "SpectraTextDanger",
        OutputSeverity.Warning => "SpectraMode",
        OutputSeverity.Command => "SpectraTextMuted",
        _ => "SpectraTextBody",
    });

    private static IBrush? Resource(string key)
        => Application.Current?.TryFindResource(key, out object? value) == true ? value as IBrush : null;
}

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
