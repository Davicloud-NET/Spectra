using SpectraEngine.Core.Audio.Propagation;

namespace SpectraEngine.Core.Entities;

/// <summary>How a sound is to be played: how loud, how fast, how far it reaches and whether it repeats.</summary>
/// <param name="Gain">Linear loudness before distance. 1 is the file as it is. Not below zero.</param>
/// <param name="Pitch">Playback speed. 1 is the file as it is, 2 is twice as fast. Above zero.</param>
/// <param name="MinDistance">The sound is at full volume inside this distance.</param>
/// <param name="MaxDistance">The sound is silent beyond this distance.</param>
/// <param name="IsLooped">
/// Whether the sound repeats until it is stopped: the file's loop region, or
/// the whole sound when it has none. A sound that is not looped plays once
/// through and ignores the region.
/// </param>
public readonly record struct SoundEmitterSettings(
    float Gain, float Pitch, float MinDistance, float MaxDistance, bool IsLooped)
{
    /// <summary>
    /// What is worked out for the sound on its way to the listener. Everything,
    /// unless it is set.
    /// </summary>
    public SoundSimulation Simulated { get; init; } = SoundSimulation.All;
}
