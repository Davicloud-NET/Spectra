namespace SpectraEngine.Bsp.Tests;

// Test tones in, levels out. Mono.
internal static class ToneSignal
{
    public static short[] Synthesize(int frames, int rate, ReadOnlySpan<TestTone> tones)
    {
        var pcm = new short[frames];
        for (int n = 0; n < frames; n++)
        {
            double sum = 0;
            foreach (TestTone tone in tones)
                sum += tone.Amplitude * Math.Sin((2 * Math.PI * tone.Hz * n / rate) + tone.Phase);

            pcm[n] = (short)Math.Round(sum * short.MaxValue);
        }

        return pcm;
    }

    // Peak amplitude of one frequency. The window must hold whole cycles of
    // every tone in it, or they leak into each other.
    public static double Level(ReadOnlySpan<float> window, int rate, double hz)
    {
        double step = 2 * Math.PI * hz / rate;
        double real = 0;
        double imaginary = 0;
        for (int n = 0; n < window.Length; n++)
        {
            real += window[n] * Math.Cos(step * n);
            imaginary += window[n] * Math.Sin(step * n);
        }

        return 2 * Math.Sqrt((real * real) + (imaginary * imaginary)) / window.Length;
    }

    public static double Decibels(double ratio) => 20 * Math.Log10(ratio);

    // The biggest move between two neighbouring samples, the one from
    // previous into the block included. Leaves the block's last sample in previous.
    public static double LargestMove(ReadOnlySpan<float> samples, ref float previous)
    {
        double largest = 0;
        foreach (float sample in samples)
        {
            largest = Math.Max(largest, Math.Abs(sample - previous));
            previous = sample;
        }

        return largest;
    }
}
