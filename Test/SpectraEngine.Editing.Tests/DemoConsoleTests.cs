using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Executable;

namespace SpectraEngine.Editing.Tests;

/// <summary>The demo's console front end: startup lines in, printed lines to the log.</summary>
public sealed class DemoConsoleTests
{
    [Fact]
    public void Startup_lines_are_submitted_in_the_order_they_were_given()
    {
        var host = new EngineHost(NullLogger.Instance);

        DemoConsole.Attach(host, ["ent_watch on; wait; ent_list", "echo done"], NullLogger.Instance);

        host.TryTakeConsoleLine(out string? line).ShouldBeTrue();
        line.ShouldBe("ent_watch on; wait; ent_list");
        host.TryTakeConsoleLine(out line).ShouldBeTrue();
        line.ShouldBe("echo done");
        host.TryTakeConsoleLine(out _).ShouldBeFalse();
    }

    [Fact]
    public void What_the_console_prints_is_logged_as_information_with_its_severity_in_the_text()
    {
        var host = new EngineHost(NullLogger.Instance);
        var log = new RecordingLogger();
        DemoConsole.Attach(host, [], log);

        ConsoleLine[] printed =
        [
            new(0, LogLevel.Information, "ent 12 send relay.OnTrigger -> door.Open"),
            new(1, LogLevel.Warning, "a quote was not closed"),
            new(2, LogLevel.Error, "no command named 'noclip'. Type help."),
        ];
        host.PublishFrame(TimeSpan.Zero, _ => new FrameSnapshot { ConsoleLines = printed });

        log.Entries.ShouldBe(
        [
            (LogLevel.Information, "[console] ent 12 send relay.OnTrigger -> door.Open"),
            (LogLevel.Information, "[console] warning: a quote was not closed"),
            (LogLevel.Information, "[console] error: no command named 'noclip'. Type help."),
        ]);
    }

    [Fact]
    public void A_frame_that_printed_nothing_logs_nothing()
    {
        var host = new EngineHost(NullLogger.Instance);
        var log = new RecordingLogger();
        DemoConsole.Attach(host, [], log);

        host.PublishFrame(TimeSpan.Zero, _ => new FrameSnapshot());

        log.Entries.ShouldBeEmpty();
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public bool IsEnabled(LogLevel logLevel) => true;

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
