using System.Diagnostics;
using SpectraEngine.Core.Physics.Character;

namespace SpectraEngine.Scripting.Tests;

// Times the mover it stands in front of, tick by tick, and nothing else of
// the tick.
internal sealed class TimedMover(ICharacterMover inner) : ICharacterMover
{
    private readonly List<long> _ticks = new(4096);

    public int Calls => _ticks.Count;

    // The tick in the middle. A walk has a few ticks that build the
    // collision of a new stretch of the level, and those are no mover's own.
    public double MedianNanoseconds => Nanoseconds(Sorted()[_ticks.Count / 2]);

    public double MeanNanoseconds => Nanoseconds(_ticks.Sum()) / _ticks.Count;

    public double WorstNanoseconds => Nanoseconds(Sorted()[^1]);

    public void Tick(
        ref CharacterState state,
        in CharacterCommand command,
        ICharacterCollisionSource source,
        CharacterTuning tuning,
        float deltaTime)
    {
        long started = Stopwatch.GetTimestamp();
        inner.Tick(ref state, in command, source, tuning, deltaTime);
        _ticks.Add(Stopwatch.GetTimestamp() - started);
    }

    private long[] Sorted()
    {
        long[] sorted = [.. _ticks];
        Array.Sort(sorted);
        return sorted;
    }

    private static double Nanoseconds(long stopwatchTicks) =>
        Stopwatch.GetElapsedTime(0, stopwatchTicks).TotalNanoseconds;
}
