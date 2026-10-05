using System;

namespace SpectraEngine.Core.Audio.Acoustics;

/// <summary>
/// What a sound has lost on its way, in decibels. Losses add, so the walls
/// along a path are summed first and turned into gains once.
/// </summary>
/// <param name="Db">Decibels the whole sound lost.</param>
/// <param name="HfDb">Decibels the high end lost beyond that.</param>
public readonly record struct AcousticLoss(float Db, float HfDb)
{
    /// <summary>The most walls can take off a sound. Past it the sound stays this quiet.</summary>
    // About a metre of concrete. Distance fades a sound to nothing, walls alone do not.
    public const float MaxDb = 30f;

    /// <summary>The most walls can take off the high end beyond the overall loss.</summary>
    // OpenAL's filter takes 2 dB off 1 kHz at this depth and 12 dB at 40.
    public const float MaxHfDb = 26f;

    /// <summary>The loss of two things one after the other.</summary>
    public static AcousticLoss operator +(AcousticLoss first, AcousticLoss second) =>
        new(first.Db + second.Db, first.HfDb + second.HfDb);

    /// <summary>
    /// The factors the engine applies for this loss. Neither goes above 1, and
    /// neither goes below what <see cref="MaxDb"/> and <see cref="MaxHfDb"/> leave.
    /// </summary>
    public AcousticGains ToGains() => new(ToGain(Db, MaxDb), ToGain(HfDb, MaxHfDb));

    private static float ToGain(float lossDb, float maxDb)
    {
        // Written this way round so a NaN is no loss. OpenAL refuses a NaN gain.
        if (!(lossDb > 0f)) return 1f;

        return MathF.Pow(10f, -MathF.Min(lossDb, maxDb) / 20f);
    }
}
