using System;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// The paths one emitter's sound takes to the listener: none, one or two.
/// </summary>
// Two, so a path round a corner can sit beside the one through a wall.
public readonly struct SoundPaths
{
    /// <summary>The most paths one emitter can have.</summary>
    public const int Capacity = 2;

    private readonly SoundPath _first;
    private readonly SoundPath _second;

    /// <summary>One path.</summary>
    public SoundPaths(in SoundPath only)
    {
        _first = only;
        Count = 1;
    }

    /// <summary>Two paths.</summary>
    public SoundPaths(in SoundPath first, in SoundPath second)
    {
        _first = first;
        _second = second;
        Count = 2;
    }

    /// <summary>No path: the sound does not reach the listener.</summary>
    public static SoundPaths None => default;

    /// <summary>How many paths are set.</summary>
    public int Count { get; }

    /// <summary>The path at <paramref name="index"/>, which must be below <see cref="Count"/>.</summary>
    public SoundPath this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"This emitter has {Count} path(s).");

            return index == 0 ? _first : _second;
        }
    }
}
