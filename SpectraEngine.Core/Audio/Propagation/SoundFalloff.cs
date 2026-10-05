using System;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>How loud a sound is at a distance.</summary>
public static class SoundFalloff
{
    /// <summary>
    /// The gain at <paramref name="distance"/>: 1 inside
    /// <paramref name="minDistance"/>, then falling off inversely and tapered
    /// so it reaches 0 at <paramref name="maxDistance"/>, and 0 beyond.
    /// </summary>
    // Bad input gives silence or a hard edge, never NaN: the result goes
    // straight to the audio device. A max at or below the min is a hard edge
    // at the min. An infinite max is the plain inverse curve.
    public static float Gain(float distance, float minDistance, float maxDistance)
    {
        if (float.IsNaN(distance) || float.IsNaN(minDistance) || float.IsNaN(maxDistance))
            return 0f;

        float near = MathF.Max(minDistance, 0f);

        if (distance <= near) return 1f;
        if (distance >= maxDistance) return 0f;

        float inverse = near / distance;
        if (float.IsPositiveInfinity(maxDistance)) return inverse;

        // The taper: 1 at the min, 0 at the max.
        return inverse * ((maxDistance - distance) / (maxDistance - near));
    }
}
