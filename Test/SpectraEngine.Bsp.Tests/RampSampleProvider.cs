using SpectraEngine.Core.Audio;

namespace SpectraEngine.Bsp.Tests;

// Sample at frame n is n, so an uploaded buffer says which source frames it holds.
// A constant signal could not tell a wrap from no wrap.
internal sealed class RampSampleProvider : IAudioSampleProvider
{
    public RampSampleProvider(AudioFormat format, long frameCount, LoopRegion loop)
    {
        Format = format;
        FrameCount = frameCount;
        Loop = loop;
    }

    public AudioFormat Format { get; }

    public long FrameCount { get; }

    public LoopRegion Loop { get; }

    // Total frames handed out, to catch a fill doing extra work.
    public long FramesRead { get; private set; }

    public int ReadFrames(long offsetFrames, Span<short> destination, int frameCount)
    {
        int channels = Format.Channels;
        long available = FrameCount - offsetFrames;
        if (available <= 0 || frameCount <= 0) return 0;

        int frames = (int)Math.Min(frameCount, available);
        frames = Math.Min(frames, destination.Length / channels);

        for (int i = 0; i < frames; i++)
            for (int c = 0; c < channels; c++)
                destination[(i * channels) + c] = (short)(offsetFrames + i);

        FramesRead += frames;
        return frames;
    }
}
