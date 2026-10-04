using System;
using System.Globalization;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Decides whether two readbacks are the same picture drawn by two pipelines.
/// </summary>
public static class PipelineCompare
{
    /// <summary>
    /// The largest per-channel difference a texel may have, in 8-bit levels.
    /// </summary>
    // The G-buffer keeps albedo in eight bits and rebuilds position from depth.
    // That is worth one level against the forward path. Two leaves room for a
    // driver that rounds the other way.
    public const int Tolerance = 2;

    /// <summary>
    /// How many texels of <paramref name="pixelCount"/> may differ by more than
    /// <see cref="Tolerance"/>.
    /// </summary>
    // A shadow edge is a depth comparison, and the two paths reach the same
    // world position by different arithmetic. A texel may land either side.
    public static int AllowedOutliers(int pixelCount) => pixelCount / 500;

    /// <summary>What a comparison measured.</summary>
    /// <param name="MaxDelta">Largest per-channel difference, in 8-bit levels.</param>
    /// <param name="Outliers">Texels that differ by more than <see cref="Tolerance"/>.</param>
    /// <param name="WorstPixel">Index of the texel with the largest difference.</param>
    /// <param name="PixelCount">Texels compared.</param>
    public readonly record struct Reading(int MaxDelta, int Outliers, int WorstPixel, int PixelCount)
    {
        /// <summary>True when the two readbacks are the same picture.</summary>
        public bool Passes => Outliers <= AllowedOutliers(PixelCount);

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "{0} of {1} texels differ by more than {2} ({3} allowed), max delta {4} at texel {5}",
            Outliers, PixelCount, Tolerance, AllowedOutliers(PixelCount), MaxDelta, WorstPixel);
    }

    /// <summary>
    /// Compares two 8-bit RGBA readbacks. Alpha is ignored: a pipeline may
    /// leave anything there.
    /// </summary>
    /// <exception cref="ArgumentException">The two spans are not the same length, or not whole texels.</exception>
    public static Reading Compare(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        if (first.Length != second.Length)
        {
            throw new ArgumentException(
                $"The two readbacks must be the same size; got {first.Length} and {second.Length} bytes.",
                nameof(second));
        }

        if (first.Length == 0 || first.Length % PixelReadback.BytesPerPixel != 0)
        {
            throw new ArgumentException(
                $"A readback is whole 8-bit RGBA texels; {first.Length} bytes is not.", nameof(first));
        }

        int pixels = first.Length / PixelReadback.BytesPerPixel;
        int maxDelta = 0;
        int worstPixel = 0;
        int outliers = 0;

        for (int pixel = 0; pixel < pixels; pixel++)
        {
            int i = pixel * PixelReadback.BytesPerPixel;
            int delta = Math.Max(
                Math.Abs(first[i] - second[i]),
                Math.Max(Math.Abs(first[i + 1] - second[i + 1]), Math.Abs(first[i + 2] - second[i + 2])));

            if (delta > Tolerance)
                outliers++;

            if (delta > maxDelta)
            {
                maxDelta = delta;
                worstPixel = pixel;
            }
        }

        return new Reading(maxDelta, outliers, worstPixel, pixels);
    }
}
