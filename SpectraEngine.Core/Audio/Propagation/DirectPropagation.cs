using System;
using System.Numerics;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// Propagation by distance alone: every emitter is heard where it is, as loud
/// as <see cref="SoundFalloff"/> says and unmuffled. Nothing is in the way.
/// </summary>
public sealed class DirectPropagation : ISoundPropagation
{
    /// <inheritdoc/>
    public void Resolve(in SoundListener listener, ReadOnlySpan<SoundQuery> emitters, Span<SoundPaths> results)
    {
        if (results.Length < emitters.Length)
        {
            throw new ArgumentException(
                $"{emitters.Length} emitters need {emitters.Length} results, and there is room for {results.Length}.",
                nameof(results));
        }

        for (int i = 0; i < emitters.Length; i++)
        {
            ref readonly SoundQuery emitter = ref emitters[i];

            float distance = Vector3.Distance(listener.Position, emitter.Position);
            float gain = SoundFalloff.Gain(distance, emitter.MinDistance, emitter.MaxDistance);

            results[i] = new SoundPaths(new SoundPath(emitter.Position, gain, GainHf: 1f));
        }
    }
}
