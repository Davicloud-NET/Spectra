using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Silk.NET.OpenAL;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// <see cref="IAudioBackend"/> over Silk.NET's OpenAL bindings: the default
/// device and one context.
/// </summary>
// The context is current for the process (core alcMakeContextCurrent), so the
// main thread can open it and the render thread use it. Don't switch to
// ALC_EXT_thread_local_context.
// alGetError is checked on create and upload only, not per property write.
public sealed unsafe class OpenAlBackend : IAudioBackend
{
    private readonly ILogger _logger;
    private readonly AL _al;
    private readonly ALContext _alc;

    private Device* _device;
    private Context* _context;
    private bool _disposed;

    private OpenAlBackend(ILogger logger, AL al, ALContext alc, Device* device, Context* context, string deviceName)
    {
        _logger = logger;
        _al = al;
        _alc = alc;
        _device = device;
        _context = context;
        DeviceName = deviceName;
    }

    /// <inheritdoc />
    public string DeviceName { get; }

    /// <summary>
    /// Opens the default device and makes a context current, or reports why it
    /// could not. Never throws.
    /// </summary>
    public static bool TryCreate(
        ILogger logger,
        [NotNullWhen(true)] out IAudioBackend? backend,
        out string failureReason)
    {
        backend = null;
        failureReason = string.Empty;

        AL al;
        ALContext alc;
        try
        {
            // GetApi throws when no OpenAL runtime is installed. soft asks for
            // the packaged OpenAL Soft first: a system OpenAL can be older and
            // lack filters.
            SilkPlatform.UsePortableRuntimeId();
            alc = ALContext.GetApi(soft: true);
            al = AL.GetApi(soft: true);
        }
        catch (Exception ex)
        {
            failureReason = $"the OpenAL runtime could not be loaded ({ex.GetType().Name}: {ex.Message})";
            return false;
        }

        Device* device = null;
        Context* context = null;
        try
        {
            device = alc.OpenDevice(string.Empty);
            if (device is null)
            {
                failureReason = "no audio output device is available";
                alc.Dispose();
                al.Dispose();
                return false;
            }

            context = alc.CreateContext(device, null);
            if (context is null)
            {
                failureReason = $"the audio device refused a context ({alc.GetError(device)})";
                alc.CloseDevice(device);
                alc.Dispose();
                al.Dispose();
                return false;
            }

            if (!alc.MakeContextCurrent(context))
            {
                failureReason = $"the audio context could not be made current ({alc.GetError(device)})";
                alc.DestroyContext(context);
                alc.CloseDevice(device);
                alc.Dispose();
                al.Dispose();
                return false;
            }
        }
        catch (Exception ex)
        {
            failureReason = $"opening the audio device threw ({ex.GetType().Name}: {ex.Message})";
            if (context is not null) alc.DestroyContext(context);
            if (device is not null) alc.CloseDevice(device);
            alc.Dispose();
            al.Dispose();
            return false;
        }

        string name = alc.GetContextProperty(device, GetContextString.DeviceSpecifier) ?? "unnamed device";

        // The engine computes loudness from distance itself, so OpenAL only pans.
        al.DistanceModel(DistanceModel.None);

        LogCapabilities(logger, al, alc, device);

        backend = new OpenAlBackend(logger, al, alc, device, context, name);
        return true;
    }

    // IsExtensionPresent only. AL.TryGetExtension reflects, which an AOT build trims.
    private static void LogCapabilities(ILogger logger, AL al, ALContext alc, Device* device)
    {
        logger.LogInformation(
            "OpenAL {Version} by {Vendor}, renderer {Renderer}. EFX {Efx}, HRTF {Hrtf}, loopback {Loopback}",
            al.GetStateProperty(StateString.Version),
            al.GetStateProperty(StateString.Vendor),
            al.GetStateProperty(StateString.Renderer),
            YesOrNo(alc.IsExtensionPresent(device, "ALC_EXT_EFX")),
            YesOrNo(alc.IsExtensionPresent(device, "ALC_SOFT_HRTF")),
            YesOrNo(alc.IsExtensionPresent(device, "ALC_SOFT_loopback")));
    }

    private static string YesOrNo(bool present) => present ? "yes" : "no";

    /// <inheritdoc />
    public uint CreateBuffer()
    {
        uint buffer = _al.GenBuffer();
        CheckError("creating a buffer");
        return buffer;
    }

    /// <inheritdoc />
    public void DestroyBuffer(uint buffer) => _al.DeleteBuffer(buffer);

    /// <inheritdoc />
    public void UploadBuffer(uint buffer, AudioBufferFormat format, ReadOnlySpan<short> pcm, int sampleRate)
    {
        if (pcm.IsEmpty) return;

        BufferFormat alFormat = format == AudioBufferFormat.Mono16 ? BufferFormat.Mono16 : BufferFormat.Stereo16;
        fixed (short* data = pcm)
            _al.BufferData(buffer, alFormat, data, pcm.Length * AudioFormat.BytesPerSample, sampleRate);

        // A failed upload leaves the old contents, so a stream would repeat a chunk.
        CheckError("uploading PCM");
    }

    /// <inheritdoc />
    public bool TryCreateSource(out uint source)
    {
        source = _al.GenSource();
        AudioError error = _al.GetError();
        if (error == AudioError.NoError && source != 0)
            return true;

        // A driver at its source limit reports OutOfMemory here.
        if (source != 0) _al.DeleteSource(source);
        source = 0;
        return false;
    }

    /// <inheritdoc />
    public void DestroySource(uint source) => _al.DeleteSource(source);

    /// <inheritdoc />
    public void ConfigureSource(uint source, in AudioSourceSettings settings)
    {
        _al.SetSourceProperty(source, SourceFloat.Gain, settings.Gain);
        _al.SetSourceProperty(source, SourceFloat.Pitch, settings.Pitch);
        _al.SetSourceProperty(source, SourceVector3.Position, settings.Position.X, settings.Position.Y, settings.Position.Z);
        _al.SetSourceProperty(source, SourceVector3.Velocity, settings.Velocity.X, settings.Velocity.Y, settings.Velocity.Z);
        _al.SetSourceProperty(source, SourceBoolean.SourceRelative, settings.Relative);

        // GainHf is not applied: it needs an EFX filter and this backend makes none.

        // Never set AL_LOOPING, loops go through the buffer queue. Cleared
        // here because pooled sources are reused.
        _al.SetSourceProperty(source, SourceBoolean.Looping, false);
    }

    /// <inheritdoc />
    public AudioSourceState GetSourceState(uint source)
    {
        _al.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
        return (SourceState)state switch
        {
            SourceState.Playing => AudioSourceState.Playing,
            SourceState.Paused => AudioSourceState.Paused,
            SourceState.Stopped => AudioSourceState.Stopped,
            _ => AudioSourceState.Initial,
        };
    }

    /// <inheritdoc />
    public int GetBuffersProcessed(uint source)
    {
        _al.GetSourceProperty(source, GetSourceInteger.BuffersProcessed, out int processed);
        return processed;
    }

    /// <inheritdoc />
    public int GetBuffersQueued(uint source)
    {
        _al.GetSourceProperty(source, GetSourceInteger.BuffersQueued, out int queued);
        return queued;
    }

    /// <inheritdoc />
    public void SetSourceBuffer(uint source, uint buffer) =>
        _al.SetSourceProperty(source, SourceInteger.Buffer, buffer);

    /// <inheritdoc />
    public void QueueBuffer(uint source, uint buffer)
    {
        uint handle = buffer;
        _al.SourceQueueBuffers(source, 1, &handle);
    }

    /// <inheritdoc />
    public uint UnqueueBuffer(uint source)
    {
        uint handle = 0;
        _al.SourceUnqueueBuffers(source, 1, &handle);
        return handle;
    }

    /// <inheritdoc />
    public void Play(uint source) => _al.SourcePlay(source);

    /// <inheritdoc />
    public void Stop(uint source) => _al.SourceStop(source);

    /// <inheritdoc />
    public void Pause(uint source) => _al.SourcePause(source);

    /// <inheritdoc />
    public void SetListener(Vector3 position, Vector3 velocity, Vector3 forward, Vector3 up)
    {
        _al.SetListenerProperty(ListenerVector3.Position, position.X, position.Y, position.Z);
        _al.SetListenerProperty(ListenerVector3.Velocity, velocity.X, velocity.Y, velocity.Z);

        // AL_ORIENTATION is one six-float array (forward, up). Two vec3 calls are rejected.
        float* orientation = stackalloc float[6]
        {
            forward.X, forward.Y, forward.Z,
            up.X, up.Y, up.Z,
        };
        _al.SetListenerProperty(ListenerFloatArray.Orientation, orientation);
    }

    /// <inheritdoc />
    public void SetListenerGain(float gain) => _al.SetListenerProperty(ListenerFloat.Gain, gain);

    /// <summary>Drops the context and closes the device. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unbind first: destroying the current context is undefined in ALC
        // and crashes on some drivers.
        _alc.MakeContextCurrent(null);

        if (_context is not null)
        {
            _alc.DestroyContext(_context);
            _context = null;
        }

        if (_device is not null)
        {
            _alc.CloseDevice(_device);
            _device = null;
        }

        _al.Dispose();
        _alc.Dispose();
    }

    private void CheckError(string what)
    {
        AudioError error = _al.GetError();
        if (error != AudioError.NoError)
            _logger.LogWarning("OpenAL reported {Error} while {What}", error, what);
    }
}
