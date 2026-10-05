using System;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.OpenAL;

namespace OpenAlFilterSpike.Checks;

// Question 4: changing the filter while a sound plays. The high end.
internal static unsafe class LiveChangeChecks
{
    private const double HighHz = 8000;

    private static readonly Tone High = new(HighHz, ToneCapture.Level);
    private static readonly int[] CallSizes = [64, 256, 1024, Signal.Window];

    internal static void Run(Report report, OpenAl lib)
    {
        CopyAtAttach(report, lib);

        report.Note("4", "samples after the change", "first sample off the old level, first sample at the new one, per render call size");
        Transitions(report, lib, "4b", "AL_GAIN on the source, 1 to 0.1", 1f,
            (rig, source, _) => rig.Al.SetSourceProperty(source, SourceFloat.Gain, 0.1f));
        Transitions(report, lib, "4c", "AL_LOWPASS_GAIN, 1 to 0.1", 1f,
            (rig, source, filter) => rig.Attach(source, filter, 0.1f, 1f));
        Transitions(report, lib, "4d", "AL_LOWPASS_GAINHF, 1 to 0.1", 1f,
            (rig, source, filter) => rig.Attach(source, filter, 1f, 0.1f));
        Transitions(report, lib, "4e", "first attach, gain HF 0.1", null,
            (rig, source, filter) => rig.Attach(source, filter, 1f, 0.1f));
        Transitions(report, lib, "4f", "detach from gain HF 0.1", 0.1f,
            (rig, source, _) => rig.Detach(source));

        EnvelopeAcrossTheStep(report, lib);
        AttachingAgainUnchanged(report, lib);
    }

    private static void CopyAtAttach(Report report, OpenAl lib)
    {
        using Rig rig = Rig.Open(lib);
        uint source = ToneCapture.Start(rig, High, 1f, out uint filter);
        double before = Signal.Amplitude(rig.Render(Signal.Window), HighHz);

        rig.Efx.Filterf(filter, Efx.LowpassGainHf, 0.1f);
        rig.Render(Signal.Window);
        double afterSet = Signal.Amplitude(rig.Render(Signal.Window), HighHz);

        rig.Efx.Sourcei(source, Efx.DirectFilter, (int)filter);
        rig.Render(Signal.Window);
        double afterAttach = Signal.Amplitude(rig.Render(Signal.Window), HighHz);

        bool copied = Math.Abs(Signal.Decibels(afterSet / before)) < 0.001 && Signal.Decibels(afterAttach / before) < -20;
        report.Check("4a", "alFilterf alone changes nothing", copied,
            $"8 kHz: {Signal.Decibels(afterSet / before):F3} dB after alFilterf, {Signal.Decibels(afterAttach / before):F2} dB after attaching again");
    }

    private static void Transitions(Report report, OpenAl lib, string id, string what, float? startGainHf, Change change)
    {
        StringBuilder line = new();
        foreach (int callSize in CallSizes)
        {
            ToneCapture.AcrossChange(lib, High, startGainHf, change, callSize, out float[] before, out float[] after);
            double from = Signal.Amplitude(before, HighHz);
            double to = Signal.Amplitude(after.AsSpan(Signal.Window / 2), HighHz);
            double[] envelope = Signal.Envelope(after, HighHz);

            // Five percent of the step, in either direction.
            double tolerance = Math.Abs(to - from) * 0.05;
            int moved = Array.FindIndex(envelope, level => Math.Abs(level - from) > tolerance);
            int arrived = Array.FindLastIndex(envelope, level => Math.Abs(level - to) > tolerance) + 1;
            line.Append($"{callSize}: {moved} and {arrived}.  ");
        }

        report.Note(id, what, line.ToString().TrimEnd());
    }

    private static void EnvelopeAcrossTheStep(Report report, OpenAl lib)
    {
        ToneCapture.AcrossChange(lib, High, 1f, (rig, source, filter) => rig.Attach(source, filter, 1f, 0.1f),
            1024, out float[] before, out float[] after);
        double from = Signal.Amplitude(before, HighHz);
        double[] envelope = Signal.Envelope(after, HighHz);

        StringBuilder line = new();
        foreach (int sample in (int[])[0, 2, 4, 6, 8, 12, 16, 24, 32, 64, 128])
            line.Append($"{sample}: {Signal.Decibels(envelope[sample] / from):F1}  ");
        report.Note("4g", "8 kHz level in dB, by sample after 4d", line.ToString().TrimEnd());
    }

    // The engine could attach every frame whether or not the value moved.
    private static void AttachingAgainUnchanged(Report report, OpenAl lib)
    {
        string once = Signal.Hash(MemoryMarshal.AsBytes(RenderFrames(lib, attachEveryFrame: false).AsSpan()));
        string everyFrame = Signal.Hash(MemoryMarshal.AsBytes(RenderFrames(lib, attachEveryFrame: true).AsSpan()));
        report.Check("4h", "attaching the same values every frame", once == everyFrame, "the same bytes as attaching once");
    }

    private static float[] RenderFrames(OpenAl lib, bool attachEveryFrame)
    {
        const int Frame = Signal.Rate / 60;
        using Rig rig = Rig.Open(lib);
        uint source = rig.CreateSource(rig.CreateBuffer(ResponseChecks.TestSignal()));
        uint filter = rig.Efx.CreateLowpass();
        rig.Attach(source, filter, 0.8f, 0.3f);
        rig.Al.SourcePlay(source);

        float[] rendered = new float[30 * Frame];
        for (int done = 0; done < rendered.Length; done += Frame)
        {
            if (attachEveryFrame)
                rig.Attach(source, filter, 0.8f, 0.3f);
            rig.Render(rendered.AsSpan(done, Frame));
        }

        return rendered;
    }
}
