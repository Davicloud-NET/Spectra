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

    /// <summary>
    /// The furthest <see cref="GainHf"/> moves in one <see cref="Step"/> that
    /// does not land. It makes a sound slower to go dull than to go quiet:
    /// from 1 to 0.1 is 95% done after 0.75 s at 60 steps a second.
    /// </summary>
    // OpenAL switches to a new high-end gain between two samples, which knocks
    // the low end for a few samples. At this size a 200 Hz tone moves 4.0% of
    // its amplitude at worst, against 2.6% alone and 20.6% with no limit. A
    // rise from under 0.05 is rougher, up to 9.6%: the filter takes from the
    // bass down there and one step gives it back.
    public const float MaxGainHfStep = 0.02f;

    /// <summary>
    /// The furthest <see cref="GainHf"/> moves in a second, which is
    /// <see cref="MaxGainHfStep"/> a step at 60 steps a second. Above that the
    /// steps get smaller and a change takes as long.
    /// </summary>
    // The audio device mixes in blocks of its own length and takes the newest
    // value for each, so at a high frame rate several steps reach it as one.
    public const float MaxGainHfRate = 1.2f;

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
    /// the emitter or the listener teleported. The high-end gain also lands at
    /// once while the path is silent.
    /// </summary>
    /// <param name="timeConstant">Seconds; zero or less means no smoothing.</param>
    public void Step(
        in SoundPath target,
        float deltaSeconds,
        bool jumped = false,
        float timeConstant = DefaultTimeConstant)
    {
        float blend = jumped || !_hasShown ? 1f : Blend(deltaSeconds, timeConstant);

        // A step in a sound nobody hears needs no care.
        bool lands = blend >= 1f || Gain <= LandingDistance;

        float gainHf = Approach(GainHf, target.GainHf, lands ? 1f : blend);
        if (!lands) gainHf = Limit(GainHf, gainHf, deltaSeconds);

        Gain = Approach(Gain, target.Gain, blend);
        GainHf = gainHf;
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

    private static float Limit(float shown, float next, float deltaSeconds)
    {
        float most = MathF.Max(0f, MathF.Min(MaxGainHfStep, MaxGainHfRate * deltaSeconds));
        float move = next - shown;

        // Written so a NaN limit changes nothing: nothing moved then.
        if (!(MathF.Abs(move) > most)) return next;

        return shown + MathF.CopySign(most, move);
    }
}
