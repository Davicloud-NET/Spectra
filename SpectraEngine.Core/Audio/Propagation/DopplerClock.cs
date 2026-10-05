using System;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// How long each frame took on the two clocks things move by: the frame's own
/// length, and the simulation ticks that ran in it. One for a listener and
/// all it hears. Every <see cref="DopplerShift"/> is stepped against it once
/// a frame.
/// </summary>
// A camera moves every frame and an entity every tick. Against frame time
// alone a mover stands still on most frames at 144 a second and jumps on the
// rest, and its pitch flutters by tens of cents. So a path's rate is fitted
// to both clocks at once, and steady motion of either end reads as steady.
public struct DopplerClock
{
    /// <summary>
    /// Seconds for a path's rate to cover about two thirds of a change. A
    /// tenth of a second covers 92%.
    /// </summary>
    public const float TimeConstant = 0.04f;

    // Keeps the fit from telling the clocks apart on less than they differ
    // by. In seconds: a frame and its ticks have to be about 0.1 ms apart
    // before the difference counts.
    private const double Ridge = 1e-6;

    // A frame shorter than this share of its ticks counts as long as they
    // were. Zero is the usual one: a caller with no frame time to give. A
    // run of such frames would leave the rate that goes with the frames
    // unknown, and the fit with no answer.
    private const double ShortestFrame = 1e-4;

    // The fit's sums over the frames so far, each fading by Keep a frame:
    // the mean of the two clocks, half their difference, and that half
    // squared over the mean.
    private double _mean;
    private double _cross;
    private double _spread;

    // Goes up every frame. A shift that was not stepped on the frame before
    // has nothing to measure from.
    internal long Frame { get; private set; }

    // How much of each sum is kept this frame.
    internal double Keep { get; private set; }

    // Half the difference of the two clocks this frame, over their mean.
    internal double Skew { get; private set; }

    // What a shift's two sums are multiplied by to give its path's rate.
    internal double GrownWeight { get; private set; }

    internal double SkewedWeight { get; private set; }

    // The longer of the two clocks this frame. A path can only have changed over that long.
    internal float Span { get; private set; }

    /// <summary>Takes a frame's two lengths. Call once a frame, before any shift is stepped.</summary>
    /// <param name="frameSeconds">How long the frame took. With zero the frame counts as long as its ticks.</param>
    /// <param name="tickSeconds">How much simulated time the frame's ticks covered. Zero when none ran.</param>
    public void Advance(float frameSeconds, float tickSeconds)
    {
        Frame++;

        double frame = Seconds(frameSeconds);
        double ticks = Seconds(tickSeconds);
        if (frame < ticks * ShortestFrame)
            frame = ticks;

        double mean = (frame + ticks) / 2d;
        Span = (float)Math.Max(frame, ticks);

        if (mean <= 0d)
        {
            Keep = 1d;
            Skew = 0d;
            return;
        }

        // Starts as if the two clocks had kept pace for a long while, so the
        // first rates come in from nothing and do not jump.
        if (!(_mean > 0d))
            _mean = TimeConstant;

        double skew = (frame - ticks) / 2d;
        Keep = Math.Exp(-mean / TimeConstant);
        Skew = skew / mean;

        _mean = (_mean * Keep) + mean;
        _cross = (_cross * Keep) + skew;
        _spread = (_spread * Keep) + (skew * Skew);

        Solve();
    }

    /// <summary>Forgets the frames so far, for another level. Every shift starts over.</summary>
    public void Reset()
    {
        long frame = Frame + 1;
        this = default;
        Frame = frame;
    }

    // The two by two system of the fit, solved for the sum of the two rates.
    // The ridge is on the rate that goes with the ticks: where nothing tells
    // the clocks apart, a path counts as changing with the frames.
    // The sum is right while the ticks keep pace with the frames over time.
    // If they stop, what moved on them reads as moving for most of a second
    // more. A pause or a time scale has to weigh the tick rate by the pace.
    private void Solve()
    {
        double mean = _mean + Ridge;
        double cross = _cross - Ridge;
        double spread = _spread + Ridge;
        double determinant = (mean * spread) - (cross * cross);

        GrownWeight = spread / determinant;
        SkewedWeight = -cross / determinant;
    }

    // Written so NaN is no time at all.
    private static double Seconds(float seconds) => seconds > 0f && float.IsFinite(seconds) ? seconds : 0d;
}
