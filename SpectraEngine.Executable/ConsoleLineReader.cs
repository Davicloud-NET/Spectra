using System;
using System.IO;
using System.Threading;

namespace SpectraEngine.Executable;

// Feeds lines typed in the terminal to the engine's console.
internal static class ConsoleLineReader
{
    // A background thread, and nobody joins it: one parked in ReadLine must
    // not keep the process alive after the window has closed.
    public static Thread Start(TextReader input, Func<string, bool> submit)
    {
        var thread = new Thread(() => Pump(input, submit))
        {
            IsBackground = true,
            Name = "Console input",
        };
        thread.Start();
        return thread;
    }

    // Returns at the end of input: a closed or redirected stdin reads null.
    public static void Pump(TextReader input, Func<string, bool> submit)
    {
        while (input.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                submit(line);
        }
    }
}
