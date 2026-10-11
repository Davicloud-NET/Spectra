namespace SpectraEngine.Bsp.Tests;

// The engine loop needs a window, so no test runs it. A snapshot that hands
// over the console's lines without the console's count leaves the lines
// dropped at the end of a burst unreported, and nothing else fails. So the
// pair is checked in the source text.
public sealed class ConsoleCountWiringTests
{
    [Fact]
    public void The_engine_sends_the_consoles_count_with_the_lines_it_drains()
    {
        string engine = File.ReadAllText(Path.Combine(SourceRoot(), "SpectraEngine.Core", "Engine.cs"));

        engine.ShouldContain("ConsoleLines = _console.Output.Drain()");
        engine.ShouldContain(
            "ConsoleLinesWritten = _console.Output.Written",
            customMessage: "a reader learns from the count how many lines the console could not hold");
    }

    // Repo root: the nearest ancestor holding a solution file.
    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"No solution file above {AppContext.BaseDirectory}; the source test needs the repo.");
    }
}
