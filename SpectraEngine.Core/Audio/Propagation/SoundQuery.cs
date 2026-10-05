using System.Numerics;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>What one emitter asks of <see cref="ISoundPropagation"/>.</summary>
/// <param name="Position">Where the sound is, in world space.</param>
/// <param name="MinDistance">The sound is at full volume inside this distance.</param>
/// <param name="MaxDistance">The sound is silent beyond this distance.</param>
public readonly record struct SoundQuery(Vector3 Position, float MinDistance, float MaxDistance);
