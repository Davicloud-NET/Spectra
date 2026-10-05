using Microsoft.Extensions.Logging;
using SpectraEngine.Core.ConsoleSystem;
using SpectraEngine.Core.Hosting;
using System.Collections.Generic;

namespace SpectraEngine.Executable;

// The demo's way into the engine's console and back out: startup lines go in,
// and whatever the console prints goes to the log, where it can be grepped.
internal static class DemoConsole
{
    // What every console line in the log starts with.
    public const string Prefix = "[console]";

    public static void Attach(EngineHost host, IReadOnlyList<string> commands, ILogger logger)
    {
        // Runs on the render thread, and only logs when there is something to say.
        host.FrameCompleted += snapshot =>
        {
            foreach (ConsoleLine line in snapshot.ConsoleLines)
                logger.LogInformation("{Line}", Describe(in line));
        };

        foreach (string command in commands)
        {
            if (!host.SubmitConsoleLine(command))
                logger.LogWarning("The console's queue is full, so this line was dropped: {Line}", command);
        }
    }

    // All at Information: a refused command is the console's news, not a
    // fault in the run. The severity stays readable in the text.
    public static string Describe(in ConsoleLine line) => line.Severity switch
    {
        LogLevel.Error => $"{Prefix} error: {line.Text}",
        LogLevel.Warning => $"{Prefix} warning: {line.Text}",
        _ => $"{Prefix} {line.Text}",
    };
}
