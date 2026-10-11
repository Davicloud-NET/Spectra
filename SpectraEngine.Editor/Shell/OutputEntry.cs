using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SpectraEngine.Core.ConsoleSystem;
using System;
using System.Collections.Generic;

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

    /// <summary>The file and the line it came from, or null for a line with no place.</summary>
    public ConsolePlace? Place { get; internal init; }

    /// <summary>Whether a click on the line can go somewhere.</summary>
    public bool HasPlace => Place is not null;

    /// <summary>The file's name and the line, as <c>door.luau:12</c>. Empty with no place.</summary>
    public string PlaceLabel => Place is { } place ? OutputPlaceText.Short(place) : string.Empty;

    /// <summary>What a click on the line does, with the whole path. Null with no place.</summary>
    public string? PlaceTip => Place is { } place ? OutputPlaceText.Tip(place) : null;

    /// <summary>The calls under an error, or null for a line with none.</summary>
    public OutputStack? Stack { get; internal init; }

    // What shows a place when somebody asks for this line's.
    internal Action<ConsolePlace>? Opener { get; init; }

    /// <summary>Asks for the place to be shown. Does nothing for a line with no place.</summary>
    public void Open()
    {
        if (Place is { } place)
            Opener?.Invoke(place);
    }

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

    // Whether another line says the same thing from the same place, by the
    // same way there. Only then do the two fold into a count.
    internal bool IsSame(
        OutputSeverity severity, string text, ConsolePlace? place, IReadOnlyList<ConsolePlace> calls) =>
        Severity == severity
        && string.Equals(Text, text, StringComparison.Ordinal)
        && Place == place
        && (Stack is null ? calls.Count == 0 : Stack.IsSame(calls));

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
