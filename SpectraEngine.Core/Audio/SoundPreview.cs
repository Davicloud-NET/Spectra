using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Hosting;
using System;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// Plays one sound file by itself, so it can be listened to without a level
/// running. One at a time, once through, at the listener and at full gain.
/// Render thread only.
/// </summary>
// The file is loaded the way a level loads its sounds, so what is heard is
// the cooked sound a game plays. It plays on the audio manager's preview
// source, which is no level's to take, and a level ending leaves it alone.
public sealed class SoundPreview
{
    private readonly AudioManager _audio;
    private readonly AssetManager _assets;
    private readonly ILogger _logger;

    /// <summary>Builds a preview that plays on <paramref name="audio"/>.</summary>
    /// <param name="assets">Where the sound is loaded from.</param>
    /// <param name="logger">Told why a sound a host asked for was not played.</param>
    public SoundPreview(AudioManager audio, AssetManager assets, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(logger);

        _audio = audio;
        _assets = assets;
        _logger = logger;
    }

    /// <summary>The content path of the sound that is playing, or empty when none is.</summary>
    public string Path { get; private set; } = string.Empty;

    /// <summary>Whether a sound is playing.</summary>
    public bool IsPlaying => Path.Length > 0;

    /// <summary>
    /// Takes what <paramref name="host"/> was last asked through
    /// <see cref="EngineHost.RequestSoundPreview"/>, if anything, and does it.
    /// Call once a frame.
    /// </summary>
    public void TakeRequest(EngineHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (host.TryTakeSoundPreviewRequest(out string? request))
            Apply(request);
    }

    /// <summary>
    /// Does what a host asked: plays the file, or stops when the path is
    /// empty. A file that cannot be played is logged once, with the reason.
    /// </summary>
    public void Apply(string request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Length == 0)
        {
            Stop();
            return;
        }

        if (!Play(request, out string refusal))
            _logger.LogWarning("Sound {Path} was not played: {Reason}", request, refusal);
    }

    /// <summary>
    /// Plays a file from its start, in place of what was playing. False when
    /// it cannot be played, and <paramref name="refusal"/> then says why.
    /// </summary>
    /// <param name="path">The sound's content path, such as <c>Sounds/door_open.wav</c>.</param>
    public bool Play(string path, out string refusal)
    {
        ArgumentNullException.ThrowIfNull(path);

        Stop();

        if (!_audio.IsEnabled)
        {
            refusal = _audio.DisabledReason.Length == 0 ? "audio is off" : "audio is off: " + _audio.DisabledReason;
            return false;
        }

        if (!TryLoad(path, out string key, out AudioAsset? asset, out refusal))
            return false;

        // Once through: a loop region would never end by itself.
        var samples = new AssetSampleProvider(asset, LoopRegion.None);
        if (!_audio.Preview.Play(samples, AudioSourceSettings.Default))
        {
            refusal = asset.FrameCount == 0
                ? "it has no samples"
                : "the audio device has no source left for it";
            return false;
        }

        Path = key;
        return true;
    }

    /// <summary>Stops the sound. False when none was playing.</summary>
    public bool Stop()
    {
        if (Path.Length == 0)
            return false;

        _audio.Preview.Stop();
        Path = string.Empty;
        return true;
    }

    /// <summary>
    /// Notices a sound that has played to its end. Call once a frame, after
    /// <see cref="AudioManager.Update"/>.
    /// </summary>
    public void Update()
    {
        if (Path.Length > 0 && !_audio.Preview.IsPlaying)
            Path = string.Empty;
    }

    private bool TryLoad(
        string path, out string key, [NotNullWhen(true)] out AudioAsset? asset, out string refusal)
    {
        key = string.Empty;
        asset = null;

        try
        {
            key = ContentRoot.NormalizeRelativePath(path);
            asset = _assets.LoadAudio(key);

            bool isWhole = asset.Samples.Length == asset.Format.FramesToSamples(asset.FrameCount);
            refusal = isWhole ? string.Empty : "it was released while it was opened";
            return isWhole;
        }
        // A boundary: whatever the load throws becomes the reason, and nothing
        // reaches the frame loop.
        catch (Exception failure)
        {
            asset = null;
            refusal = failure.Message;
            return false;
        }
    }
}
