using System.Diagnostics;
using SpectraEngine.Core.Bsp;

// The move gesture of the openworld scenes: one isolated part moved, compiled
// from the previous world. A compile is about 0.1 ms, so the sizes are timed
// in turn inside every round. Timed one size after the other, minutes apart,
// whatever else the machine did in between read as a slope.
internal sealed class OpenWorldMove
{
    public const int Rounds = 15;

    // One compile is below timer jitter, so a sample is the mean of a burst
    // from the same previous world.
    public const int BurstIterations = 16;

    // A stage that walks the world would show about 50x.
    private const double Band = 1.5;

    private readonly List<Case> _cases = [];

    public void Add(
        int parts, CsgWorld world, List<BrushPlacement> placements, List<BrushPlacement> edited, ChunkCoord[] dirtyCells) =>
        _cases.Add(new Case(parts, world, placements, edited, dirtyCells));

    public void Measure()
    {
        foreach (Case c in _cases)
        {
            for (int warm = 0; warm < 2; warm++)
            {
                TimeEdit(c);
                TimeNoop(c);
            }
        }

        for (int round = 0; round < Rounds; round++)
        {
            Collect();

            // Another size goes first in each round, so none is always the
            // one timed right after the collection.
            for (int k = 0; k < _cases.Count; k++)
            {
                Case c = _cases[(round + k) % _cases.Count];
                c.EditMs[round] = TimeEdit(c);
                c.NoopMs[round] = TimeNoop(c);
            }
        }
    }

    public double EditMedianMs(int index) => Median(_cases[index].EditMs);

    public double NoopMedianMs(int index) => Median(_cases[index].NoopMs);

    public void PrintVerdict()
    {
        Case small = _cases[0];
        Case large = _cases[^1];

        // Largest over smallest inside each round, so both halves of a ratio
        // saw the same machine.
        var ratios = new double[Rounds];
        for (int round = 0; round < Rounds; round++)
            ratios[round] = large.EditMs[round] / small.EditMs[round];

        Array.Sort(ratios);
        double low = ratios[Rounds / 4];
        double high = ratios[Rounds - 1 - Rounds / 4];
        string verdict =
            high <= Band ? "world-size independent (within noise)"
            : low > Band ? "NOT world-size independent. Investigate before accepting this as a baseline"
            : "too noisy to say, the rounds lie on both sides of the band. Run it again on a quiet machine";

        Console.WriteLine(
            $"  verdict (MOVE gesture): one-part edit at {large.Parts / 1000}k parts = {ratios[Rounds / 2]:F2}x the edit at " +
            $"{small.Parts / 1000}k parts -> {verdict}");
        Console.WriteLine(
            $"  spread over {Rounds} rounds: the middle half of the ratios is {low:F2}x to {high:F2}x, all of them " +
            $"{ratios[0]:F2}x to {ratios[^1]:F2}x, against a band of {Band:F1}x. " +
            $"Edit us, lowest / median / highest: {string.Join("; ", _cases.Select(Spread))}");
    }

    private static string Spread(Case c) =>
        $"{c.Parts / 1000}k {c.EditMs.Min() * 1000:F0} / {Median(c.EditMs) * 1000:F0} / {c.EditMs.Max() * 1000:F0}";

    private static double TimeEdit(Case c)
    {
        var watch = Stopwatch.StartNew();
        for (int k = 0; k < BurstIterations; k++)
            _ = CsgWorld.Build(c.Edited, c.DirtyCells, c.World);

        watch.Stop();
        return watch.Elapsed.TotalMilliseconds / BurstIterations;
    }

    // Same placements and an empty dirty set: the patch path's bookkeeping floor.
    private static double TimeNoop(Case c)
    {
        var watch = Stopwatch.StartNew();
        for (int k = 0; k < BurstIterations; k++)
            _ = CsgWorld.Build(c.Placements, [], c.World);

        watch.Stop();
        return watch.Elapsed.TotalMilliseconds / BurstIterations;
    }

    private static double Median(double[] values)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static void Collect()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private sealed record Case(
        int Parts, CsgWorld World, List<BrushPlacement> Placements, List<BrushPlacement> Edited, ChunkCoord[] DirtyCells)
    {
        public double[] EditMs { get; } = new double[Rounds];

        public double[] NoopMs { get; } = new double[Rounds];
    }
}
