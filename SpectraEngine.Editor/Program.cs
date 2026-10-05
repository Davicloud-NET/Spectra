using Avalonia;
using Avalonia.Media;
using Serilog;
using SpectraEngine.Entities;
using System;
using System.IO;

namespace SpectraEngine.Editor;

internal static class Program
{
    internal static string[] StartupArgs { get; private set; } = [];

    // Created before the app so lines logged before a window exists are queued.
    internal static Shell.EngineLogRelay LogRelay { get; } = new();

    // Where the run log is written, for a message that points at it.
    internal static string LogFolder { get; } = Path.GetFullPath("logs");

    [STAThread]
    public static int Main(string[] args)
    {
        StartupArgs = args;

        // The relay sink carries engine warnings into the window.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File(Path.Combine(LogFolder, "spectra-editor-.log"), rollingInterval: RollingInterval.Day)
            .WriteTo.Sink(LogRelay)
            .CreateLogger();

        // Nothing calls into the built-in entity assembly statically, so a trimmed
        // publish would drop it without this anchor. Must run before any session:
        // the first read freezes the catalogue.
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

    // Also used by the XAML previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // The default too, so a floated panel's window gets the same face.
            .With(new FontManagerOptions { DefaultFamilyName = "avares://SpectraEngine.Editor/Assets/Fonts#IBM Plex Sans" })
            .LogToTrace();
}
