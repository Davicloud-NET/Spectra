using System;
using System.Security.Cryptography;

namespace OpenAlFilterSpike;

/// <summary>Test tones in, levels out. Everything is mono at 48 kHz.</summary>
internal static class Signal
{
    internal const int Rate = 48000;

    // 0.1 s. Every tone used here is a multiple of 10 Hz, so it holds a whole
    // number of cycles of each and a plain sum gives the level with no leakage.
    internal const int Window = 4800;

    internal static short[] Synthesize(int frames, ReadOnlySpan<Tone> tones)
    {
        short[] pcm = new short[frames];
        for (int n = 0; n < frames; n++)
        {
            double sum = 0;
            foreach (Tone tone in tones)
                sum += tone.Amplitude * Math.Sin((2 * Math.PI * tone.Hz * n / Rate) + tone.Phase);
            pcm[n] = (short)Math.Round(sum * 32767.0);
        }

        return pcm;
    }

    /// <summary>Peak amplitude of one frequency in a window that holds whole cycles of it.</summary>
    internal static double Amplitude(ReadOnlySpan<float> window, double hz)
    {
        double step = 2 * Math.PI * hz / Rate;
        double real = 0;
        double imaginary = 0;
        for (int n = 0; n < window.Length; n++)
        {
            real += window[n] * Math.Cos(step * n);
            imaginary += window[n] * Math.Sin(step * n);
        }

        return 2 * Math.Sqrt((real * real) + (imaginary * imaginary)) / window.Length;
    }

    /// <summary>
    /// The level of one frequency around every sample, from a window one
    /// period long. The period must be a whole number of samples.
    /// </summary>
    internal static double[] Envelope(ReadOnlySpan<float> samples, double hz)
    {
        int period = (int)Math.Round(Rate / hz);
        double[] envelope = new double[samples.Length - period + 1];
        for (int start = 0; start < envelope.Length; start++)
            envelope[start] = Amplitude(samples.Slice(start, period), hz);
        return envelope;
    }

    internal static double Decibels(double ratio) => ratio <= 0 ? double.NegativeInfinity : 20 * Math.Log10(ratio);

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16];
}
