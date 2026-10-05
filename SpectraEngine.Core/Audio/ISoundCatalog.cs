namespace SpectraEngine.Core.Audio;

/// <summary>
/// Answers what the simulation asks about a sound: whether it is there, how
/// long it is, what it repeats and where its markers are. It hands out no
/// samples and needs no audio device, so a server or a test can supply one.
/// </summary>
public interface ISoundCatalog
{
    /// <summary>
    /// Describes the sound at a content path. A sound that is missing or
    /// cannot be played is a false, never an exception.
    /// </summary>
    /// <param name="path">The authored sound's content path, such as <c>Sounds/door_open.wav</c>.</param>
    /// <param name="sound">The sound, when it is there.</param>
    /// <param name="reason">
    /// Why it is not, for a person to act on. It ends a sentence after a
    /// colon and has no full stop. Empty when the sound is there.
    /// </param>
    bool TryDescribe(string path, out SoundDescription sound, out string reason);
}
