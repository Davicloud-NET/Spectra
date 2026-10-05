using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Hosting;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// Plays one sound file by itself, so it can be listened to without a level
/// running. One at a time, once through, at the listener and at full gain.
/// Render thread only.
/// </summary>
// The file is read the way a level reads its sounds, so what is heard is the
// cooked sound a game plays. It plays on the audio manager's preview source,
// which is no level's to take, and a level ending leaves it alone.
public sealed class SoundPreview
{
    /// <summary>The log line for a file a host asked for that was not played.</summary>
    public const string RefusedTemplate = "Sound {Path} was not played: {Reason}";

    /// <summary>
    /// The log line for a file that plays after a host was refused it. A shell
    /// drops its problem about the refusal on it.
    /// </summary>
    public const string PlayedAfterRefusalTemplate = "Played sound {Path} after an earlier refusal";

    private readonly AudioManager _audio;
    private readonly AssetManager _assets;
    private readonly ILogger _logger;

    // What hosts asked for and did not get.
    private readonly HashSet<string> _refused = new(StringComparer.OrdinalIgnoreCase);

    // The preview's own copy of the sound that plays, let go when it stops.
    // The next time the file is asked for it is read again, so a file that
    // changed is heard as it is now.
    private AudioAsset? _asset;

    // A file a host asked for that is still being read.
    private SoundPreviewLoad? _loading;

    /// <summary>Builds a preview that plays on <paramref name="audio"/>.</summary>
    /// <param name="assets">Where the sound is read from.</param>
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

    /// <summary>Whether a file a host asked for is still being read, and so not playing yet.</summary>
    public bool IsLoading => _loading is not null;

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
    /// empty. The file is read off this thread and starts in a later
    /// <see cref="Update"/>. One that cannot be played is logged with the reason.
    /// </summary>
    public void Apply(string request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Length == 0)
        {
            Stop();
            return;
        }

        // A second press while the file is being read changes nothing.
        if (_loading is { } reading && string.Equals(reading.Request, request, StringComparison.Ordinal))
            return;

        Stop();

        if (IsAudioOff(out string refusal))
        {
            Refuse(request, refusal);
            return;
        }

        _loading = SoundPreviewLoad.Start(_assets, request);
    }

    /// <summary>
    /// Plays a file from its start, in place of what was playing, and reads it
    /// on this thread. False when it cannot be played, and
    /// <paramref name="refusal"/> then says why.
    /// </summary>
    /// <param name="path">The sound's content path, such as <c>Sounds/door_open.wav</c>.</param>
    public bool Play(string path, out string refusal)
    {
        ArgumentNullException.ThrowIfNull(path);

        Stop();

        if (IsAudioOff(out refusal))
            return false;

        return SoundPreviewLoad.TryOpen(_assets, path, out AudioAsset? asset, out refusal)
            && Start(asset, out refusal);
    }

    /// <summary>
    /// Stops the sound, and gives up on a file that is still being read.
    /// False when no sound was playing.
    /// </summary>
    public bool Stop()
    {
        _loading?.Abandon();
        _loading = null;

        if (_asset is null)
            return false;

        _audio.Preview.Stop();
        Release();
        return true;
    }

    /// <summary>
    /// Starts a file that has been read, and notices a sound that has played
    /// to its end. Call once a frame, after <see cref="AudioManager.Update"/>.
    /// </summary>
    public void Update()
    {
        if (_loading is { HasLanded: true } landed)
        {
            _loading = null;
            Finish(landed);
        }

        if (_asset is not null && !_audio.Preview.IsPlaying)
            Release();
    }

    private void Finish(SoundPreviewLoad landed)
    {
        if (!landed.TryTake(out AudioAsset? asset, out string refusal) || !Start(asset, out refusal))
        {
            Refuse(landed.Request, refusal);
            return;
        }

        if (_refused.Remove(landed.Request))
            _logger.LogInformation(PlayedAfterRefusalTemplate, landed.Request);
    }

    // Takes the sound over: it is released when it stops, or here when it
    // cannot start.
    private bool Start(AudioAsset asset, out string refusal)
    {
        // Once through: a loop region would never end by itself.
        var samples = new AssetSampleProvider(asset, LoopRegion.None);
        if (!_audio.Preview.Play(samples, AudioSourceSettings.Default))
        {
            refusal = asset.FrameCount == 0
                ? "it has no samples"
                : "the audio device has no source left for it";
            asset.Dispose();
            return false;
        }

        _asset = asset;
        Path = asset.SourcePath;
        refusal = string.Empty;
        return true;
    }

    private void Release()
    {
        _asset?.Dispose();
        _asset = null;
        Path = string.Empty;
    }

    private void Refuse(string request, string refusal)
    {
        _refused.Add(request);
        _logger.LogWarning(RefusedTemplate, request, refusal);
    }

    private bool IsAudioOff(out string refusal)
    {
        refusal = _audio.IsEnabled
            ? string.Empty
            : _audio.DisabledReason.Length == 0 ? "audio is off" : "audio is off: " + _audio.DisabledReason;

        return !_audio.IsEnabled;
    }
}
