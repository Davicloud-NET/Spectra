using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>
/// Holds the lines a console wrote until something takes them. Render thread only.
/// </summary>
public sealed class ConsoleOutput : IConsoleOutput
{
    /// <summary>
    /// How many lines are kept between two drains. A later one is dropped,
    /// unless it is an error.
    /// </summary>
    public const int Capacity = 1024;

    /// <summary>
    /// How many errors are kept past <see cref="Capacity"/>, so that a flood
    /// of other lines does not take the error that comes after it.
    /// </summary>
    public const int ErrorRoom = 64;

    private List<ConsoleLine> _lines = [];
    private long _nextSequence;

    /// <summary>How many lines are waiting.</summary>
    public int Count => _lines.Count;

    /// <summary>
    /// How many lines were written since this console was made, the dropped
    /// ones too. It is the number the next line gets.
    /// </summary>
    public long Written => _nextSequence;

    /// <inheritdoc/>
    public void Print(string text) => Write(new ConsoleLine(0, LogLevel.Information, text));

    /// <inheritdoc/>
    public void Warn(string text) => Write(new ConsoleLine(0, LogLevel.Warning, text));

    /// <inheritdoc/>
    public void Error(string text) => Write(new ConsoleLine(0, LogLevel.Error, text));

    /// <inheritdoc/>
    public void Write(in ConsoleLine line)
    {
        ArgumentNullException.ThrowIfNull(line.Text);

        // A dropped line still takes a number. The gap is how a reader learns
        // that lines are missing.
        long sequence = _nextSequence++;
        int room = line.Severity >= LogLevel.Error ? Capacity + ErrorRoom : Capacity;
        if (_lines.Count >= room)
            return;

        _lines.Add(line with { Sequence = sequence });
    }

    /// <summary>Hands the waiting lines to the caller and starts a fresh batch.</summary>
    public IReadOnlyList<ConsoleLine> Drain()
    {
        if (_lines.Count == 0)
            return Array.Empty<ConsoleLine>();

        // Hand the list over and allocate a new one: reusing it would change
        // a snapshot's contents under a UI thread still reading it.
        List<ConsoleLine> batch = _lines;
        _lines = new List<ConsoleLine>(batch.Count);
        return batch;
    }
}
