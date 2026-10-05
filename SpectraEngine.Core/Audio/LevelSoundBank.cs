using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Audio;

// The sounds a running level has asked to hear, by content path. Each is
// loaded once and started as a voice from here. Render thread only.
internal sealed class LevelSoundBank
{
    private readonly AudioManager _audio;
    private readonly AssetManager _assets;

    // Null for a sound that cannot be loaded, so it is asked for once.
    private readonly Dictionary<string, LevelSound?> _sounds = new(StringComparer.Ordinal);

    public LevelSoundBank(AudioManager audio, AssetManager assets)
    {
        _audio = audio;
        _assets = assets;
    }

    // False for a sound that cannot be played here.
    public bool TryGet(in SoundEmitter emitter, [NotNullWhen(true)] out LevelSound? sound)
    {
        if (!_sounds.TryGetValue(emitter.Path, out sound))
        {
            sound = Load(emitter.Path);
            _sounds.Add(emitter.Path, sound);
        }

        // The level counts this sound's frames. A file of another length is
        // another sound, and its loop may not fit.
        return sound is not null && sound.Asset.FrameCount == emitter.FrameCount;
    }

    // Null when the device has no source to give.
    public AudioVoice? Start(
        LevelSound sound, in SoundEmitter emitter, long startFrame, in AudioSourceSettings settings)
    {
        if (startFrame == 0 && !emitter.Loop.IsLooping && sound.IsShort)
        {
            sound.Clip ??= _audio.CreateClip(sound.Asset.Format, sound.Asset.Samples);
            return _audio.Play(sound.Clip, in settings);
        }

        return _audio.PlayStream(sound.ProviderFor(emitter.Loop), in settings, startFrame);
    }

    // Frees every buffer made here. The assets stay with the asset manager.
    public void Clear()
    {
        foreach (LevelSound? sound in _sounds.Values)
            _audio.DestroyClip(sound?.Clip);

        _sounds.Clear();
    }

    private LevelSound? Load(string path)
    {
        try
        {
            AudioAsset asset = _assets.LoadAudio(path);
            bool isWhole = asset.Samples.Length == asset.Format.FramesToSamples(asset.FrameCount);
            return isWhole ? new LevelSound(asset) : null;
        }
        // A boundary: the level said what is wrong with this sound when it
        // spawned. Here it is counted, and nothing reaches the frame loop.
        catch (Exception)
        {
            return null;
        }
    }
}
