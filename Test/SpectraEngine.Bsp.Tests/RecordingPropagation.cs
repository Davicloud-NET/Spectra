using SpectraEngine.Core.Audio.Propagation;

namespace SpectraEngine.Bsp.Tests;

// Propagation by distance alone that keeps what it was last asked, so a test
// can read what reached it.
internal sealed class RecordingPropagation : ISoundPropagation
{
    private readonly DirectPropagation _direct = new();

    // The emitters of the last frame, in the order they were asked about.
    public List<SoundQuery> Asked { get; } = [];

    public void Resolve(in SoundListener listener, ReadOnlySpan<SoundQuery> emitters, Span<SoundPaths> results)
    {
        Asked.Clear();
        Asked.AddRange(emitters);

        _direct.Resolve(in listener, emitters, results);
    }
}
