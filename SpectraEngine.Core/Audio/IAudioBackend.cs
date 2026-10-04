using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.Extensions.Logging;

namespace SpectraEngine.Core.Audio;

/// <summary>What OpenAL reports a source is doing right now.</summary>
public enum AudioSourceState
{
    /// <summary>Created and never played.</summary>
    Initial,

    /// <summary>Consuming its buffer or its queue.</summary>
    Playing,

    /// <summary>Held mid-sound; resuming continues from the same offset.</summary>
    Paused,

    /// <summary>
    /// Finished, stopped, or starved: a streaming source that ran out of
    /// queued data also reports this.
    /// </summary>
    Stopped,
}

/// <summary>PCM16 buffer layouts, the only ones core OpenAL takes.</summary>
public enum AudioBufferFormat
{
    /// <summary>One channel. Required for a positional source.</summary>
    Mono16,

    /// <summary>Two interleaved channels. Never positional.</summary>
    Stereo16,
}

/// <summary>Gain, pitch and placement of a source.</summary>
/// <param name="Gain">Linear amplitude multiplier; 1 is unattenuated.</param>
/// <param name="Pitch">Playback rate multiplier; 1 is the authored rate.</param>
/// <param name="Position">World position. Ignored by the driver for a stereo buffer.</param>
/// <param name="Velocity">World velocity, for Doppler.</param>
/// <param name="Relative">True pins the source to the listener.</param>
public readonly record struct AudioSourceSettings(
    float Gain,
    float Pitch,
    Vector3 Position,
    Vector3 Velocity,
    bool Relative)
{
    /// <summary>Unattenuated, unpitched, at the listener.</summary>
    public static AudioSourceSettings Default => new(1f, 1f, Vector3.Zero, Vector3.Zero, Relative: true);

    /// <summary>Unattenuated and unpitched at a world point.</summary>
    public static AudioSourceSettings At(Vector3 position) => new(1f, 1f, position, Vector3.Zero, Relative: false);
}

/// <summary>
/// The OpenAL calls the engine makes, behind a seam so tests can fake the
/// driver. One thread at a time, see <see cref="AudioManager"/>.
/// </summary>
// No member can ask for AL_LOOPING. Keep it that way.
public interface IAudioBackend : IDisposable
{
    /// <summary>Human-readable device name.</summary>
    string DeviceName { get; }

    /// <summary>Allocates an AL buffer handle.</summary>
    uint CreateBuffer();

    /// <summary>Frees an AL buffer handle. Must not be queued on a live source.</summary>
    void DestroyBuffer(uint buffer);

    /// <summary>Uploads interleaved PCM16 into a buffer, replacing whatever it held.</summary>
    void UploadBuffer(uint buffer, AudioBufferFormat format, ReadOnlySpan<short> pcm, int sampleRate);

    /// <summary>Allocates an AL source handle. False when the driver refuses.</summary>
    bool TryCreateSource(out uint source);

    /// <summary>Frees an AL source handle.</summary>
    void DestroySource(uint source);

    /// <summary>Applies gain, pitch, position, velocity and listener-relative in one call.</summary>
    void ConfigureSource(uint source, in AudioSourceSettings settings);

    /// <summary>Reads the source's play state.</summary>
    AudioSourceState GetSourceState(uint source);

    /// <summary>Buffers the source has finished with and is waiting to hand back.</summary>
    int GetBuffersProcessed(uint source);

    /// <summary>Buffers queued on the source, processed ones included.</summary>
    int GetBuffersQueued(uint source);

    /// <summary>
    /// Binds a single buffer to a static source, or detaches with 0. AL
    /// refuses to queue on a source still holding a static buffer.
    /// </summary>
    void SetSourceBuffer(uint source, uint buffer);

    /// <summary>Appends a buffer to the source's play queue.</summary>
    void QueueBuffer(uint source, uint buffer);

    /// <summary>Takes one processed buffer back off the head of the queue.</summary>
    uint UnqueueBuffer(uint source);

    /// <summary>Starts or resumes the source.</summary>
    void Play(uint source);

    /// <summary>Stops the source and rewinds it. Processed buffers stay queued until unqueued.</summary>
    void Stop(uint source);

    /// <summary>Holds the source at its current offset.</summary>
    void Pause(uint source);

    /// <summary>Places the listener.</summary>
    void SetListener(Vector3 position, Vector3 velocity, Vector3 forward, Vector3 up);

    /// <summary>Master gain, applied by the driver after every source's own.</summary>
    void SetListenerGain(float gain);
}

/// <summary>Opens the audio device, or says why it could not.</summary>
/// <param name="failureReason">Logged verbatim, so write it for a user: "no audio device".</param>
public delegate bool AudioBackendFactory(
    ILogger logger,
    [NotNullWhen(true)] out IAudioBackend? backend,
    out string failureReason);
