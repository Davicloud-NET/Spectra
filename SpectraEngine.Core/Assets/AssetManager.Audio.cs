using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets.Audio;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Core.Assets;

// Audio: reads cooked .saudio only. Core has no WAV decoder, so an uncooked
// sound is refused. Probe and open both go through AudioContentPath.Resolve.
public sealed partial class AssetManager
{
    private readonly object _audioSync = new();
    private readonly Dictionary<string, AudioAsset> _audio = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of cooked sounds currently open. Any thread.</summary>
    public int AudioCount
    {
        get { lock (_audioSync) return _audio.Count; }
    }

    /// <summary>
    /// Opens a cooked sound, or returns the cached one. Any thread. Throws on a
    /// missing or unplayable sound; there is no placeholder to fall back to.
    /// </summary>
    /// <param name="relativePath">
    /// Content path of the authored sound, e.g. <c>Sounds/door_open.wav</c>. The
    /// cooked file beside it is what gets opened.
    /// </param>
    /// <exception cref="FileNotFoundException">No mounted source has that sound.</exception>
    /// <exception cref="Audio.SaudioFormatException">The cooked file is not one this engine can play.</exception>
    /// <exception cref="InvalidDataException">The path names an authored sound with no cooked file beside it.</exception>
    public AudioAsset LoadAudio(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string key = ContentRoot.NormalizeRelativePath(relativePath);

        lock (_audioSync)
        {
            if (_audio.TryGetValue(key, out AudioAsset? cached)) return cached;
        }

        AudioAsset asset = ReadAudioThroughContent(key);

        lock (_audioSync)
        {
            // Lost a race: keep theirs, release ours so the mount is not held.
            if (_audio.TryGetValue(key, out AudioAsset? raced))
            {
                asset.Dispose();
                return raced;
            }

            _audio[key] = asset;
        }

        _logger.LogInformation(
            "Loaded sound {Path} ({Frames} frames, {Format}{Loop})",
            asset.ResolvedPath,
            asset.FrameCount,
            asset.Format,
            asset.Loop.IsLooping ? $", loop {asset.Loop}" : string.Empty);

        return asset;
    }

    /// <summary>
    /// Whether any mounted source can answer for <paramref name="relativePath"/>
    /// as a sound. Any thread.
    /// </summary>
    public bool AudioExists(string relativePath)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);
        return Content.Exists(AudioContentPath.Resolve(Content, key));
    }

    /// <summary>
    /// Releases one open sound and its content reference. Render thread only.
    /// Returns false when nothing was open under that path. A voice a level
    /// streams from the sound runs dry and ends. Clips already created from it
    /// keep playing; they hold their own copy of the samples.
    /// </summary>
    // A level streams its looped and long sounds from these bytes in place,
    // on the render thread. Released from another one, a read in progress
    // would be left with unmapped memory.
    public bool UnloadAudio(string relativePath)
    {
        string key = ContentRoot.NormalizeRelativePath(relativePath);

        AudioAsset? asset;
        lock (_audioSync)
        {
            if (!_audio.Remove(key, out asset)) return false;
        }

        asset.Dispose();
        _logger.LogInformation("Unloaded sound {Path}", key);
        return true;
    }

    // Any thread.
    private AudioAsset ReadAudioThroughContent(string key)
    {
        string resolved = AudioContentPath.Resolve(Content, key);

        if (!AudioContentPath.IsCooked(resolved))
        {
            throw new InvalidDataException(
                $"Sound '{resolved}' has no cooked '{AudioContentPath.CookedPathFor(key)}' beside it, and the " +
                "engine reads cooked audio only; run scook over the project.");
        }

        ContentBlob blob = OpenOrThrow(resolved);

        try
        {
            // The asset keeps the blob: its samples are a span into these bytes.
            SaudioInfo info = SaudioReader.Read(blob.Span, resolved);
            return new AudioAsset(key, resolved, info, blob);
        }
        catch
        {
            blob.Dispose();
            throw;
        }
    }

    // Blobs are content references, not GPU objects, so this needs no render thread.
    private void ReleaseAudioResources()
    {
        List<AudioAsset> open;
        lock (_audioSync)
        {
            if (_audio.Count == 0) return;

            open = new List<AudioAsset>(_audio.Values);
            _audio.Clear();
        }

        for (int i = 0; i < open.Count; i++) open[i].Dispose();
        _logger.LogInformation("Asset manager released {Count} open sound(s)", open.Count);
    }
}
