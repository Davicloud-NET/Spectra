using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SpectraEngine.Editor.Shell;

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
