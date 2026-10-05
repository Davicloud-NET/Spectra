using System;
using System.Diagnostics;

namespace SoundCornersSpike;

internal static class Timing
{
    // Fewer runs for the slow cases. Set from the command line.
    public static double Scale { get; set; } = 1.0;

    private const double RoundBudgetMs = 400.0;

    /// <summary>
    /// The median of <paramref name="runs"/> timings, taken in three rounds.
    /// The spread is how far the three rounds' medians lie apart, as a share
    /// of the middle one.
    /// </summary>
    public static Timed Measure(Action action, int runs = 21, int warmup = 2)
    {
        runs = Math.Max(3, (int)(runs * Scale));

        long first = Stopwatch.GetTimestamp();
        for (int i = 0; i < warmup; i++) action();
        double each = Stopwatch.GetElapsedTime(first).TotalMilliseconds / Math.Max(warmup, 1);

        // A slow case gets fewer runs, down to three a round.
        if (each * runs > RoundBudgetMs) runs = Math.Max(3, (int)(RoundBudgetMs / each));

        Span<double> rounds = stackalloc double[3];
        double low = double.MaxValue, high = 0.0;
        var samples = new double[runs];

        for (int round = 0; round < 3; round++)
        {
            for (int i = 0; i < runs; i++)
            {
                long start = Stopwatch.GetTimestamp();
                action();
                samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }

            Array.Sort(samples);
            rounds[round] = samples[runs / 2];
            low = Math.Min(low, samples[0]);
            high = Math.Max(high, samples[runs - 1]);
        }

        rounds.Sort();
        double middle = rounds[1];
        double spread = middle > 0.0 ? (rounds[2] - rounds[0]) / middle * 100.0 : 0.0;
        return new Timed(middle, spread, low, high);
    }

    /// <summary>Nanoseconds per call of a cheap operation run <paramref name="count"/> times a sample.</summary>
    public static (double Nanoseconds, double SpreadPercent) PerCall(Action batch, int count, int runs = 15)
    {
        Timed timed = Measure(batch, runs);
        return (timed.MedianMs * 1e6 / count, timed.SpreadPercent);
    }
}
