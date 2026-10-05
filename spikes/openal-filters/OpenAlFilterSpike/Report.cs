using System;

namespace OpenAlFilterSpike;

// Prints one line per check and counts failures. Lines that start with INFO or
// TIME differ between runs and builds. Every other line should not.
internal sealed class Report
{
    internal int Failures { get; private set; }

    internal void Check(string id, string what, bool passed, string detail)
    {
        if (!passed)
            Failures++;
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {id,-3} {what,-38} {detail}");
    }

    // A measurement or an observation. Not a verdict.
    internal void Note(string id, string what, string value) => Console.WriteLine($"      {id,-3} {what,-38} {value}");

    internal void Info(string what, string value) => Console.WriteLine($"INFO  {what,-42} {value}");

    internal void Time(string id, string what, string value) => Console.WriteLine($"TIME  {id,-3} {what,-38} {value}");

    // Runs a group of checks. An exception is a failure, not a crash of the run.
    internal void Run(string id, string what, Action body)
    {
        try
        {
            body();
        }
        catch (Exception e) // boundary: one broken group must not hide the others
        {
            Check(id, what, false, $"{e.GetType().Name}: {e.Message}");
        }

        Console.WriteLine();
    }
}
