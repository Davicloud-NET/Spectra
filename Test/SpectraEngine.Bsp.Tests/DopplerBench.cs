using SpectraEngine.Core.Audio.Propagation;

namespace SpectraEngine.Bsp.Tests;

// One path watched the way the engine watches it: ticks of a sixtieth of a
// second, and frames at a rate of their own. Its length is given the time on
// both clocks, so a test says which end moves on which.
internal sealed class DopplerBench
{
    public const double TickSeconds = 1.0 / 60.0;

    private readonly Func<double, double, double> _length;
    private readonly double _frameSeconds;
    private readonly double _unevenness;
    private readonly Random _random = new(1);

    private DopplerClock _clock;
    private DopplerShift _shift;
    private double _owed;

    // length: the path's length, from the seconds the ticks have covered and
    // the seconds the frames have. unevenness: how far a frame's length
    // strays from the rate, as a share of it.
    public DopplerBench(double framesPerSecond, Func<double, double, double> length, double unevenness = 0)
    {
        _length = length;
        _frameSeconds = 1.0 / framesPerSecond;
        _unevenness = unevenness;
    }

    public float Strength { get; set; } = 1f;

    public long Ticks { get; private set; }

    public double TickedSeconds => Ticks * TickSeconds;

    public double FrameSeconds { get; private set; }

    public float Factor => _shift.Factor;

    public float Rate => _shift.Rate;

    public static double Cents(double factor) => 1200 * Math.Log2(factor);

    // One frame. Returns the factor it gives.
    public float Step(bool jumped = false)
    {
        double frame = _frameSeconds * (1 + (_unevenness * ((_random.NextDouble() * 2) - 1)));
        FrameSeconds += frame;
        _owed += frame;

        int ticks = (int)(_owed / TickSeconds);
        _owed -= ticks * TickSeconds;
        Ticks += ticks;

        _clock.Advance((float)frame, (float)(ticks * TickSeconds));
        _shift.Step((float)_length(TickedSeconds, FrameSeconds), in _clock, jumped, Strength);
        return _shift.Factor;
    }

    public void Run(double seconds)
    {
        double until = FrameSeconds + seconds;
        while (FrameSeconds < until)
            Step();
    }

    // Runs on, and returns the lowest and the highest factor of those frames in cents.
    public (double Lowest, double Highest) CentsOver(double seconds)
    {
        double lowest = double.PositiveInfinity;
        double highest = double.NegativeInfinity;

        double until = FrameSeconds + seconds;
        while (FrameSeconds < until)
        {
            double cents = Cents(Step());
            lowest = Math.Min(lowest, cents);
            highest = Math.Max(highest, cents);
        }

        return (lowest, highest);
    }
}
