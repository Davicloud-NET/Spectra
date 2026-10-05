using SpectraEngine.Core.Audio;

namespace SpectraEngine.Bsp.Tests;

// Knows the sounds a test gives it, and keeps what it was asked.
internal sealed class FakeSoundCatalog : ISoundCatalog
{
    private readonly Dictionary<string, SoundDescription> _sounds = new(StringComparer.Ordinal);

    public List<string> Asked { get; } = [];

    public void Add(string path, SoundDescription sound) => _sounds[path] = sound;

    public bool TryDescribe(string path, out SoundDescription sound, out string reason)
    {
        Asked.Add(path);

        bool found = _sounds.TryGetValue(path, out sound);
        reason = found ? "" : "the test has no such sound";
        return found;
    }
}
