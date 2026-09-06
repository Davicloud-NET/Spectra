using SpectraEngine.Core.Graphics;
using System;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The conversions a colour picker needs: hue/saturation/value against sRGB,
/// and sRGB against the linear light the engine stores.
/// </summary>
/// <remarks>
/// <para>
/// <b>The picker works in sRGB and the scene stores linear.</b> That is the same
/// split the material format already keeps: a <c>color</c> directive is written
/// in sRGB and stored linear, because a hex a person types is a display code
/// rather than a quantity of light. A picker that dragged in linear would have
/// its whole top half look identical.
/// </para>
/// <para>
/// <b>The linear conversions go through Core's <see cref="ColorSpace"/> rather
/// than a fifth copy of the curve.</b> There are already four in the tree, and
/// the panel's own hex path is one of them; a picker that disagreed with the
/// box beside it by a code value would be impossible to explain.
/// </para>
/// </remarks>
public static class ColorMath
{
    /// <summary>An sRGB triple from hue in degrees and saturation/value in 0..1.</summary>
    public static Vector3 HsvToSrgb(float hueDegrees, float saturation, float value)
    {
        float h = ((hueDegrees % 360f) + 360f) % 360f / 60f;
        float s = Math.Clamp(saturation, 0f, 1f);
        float v = Math.Clamp(value, 0f, 1f);

        float c = v * s;
        float x = c * (1f - MathF.Abs((h % 2f) - 1f));
        float m = v - c;

        (float r, float g, float b) = (int)h switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };

        return new Vector3(r + m, g + m, b + m);
    }

    /// <summary>Hue in degrees and saturation/value in 0..1, from an sRGB triple.</summary>
    /// <remarks>
    /// A grey reports hue 0 rather than an undefined one, so a picker opened on
    /// white does not jump to a random corner of the strip.
    /// </remarks>
    public static (float Hue, float Saturation, float Value) SrgbToHsv(Vector3 srgb)
    {
        float r = Math.Clamp(srgb.X, 0f, 1f);
        float g = Math.Clamp(srgb.Y, 0f, 1f);
        float b = Math.Clamp(srgb.Z, 0f, 1f);

        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;

        float hue = 0f;
        if (delta > 1e-6f)
        {
            if (max == r) hue = 60f * (((g - b) / delta) % 6f);
            else if (max == g) hue = 60f * (((b - r) / delta) + 2f);
            else hue = 60f * (((r - g) / delta) + 4f);
        }

        if (hue < 0f) hue += 360f;

        float saturation = max <= 1e-6f ? 0f : delta / max;
        return (hue, saturation, max);
    }

    /// <summary>Linear light to the sRGB a person reads, clamped to displayable.</summary>
    public static Vector3 LinearToSrgb(Vector3 linear) =>
        ColorSpace.LinearToSrgb(Vector3.Clamp(linear, Vector3.Zero, Vector3.One));

    /// <summary>The sRGB a person typed to the linear light the engine stores.</summary>
    public static Vector3 SrgbToLinear(Vector3 srgb) => ColorSpace.SrgbToLinear(srgb);

    /// <summary>An sRGB triple as <c>#RRGGBB</c>.</summary>
    public static string ToHex(Vector3 srgb) =>
        $"#{ToByte(srgb.X):X2}{ToByte(srgb.Y):X2}{ToByte(srgb.Z):X2}";

    /// <summary>Reads <c>#RRGGBB</c> or <c>RRGGBB</c> into an sRGB triple.</summary>
    /// <remarks>
    /// Six digits only, the same rule the panel's own hex cell keeps: a
    /// three-digit form and an alpha form are both things somebody could type,
    /// and accepting one spelling in the picker that the box beside it refuses
    /// would be worse than refusing both.
    /// </remarks>
    public static bool TryParseHex(string? text, out Vector3 srgb)
    {
        srgb = default;
        if (text is null) return false;

        ReadOnlySpan<char> span = text.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#') span = span[1..];
        if (span.Length != 6) return false;

        if (!byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
            !byte.TryParse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
            !byte.TryParse(span[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        srgb = new Vector3(r / 255f, g / 255f, b / 255f);
        return true;
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
}
