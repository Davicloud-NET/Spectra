using System;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// Decides how each emitter's sound reaches the listener: where it seems to
/// come from, how loud and how muffled.
/// </summary>
// No scene in the signature. An implementation that needs the world takes it
// in its constructor, so callers stay the same when walls and corners arrive.
public interface ISoundPropagation
{
    /// <summary>
    /// Answers every emitter: <paramref name="results"/>[i] is for
    /// <paramref name="emitters"/>[i]. Allocates nothing.
    /// </summary>
    /// <param name="results">At least as long as <paramref name="emitters"/>.</param>
    void Resolve(in SoundListener listener, ReadOnlySpan<SoundQuery> emitters, Span<SoundPaths> results);
}
