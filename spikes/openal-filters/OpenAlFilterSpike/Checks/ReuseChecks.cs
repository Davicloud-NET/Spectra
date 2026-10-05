using System;
using Silk.NET.OpenAL;

namespace OpenAlFilterSpike.Checks;

// Question 5: a pooled source handed to another sound, and one filter object for every source.
internal static unsafe class ReuseChecks
{
    private const double FirstHz = 8000;
    private const double SecondHz = 10000;

    internal static void Run(Report report, OpenAl lib)
    {
        PooledSource(report, lib);
        ScratchFilter(report, lib);
    }

    private static void PooledSource(Report report, OpenAl lib)
    {
        using Rig rig = Rig.Open(lib);
        AL al = rig.Al;
        uint first = rig.CreateBuffer(2.0, new Tone(FirstHz, 0.5));
        uint second = rig.CreateBuffer(2.0, new Tone(FirstHz, 0.5));

        uint source = rig.Play(first);
        double open = Settled(rig, FirstHz);

        rig.Attach(source, rig.Efx.CreateLowpass(), 1f, 0.1f);
        double filtered = Settled(rig, FirstHz);

        // What AudioSourcePool does when it hands a source on.
        al.SourceStop(source);
        al.SetSourceProperty(source, SourceInteger.Buffer, 0u);
        al.SetSourceProperty(source, SourceInteger.Buffer, second);
        al.SourcePlay(source);
        double reused = Settled(rig, FirstHz);

        rig.Detach(source);
        AudioError error = al.GetError();
        double reset = Settled(rig, FirstHz);

        bool kept = Math.Abs(Signal.Decibels(reused / filtered)) < 0.001;
        bool cleared = Math.Abs(Signal.Decibels(reset / open)) < 0.001 && error == AudioError.NoError;
        report.Check("5a", "stop and a new buffer keep the filter", kept,
            $"8 kHz: {Signal.Decibels(filtered / open):F2} dB with the filter, {Signal.Decibels(reused / open):F2} dB on the next sound");
        report.Check("5b", "AL_FILTER_NULL clears it", cleared, $"{Signal.Decibels(reset / open):F3} dB, error {error}");
    }

    private static void ScratchFilter(Report report, OpenAl lib)
    {
        Levels own = TwoSources(lib, scratch: false);
        Levels shared = TwoSources(lib, scratch: true);

        bool same = Math.Abs(Signal.Decibels(shared.First / own.First)) < 0.001
            && Math.Abs(Signal.Decibels(shared.Second / own.Second)) < 0.001;
        bool survives = Math.Abs(Signal.Decibels(shared.FirstAfterDelete / own.First)) < 0.001
            && Math.Abs(Signal.Decibels(shared.SecondAfterDelete / own.Second)) < 0.001;

        report.Check("5c", "one filter object, two sources", same,
            $"8 kHz at gain HF 0.1: {Signal.Decibels(shared.First / own.Open):F2} dB against {Signal.Decibels(own.First / own.Open):F2} dB with a filter of its own. "
            + $"10 kHz at 0.5: {Signal.Decibels(shared.Second / own.OpenSecond):F2} dB against {Signal.Decibels(own.Second / own.OpenSecond):F2} dB");
        report.Check("5d", "the object set to 1, then deleted", survives,
            $"8 kHz {Signal.Decibels(shared.FirstAfterDelete / own.Open):F2} dB, 10 kHz {Signal.Decibels(shared.SecondAfterDelete / own.OpenSecond):F2} dB");
    }

    private readonly record struct Levels(
        double Open, double OpenSecond, double First, double Second, double FirstAfterDelete, double SecondAfterDelete);

    // Two sources play at once, an 8 kHz tone at gain HF 0.1 and a 10 kHz tone at 0.5.
    private static Levels TwoSources(OpenAl lib, bool scratch)
    {
        using Rig rig = Rig.Open(lib);
        uint first = rig.Play(rig.CreateBuffer(2.0, new Tone(FirstHz, 0.25)));
        uint second = rig.Play(rig.CreateBuffer(2.0, new Tone(SecondHz, 0.25)));
        rig.Render(2 * Signal.Window);
        float[] open = rig.Render(Signal.Window);

        uint filter = rig.Efx.CreateLowpass();
        uint other = scratch ? filter : rig.Efx.CreateLowpass();
        rig.Attach(first, filter, 1f, 0.1f);
        rig.Attach(second, other, 1f, 0.5f);
        rig.Render(2 * Signal.Window);
        float[] filtered = rig.Render(Signal.Window);

        rig.Efx.Filterf(filter, Efx.LowpassGainHf, 1f);
        rig.Efx.Delete(filter);
        rig.Render(2 * Signal.Window);
        float[] afterDelete = rig.Render(Signal.Window);

        return new Levels(
            Signal.Amplitude(open, FirstHz), Signal.Amplitude(open, SecondHz),
            Signal.Amplitude(filtered, FirstHz), Signal.Amplitude(filtered, SecondHz),
            Signal.Amplitude(afterDelete, FirstHz), Signal.Amplitude(afterDelete, SecondHz));
    }

    private static double Settled(Rig rig, double hz)
    {
        rig.Render(2 * Signal.Window);
        return Signal.Amplitude(rig.Render(Signal.Window), hz);
    }
}
