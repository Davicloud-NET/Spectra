namespace SpectraEngine.Core.Audio;

/// <summary>What <see cref="SoundPresenter"/> made of the level's sounds on its last frame.</summary>
/// <param name="Playing">Sounds the level is playing, heard or not.</param>
/// <param name="WithSource">Sounds that have a source on the audio device.</param>
/// <param name="WithoutSource">
/// Sounds loud enough to hear that have no source, because louder ones hold
/// them all.
/// </param>
/// <param name="RefusedStarts">Sounds the device refused a source, since the level started.</param>
/// <param name="Unplayable">Sounds in earshot whose file could not be loaded here.</param>
public readonly record struct SoundStats(
    int Playing, int WithSource, int WithoutSource, int RefusedStarts, int Unplayable)
{
    /// <summary>
    /// Sounds with nothing to hear: out of earshot, over, or on a node that
    /// is out of the scene.
    /// </summary>
    public int Silent => Playing - WithSource - WithoutSource - Unplayable;
}
