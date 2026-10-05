using System;
using System.Diagnostics;

namespace LuauSpike;

internal static class Bench
{
    internal const int Rounds = 9;

    internal static double Nanoseconds(long ticks) => ticks * 1e9 / Stopwatch.Frequency;

    // Best of several rounds. One round runs the body once, and the body does `operations` operations.
    internal static double BestNanosecondsPerOperation(int rounds, long operations, Action body)
    {
        long best = long.MaxValue;
        for (int round = 0; round < rounds; round++)
        {
            long start = Stopwatch.GetTimestamp();
            body();
            long elapsed = Stopwatch.GetTimestamp() - start;
            if (elapsed < best)
                best = elapsed;
        }

        return Nanoseconds(best) / operations;
    }

    // Median of single runs, in microseconds.
    internal static double MedianMicroseconds(int runs, Action body)
    {
        double[] samples = new double[runs];
        for (int run = 0; run < runs; run++)
        {
            long start = Stopwatch.GetTimestamp();
            body();
            samples[run] = Nanoseconds(Stopwatch.GetTimestamp() - start) / 1000.0;
        }

        Array.Sort(samples);
        return samples[runs / 2];
    }
}
