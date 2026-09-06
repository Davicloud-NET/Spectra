using Avalonia;
using Serilog;
using SpectraEngine.Entities;
using System;

namespace SpectraEngine.Editor;

/// <summary>The editor shell's entry point.</summary>
internal static class Program
{
    /// <summary>
    /// The graphics backend the viewport runs on, from the command line.
    /// Read by <see cref="MainWindow"/> when it starts the session.
    /// </summary>
    internal static string[] StartupArgs { get; private set; } = [];

    /// <summary>
    /// Carries engine log lines into the window. Created before the app, like
    /// <see cref="StartupArgs"/>, because the logger is configured before a
    /// window exists and the lines written in between are exactly the ones
    /// somebody needs when a session opens wrong.
    /// </summary>
    internal static Shell.EngineLogRelay LogRelay { get; } = new();

    // STAThread because Windows requires it of any thread that owns windows and
    // touches the shell APIs (drag and drop, file dialogs) an editor will grow.
    [STAThread]
    public static int Main(string[] args)
    {
        StartupArgs = args;

        // The relay is a FOURTH sink, beside the three that leave the process.
        // Without it the engine's own warnings reached the console, the debugger
        // and a file, and nothing carried them into the window: a texture that
        // would not decode reported into a log the person using the editor was
        // not reading, and the editor's panel said nothing had gone wrong.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File("logs/spectra-editor-.log", rollingInterval: RollingInterval.Day)
            .WriteTo.Sink(LogRelay)
            .CreateLogger();

        // Loads the built-in entity assembly, whose generated module initializers
        // are what put logic_relay, logic_timer and math_counter in the catalogue.
        // Nothing here statically calls into it - a level names those classes as
        // text - so without the anchor a trimmed publish drops the assembly and
        // every map naming them opens with placeholders that behave as nothing.
        // Before any session exists, because the first read freezes the catalogue.
        BuiltinEntities.EnsureRegistered();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "The editor terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>The app builder, also used by the XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Embedded rather than a system face: the shell's type scale is
            // tuned at 11, 12 and 13px, where Segoe UI's hinting and Inter's
            // are noticeably different, and a tool that renders differently on
            // two machines is a tool nobody can tune.
            .WithInterFont()
            .LogToTrace();
}
