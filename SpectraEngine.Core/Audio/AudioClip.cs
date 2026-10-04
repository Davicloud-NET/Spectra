using System;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// A decoded sound plus its loop points. Created and destroyed through
/// <see cref="AudioManager"/>, which owns the AL buffer.
/// </summary>
// Samples are kept only for a looping clip, which is fed through a buffer
// queue. A one-shot lives in one AL buffer and is never read again.
public sealed class AudioClip
{
    private short[]? _samples;

    internal AudioClip(AudioFormat format, LoopRegion loop, long frameCount, uint buffer, short[]? samples)
    {
        Format = format;
        Loop = loop;
        FrameCount = frameCount;
        Buffer = buffer;
        _samples = samples;
    }

    /// <summary>Rate and channel count.</summary>
    public AudioFormat Format { get; }

    /// <summary>The region this clip repeats, or <see cref="LoopRegion.None"/>.</summary>
    public LoopRegion Loop { get; }

    /// <summary>Decoded length in sample frames.</summary>
    public long FrameCount { get; }

    /// <summary>Seconds of audio.</summary>
    public double Duration => Format.FramesToSeconds(FrameCount);

    // 0 for a looping clip.
    internal uint Buffer { get; private set; }

    /// <summary>True once the manager has destroyed it; playing it is a no-op afterwards.</summary>
    public bool IsDestroyed { get; private set; }

    // Interleaved PCM, looping clips only.
    internal ReadOnlySpan<short> Samples => _samples is { } samples ? new ReadOnlySpan<short>(samples) : default;

    internal bool HasSamples => _samples is not null;

    internal void MarkDestroyed()
    {
        IsDestroyed = true;
        Buffer = 0;
        _samples = null;
    }
}

// Feeds a looping clip's samples to a StreamingVoice.
internal sealed class ClipSampleProvider : IAudioSampleProvider
{
    private readonly AudioClip _clip;

    public ClipSampleProvider(AudioClip clip) => _clip = clip;

    public AudioFormat Format => _clip.Format;

    public long FrameCount => _clip.FrameCount;

    public LoopRegion Loop => _clip.Loop;

    public int ReadFrames(long offsetFrames, Span<short> destination, int frameCount)
    {
        ReadOnlySpan<short> samples = _clip.Samples;
        if (samples.IsEmpty || frameCount <= 0) return 0;

        int channels = _clip.Format.Channels;
        long available = _clip.FrameCount - offsetFrames;
        if (available <= 0) return 0;

        int frames = (int)Math.Min(frameCount, available);
        frames = Math.Min(frames, destination.Length / channels);
        if (frames <= 0) return 0;

        samples.Slice((int)(offsetFrames * channels), frames * channels).CopyTo(destination);
        return frames;
    }
}
