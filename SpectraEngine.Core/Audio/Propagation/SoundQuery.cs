using System.Numerics;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>What one emitter asks of <see cref="ISoundPropagation"/>.</summary>
/// <param name="Position">Where the sound is, in world space.</param>
/// <param name="MinDistance">The sound is at full volume inside this distance.</param>
/// <param name="MaxDistance">The sound is silent beyond this distance.</param>
/// <param name="Simulated">
/// What to work out for this sound: its own switches, less the ones the engine
/// has off. A part that is off leaves the path as if it did nothing.
/// </param>
public readonly record struct SoundQuery(
    Vector3 Position, float MinDistance, float MaxDistance, SoundSimulation Simulated = SoundSimulation.All);
