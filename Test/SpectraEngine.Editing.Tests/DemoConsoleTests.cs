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
    public void A_line_from_a_script_is_logged_with_its_file_and_line_in_front_of_the_text()
    {
        var host = new EngineHost(NullLogger.Instance);
        var log = new RecordingLogger();
        DemoConsole.Attach(host, [], log);

        ConsoleLine[] printed =
        [
            new(0, LogLevel.Information, "Spinner turns on a script") { Place = new ConsolePlace("Scripts/spinner.luau", 5) },
            new(1, LogLevel.Warning, "careful") { Place = new ConsolePlace("Scripts/spinner.luau", 9) },
        ];
        host.PublishFrame(TimeSpan.Zero, _ => new FrameSnapshot { ConsoleLines = printed });

        log.Entries.ShouldBe(
        [
            (LogLevel.Information, "[console] Scripts/spinner.luau:5: Spinner turns on a script"),
            (LogLevel.Information, "[console] warning: Scripts/spinner.luau:9: careful"),
        ]);
    }

    [Fact]
    public void An_error_from_a_script_is_logged_with_a_line_for_each_caller_under_it()
    {
        var host = new EngineHost(NullLogger.Instance);
        var log = new RecordingLogger();
        DemoConsole.Attach(host, [], log);

        ConsoleLine[] printed =
        [
            new(0, LogLevel.Error, "it broke")
            {
                Place = new ConsolePlace("Scripts/door.luau", 12),
                Stack = [new ConsolePlace("Scripts/door.luau", 30, "press"), new ConsolePlace("Scripts/lobby.luau", 4)],
            },
        ];
        host.PublishFrame(TimeSpan.Zero, _ => new FrameSnapshot { ConsoleLines = printed });

        log.Entries.ShouldBe(
        [
            (LogLevel.Information, "[console] error: Scripts/door.luau:12: it broke"),
            (LogLevel.Information, "[console] error:     Scripts/door.luau:30 in press"),
            (LogLevel.Information, "[console] error:     Scripts/lobby.luau:4"),
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
