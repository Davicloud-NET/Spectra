using SpectraEngine.Core.Assets;
using System;

namespace SpectraEngine.Core.Audio;

// Feeds a loaded sound to a streaming voice straight from the asset's own
// samples, with no copy. It holds no position, so every voice playing the
// sound can share one.
internal sealed class AssetSampleProvider : IAudioSampleProvider
{
    private readonly AudioAsset _asset;

    public AssetSampleProvider(AudioAsset asset, LoopRegion loop)
    {
        _asset = asset;
        Loop = loop;
    }

    public AudioFormat Format => _asset.Format;

    public long FrameCount => _asset.FrameCount;

    public LoopRegion Loop { get; }

    public int ReadFrames(long offsetFrames, Span<short> destination, int frameCount)
    {
        // Empty once the asset is released. The voice then runs dry and ends.
        ReadOnlySpan<short> samples = _asset.Samples;
        int channels = _asset.Format.Channels;

        long available = (samples.Length / channels) - offsetFrames;
        if (offsetFrames < 0 || available <= 0 || frameCount <= 0) return 0;

        int frames = (int)Math.Min(frameCount, available);
        frames = Math.Min(frames, destination.Length / channels);
        if (frames <= 0) return 0;

        samples.Slice((int)(offsetFrames * channels), frames * channels).CopyTo(destination);
        return frames;
    }
}
