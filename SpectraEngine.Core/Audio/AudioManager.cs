using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Extensions.Logging;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// The engine's audio device: one OpenAL context, one listener, a fixed pool of
/// sources, and the voices playing on them. Render thread only, except
/// <see cref="Initialize"/> and <see cref="Shutdown"/>. With no device every
/// call is a no-op.
/// </summary>
// One thread for all AL calls: alGetError is a single latch per context, and
// the pool and the stream pump both read driver state and then act on it.
// Other threads post through EngineHost.EnqueueCommand.
// Loops never use AL_LOOPING, see AudioLoopCursor.
public sealed class AudioManager : IDisposable
{
    /// <summary>Sources asked of the driver. It may grant fewer.</summary>
    public const int DefaultSourceCount = 32;

    private readonly ILogger _logger;
    private readonly AudioBackendFactory _factory;
    private readonly int _requestedSourceCount;
    private readonly List<AudioVoice> _voices = new();
    private readonly List<AudioClip> _clips = new();

    private IAudioBackend? _backend;
    private AudioSourcePool? _pool;
    private float _masterGain = 1f;
    private bool _initialized;
    private bool _disposed;

    /// <summary>Creates a manager that opens the default OpenAL device on <see cref="Initialize"/>.</summary>
    public AudioManager(ILogger logger)
        : this(logger, OpenAlBackend.TryCreate, DefaultSourceCount)
    {
    }

    /// <summary>Creates a manager over a supplied backend factory, for tests with no sound card.</summary>
    public AudioManager(ILogger logger, AudioBackendFactory factory, int sourceCount = DefaultSourceCount)
    {
        if (sourceCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceCount), sourceCount, "A source pool needs at least one source.");

        _logger = logger;
        _factory = factory;
        _requestedSourceCount = sourceCount;
    }

    /// <summary>False when no device could be opened. Every call is a no-op in that state.</summary>
    public bool IsEnabled => _backend is not null;

    /// <summary>Why audio is off, or empty when it is on. Logged once at startup.</summary>
    public string DisabledReason { get; private set; } = string.Empty;

    /// <summary>The opened device's name, or empty when disabled.</summary>
    public string DeviceName => _backend?.DeviceName ?? string.Empty;

    /// <summary>Sources the driver granted; 0 when disabled.</summary>
    public int SourceCount => _pool?.Capacity ?? 0;

    /// <summary>Voices currently playing.</summary>
    public int ActiveVoiceCount => _voices.Count;

    /// <summary>Sounds cut off because every source was busy.</summary>
    public int StolenVoiceCount => _pool?.StolenCount ?? 0;

    /// <summary>Sounds dropped because every source was carrying a stream.</summary>
    public int DroppedVoiceCount => _pool?.StarvedCount ?? 0;

    /// <summary>Master gain, applied after every source's own. Negative values clamp to zero.</summary>
    public float MasterGain
    {
        get => _masterGain;
        set
        {
            _masterGain = Math.Max(0f, value);
            _backend?.SetListenerGain(_masterGain);
        }
    }

    /// <summary>Where the listener is, in world units.</summary>
    public Vector3 ListenerPosition { get; private set; }

    /// <summary>Which way the listener faces.</summary>
    public Vector3 ListenerForward { get; private set; } = -Vector3.UnitZ;

    /// <summary>The listener's up axis.</summary>
    public Vector3 ListenerUp { get; private set; } = Vector3.UnitY;

    /// <summary>The listener's velocity, for Doppler.</summary>
    public Vector3 ListenerVelocity { get; private set; }

    /// <summary>
    /// Opens the audio device, or disables audio and logs why. Main thread,
    /// before the render thread starts. Idempotent.
    /// </summary>
    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        if (!_factory(_logger, out IAudioBackend? backend, out string failureReason))
        {
            DisabledReason = string.IsNullOrEmpty(failureReason) ? "the audio device could not be opened" : failureReason;

            // Warning, not Error: smoke gates grep for ERR and no sound card is fine.
            _logger.LogWarning("Audio disabled: {Reason}. Every audio call is a no-op for this session", DisabledReason);
            return;
        }

        _backend = backend;
        _pool = new AudioSourcePool(backend, _requestedSourceCount);
        backend.SetListenerGain(_masterGain);
        backend.SetListener(ListenerPosition, ListenerVelocity, ListenerForward, ListenerUp);

        _logger.LogInformation(
            "Audio manager initialized on {Device} with {Sources} sources",
            backend.DeviceName,
            _pool.Capacity);

        if (_pool.Capacity < _requestedSourceCount)
        {
            _logger.LogWarning(
                "The audio driver granted {Granted} of {Requested} sources; sounds past that will reclaim or be dropped",
                _pool.Capacity,
                _requestedSourceCount);
        }
    }

    /// <summary>
    /// Stops everything, frees every clip and source, and closes the device.
    /// Main thread, after the render thread has been joined. Idempotent.
    /// </summary>
    public void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;

        if (_backend is null)
        {
            _logger.LogInformation("Audio manager shut down (was disabled: {Reason})", DisabledReason);
            return;
        }

        for (int i = 0; i < _voices.Count; i++)
        {
            _voices[i].Stop();
            _voices[i].Detach();
        }

        _voices.Clear();

        for (int i = 0; i < _clips.Count; i++)
        {
            if (_clips[i].Buffer != 0) _backend.DestroyBuffer(_clips[i].Buffer);
            _clips[i].MarkDestroyed();
        }

        _clips.Clear();

        _pool?.ReleaseAll();
        _pool?.Dispose();
        _pool = null;

        _backend.Dispose();
        _backend = null;

        _logger.LogInformation("Audio manager shut down");
    }

    /// <summary>
    /// Places the listener. Render thread, once a frame, from the active camera.
    /// </summary>
    public void SetListener(Vector3 position, Vector3 forward, Vector3 up) =>
        SetListener(position, forward, up, Vector3.Zero);

    /// <inheritdoc cref="SetListener(Vector3, Vector3, Vector3)" />
    public void SetListener(Vector3 position, Vector3 forward, Vector3 up, Vector3 velocity)
    {
        ListenerPosition = position;
        ListenerForward = forward;
        ListenerUp = up;
        ListenerVelocity = velocity;
        _backend?.SetListener(position, velocity, forward, up);
    }

    /// <summary>
    /// Uploads decoded PCM16 and returns the clip that owns it, or null when
    /// audio is disabled. Render thread.
    /// </summary>
    /// <param name="pcm">Interleaved samples. Length must be a whole number of frames.</param>
    /// <param name="loop">The region to repeat, in sample frames.</param>
    public AudioClip? CreateClip(AudioFormat format, ReadOnlySpan<short> pcm, LoopRegion loop = default)
    {
        if (_backend is null) return null;

        if (pcm.Length % format.Channels != 0)
            throw new ArgumentException("PCM length is not a whole number of sample frames.", nameof(pcm));

        long frames = format.SamplesToFrames(pcm.Length);
        if (loop.IsLooping && loop.EndFrame > frames)
            throw new ArgumentOutOfRangeException(nameof(loop), loop, "A loop cannot end past the clip.");

        AudioClip clip;
        if (loop.IsLooping)
        {
            // No AL buffer: a looping clip is queued from this array.
            clip = new AudioClip(format, loop, frames, buffer: 0, samples: pcm.ToArray());
        }
        else
        {
            uint buffer = _backend.CreateBuffer();
            _backend.UploadBuffer(buffer, ToBufferFormat(format), pcm, format.SampleRate);
            clip = new AudioClip(format, loop, frames, buffer, samples: null);
        }

        _clips.Add(clip);
        return clip;
    }

    /// <summary>
    /// Stops every voice playing the clip and frees it. Render thread. Idempotent.
    /// </summary>
    public void DestroyClip(AudioClip? clip)
    {
        if (_backend is null || clip is null || clip.IsDestroyed) return;
        if (!_clips.Remove(clip)) return;

        if (clip.Buffer != 0)
        {
            // Release the sources first. Deleting a buffer still bound to a
            // source is AL_INVALID_OPERATION and the buffer leaks.
            RetireVoicesPlaying(clip);
            _backend.DestroyBuffer(clip.Buffer);
        }

        clip.MarkDestroyed();
    }

    /// <summary>
    /// Plays a clip once, or on its loop when it has one. Returns null when
    /// audio is disabled, the clip is gone, or every source is carrying a
    /// stream. Render thread.
    /// </summary>
    public AudioVoice? Play(AudioClip? clip) => Play(clip, AudioSourceSettings.Default);

    /// <inheritdoc cref="Play(AudioClip)" />
    public AudioVoice? Play(AudioClip? clip, in AudioSourceSettings settings)
    {
        if (_backend is null || _pool is null || clip is null || clip.IsDestroyed) return null;

        bool streaming = clip.Loop.IsLooping;
        if (!_pool.TryAcquire(streaming, out uint source)) return null;
        RetireVoiceOn(source);

        AudioVoice voice = streaming
            ? new StreamingVoice(_backend, source, new ClipSampleProvider(clip), settings)
            : new StaticVoice(_backend, source, clip, settings);

        return Track(voice, source);
    }

    /// <summary>
    /// Plays a long sound through a buffer queue fed by
    /// <paramref name="provider"/>. Returns null when audio is disabled or no
    /// source could be had. Render thread.
    /// </summary>
    /// <param name="startFrame">The sample frame to start at, for a sound that is already under way.</param>
    public StreamingVoice? PlayStream(
        IAudioSampleProvider provider, in AudioSourceSettings settings, long startFrame = 0)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentOutOfRangeException.ThrowIfNegative(startFrame);
        if (_backend is null || _pool is null) return null;
        if (!_pool.TryAcquire(streaming: true, out uint source)) return null;
        RetireVoiceOn(source);

        var voice = new StreamingVoice(_backend, source, provider, settings, startFrame: startFrame);
        return (StreamingVoice?)Track(voice, source);
    }

    /// <summary>
    /// Ends a voice and frees its source now, where <see cref="AudioVoice.Stop"/>
    /// leaves the source held until the next <see cref="Update"/>. Render thread.
    /// </summary>
    public void Release(AudioVoice? voice)
    {
        if (_pool is null || voice is null) return;

        int index = _voices.IndexOf(voice);
        if (index < 0) return;

        uint source = voice.Source;
        voice.Stop();
        voice.Detach();
        _pool.Release(source);
        _voices.RemoveAt(index);
    }

    /// <summary>Stops every voice without touching the clips they were playing. Render thread.</summary>
    public void StopAll()
    {
        for (int i = 0; i < _voices.Count; i++)
            _voices[i].Stop();
    }

    /// <summary>
    /// Refills every streaming queue and returns finished sources to the pool.
    /// Render thread, every frame: a skipped frame makes streams stutter.
    /// Returns the number of voices still playing.
    /// </summary>
    public int Update()
    {
        if (_backend is null || _pool is null) return 0;

        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            AudioVoice voice = _voices[i];
            if (voice.Update()) continue;

            uint source = voice.Source;
            voice.Detach();
            _pool.Release(source);
            _voices.RemoveAt(i);
        }

        return _voices.Count;
    }

    /// <summary>Shuts down if it has not already.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
    }

    private AudioVoice Track(AudioVoice voice, uint source)
    {
        // A zero-length stream finishes in its constructor. Give the source back.
        if (voice.IsFinished)
        {
            voice.Detach();
            _pool!.Release(source);
            return voice;
        }

        _voices.Add(voice);
        return voice;
    }

    // A full pool hands a one-shot's source to the new sound. The voice that
    // had it must let go, or it would go on configuring a sound that is not its own.
    private void RetireVoiceOn(uint source)
    {
        for (int i = 0; i < _voices.Count; i++)
        {
            if (_voices[i].Source != source) continue;

            _voices[i].Detach();
            _voices.RemoveAt(i);
            return;
        }
    }

    // Only static voices bind the clip's buffer. Streaming voices own theirs.
    private void RetireVoicesPlaying(AudioClip clip)
    {
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            if (_voices[i] is not StaticVoice voice || voice.Clip != clip) continue;

            uint source = voice.Source;
            voice.Stop();
            voice.Detach();
            _pool!.Release(source);
            _voices.RemoveAt(i);
        }
    }

    private static AudioBufferFormat ToBufferFormat(AudioFormat format) =>
        format.Channels == 1 ? AudioBufferFormat.Mono16 : AudioBufferFormat.Stereo16;
}
