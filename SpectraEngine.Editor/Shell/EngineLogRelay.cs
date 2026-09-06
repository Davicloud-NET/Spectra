using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace SpectraEngine.Editor.Shell;

/// <summary>One engine log line on its way to the shell.</summary>
/// <param name="Severity">How loud it is, in the shell's own vocabulary.</param>
/// <param name="Template">
/// The message template, before its holes were filled. This is what makes two
/// reports of the same condition about two different files one problem each and
/// two reports about the same file one problem with a count.
/// </param>
/// <param name="Message">The rendered text.</param>
/// <param name="Subject">The asset path or file the line is about, or empty.</param>
/// <param name="Timestamp">When the engine wrote it.</param>
/// <param name="IsResolution">Whether this line says an earlier failure is over.</param>
public readonly record struct EngineLogLine(
    OutputSeverity Severity,
    string Template,
    string Message,
    string Subject,
    DateTime Timestamp,
    bool IsResolution);

/// <summary>
/// Carries the engine's own log lines into the shell, so a warning the render
/// thread writes reaches the window rather than only a file.
/// </summary>
/// <remarks>
/// <para>
/// <b>The editor's whole diagnostic surface used to be what the shell itself
/// wrote.</b> Serilog went to the console, the debugger and a rolling file;
/// nothing carried an engine line into the window. So a texture that would not
/// decode, a material naming a file that is not there, a map node that lost its
/// mesh - every one of them reported into a log the person using the editor was
/// not reading, and the editor's own panel said "no problems" while it happened.
/// </para>
/// <para>
/// <b>One hop per burst, and that is the design.</b> A background compile can
/// warn once per frame; posting per line would put hundreds of dispatcher jobs
/// behind the frame that produced them. Lines queue on whatever thread wrote
/// them and exactly one drain is scheduled, which is the same coalescing the
/// snapshot pump uses.
/// </para>
/// <para>
/// <b>It names no UI type at all.</b> The poster arrives as an
/// <see cref="Action{T}"/>, so this class is testable without a dispatcher and
/// the window decides what "on the UI thread" means.
/// </para>
/// </remarks>
public sealed class EngineLogRelay : ILogEventSink
{
    /// <summary>How many lines may queue before the oldest are dropped.</summary>
    public const int Capacity = 1024;

    private readonly ConcurrentQueue<EngineLogLine> _queue = new();
    private Action<Action>? _post;
    private int _drainScheduled;
    private int _dropped;

    /// <summary>
    /// Property names a subject is read from, in order. Hand-written rather than
    /// discovered, because a scan of every property would find an id or a count
    /// and call it a file name.
    /// </summary>
    private static readonly string[] SubjectProperties =
        ["Path", "Texture", "Material", "Model", "Shader", "File", "Bundle"];

    /// <summary>
    /// Templates that mean an earlier failure is OVER. Matched whole rather than
    /// by keyword: a line that merely mentions a path must not clear a problem.
    /// </summary>
    private static readonly string[] ResolutionTemplates =
    [
        "Loaded texture {Path} after an earlier failure ({Description})",
        "Texture {Verb} {Path} ({Description})",
    ];

    /// <summary>Lines lost to overflow since construction.</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>Raised once per line, in order, on the poster's thread.</summary>
    public event Action<EngineLogLine>? LineArrived;

    /// <summary>
    /// Raised when a drain found lines had been dropped, with how many.
    /// </summary>
    /// <remarks>
    /// Reported rather than swallowed: a diagnostic channel that silently loses
    /// its worst moment is the failure this class exists to fix.
    /// </remarks>
    public event Action<int>? LinesDropped;

    /// <summary>
    /// Starts delivery. Anything logged before this queues and arrives now.
    /// </summary>
    /// <param name="post">
    /// Schedules work on the thread that owns the shell's collections.
    /// </param>
    public void Attach(Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        _post = post;

        // Startup logs before the window exists - the content root, the entity
        // catalogue, a pack that would not mount - and those are exactly the
        // lines somebody needs when a session opens wrong.
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
            // The NEWEST is dropped, not the oldest: the queue already holds the
            // start of whatever went wrong, and that is the part worth keeping.
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

    /// <summary>Delivers everything queued. The poster's thread.</summary>
    internal void Drain()
    {
        // Cleared FIRST, so a line enqueued while this drains schedules another
        // hop rather than being left sitting in the queue until the next one.
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

        // One outstanding drain at a time: a compile warning per frame would
        // otherwise queue one dispatcher job per frame behind the frame.
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

        // The exception type and message, never the stack: the file has the
        // stack, and a panel row is one line.
        return $"{message}: {logEvent.Exception.GetType().Name}: {logEvent.Exception.Message}";
    }
}
