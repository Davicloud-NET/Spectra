using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>
/// Holds the lines a console wrote until something takes them. Render thread only.
/// </summary>
public sealed class ConsoleOutput : IConsoleOutput
{
    /// <summary>How many lines are kept between two drains. Later ones are dropped.</summary>
    public const int Capacity = 1024;

    private List<ConsoleLine> _lines = [];
    private long _nextSequence;

    /// <summary>How many lines are waiting.</summary>
    public int Count => _lines.Count;

    /// <inheritdoc/>
    public void Print(string text) => Add(LogLevel.Information, text);

    /// <inheritdoc/>
    public void Warn(string text) => Add(LogLevel.Warning, text);

    /// <inheritdoc/>
    public void Error(string text) => Add(LogLevel.Error, text);

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

    private void Add(LogLevel severity, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // A dropped line still takes a number. The gap is how a reader learns
        // that lines are missing.
        long sequence = _nextSequence++;
        if (_lines.Count >= Capacity)
            return;

        _lines.Add(new ConsoleLine(sequence, severity, text));
    }
}
