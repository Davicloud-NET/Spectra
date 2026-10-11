using System;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Turns a wait in seconds into whole ticks. Everything that waits in an
/// <see cref="EntityWorld"/> rounds by this one rule: a wire's delay, a think,
/// a timer, a mover's trip.
/// </summary>
public static class WholeTicks
{
    // Seconds and the step are floats, so 0.1 s at 60 ticks a second comes
    // out a hair off 6. A count this close above a whole number is that number.
    private const double Slack = 1e-3;

    // The float error grows with the count, so a long wait gets a millionth
    // of itself. Half a tick at most, or the rounding would lose a tick.
    private const double SlackPerTick = 1e-6;
    private const double MostSlack = 0.5;

    /// <summary>
    /// What <see cref="In"/> gives for a wait too long to count. As a tick
    /// it is one that never comes.
    /// </summary>
    public const long Never = long.MaxValue;

    /// <summary>
    /// How many ticks a wait takes, rounded up: an event is due on the first
    /// tick at or after its time. Any wait above zero is at least one tick.
    /// Zero, a negative wait and NaN are no ticks. A wait too long to count
    /// is <see cref="Never"/>.
    /// </summary>
    /// <param name="seconds">How long to wait.</param>
    /// <param name="step">The length of a tick in seconds, above zero.</param>
    public static long In(double seconds, float step)
    {
        // Written so NaN is refused too.
        if (!(step > 0f) || float.IsPositiveInfinity(step))
            throw new ArgumentOutOfRangeException(nameof(step), step, "A step is a number of seconds above zero.");

        // Written so NaN is no wait.
        if (!(seconds > 0d))
            return 0;

        double ticks = seconds / step;
        double whole = Math.Ceiling(ticks - Math.Clamp(ticks * SlackPerTick, Slack, MostSlack));

        // A long cannot hold 2^63, which is what its largest value widens to.
        if (whole >= Never)
            return Never;

        return whole < 1d ? 1 : (long)whole;
    }
}
