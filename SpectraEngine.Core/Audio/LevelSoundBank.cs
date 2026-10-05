using Microsoft.Extensions.Logging;
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
    private readonly ILogger _logger;

    // Null for a sound that cannot be loaded, so it is asked for once.
    private readonly Dictionary<string, LevelSound?> _sounds = new(StringComparer.Ordinal);

    public LevelSoundBank(AudioManager audio, AssetManager assets, ILogger logger)
    {
        _audio = audio;
        _assets = assets;
        _logger = logger;
    }

    // False for a sound that cannot be played here. The log says why, once
    // for each path.
    public bool TryGet(in SoundEmitter emitter, [NotNullWhen(true)] out LevelSound? sound)
    {
        if (!_sounds.TryGetValue(emitter.Path, out sound) || sound is { Asset.IsReleased: true })
        {
            // Unloaded since it was opened. The file is opened again, and a
            // buffer made from the old samples goes with them.
            _audio.DestroyClip(sound?.Clip);
            sound = Load(emitter.Path);
            _sounds[emitter.Path] = sound;
        }

        if (sound is null)
            return false;

        // The level counts this sound's frames. A file of another length is
        // another sound, and its loop may not fit.
        if (sound.Asset.FrameCount == emitter.FrameCount)
            return true;

        if (!sound.HasLengthWarning)
        {
            sound.HasLengthWarning = true;
            _logger.LogWarning(
                "Sound {Path} will not be heard: the level counted {Counted} frames and the file has {Loaded}",
                emitter.Path,
                emitter.FrameCount,
                sound.Asset.FrameCount);
        }

        return false;
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
            if (asset.Samples.Length == asset.Format.FramesToSamples(asset.FrameCount))
                return new LevelSound(asset);

            _logger.LogWarning("Sound {Path} will not be heard: it was released while it was opened", path);
        }
        // A boundary: whatever the load throws is logged here, and nothing
        // reaches the frame loop.
        catch (Exception failure)
        {
            _logger.LogWarning("Sound {Path} will not be heard: {Reason}", path, failure.Message);
        }

        return null;
    }
}
