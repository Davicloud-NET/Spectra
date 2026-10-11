using System;

namespace SpectraEngine.Core.Entities;

// An entity world's time: the tick it is on and the step it was made with.
// Every time in seconds is the product of the two, never a running sum, so
// it does not drift.
internal sealed class TickClock
{
    public TickClock(float step)
    {
        // Written so NaN is refused too.
        if (!(step > 0f) || float.IsPositiveInfinity(step))
            throw new ArgumentOutOfRangeException(nameof(step), step, "A step is a number of seconds above zero.");

        Step = step;
    }

    public float Step { get; }

    public long Tick { get; private set; }

    public float Time => TimeAt(Tick);

    public void Restart() => Tick = 0;

    public void Advance() => Tick++;

    public long TicksIn(double seconds) => WholeTicks.In(seconds, Step);

    // The tick a wait that starts now ends on. No wait is the tick being run,
    // so the drain in progress takes it, or the next one. A wait too long to
    // count never ends.
    public long TickAfter(double seconds)
    {
        long ticks = TicksIn(seconds);
        return ticks > long.MaxValue - Tick ? long.MaxValue : Tick + ticks;
    }

    // The tick a world time falls on, counted from the start of the level and
    // not from now: late in a level a float time is too coarse to take now
    // away from. A time already past is the tick being run.
    public long TickAt(double time) => Math.Max(Tick, TicksIn(time));

    public float TimeAt(long tick) =>
        tick == long.MaxValue ? float.PositiveInfinity : (float)(tick * (double)Step);
}
