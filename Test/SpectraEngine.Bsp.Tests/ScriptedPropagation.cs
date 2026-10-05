using SpectraEngine.Core.Audio.Propagation;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

// Answers every emitter the same way, so a test can tell what the presenter
// took from the path and what it took from the emitter.
internal sealed class ScriptedPropagation : ISoundPropagation
{
    // How many paths each emitter gets: none, one or two.
    public int PathCount { get; set; } = 1;

    public float Gain { get; set; } = 1f;

    public float GainHf { get; set; } = 1f;

    // Added to the emitter's position: where the sound seems to come from.
    public Vector3 Offset { get; set; }

    public int LastEmitterCount { get; private set; }

    public void Resolve(in SoundListener listener, ReadOnlySpan<SoundQuery> emitters, Span<SoundPaths> results)
    {
        LastEmitterCount = emitters.Length;

        for (int i = 0; i < emitters.Length; i++)
        {
            var first = new SoundPath(emitters[i].Position + Offset, Gain, GainHf);

            // The second path is nothing like the first, so playing it shows.
            var second = new SoundPath(emitters[i].Position - Offset, 1f, 1f);

            results[i] = PathCount switch
            {
                0 => SoundPaths.None,
                1 => new SoundPaths(in first),
                _ => new SoundPaths(in first, in second),
            };
        }
    }
}
