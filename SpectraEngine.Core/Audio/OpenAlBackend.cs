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

    // Null on a device that plays.
    private readonly OpenAlLoopback? _loopback;

    // Null when the library has no filters.
    private OpenAlLowPass? _lowPass;

    private Device* _device;
    private Context* _context;
    private bool _disposed;

    private OpenAlBackend(
        ILogger logger, AL al, ALContext alc, Device* device, Context* context, OpenAlLoopback? loopback)
    {
        _logger = logger;
        _al = al;
        _alc = alc;
        _device = device;
        _context = context;
        _loopback = loopback;
        DeviceName = alc.GetContextProperty(device, GetContextString.DeviceSpecifier) ?? "unnamed device";
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
        bool isOpen = TryOpen(logger, inMemory: false, withheld: null, out OpenAlBackend? opened, out failureReason);
        backend = opened;
        return isOpen;
    }

    // Opens a device that renders into memory and reaches no sound card, for
    // tests of what the library does to a sound. Sounds move on only when
    // Render is called.
    // withheld names extensions and entry points to treat as absent.
    internal static bool TryCreateLoopback(
        ILogger logger,
        [NotNullWhen(true)] out OpenAlBackend? backend,
        out string failureReason,
        Predicate<string>? withheld = null) =>
        TryOpen(logger, inMemory: true, withheld, out backend, out failureReason);

    private static bool TryOpen(
        ILogger logger,
        bool inMemory,
        Predicate<string>? withheld,
        [NotNullWhen(true)] out OpenAlBackend? backend,
        out string failureReason)
    {
        backend = null;
        if (!TryLoadLibrary(out AL? al, out ALContext? alc, out failureReason))
            return false;

        OpenAlLoopback? loopback = null;
        Device* device = null;
        Context* context = null;
        try
        {
            if (inMemory) loopback = OpenAlLoopback.TryLoad(alc);

            failureReason = inMemory && loopback is null
                ? "this OpenAL has no loopback device"
                : OpenDevice(alc, loopback, out device, out context);
        }
        catch (Exception ex)
        {
            // Reported to the caller, which goes on with no sound.
            failureReason = $"opening the audio device threw ({ex.GetType().Name}: {ex.Message})";
        }

        if (failureReason.Length > 0)
        {
            if (context is not null) alc.DestroyContext(context);
            if (device is not null) alc.CloseDevice(device);
            alc.Dispose();
            al.Dispose();
            return false;
        }

        // The engine computes loudness from distance itself, so OpenAL only pans.
        al.DistanceModel(DistanceModel.None);

        LogCapabilities(logger, al, alc, device);

        backend = new OpenAlBackend(logger, al, alc, device, context, loopback);
        backend.MakeLowPass(withheld);
        return true;
    }

    private static bool TryLoadLibrary(
        [NotNullWhen(true)] out AL? al,
        [NotNullWhen(true)] out ALContext? alc,
        out string failureReason)
    {
        al = null;
        alc = null;
        failureReason = string.Empty;

        try
        {
            // GetApi throws when no OpenAL runtime is installed. soft asks for
            // the packaged OpenAL Soft first: a system OpenAL can be older and
            // lack filters.
            SilkPlatform.UsePortableRuntimeId();
            alc = ALContext.GetApi(soft: true);
            al = AL.GetApi(soft: true);
            return true;
        }
        catch (Exception ex)
        {
            alc?.Dispose();
            alc = null;
            failureReason = $"the OpenAL runtime could not be loaded ({ex.GetType().Name}: {ex.Message})";
            return false;
        }
    }

    // Returns why it could not, or nothing when the device is open and its
    // context current. The caller closes whatever was opened.
    private static string OpenDevice(ALContext alc, OpenAlLoopback? loopback, out Device* device, out Context* context)
    {
        context = null;
        device = loopback is null ? alc.OpenDevice(string.Empty) : loopback.OpenDevice();
        if (device is null)
            return "no audio output device is available";

        context = loopback is null ? alc.CreateContext(device, null) : loopback.CreateContext(alc, device);
        if (context is null)
            return $"the audio device refused a context ({alc.GetError(device)})";

        if (!alc.MakeContextCurrent(context))
            return $"the audio context could not be made current ({alc.GetError(device)})";

        return string.Empty;
    }

    private void MakeLowPass(Predicate<string>? withheld)
    {
        _lowPass = OpenAlLowPass.TryCreate(_al, _alc, _device, withheld, out string reason);
        if (_lowPass is null)
        {
            _logger.LogWarning(
                "No low-pass filter: {Reason}. Sounds behind walls will be quieter but not duller", reason);
        }
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
        {
            _lowPass?.Track(source);
            return true;
        }

        // A driver at its source limit reports OutOfMemory here.
        if (source != 0) _al.DeleteSource(source);
        source = 0;
        return false;
    }

    /// <inheritdoc />
    public void DestroySource(uint source)
    {
        _al.DeleteSource(source);
        _lowPass?.Forget(source);
    }

    /// <inheritdoc />
    public void ConfigureSource(uint source, in AudioSourceSettings settings)
    {
        _al.SetSourceProperty(source, SourceFloat.Gain, settings.Gain);
        _al.SetSourceProperty(source, SourceFloat.Pitch, settings.Pitch);
        _al.SetSourceProperty(source, SourceVector3.Position, settings.Position.X, settings.Position.Y, settings.Position.Z);
        _al.SetSourceProperty(source, SourceVector3.Velocity, settings.Velocity.X, settings.Velocity.Y, settings.Velocity.Z);
        _al.SetSourceProperty(source, SourceBoolean.SourceRelative, settings.Relative);

        // A pooled source still has the last sound's filter. Every sound
        // starts with a call here, which is what clears it.
        _lowPass?.Apply(source, settings.GainHf);

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

    // Mixes the next samples of a loopback device, one float a frame.
    internal void Render(Span<float> samples)
    {
        if (_loopback is null)
            throw new InvalidOperationException("Only a loopback device renders into memory.");

        _loopback.Render(_device, samples);
    }

    /// <summary>Drops the context and closes the device. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // While the context is still current.
        _lowPass?.Delete();
        _lowPass = null;

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
