using System;
using System.Runtime.InteropServices;
using Silk.NET.OpenAL;

namespace OpenAlFilterSpike.Checks;

// Question 2: the loopback device through function pointers.
internal static unsafe class LoopbackChecks
{
    private static readonly (string Name, int Value)[] Constants =
    [
        ("ALC_FREQUENCY", Loopback.Frequency),
        ("ALC_FORMAT_CHANNELS_SOFT", Loopback.FormatChannels),
        ("ALC_FORMAT_TYPE_SOFT", Loopback.FormatType),
        ("ALC_SHORT_SOFT", Loopback.Short),
        ("ALC_FLOAT_SOFT", Loopback.Float),
        ("ALC_MONO_SOFT", Loopback.Mono),
        ("ALC_STEREO_SOFT", Loopback.Stereo),
        ("ALC_HRTF_SOFT", Loopback.Hrtf),
        ("ALC_OUTPUT_LIMITER_SOFT", Loopback.OutputLimiter),
    ];

    internal static void Run(Report report, OpenAl lib)
    {
        Loopback? loopback = lib.Loopback;
        report.Check("2a", "ALC_SOFT_loopback, three entry points", loopback is not null,
            $"extension {(lib.Alc.IsExtensionPresent(null, Loopback.ExtensionName) ? "present" : "absent")}");
        if (loopback is null)
            return;

        Device* probe = loopback.OpenDevice(null);
        bool monoFloat = loopback.IsRenderFormatSupported(probe, 48000, Loopback.Mono, Loopback.Float) != 0;
        bool monoShort = loopback.IsRenderFormatSupported(probe, 48000, Loopback.Mono, Loopback.Short) != 0;
        bool stereoFloat = loopback.IsRenderFormatSupported(probe, 48000, Loopback.Stereo, Loopback.Float) != 0;
        bool otherRate = loopback.IsRenderFormatSupported(probe, 44100, Loopback.Mono, Loopback.Float) != 0;
        bool nonsense = loopback.IsRenderFormatSupported(probe, 48000, 0x1234, Loopback.Float) != 0;
        report.Check("2b", "alcIsRenderFormatSupportedSOFT", monoFloat && monoShort && stereoFloat && otherRate && !nonsense,
            $"device \"{lib.Alc.GetContextProperty(probe, GetContextString.DeviceSpecifier)}\". "
            + $"48 kHz mono float {monoFloat}, mono 16 bit {monoShort}, stereo float {stereoFloat}, 44.1 kHz {otherRate}, a made-up layout {nonsense}");

        int matching = 0;
        foreach ((string name, int value) in Constants)
            if (lib.Alc.GetEnumValue(probe, name) == value) matching++;
        report.Check("2b", "header constants against alcGetEnumValue", matching == Constants.Length, $"{matching} of {Constants.Length} equal");
        lib.Alc.CloseDevice(probe);

        using (Rig rig = Rig.Open(lib))
        {
            report.Check("2c", "context format", Read(lib, rig, Loopback.Frequency) == 48000
                && Read(lib, rig, Loopback.FormatChannels) == Loopback.Mono && Read(lib, rig, Loopback.FormatType) == Loopback.Float,
                $"{Read(lib, rig, Loopback.Frequency)} Hz, channels 0x{Read(lib, rig, Loopback.FormatChannels):X}, type 0x{Read(lib, rig, Loopback.FormatType):X}, "
                + $"limiter {Read(lib, rig, Loopback.OutputLimiter)}, HRTF {Read(lib, rig, Loopback.Hrtf)}");

            float[] silence = rig.Render(Signal.Window);
            report.Check("2d", "no source, float", silence.AsSpan().IndexOfAnyExcept(0f) < 0, "every sample is 0");

            uint buffer = rig.CreateBuffer(1.0, new Tone(1000, 0.5));
            rig.Play(buffer);
            float[] first = rig.Render(Signal.Window);
            float[] second = rig.Render(Signal.Window);
            double level = Signal.Amplitude(second, 1000);
            report.Check("2e", "a 1 kHz tone at 0.5, float", Math.Abs(Signal.Decibels(level / 0.5)) < 0.01,
                $"amplitude {level:F5}, {Signal.Decibels(level / 0.5):F3} dB from the source. First sample of the first call {first[0]:F5}, second {first[1]:F5}");
        }

        using (Rig rig = Rig.Open(lib))
        {
            // 0.1 s of sound. Only rendering moves it along.
            uint source = rig.Play(rig.CreateBuffer(0.1, new Tone(1000, 0.5)));
            rig.Render(Signal.Window / 2);
            rig.Al.GetSourceProperty(source, GetSourceInteger.SourceState, out int midway);
            rig.Al.GetSourceProperty(source, GetSourceInteger.SampleOffset, out int offset);
            rig.Render(Signal.Window);
            rig.Al.GetSourceProperty(source, GetSourceInteger.SourceState, out int after);
            report.Check("2f", "the source moves only when rendered",
                (SourceState)midway == SourceState.Playing && offset == Signal.Window / 2 && (SourceState)after == SourceState.Stopped,
                $"after 2400 samples: {(SourceState)midway}, offset {offset}. After 4800 more: {(SourceState)after}");
        }

        using (Rig rig = Rig.Open(lib, Loopback.Short))
        {
            uint buffer = rig.CreateBuffer(1.0, new Tone(1000, 0.5));
            rig.Play(buffer);
            rig.Render16(Signal.Window);
            short[] rendered = rig.Render16(Signal.Window);
            float[] scaled = Array.ConvertAll(rendered, sample => sample / 32768f);
            double level = Signal.Amplitude(scaled, 1000);
            short peak = 0;
            foreach (short sample in rendered)
                peak = Math.Max(peak, Math.Abs(sample));
            report.Check("2g", "the same, 16 bit", Math.Abs(Signal.Decibels(level / 0.5)) < 0.01, $"amplitude {level:F5}, peak sample {peak}");
        }

        string float1 = Signal.Hash(MemoryMarshal.AsBytes(RenderScenario(lib, [1024, 333, 4096, 17]).AsSpan()));
        string float2 = Signal.Hash(MemoryMarshal.AsBytes(RenderScenario(lib, [1024, 333, 4096, 17]).AsSpan()));
        string short1 = Signal.Hash(MemoryMarshal.AsBytes(RenderScenario16(lib, limiter: false).AsSpan()));
        string short2 = Signal.Hash(MemoryMarshal.AsBytes(RenderScenario16(lib, limiter: false).AsSpan()));
        string input = Signal.Hash(MemoryMarshal.AsBytes(ResponseChecks.TestSignal().AsSpan()));
        report.Check("2h", "two renders in one process", float1 == float2 && short1 == short2,
            $"input {input}, float {float1}, 16 bit {short1}");

        string oneCall = Signal.Hash(MemoryMarshal.AsBytes(RenderScenario(lib, [Signal.Rate]).AsSpan()));
        string frames = Signal.Hash(MemoryMarshal.AsBytes(RenderScenario(lib, [800]).AsSpan()));
        report.Check("2h", "one call, 800 at a time, uneven calls", oneCall == float1 && frames == float1, "the same bytes");

        short[] plain = RenderScenario16(lib, limiter: false);
        short[] limited = RenderScenario16(lib, limiter: true);
        int differing = 0;
        for (int i = 0; i < plain.Length; i++)
            if (plain[i] != limited[i]) differing++;
        report.Note("2i", "the limiter when the context does not say",
            $"float {DefaultLimiter(lib, Loopback.Float)}, 16 bit {DefaultLimiter(lib, Loopback.Short)}. "
            + $"With it, {differing} of {plain.Length} 16 bit samples differ from the limiter-off render");
    }

    private static int DefaultLimiter(OpenAl lib, int type)
    {
        Loopback loopback = lib.Loopback ?? throw new InvalidOperationException("no loopback");
        Device* device = loopback.OpenDevice(null);
        int* attributes = stackalloc int[]
        {
            Loopback.FormatChannels, Loopback.Mono, Loopback.FormatType, type, Loopback.Frequency, Signal.Rate, 0,
        };
        Context* context = lib.Alc.CreateContext(device, attributes);

        int value = 0;
        lib.Alc.GetContextProperty(device, (GetContextInteger)Loopback.OutputLimiter, 1, &value);
        lib.Alc.DestroyContext(context);
        lib.Alc.CloseDevice(device);
        return value;
    }

    // One second of the filtered test signal, rendered in calls of the given sizes in turn.
    private static float[] RenderScenario(OpenAl lib, int[] calls)
    {
        using Rig rig = Rig.Open(lib);
        StartFiltered(rig);

        float[] rendered = new float[Signal.Rate];
        for (int done = 0, call = 0; done < rendered.Length; call++)
        {
            int frames = Math.Min(calls[call % calls.Length], rendered.Length - done);
            rig.Render(rendered.AsSpan(done, frames));
            done += frames;
        }

        return rendered;
    }

    private static short[] RenderScenario16(OpenAl lib, bool limiter)
    {
        if (!limiter)
        {
            using Rig rig = Rig.Open(lib, Loopback.Short);
            StartFiltered(rig);
            return rig.Render16(Signal.Rate);
        }

        // Rig.Open switches the limiter off. This context leaves it alone.
        Loopback loopback = lib.Loopback ?? throw new InvalidOperationException("no loopback");
        Device* device = loopback.OpenDevice(null);
        int* attributes = stackalloc int[]
        {
            Loopback.FormatChannels, Loopback.Mono, Loopback.FormatType, Loopback.Short, Loopback.Frequency, Signal.Rate, 0,
        };
        Context* context = lib.Alc.CreateContext(device, attributes);
        lib.Alc.MakeContextCurrent(context);
        lib.Al.DistanceModel(DistanceModel.None);

        Efx efx = Efx.TryLoad(lib.Al, lib.Alc, device) ?? throw new InvalidOperationException("no EFX");
        short[] pcm = ResponseChecks.TestSignal();
        uint buffer = lib.Al.GenBuffer();
        fixed (short* data = pcm)
            lib.Al.BufferData(buffer, BufferFormat.Mono16, data, pcm.Length * sizeof(short), Signal.Rate);
        uint source = lib.Al.GenSource();
        lib.Al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
        lib.Al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
        uint filter = efx.CreateLowpass();
        efx.Filterf(filter, Efx.LowpassGainHf, 0.25f);
        efx.Sourcei(source, Efx.DirectFilter, (int)filter);
        lib.Al.SourcePlay(source);

        short[] rendered = new short[Signal.Rate];
        fixed (short* data = rendered)
            loopback.RenderSamples(device, data, rendered.Length);

        lib.Alc.MakeContextCurrent(null);
        lib.Alc.DestroyContext(context);
        lib.Alc.CloseDevice(device);
        return rendered;
    }

    private static void StartFiltered(Rig rig)
    {
        uint buffer = rig.CreateBuffer(ResponseChecks.TestSignal());
        uint source = rig.CreateSource(buffer);
        rig.Attach(source, rig.Efx.CreateLowpass(), gain: 1f, gainHf: 0.25f);
        rig.Al.SourcePlay(source);
    }

    private static int Read(OpenAl lib, Rig rig, int attribute)
    {
        int value = 0;
        lib.Alc.GetContextProperty(rig.Device, (GetContextInteger)attribute, 1, &value);
        return value;
    }
}
