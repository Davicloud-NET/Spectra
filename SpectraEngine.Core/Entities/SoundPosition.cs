namespace SpectraEngine.Core.Entities;

/// <summary>Where a playing sound is in its file.</summary>
/// <param name="Pass">
/// How many times a looped sound has turned round. Zero for a sound that
/// plays once.
/// </param>
/// <param name="Frame">
/// The sample frame of the file it is at. A looped sound stays below its loop
/// end. A sound that plays once stops at its frame count.
/// </param>
public readonly record struct SoundPosition(long Pass, long Frame);
