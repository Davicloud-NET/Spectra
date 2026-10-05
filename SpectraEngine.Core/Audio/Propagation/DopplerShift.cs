using System;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// The pitch one path is heard at, from how fast the path is getting longer
/// or shorter: higher while the sound and the listener close in, lower while
/// they part. One per path. It knows no positions and no velocities, so a
/// moving listener and a moving sound count alike.
/// </summary>
public struct DopplerShift
{
    /// <summary>The speed of sound, in units a second. A unit is a metre.</summary>
    public const float SpeedOfSound = 343f;

    /// <summary>The lowest factor: an octave down.</summary>
    public const float MinFactor = 0.5f;

    /// <summary>The highest factor: an octave up.</summary>
    public const float MaxFactor = 2f;

    /// <summary>
    /// A path that grows or shrinks faster than this in one step has jumped:
    /// an end was teleported, or the path now runs another way. Half the speed
    /// of sound, which is where closing in reaches <see cref="MaxFactor"/>.
    /// </summary>
    public const float JumpSpeed = SpeedOfSound / 2f;

    /// <summary>
    /// A path that changes slower than this is at rest and shifts nothing.
    /// In units a second: a tenth of a cent.
    /// </summary>
    // A source at its own pitch needs no resampling on the device.
    public const float RestSpeed = 0.02f;

    // Smaller than any sum that still shifts the pitch. Keeps a path that
    // came to rest from fading through the denormals.
    private const double Negligible = 1e-30;

    // What the path has grown by, each step fading as the clock says, and
    // the same weighted by how far the clocks were apart on its frame.
    private double _grown;
    private double _skewed;

    private float _length;
    private long _nextFrame;

    // The factor less one, so a shift that was never stepped changes nothing.
    private float _shift;

    /// <summary>What to multiply a sound's pitch by. 1 until the path's length has been seen changing.</summary>
    public readonly float Factor => 1f + _shift;

    /// <summary>How fast the path is growing, in units a second. Below zero while it shrinks.</summary>
    public float Rate { get; private set; }

    /// <summary>
    /// Takes the path's length for the frame <paramref name="clock"/> was
    /// last advanced by. The first length shifts nothing, and neither does
    /// one after a frame that was left out or a jump.
    /// </summary>
    /// <param name="pathLength">How far the sound travels to the listener, in units.</param>
    /// <param name="jumped">True when an end is known to have teleported.</param>
    /// <param name="strength">Scales the shift, counted in cents: 1 is real, 0 is none.</param>
    public void Step(float pathLength, in DopplerClock clock, bool jumped = false, float strength = 1f)
    {
        bool isFirst = clock.Frame != _nextFrame;
        float grown = pathLength - _length;

        _nextFrame = clock.Frame + 1;
        _length = pathLength;

        // Written so a length that is not a number counts as a jump.
        if (isFirst || jumped || !(MathF.Abs(grown) <= JumpSpeed * clock.Span))
        {
            _grown = 0d;
            _skewed = 0d;
            _shift = 0f;
            Rate = 0f;
            return;
        }

        _grown = Fade(_grown, clock.Keep) + grown;
        _skewed = Fade(_skewed, clock.Keep) + (grown * clock.Skew);

        double rate = (_grown * clock.GrownWeight) + (_skewed * clock.SkewedWeight);
        Rate = Math.Abs(rate) < RestSpeed ? 0f : (float)rate;
        _shift = FactorFor(Rate, strength) - 1f;
    }

    /// <summary>Forgets the path, so the next <see cref="Step"/> shifts nothing.</summary>
    public void Reset() => this = default;

    /// <summary>
    /// The factor for a path that grows by <paramref name="rate"/> units a
    /// second: the speed of sound over the speed of sound plus the rate,
    /// kept between <see cref="MinFactor"/> and <see cref="MaxFactor"/>.
    /// </summary>
    /// <param name="strength">Scales the shift, counted in cents: 2 squares the factor, 0 gives 1.</param>
    public static float FactorFor(float rate, float strength = 1f)
    {
        if (float.IsNaN(rate) || float.IsNaN(strength))
            return 1f;

        // A path that shrinks at the speed of sound or faster has no factor.
        float real = rate > -SpeedOfSound ? SpeedOfSound / (SpeedOfSound + rate) : MaxFactor;
        real = Math.Clamp(real, MinFactor, MaxFactor);

        float scaled = strength == 1f ? real : MathF.Pow(real, strength);
        return Math.Clamp(scaled, MinFactor, MaxFactor);
    }

    private static double Fade(double sum, double keep)
    {
        double faded = sum * keep;
        return Math.Abs(faded) < Negligible ? 0d : faded;
    }
}
