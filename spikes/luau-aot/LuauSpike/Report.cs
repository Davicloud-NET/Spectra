using System;

namespace LuauSpike;

// Prints one line per check and counts failures.
internal sealed class Report
{
    internal int Failures { get; private set; }

    internal void Check(string id, string what, bool passed, string detail)
    {
        if (!passed)
            Failures++;
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {id,-4} {what,-34} {detail}");
    }

    internal void Skip(string id, string what, string why) => Console.WriteLine($"SKIP  {id,-4} {what,-34} {why}");

    // A measurement or an observation. Not a verdict.
    internal void Note(string id, string what, string value) => Console.WriteLine($"      {id,-4} {what,-34} {value}");

    // Runs a check body. An exception is a failure, not a crash of the run.
    internal void Run(string id, string what, Action body)
    {
        try
        {
            body();
        }
        catch (Exception e) // boundary: one broken check must not hide the others
        {
            Check(id, what, false, $"{e.GetType().Name}: {e.Message}");
        }
    }
}
