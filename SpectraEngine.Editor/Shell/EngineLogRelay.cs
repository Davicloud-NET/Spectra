using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace SpectraEngine.Editor.Shell;

/// <summary>One engine log line on its way to the shell.</summary>
/// <param name="Template">The unrendered message template. Problems are grouped by it.</param>
/// <param name="Subject">The asset path or file the line is about, or empty.</param>
/// <param name="IsResolution">Whether this line says an earlier failure is over.</param>
public readonly record struct EngineLogLine(
    OutputSeverity Severity,
    string Template,
    string Message,
    string Subject,
    DateTime Timestamp,
    bool IsResolution);

/// <summary>
/// Serilog sink that carries the engine's warnings and errors into the shell.
/// </summary>
// Lines queue on the writing thread and one drain is posted per burst.
// The poster is an Action so this needs no dispatcher in tests.
public sealed class EngineLogRelay : ILogEventSink
{
    /// <summary>How many lines may queue before new ones are dropped.</summary>
    public const int Capacity = 1024;

    private readonly ConcurrentQueue<EngineLogLine> _queue = new();
    private Action<Action>? _post;
    private int _drainScheduled;
    private int _dropped;

    // Property names a subject is read from, in order.
    private static readonly string[] SubjectProperties =
        ["Path", "Texture", "Material", "Model", "Shader", "File", "Bundle"];

    // Templates that end an earlier failure. Matched whole: a line that only
    // mentions a path must not clear a problem.
    private static readonly string[] ResolutionTemplates =
    [
        "Loaded texture {Path} after an earlier failure ({Description})",
        "Texture {Verb} {Path} ({Description})",
    ];

    /// <summary>Lines lost to overflow since construction.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>Raised once per line, in order, on the poster's thread.</summary>
    public event Action<EngineLogLine>? LineArrived;

    /// <summary>Raised when a drain found lines had been dropped, with how many.</summary>
    public event Action<int>? LinesDropped;

    /// <summary>
    /// Starts delivery. Anything logged before this queues and arrives now.
    /// </summary>
    /// <param name="post">Schedules work on the thread that owns the shell's collections.</param>
    public void Attach(Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        _post = post;

        // Startup lines logged before the window existed.
        if (!_queue.IsEmpty) ScheduleDrain();
    }

    /// <summary>Stops delivery. Lines logged afterwards queue.</summary>
    public void Detach() => _post = null;

    /// <summary>Serilog's entry point. Any thread.</summary>
    public void Emit(LogEvent logEvent)
    {
        if (logEvent is null) return;

        bool resolution = IsResolution(logEvent.MessageTemplate.Text);
        if (logEvent.Level < LogEventLevel.Warning && !resolution)
            return;

        if (_queue.Count >= Capacity)
        {
            // Drop the newest: the queue holds the start of whatever went wrong.
            Interlocked.Increment(ref _dropped);
            return;
        }

        _queue.Enqueue(new EngineLogLine(
            SeverityOf(logEvent.Level),
            logEvent.MessageTemplate.Text,
            Render(logEvent),
            SubjectOf(logEvent),
            logEvent.Timestamp.LocalDateTime,
            resolution));

        ScheduleDrain();
    }

    /// <summary>Whether a template says an earlier failure is over.</summary>
    public static bool IsResolution(string template)
    {
        foreach (string known in ResolutionTemplates)
        {
            if (string.Equals(template, known, StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>The path or file a line is about, or empty.</summary>
    public static string SubjectOf(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        foreach (string name in SubjectProperties)
        {
            if (logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? value) &&
                value is ScalarValue { Value: string text } &&
                text.Length > 0)
            {
                return text;
            }
        }

        return string.Empty;
    }

    // Runs on the poster's thread.
    internal void Drain()
    {
        // Clear first, so a line enqueued during the drain schedules another.
        Volatile.Write(ref _drainScheduled, 0);

        int dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0) LinesDropped?.Invoke(dropped);

        while (_queue.TryDequeue(out EngineLogLine line))
            LineArrived?.Invoke(line);
    }

    private void ScheduleDrain()
    {
        Action<Action>? post = _post;
        if (post is null) return;

        // One outstanding drain at a time.
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 1) return;

        post(Drain);
    }

    private static OutputSeverity SeverityOf(LogEventLevel level) => level switch
    {
        LogEventLevel.Fatal or LogEventLevel.Error => OutputSeverity.Error,
        LogEventLevel.Warning => OutputSeverity.Warning,
        _ => OutputSeverity.Info,
    };

    private static string Render(LogEvent logEvent)
    {
        string message = logEvent.RenderMessage(CultureInfo.InvariantCulture);
        if (logEvent.Exception is null) return message;

        // No stack: a panel row is one line, and the log file has it.
        return $"{message}: {logEvent.Exception.GetType().Name}: {logEvent.Exception.Message}";
    }
}
