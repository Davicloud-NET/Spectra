using System;
using Silk.NET.OpenAL;

namespace OpenAlFilterSpike;

/// <summary>
/// One loopback device with one current context. Nothing here reaches a sound
/// card: the mix only runs when <see cref="Render(int)"/> asks for samples.
/// </summary>
internal sealed unsafe class Rig : IDisposable
{
    private readonly ALContext _alc;
    private Device* _device;
    private Context* _context;

    private Rig(OpenAl lib, Loopback loopback, Device* device, Context* context, Efx efx)
    {
        _alc = lib.Alc;
        _device = device;
        _context = context;
        Al = lib.Al;
        Loopback = loopback;
        Efx = efx;
    }

    internal AL Al { get; }

    internal Efx Efx { get; }

    internal Loopback Loopback { get; }

    internal Device* Device => _device;

    /// <summary>Opens a mono loopback device at 48 kHz, float samples unless told otherwise.</summary>
    internal static Rig Open(OpenAl lib, int type = Loopback.Float)
    {
        Loopback loopback = lib.Loopback ?? throw new InvalidOperationException("The library has no loopback device.");

        Device* device = loopback.OpenDevice(null);
        if (device is null)
            throw new InvalidOperationException("alcLoopbackOpenDeviceSOFT returned null.");

        // A loopback context must be told its format. The limiter and HRTF are
        // switched off so the output is the mix and nothing else.
        int* attributes = stackalloc int[]
        {
            Loopback.FormatChannels, Loopback.Mono,
            Loopback.FormatType, type,
            Loopback.Frequency, Signal.Rate,
            Loopback.Hrtf, 0,
            Loopback.OutputLimiter, 0,
            0,
        };

        Context* context = lib.Alc.CreateContext(device, attributes);
        if (context is null || !lib.Alc.MakeContextCurrent(context))
        {
            lib.Alc.CloseDevice(device);
            throw new InvalidOperationException("The loopback device refused a context.");
        }

        // The engine does the same: it computes loudness from distance itself.
        lib.Al.DistanceModel(DistanceModel.None);

        Efx efx = Efx.TryLoad(lib.Al, lib.Alc, device)
            ?? throw new InvalidOperationException("The loopback device has no EFX.");
        return new Rig(lib, loopback, device, context, efx);
    }

    internal float[] Render(int frames)
    {
        float[] samples = new float[frames];
        Render(samples);
        return samples;
    }

    internal void Render(Span<float> samples)
    {
        fixed (float* data = samples)
            Loopback.RenderSamples(_device, data, samples.Length);
    }

    internal short[] Render16(int frames)
    {
        short[] samples = new short[frames];
        fixed (short* data = samples)
            Loopback.RenderSamples(_device, data, frames);
        return samples;
    }

    /// <summary>A mono 16 bit buffer holding the tones, as the engine uploads PCM.</summary>
    internal uint CreateBuffer(double seconds, params Tone[] tones)
    {
        short[] pcm = Signal.Synthesize((int)(seconds * Signal.Rate), tones);
        return CreateBuffer(pcm);
    }

    internal uint CreateBuffer(ReadOnlySpan<short> pcm)
    {
        uint buffer = Al.GenBuffer();
        fixed (short* data = pcm)
            Al.BufferData(buffer, BufferFormat.Mono16, data, pcm.Length * sizeof(short), Signal.Rate);
        return buffer;
    }

    /// <summary>A source at the listener holding the buffer. Not playing yet.</summary>
    internal uint CreateSource(uint buffer)
    {
        uint source = Al.GenSource();
        Al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
        Al.SetSourceProperty(source, SourceVector3.Position, 0f, 0f, 0f);
        Al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
        return source;
    }

    internal uint Play(uint buffer)
    {
        uint source = CreateSource(buffer);
        Al.SourcePlay(source);
        return source;
    }

    /// <summary>Sets the gains on a filter and attaches it, which is what copies them to the source.</summary>
    internal void Attach(uint source, uint filter, float gain, float gainHf)
    {
        Efx.Filterf(filter, Efx.LowpassGain, gain);
        Efx.Filterf(filter, Efx.LowpassGainHf, gainHf);
        Efx.Sourcei(source, Efx.DirectFilter, (int)filter);
    }

    internal void Detach(uint source) => Efx.Sourcei(source, Efx.DirectFilter, Efx.FilterNull);

    public void Dispose()
    {
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
    }
}
