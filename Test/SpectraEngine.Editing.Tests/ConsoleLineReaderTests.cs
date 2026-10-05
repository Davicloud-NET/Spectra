using SpectraEngine.Executable;
using System.IO;

namespace SpectraEngine.Editing.Tests;

/// <summary>The demo's terminal console: lines typed while it runs.</summary>
public sealed class ConsoleLineReaderTests
{
    [Fact]
    public void A_reader_submits_each_line_and_ends_at_the_end_of_input()
    {
        var submitted = new List<string>();
        var input = new StringReader("ent_list\n\n   \nent_fire \"Main Door\" Open\r\necho done");

        Thread reader = ConsoleLineReader.Start(input, line =>
        {
            lock (submitted)
                submitted.Add(line);

            return true;
        });

        reader.Join(TimeSpan.FromSeconds(5)).ShouldBeTrue("the reader did not stop at the end of its input");

        // As typed. A blank line is nothing to run.
        submitted.ShouldBe(["ent_list", "ent_fire \"Main Door\" Open", "echo done"]);
    }

    [Fact]
    public void The_reader_runs_on_a_background_thread()
    {
        // A foreground thread parked in ReadLine would keep the demo's process
        // alive after its window closed.
        using var release = new ManualResetEventSlim();
        Thread reader = ConsoleLineReader.Start(new ParkedReader(release), _ => true);

        // Read while it is parked: a finished thread reports false.
        reader.IsBackground.ShouldBeTrue();

        release.Set();
        reader.Join(TimeSpan.FromSeconds(5)).ShouldBeTrue();
    }

    [Fact]
    public void A_full_queue_does_not_stop_the_reader()
    {
        int offered = 0;

        ConsoleLineReader.Pump(new StringReader("one\ntwo\nthree"), _ =>
        {
            offered++;
            return false;
        });

        offered.ShouldBe(3);
    }

    // Waits in ReadLine the way a terminal does, then reports the end of input.
    private sealed class ParkedReader(ManualResetEventSlim release) : TextReader
    {
        public override string? ReadLine()
        {
            release.Wait();
            return null;
        }
    }
}
