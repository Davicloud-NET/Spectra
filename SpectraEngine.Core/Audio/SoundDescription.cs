using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// What the simulation knows about a sound without its samples: how long it
/// is, what the file says to repeat and where its markers are.
/// </summary>
public readonly struct SoundDescription
{
    private readonly IReadOnlyList<AudioMarker>? _markers;

    /// <param name="frameCount">Length in sample frames.</param>
    /// <param name="sampleRate">Sample frames a second.</param>
    /// <param name="loop">The region the file says to repeat, or <see cref="LoopRegion.None"/>.</param>
    /// <param name="markers">The sound's markers in frame order, or null for none.</param>
    public SoundDescription(
        long frameCount, int sampleRate, LoopRegion loop = default, IReadOnlyList<AudioMarker>? markers = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (loop.EndFrame > frameCount)
            throw new ArgumentOutOfRangeException(nameof(loop), loop, "A loop cannot end past the sound.");

        FrameCount = frameCount;
        SampleRate = sampleRate;
        Loop = loop;
        _markers = markers;
    }

    /// <summary>Length in sample frames.</summary>
    public long FrameCount { get; }

    /// <summary>Sample frames a second.</summary>
    public int SampleRate { get; }

    /// <summary>The region the file says to repeat, or <see cref="LoopRegion.None"/>.</summary>
    public LoopRegion Loop { get; }

    /// <summary>The sound's markers in frame order. Empty when it has none.</summary>
    public IReadOnlyList<AudioMarker> Markers => _markers ?? Array.Empty<AudioMarker>();

    /// <summary>How many seconds into the sound a frame is.</summary>
    public double SecondsAt(long frame) => SampleRate > 0 ? (double)frame / SampleRate : 0d;
}
