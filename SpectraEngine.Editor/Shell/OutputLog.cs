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
/// <remarks>
/// <b>A class rather than a record now, because a repeat updates in place.</b>
/// A background compile can report the same line every frame, and five hundred
/// identical rows push everything else out of a bounded history while telling
/// the reader nothing the first one did not.
/// </remarks>
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

    /// <summary>
    /// The colour this line is set in, from the token dictionary.
    /// </summary>
    /// <remarks>
    /// <b>An error is TextDanger, not the accent.</b> The accent as text is
    /// 4.0:1 on the panel, which is not an error message - the split exists in
    /// the palette precisely so this row can be red and legible at the same
    /// time. A command echo is muted, because the reply beneath it is the part
    /// worth reading; the user already knows what they typed.
    /// </remarks>
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
/// The editor's diagnostic history: everything that used to be a single
/// status-bar sentence.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole diagnostic surface of this application was one line of text that
/// anything could overwrite.</b> About thirty call sites wrote to it, none of
/// them knew what was already there, and a failure reported while the user was
/// looking elsewhere was gone by the time they looked back - which for a save
/// failure or a content error is the difference between a problem and a lost
/// afternoon. The status line still exists and still shows the newest entry;
/// what changed is that the entry before it survives.
/// </para>
/// <para>
/// <b>Bounded, like every other queue in this shell, and for the same reason.</b>
/// A background compile can report a content warning per frame, and an unbounded
/// list is a memory leak with a scrollbar. The oldest lines go first, which is
/// the right end to lose: the newest failure is the one being investigated.
/// </para>
/// <para>UI thread only.</para>
/// </remarks>
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
    /// <remarks>
    /// <b>It used to answer "are there problems", and it could not.</b> This log
    /// is bounded, so its error count falls back to zero as failures scroll out
    /// of the buffer: enough chatter after a broken material and the header said
    /// "no problems" over a level that was still broken. That question belongs
    /// to <see cref="ProblemList"/>, which keeps standing conditions rather than
    /// recent lines. This header says what it can actually see.
    /// </remarks>
    public string HistoryLabel => $"Shell output, {Entries.Count} of {Capacity} lines";

    /// <summary>Raised after an entry is appended, so a view can scroll to it.</summary>
    public event Action<OutputEntry>? Appended;

    /// <summary>Appends one line.</summary>
    public void Append(OutputSeverity severity, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Wall-clock rather than a frame number: the reader is a person
        // correlating this against something they just did.
        string time = DateTime.Now.ToString("HH:mm:ss");

        // A repeat of the line already at the bottom grows a count instead of a
        // row. Compared against the LAST entry only: two lines alternating are
        // two conditions and still get two rows, which is stated here because
        // the cheaper-looking "search the whole log" would merge them.
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
