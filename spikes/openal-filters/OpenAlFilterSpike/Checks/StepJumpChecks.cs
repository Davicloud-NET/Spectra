using System;
using System.Text;

namespace OpenAlFilterSpike.Checks;

// Question 4, the low end: what a change of gain HF does to a tone the filter
// should leave alone. The change is made while the tone sits on its peak.
internal static class StepJumpChecks
{
    private const double LowHz = 200;

    // Samples of sound between two filter updates at 60 frames a second.
    private const int Frame = Signal.Rate / 60;

    private static readonly Tone LowAtPeak = new(LowHz, ToneCapture.Level, Math.PI / 2);

    internal static void Run(Report report, OpenAl lib)
    {
        report.Note("4i", "200 Hz tone, biggest move between two samples", $"the tone alone: {CleanStep():F1}% of its amplitude");
        Jump(report, lib, null, 0.85f);
        Jump(report, lib, null, 0.5f);
        Jump(report, lib, null, 0.01f);
        Jump(report, lib, 0.9999f, 0.85f);
        Jump(report, lib, 0.5f, 0.45f);
        Jump(report, lib, 0.5f, 0.25f);
        Jump(report, lib, 0.1f, 0.01f);
        Jump(report, lib, 0.5f, null);

        SamplesAcrossTheStep(report, lib);

        SmoothedSwing(report, lib, updatesPerFrame: 1);
        SmoothedSwing(report, lib, updatesPerFrame: 4);
        SmoothedSwing(report, lib, updatesPerFrame: 1, maxStep: 0.02);
    }

    private static double CleanStep() => 200 * Math.Sin(Math.PI * LowHz / Signal.Rate);

    private static void Jump(Report report, OpenAl lib, float? from, float? to)
    {
        ToneCapture.AcrossChange(lib, LowAtPeak, from, Apply(to), 1024, out float[] before, out float[] after);
        double amplitude = Signal.Amplitude(before, LowHz);

        double largest = 0;
        int largestAt = 0;
        float previous = before[^1];
        for (int n = 0; n < 64; n++)
        {
            double move = Math.Abs(after[n] - previous);
            if (move > largest)
            {
                largest = move;
                largestAt = n;
            }

            previous = after[n];
        }

        report.Note("4i", $"  gain HF {Name(from)} to {Name(to)}",
            $"{100 * largest / amplitude:F1}% of the amplitude, at sample {largestAt}");
    }

    private static void SamplesAcrossTheStep(Report report, OpenAl lib)
    {
        ToneCapture.AcrossChange(lib, LowAtPeak, null, Apply(0.85f), 1024, out float[] before, out float[] after);
        StringBuilder line = new();
        line.Append($"{before[^2]:F4} {before[^1]:F4} |");
        for (int n = 0; n < 6; n++)
            line.Append($" {after[n]:F4}");
        report.Note("4j", "samples around none to 0.85", line.ToString());
    }

    // The engine's smoother, moving gain HF from 1 to 0.1 with a 0.1 s time
    // constant, one update per frame or several. maxStep caps how far one
    // update may move it.
    private static void SmoothedSwing(Report report, OpenAl lib, int updatesPerFrame, double maxStep = 1)
    {
        using Rig rig = Rig.Open(lib);
        uint source = ToneCapture.Start(rig, LowAtPeak, null, out uint filter);
        float[] before = rig.Render(Signal.Window);
        double amplitude = Signal.Amplitude(before, LowHz);

        int interval = Frame / updatesPerFrame;
        double blend = 1 - Math.Exp(-(double)interval / Signal.Rate / 0.1);
        double gainHf = 1;
        float previous = before[^1];
        double largest = 0;
        int updates = 0;
        int overClean = 0;
        float[] chunk = new float[interval];

        while (gainHf - 0.1 > 1e-4)
        {
            gainHf -= Math.Min((gainHf - 0.1) * blend, maxStep);
            rig.Attach(source, filter, 1f, (float)gainHf);
            rig.Render(chunk);

            double move = Math.Abs(chunk[0] - previous);
            largest = Math.Max(largest, move);
            if (100 * move / amplitude > CleanStep()) overClean++;
            previous = chunk[^1];
            updates++;
        }

        string cap = maxStep < 1 ? $", steps of {maxStep} at most" : string.Empty;
        report.Note("4k", $"smoothed 1 to 0.1, {updatesPerFrame} per frame{cap}",
            $"{updates} updates, biggest move {100 * largest / amplitude:F1}% of the amplitude, {overClean} updates above the tone's own");
    }

    private static Change Apply(float? gainHf) => (rig, source, filter) =>
    {
        if (gainHf is null)
            rig.Detach(source);
        else
            rig.Attach(source, filter, 1f, gainHf.Value);
    };

    private static string Name(float? gainHf) => gainHf is null ? "none" : $"{gainHf.Value}";
}
