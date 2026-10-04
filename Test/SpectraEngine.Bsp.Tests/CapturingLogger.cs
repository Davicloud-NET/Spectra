using Microsoft.Extensions.Logging;

namespace SpectraEngine.Bsp.Tests;

// Records every formatted message so tests can assert on logging.
// Locked: code under test also logs from the thread pool.
internal sealed class CapturingLogger : ILogger
{
    private readonly object _lock = new();
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<string> MessagesAt(LogLevel level)
    {
        lock (_lock)
            return _entries.Where(e => e.Level == level).Select(e => e.Message).ToArray();
    }

    public string Describe()
    {
        lock (_lock)
            return _entries.Count == 0
                ? "(no log entries)"
                : string.Join(Environment.NewLine, _entries.Select(e => $"[{e.Level}] {e.Message}"));
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_lock)
            _entries.Add((logLevel, formatter(state, exception)));
    }
}
