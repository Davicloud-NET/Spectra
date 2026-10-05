using System.Numerics;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// One way a sound reaches the listener: where it seems to come from, how loud
/// it arrives and how muffled.
/// </summary>
/// <param name="Position">The apparent world position, which is what gets panned.</param>
/// <param name="Gain">Linear loudness on arrival; 1 is full volume, 0 is silent.</param>
/// <param name="GainHf">How much of the high end survives; 1 is all of it.</param>
public readonly record struct SoundPath(Vector3 Position, float Gain, float GainHf);
