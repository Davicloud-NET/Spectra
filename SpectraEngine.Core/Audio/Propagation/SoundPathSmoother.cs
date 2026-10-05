using System;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// Moves the gain and high-end gain a voice plays at toward what propagation
/// asks for, over time, so a sudden change is not a click. It lands on the
/// target once the rest is too small to hear. One per path. The path's
/// position is not smoothed.
/// </summary>
public struct SoundPathSmoother
{
    /// <summary>
    /// Seconds to cover about two thirds of a change. Three of them cover 95%.
    /// </summary>
    public const float DefaultTimeConstant = 0.1f;

    // 80 dB down. A jump this small cannot be heard.
    private const float LandingDistance = 1e-4f;

    private bool _hasShown;

    /// <summary>The gain to play at now.</summary>
    public float Gain { get; private set; }

    /// <summary>The high-end gain to play at now.</summary>
    public float GainHf { get; private set; }

    /// <summary>
    /// Moves toward the target by <paramref name="deltaSeconds"/> of time. Lands
    /// on it at once on the first call, and when <paramref name="jumped"/> says
    /// the emitter or the listener teleported.
    /// </summary>
    /// <param name="timeConstant">Seconds; zero or less means no smoothing.</param>
    public void Step(
        in SoundPath target,
        float deltaSeconds,
        bool jumped = false,
        float timeConstant = DefaultTimeConstant)
    {
        float blend = jumped || !_hasShown ? 1f : Blend(deltaSeconds, timeConstant);

        Gain = Approach(Gain, target.Gain, blend);
        GainHf = Approach(GainHf, target.GainHf, blend);
        _hasShown = true;
    }

    /// <summary>Forgets what was shown, so the next <see cref="Step"/> lands on its target.</summary>
    public void Reset() => this = default;

    // The share of the remaining distance to cover. Exponential, so two half
    // steps land where one whole step does and the frame rate does not matter.
    private static float Blend(float deltaSeconds, float timeConstant)
    {
        // Written so NaN takes the same branch as zero.
        if (!(timeConstant > 0f)) return 1f;
        if (!(deltaSeconds > 0f)) return 0f;

        return 1f - MathF.Exp(-deltaSeconds / timeConstant);
    }

    private static float Approach(float shown, float target, float blend)
    {
        // A NaN or an infinity would stay in the state for good.
        if (!float.IsFinite(target)) target = 0f;
        if (blend >= 1f) return target;

        // Written so a NaN blend moves nothing too.
        if (!(blend > 0f)) return shown;

        float next = shown + ((target - shown) * blend);

        // An exponential never arrives, and close to the target the step gets
        // too small for a float to hold. A fade to silence has to end at zero.
        if (MathF.Abs(target - next) <= LandingDistance) return target;

        // Rounding can land a hair past the target.
        return target >= shown ? MathF.Min(next, target) : MathF.Max(next, target);
    }
}
