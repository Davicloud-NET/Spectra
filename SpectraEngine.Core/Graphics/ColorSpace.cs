using System;
using System.Numerics;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Converts colours typed as numbers between sRGB and linear. Textures and
/// render targets convert in hardware and do not go through this.
/// </summary>
// The piecewise sRGB curve, not pow(2.2): it has to match what the hardware
// does near black.
public static class ColorSpace
{
    /// <summary>
    /// Decodes one sRGB-encoded channel value to linear. Values outside 0..1
    /// are not clamped.
    /// </summary>
    public static float SrgbToLinear(float value) =>
        value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    /// <summary>Encodes one linear channel value in 0..1 as sRGB.</summary>
    public static float LinearToSrgb(float value) =>
        value <= 0.0031308f
            ? value * 12.92f
            : 1.055f * MathF.Pow(value, 1f / 2.4f) - 0.055f;

    /// <summary>Decodes an RGB triple from sRGB to linear.</summary>
    public static Vector3 SrgbToLinear(Vector3 color) =>
        new(SrgbToLinear(color.X), SrgbToLinear(color.Y), SrgbToLinear(color.Z));

    /// <summary>Encodes an RGB triple from linear to sRGB.</summary>
    public static Vector3 LinearToSrgb(Vector3 color) =>
        new(LinearToSrgb(color.X), LinearToSrgb(color.Y), LinearToSrgb(color.Z));

    /// <summary>
    /// Decodes an RGBA colour from sRGB to linear. Alpha is not converted.
    /// </summary>
    public static Vector4 SrgbToLinear(Vector4 color) =>
        new(SrgbToLinear(color.X), SrgbToLinear(color.Y), SrgbToLinear(color.Z), color.W);
}

/// <summary>
/// The colours the backends clear to, in linear values. Linear because a
/// clear into an sRGB target is encoded by the target.
/// </summary>
public static class ClearColors
{
    /// <summary>
    /// The empty-scene background: cornflower blue, <c>#6495ED</c> as a display colour.
    /// </summary>
    public static readonly Vector4 Sky = ColorSpace.SrgbToLinear(new Vector4(0.392f, 0.584f, 0.929f, 1f));

    /// <summary>
    /// Black, the wireframe pipeline's background.
    /// </summary>
    public static readonly Vector4 Wireframe = new(0f, 0f, 0f, 1f);
}
