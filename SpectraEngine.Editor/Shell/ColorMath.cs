using SpectraEngine.Core.Graphics;
using System;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// The conversions a colour picker needs: hue/saturation/value against sRGB,
/// and sRGB against the linear light the engine stores.
/// </summary>
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

    /// <summary>
    /// Hue in degrees and saturation/value in 0..1, from an sRGB triple. A grey reports hue 0.
    /// </summary>
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

    /// <summary>Linear to sRGB, clamped to displayable.</summary>
    public static Vector3 LinearToSrgb(Vector3 linear) =>
        ColorSpace.LinearToSrgb(Vector3.Clamp(linear, Vector3.Zero, Vector3.One));

    /// <summary>sRGB to linear.</summary>
    public static Vector3 SrgbToLinear(Vector3 srgb) => ColorSpace.SrgbToLinear(srgb);

    /// <summary>An sRGB triple as <c>#RRGGBB</c>.</summary>
    public static string ToHex(Vector3 srgb) =>
        $"#{ToByte(srgb.X):X2}{ToByte(srgb.Y):X2}{ToByte(srgb.Z):X2}";

    /// <summary>Reads <c>#RRGGBB</c> or <c>RRGGBB</c> into an sRGB triple.</summary>
    // Six digits only, same as the panel's hex cell.
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
