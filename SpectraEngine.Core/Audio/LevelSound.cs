using SpectraEngine.Core.Assets;

namespace SpectraEngine.Core.Audio;

// One sound a running level plays, in the two forms the device takes it: a
// buffer for a short sound played once from its start, and the asset's own
// samples for everything else.
internal sealed class LevelSound
{
    private AssetSampleProvider? _once;
    private AssetSampleProvider? _looped;

    public LevelSound(AudioAsset asset) => Asset = asset;

    public AudioAsset Asset { get; }

    public bool IsStereo => Asset.Format.Channels == 2;

    // The cook marks a sound it found too long to keep in one buffer.
    public bool IsShort => !Asset.Info.IsStreaming;

    // Made the first time the sound is played that way.
    public AudioClip? Clip { get; set; }

    // Kept, so starting a voice makes no new provider.
    public AssetSampleProvider ProviderFor(LoopRegion loop)
    {
        if (!loop.IsLooping)
            return _once ??= new AssetSampleProvider(Asset, LoopRegion.None);

        if (_looped is null || _looped.Loop != loop)
            _looped = new AssetSampleProvider(Asset, loop);

        return _looped;
    }
}
