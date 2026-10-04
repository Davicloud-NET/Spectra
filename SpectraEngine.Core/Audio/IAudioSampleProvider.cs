using System;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// Where a <see cref="StreamingVoice"/> gets its frames. Random access by
/// frame offset, because loops and seeks both jump. Called on the render
/// thread every frame, so it must not block.
/// </summary>
public interface IAudioSampleProvider
{
    /// <summary>Rate and channel count of the frames this provider returns.</summary>
    AudioFormat Format { get; }

    /// <summary>Total decoded sample frames.</summary>
    long FrameCount { get; }

    /// <summary>The region to repeat, or <see cref="LoopRegion.None"/>.</summary>
    LoopRegion Loop { get; }

    /// <summary>
    /// Copies <paramref name="frameCount"/> sample frames starting at
    /// <paramref name="offsetFrames"/> into <paramref name="destination"/>, as
    /// interleaved samples. Returns frames written, fewer at the end of the sound.
    /// </summary>
    int ReadFrames(long offsetFrames, Span<short> destination, int frameCount);
}
