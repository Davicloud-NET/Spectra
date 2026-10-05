using System;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>Adds up what the solids on one line take from a sound.</summary>
public static class WallLoss
{
    /// <summary>
    /// How deep the sound or the listener must be inside a solid before that
    /// solid costs its whole surface loss. Less deep, it costs that share.
    /// </summary>
    // A camera that dips into a wall for a frame is a few centimetres deep.
    // With the whole surface loss from the first millimetre the room behind
    // it would drop by 10 dB for that frame and come back.
    public const float EndDepth = 0.25f;

    /// <summary>
    /// The loss along a line of <paramref name="length"/>: what each of
    /// <paramref name="spans"/> takes at its thickness, added up.
    /// </summary>
    // A solid the line starts or ends in counts by how deep that end is in it.
    public static AcousticLoss Sum(ReadOnlySpan<SolidSpan> spans, float length, IAcousticMaterials materials)
    {
        ArgumentNullException.ThrowIfNull(materials);

        var total = default(AcousticLoss);

        for (int i = 0; i < spans.Length; i++)
        {
            ref readonly SolidSpan span = ref spans[i];
            AcousticPreset preset = materials.Resolve(span.Material);

            bool holdsAnEnd = span.Start <= SolidSpan.Tolerance || span.End >= length - SolidSpan.Tolerance;
            total += holdsAnEnd ? LossInside(preset, span.Thickness) : preset.LossThrough(span.Thickness);
        }

        return total;
    }

    /// <summary>Whether a line of <paramref name="length"/> ends inside the last of its <paramref name="spans"/>.</summary>
    public static bool EndsInSolid(ReadOnlySpan<SolidSpan> spans, float length) =>
        spans.Length > 0 && spans[^1].End >= length - SolidSpan.Tolerance;

    private static AcousticLoss LossInside(AcousticPreset preset, float depth)
    {
        // Also catches NaN.
        if (!(depth > 0f)) return default;

        float share = MathF.Min(depth / EndDepth, 1f);

        return new AcousticLoss(
            preset.SurfaceDb * share + preset.PerMeterDb * depth,
            preset.SurfaceHfDb * share + preset.PerMeterHfDb * depth);
    }
}
