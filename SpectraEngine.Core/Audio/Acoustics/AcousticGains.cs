namespace SpectraEngine.Core.Audio.Acoustics;

/// <summary>
/// The two factors a sound is played with after something got in its way.
/// Both are linear, between a floor and 1.
/// </summary>
/// <param name="Gain">How loud the whole sound still is; 1 is untouched.</param>
/// <param name="GainHf">
/// How much of the high end is left on top of <paramref name="Gain"/>; 1 is
/// all of it. It is the level at 5 kHz, and the very top falls to about its square.
/// </param>
public readonly record struct AcousticGains(float Gain, float GainHf);
