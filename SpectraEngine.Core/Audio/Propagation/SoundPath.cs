using System.Numerics;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// One way a sound reaches the listener: where it seems to come from, how loud
/// it arrives and how muffled.
/// </summary>
/// <param name="Position">The apparent world position, which is what gets panned.</param>
/// <param name="Gain">Linear loudness on arrival; 1 is full volume, 0 is silent.</param>
/// <param name="GainHf">
/// How dull the sound arrives, from 1 for clear down to 0. It is the level
/// left at 5 kHz, and the very top falls to about its square: 0.5 leaves half
/// at 5 kHz and a quarter at the top, 0.25 a quarter and a sixteenth. Low
/// notes pass. From about 0.1 down the middle of the sound goes as well.
/// </param>
public readonly record struct SoundPath(Vector3 Position, float Gain, float GainHf);
