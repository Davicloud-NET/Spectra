using System;

namespace Spectra.Kitchen.Audio;

/// <summary>
/// Windowed-sinc sample rate conversion for interleaved PCM16. Cook time only;
/// the runtime never resamples.
/// </summary>
public static class AudioResampler
{
    /// <summary>Kernel taps either side of the centre.</summary>
    public const int HalfTaps = 16;

    /// <summary>
    /// A frame count at <paramref name="fromRate"/> expressed at
    /// <paramref name="toRate"/>, rounded to nearest. Use this for loop points too.
    /// </summary>
    // Integer math: going through double seconds truncates and moves a loop
    // point by a frame, which clicks.
    public static long ConvertFrames(long frames, int fromRate, int toRate)
    {
        if (fromRate <= 0) throw new ArgumentOutOfRangeException(nameof(fromRate), fromRate, "A rate is positive.");
        if (toRate <= 0) throw new ArgumentOutOfRangeException(nameof(toRate), toRate, "A rate is positive.");
        if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames), frames, "A frame count is not negative.");

        return (frames * toRate + fromRate / 2) / fromRate;
    }

    /// <summary>
    /// Resamples interleaved PCM16. Returns the input array itself when the
    /// rates already match.
    /// </summary>
    public static short[] Resample(short[] samples, int channels, int fromRate, int toRate)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels), channels, "A frame has channels.");

        if (fromRate == toRate) return samples;

        long inFrames = samples.Length / channels;
        long outFrames = ConvertFrames(inFrames, fromRate, toRate);
        if (outFrames <= 0) return [];

        // Relative to the source Nyquist: band-limit when downsampling, leave
        // the signal alone when upsampling.
        double cutoff = Math.Min(1.0, (double)toRate / fromRate);

        var output = new short[checked((int)(outFrames * channels))];
        Span<double> kernel = stackalloc double[HalfTaps * 2];

        for (long frame = 0; frame < outFrames; frame++)
        {
            double centre = (double)frame * fromRate / toRate;
            long first = (long)Math.Floor(centre) - HalfTaps + 1;

            double weightSum = 0;
            for (int tap = 0; tap < kernel.Length; tap++)
            {
                double offset = (first + tap) - centre;
                double weight = Sinc(offset * cutoff) * Window(offset);
                kernel[tap] = weight;
                weightSum += weight;
            }

            // Should not happen with this window, but a zero sum would write NaN.
            if (weightSum == 0) weightSum = 1;

            for (int channel = 0; channel < channels; channel++)
            {
                double accumulated = 0;
                for (int tap = 0; tap < kernel.Length; tap++)
                {
                    // Outside the signal counts as zero, but weightSum still covers
                    // the whole kernel. Clamping or renormalising clicks at the ends.
                    long source = first + tap;
                    if (source < 0 || source >= inFrames) continue;

                    accumulated += kernel[tap] * samples[source * channels + channel];
                }

                output[frame * channels + channel] = Saturate(accumulated / weightSum);
            }
        }

        return output;
    }

    // Determinism gap: Math.Sin/Cos come from the platform libm, so Windows and
    // Linux may differ in the last ulp. Cooked audio is byte-stable per host
    // only. Inaudible; the fix would be our own deterministic sine.
    //
    // Blackman. A sharper window rings, which is pre-echo on transients.
    private static double Window(double offset)
    {
        double position = (offset + HalfTaps) / (HalfTaps * 2);
        if (position is < 0 or > 1) return 0;

        return 0.42
            - 0.5 * Math.Cos(2 * Math.PI * position)
            + 0.08 * Math.Cos(4 * Math.PI * position);
    }

    private static double Sinc(double x)
    {
        if (x == 0) return 1;

        double scaled = Math.PI * x;
        return Math.Sin(scaled) / scaled;
    }

    // Resampled peaks can overshoot full scale; a plain cast would wrap.
    private static short Saturate(double value)
    {
        double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded >= short.MaxValue) return short.MaxValue;
        if (rounded <= short.MinValue) return short.MinValue;
        return (short)rounded;
    }
}
