using System;
using System.Globalization;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Compares the picture that leaves through the shared handle with the one a
/// window would have shown. Catches a double sRGB encode, which raises no error.
/// </summary>
public static class ViewportCompare
{
    /// <summary>
    /// The largest per-channel difference that still passes, in 8-bit levels.
    /// </summary>
    // Driver rounding slack. A correct pair is identical; a double encode is
    // about 35 levels off.
    public const int Threshold = 2;

    public enum Channel
    {
        Red,
        Green,
        Blue,
        Alpha,
    }

    /// <summary>The largest per-channel difference between two readbacks, and where it was.</summary>
    /// <param name="MaxDelta">Largest absolute per-channel difference, in 8-bit levels.</param>
    /// <param name="WorstPixel">Index of the texel it was measured at, in the compared order.</param>
    /// <param name="Reference">The reference value at that texel and channel.</param>
    /// <param name="Shared">The shared value at that texel and channel.</param>
    public readonly record struct Reading(
        int MaxDelta,
        Channel WorstChannel,
        int WorstPixel,
        byte Reference,
        byte Shared,
        int PixelCount)
    {
        /// <summary>True when the two pictures agree to within <see cref="Threshold"/>.</summary>
        public bool Passes => MaxDelta <= Threshold;

        public string Verdict => Passes
            ? "PASS"
            : "FAIL (the shared picture is not the picture a window would have shown)";

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "max delta {0} on {1} at texel {2} (window {3}, shared {4}), over {5} texels",
            MaxDelta, WorstChannel, WorstPixel, Reference, Shared, PixelCount);
    }

    /// <summary>
    /// Whether a readback holds more than one colour. Check the reference first:
    /// two blank pictures compare equal.
    /// </summary>
    public static bool HasVariation(ReadOnlySpan<byte> picture)
    {
        if (picture.Length < PixelReadback.BytesPerPixel * 2) return false;

        for (int i = PixelReadback.BytesPerPixel; i < picture.Length; i++)
        {
            if (picture[i] != picture[i % PixelReadback.BytesPerPixel]) return true;
        }

        return false;
    }

    /// <summary>
    /// Compares two 8-bit RGBA readbacks of the same picture and reports the
    /// largest per-channel difference. On a tie the first texel is reported.
    /// </summary>
    /// <param name="reference">The picture an ordinary sRGB target received.</param>
    /// <param name="shared">The picture read back through the shared handle.</param>
    /// <exception cref="ArgumentException">The two spans are not the same length, or not whole texels.</exception>
    public static Reading Compare(ReadOnlySpan<byte> reference, ReadOnlySpan<byte> shared)
    {
        if (reference.Length != shared.Length)
        {
            throw new ArgumentException(
                $"The two readbacks must describe the same picture; got {reference.Length} and {shared.Length} bytes.",
                nameof(shared));
        }

        if (reference.Length == 0 || reference.Length % PixelReadback.BytesPerPixel != 0)
        {
            throw new ArgumentException(
                $"A readback is whole 8-bit RGBA texels; {reference.Length} bytes is not.", nameof(reference));
        }

        int pixels = reference.Length / PixelReadback.BytesPerPixel;
        int maxDelta = 0;
        var worstChannel = Channel.Red;
        int worstPixel = 0;
        byte worstReference = 0;
        byte worstShared = 0;

        for (int i = 0; i < reference.Length; i++)
        {
            int delta = Math.Abs(reference[i] - shared[i]);
            if (delta <= maxDelta) continue;

            maxDelta = delta;
            worstChannel = (Channel)(i % PixelReadback.BytesPerPixel);
            worstPixel = i / PixelReadback.BytesPerPixel;
            worstReference = reference[i];
            worstShared = shared[i];
        }

        return new Reading(maxDelta, worstChannel, worstPixel, worstReference, worstShared, pixels);
    }
}
