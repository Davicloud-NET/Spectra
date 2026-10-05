using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenAlFilterSpike.Checks;

// Question 3: what the filter does to a low tone and a high tone.
internal static class ResponseChecks
{
    internal static readonly double[] Frequencies =
    [
        100, 200, 500, 1000, 2000, 3000, 4000, 5000, 6000, 8000, 10000, 12000, 16000, 20000,
    ];

    private const double ToneAmplitude = 0.05;

    // OpenAL Soft's reference frequency for the low-pass filter.
    private const double ReferenceHz = 5000;

    /// <summary>Two seconds of all the tones at once, 16 bit. The phases keep the peaks apart.</summary>
    internal static short[] TestSignal()
    {
        Tone[] tones = new Tone[Frequencies.Length];
        for (int i = 0; i < tones.Length; i++)
            tones[i] = new Tone(Frequencies[i], ToneAmplitude, i * 2.399963);
        return Signal.Synthesize(2 * Signal.Rate, tones);
    }

    internal static void Run(Report report, OpenAl lib)
    {
        double[] off = Measure(lib, null, null, out string offHash);
        Measure(lib, 1f, 1f, out string unityHash);
        report.Check("3a", "filter at 1 and 1 against no filter", offHash == unityHash, "the same bytes");

        StringBuilder header = new();
        foreach (double hz in Frequencies)
            header.Append($"{(hz < 1000 ? $"{hz:F0}" : $"{hz / 1000:F0}k"),7}");
        report.Note("3", "dB against no filter, by Hz", header.ToString());

        (string Label, float Gain, float GainHf)[] settings =
        [
            ("gain HF 1", 1f, 1f),
            ("gain HF 0.5", 1f, 0.5f),
            ("gain HF 0.25", 1f, 0.25f),
            ("gain HF 0.1", 1f, 0.1f),
            ("gain HF 0.01", 1f, 0.01f),
            ("gain HF 0.001", 1f, 0.001f),
            ("gain HF 0", 1f, 0f),
            ("gain 0.5", 0.5f, 1f),
            ("gain 0.5, gain HF 0.1", 0.5f, 0.1f),
        ];

        double worstFit = 0;
        foreach ((string label, float gain, float gainHf) in settings)
        {
            double[] levels = Measure(lib, gain, gainHf, out _);
            StringBuilder row = new();
            StringBuilder model = new();
            for (int i = 0; i < levels.Length; i++)
            {
                double measured = Signal.Decibels(levels[i] / off[i]);
                double expected = Signal.Decibels(gain * ShelfModel(gainHf, Frequencies[i]));
                row.Append($"{measured,7:F2}");
                model.Append($"{expected,7:F2}");
                worstFit = Math.Max(worstFit, Math.Abs(measured - expected));
            }

            report.Note("3", label, row.ToString());
            report.Note("3", "  model", model.ToString());
        }

        report.Check("3b", "a high shelf at 5 kHz fits", worstFit < 0.1, $"worst difference from the model {worstFit:F3} dB");

        report.Note("3", "no filter, absolute", $"200 Hz at {Signal.Decibels(off[1] / ToneAmplitude):F3} dB from the source level");
    }

    /// <summary>
    /// The level of every tone with the given filter gains, or with no filter
    /// when both are null.
    /// </summary>
    internal static double[] Measure(OpenAl lib, float? gain, float? gainHf, out string hash)
    {
        using Rig rig = Rig.Open(lib);
        uint buffer = rig.CreateBuffer(TestSignal());
        uint source = rig.CreateSource(buffer);
        if (gain is not null && gainHf is not null)
            rig.Attach(source, rig.Efx.CreateLowpass(), gain.Value, gainHf.Value);
        rig.Al.SourcePlay(source);

        // The filter starts from rest. Two windows in, it has settled.
        rig.Render(2 * Signal.Window);
        float[] window = rig.Render(Signal.Window);
        hash = Signal.Hash(MemoryMarshal.AsBytes(window.AsSpan()));
        return Slice(window);
    }

    private static double[] Slice(float[] window)
    {
        double[] levels = new double[Frequencies.Length];
        for (int i = 0; i < levels.Length; i++)
            levels[i] = Signal.Amplitude(window, Frequencies[i]);
        return levels;
    }

    /// <summary>
    /// Gain of the second order high shelf from the Audio EQ Cookbook, slope 1,
    /// with the response at the reference frequency equal to the gain. OpenAL
    /// Soft does not go below 0.001.
    /// </summary>
    internal static double ShelfModel(double gainHf, double hz)
    {
        double a = Math.Max(gainHf, 0.001);
        double w0 = 2 * Math.PI * ReferenceHz / Signal.Rate;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / 2 * Math.Sqrt(2);
        double beta = 2 * Math.Sqrt(a) * alpha;

        double b0 = a * ((a + 1) + ((a - 1) * cos) + beta);
        double b1 = -2 * a * ((a - 1) + ((a + 1) * cos));
        double b2 = a * ((a + 1) + ((a - 1) * cos) - beta);
        double a0 = (a + 1) - ((a - 1) * cos) + beta;
        double a1 = 2 * ((a - 1) - ((a + 1) * cos));
        double a2 = (a + 1) - ((a - 1) * cos) - beta;

        double w = 2 * Math.PI * hz / Signal.Rate;
        return Magnitude(b0, b1, b2, w) / Magnitude(a0, a1, a2, w);
    }

    private static double Magnitude(double c0, double c1, double c2, double w)
    {
        double real = c0 + (c1 * Math.Cos(w)) + (c2 * Math.Cos(2 * w));
        double imaginary = -(c1 * Math.Sin(w)) - (c2 * Math.Sin(2 * w));
        return Math.Sqrt((real * real) + (imaginary * imaginary));
    }
}
