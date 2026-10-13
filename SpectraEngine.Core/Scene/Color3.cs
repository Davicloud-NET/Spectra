using SpectraEngine.Core.Graphics;
using System;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// A colour as a person picks one: red, green and blue as a screen shows
/// them, each 0 to 1. Every operation means what it means on a Roblox Color3.
/// </summary>
/// <param name="R">Red. Nothing keeps it between 0 and 1.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
// The engine lights in linear values. ToLinear and FromLinear cross.
public readonly record struct Color3(float R, float G, float B)
{
    // How many floats each side of the curve's own answer FromLinear tries.
    // Four was the most any value needed, measured up to three times white.
    private const int NearbySteps = 8;

    /// <summary>A colour from parts of 0 to 255. A part outside that is taken as it is.</summary>
    public static Color3 FromRgb(float r, float g, float b) => new(r / 255f, g / 255f, b / 255f);

    /// <summary>
    /// A colour from a hue, a saturation and a value, each 0 to 1. The hue
    /// goes round: 0 and 1 are both red.
    /// </summary>
    public static Color3 FromHsv(float h, float s, float v)
    {
        float turn = h * 6f;
        float sector = MathF.Floor(turn);
        float within = turn - sector;

        float p = v * (1f - s);
        float q = v * (1f - (within * s));
        float t = v * (1f - ((1f - within) * s));

        // A hue that is no number has no sector, and a cast of one to an int
        // is anything. It takes the first, and the colour has such a part too.
        float wrapped = sector - (6f * MathF.Floor(sector / 6f));
        return (float.IsFinite(wrapped) ? (int)wrapped : 0) switch
        {
            1 => new Color3(q, v, p),
            2 => new Color3(p, v, t),
            3 => new Color3(p, q, v),
            4 => new Color3(t, p, v),
            5 => new Color3(v, p, q),
            _ => new Color3(v, t, p),
        };
    }

    /// <summary>
    /// Reads a colour written as six hex digits, or as three that are each
    /// doubled, with or without a # in front.
    /// </summary>
    public static bool TryFromHex(ReadOnlySpan<char> hex, out Color3 color)
    {
        color = default;
        if (hex.Length > 0 && hex[0] == '#')
            hex = hex[1..];
        if (hex.Length is not (3 or 6))
            return false;

        Span<int> parts = stackalloc int[3];
        int digits = hex.Length / 3;
        for (int i = 0; i < 3; i++)
        {
            int high = HexDigit(hex[i * digits]);
            int low = digits == 2 ? HexDigit(hex[(i * 2) + 1]) : high;
            if (high < 0 || low < 0)
                return false;

            parts[i] = (high * 16) + low;
        }

        color = FromRgb(parts[0], parts[1], parts[2]);
        return true;
    }

    /// <summary>The colour that became a linear one, as the engine's lights hold it.</summary>
    public static Color3 FromLinear(Vector3 linear) =>
        new(DisplayOf(linear.X), DisplayOf(linear.Y), DisplayOf(linear.Z));

    /// <summary>The colour as the engine's lights hold it: linear red, green and blue.</summary>
    public Vector3 ToLinear() => ColorSpace.SrgbToLinear(new Vector3(R, G, B));

    /// <summary>The colour part of the way to the goal: 0 is this colour and 1 is the goal.</summary>
    public Color3 Lerp(Color3 goal, float alpha) =>
        new(R + ((goal.R - R) * alpha), G + ((goal.G - G) * alpha), B + ((goal.B - B) * alpha));

    /// <summary>
    /// The hue, the saturation and the value, each 0 to 1. A grey has a hue
    /// of 0, and a red with as much green as blue a hue of 1, as in Roblox.
    /// </summary>
    public (float H, float S, float V) ToHsv()
    {
        float most = MathF.Max(R, MathF.Max(G, B));
        float least = MathF.Min(R, MathF.Min(G, B));
        float spread = most - least;
        if (!(spread > 0f))
            return (0f, 0f, most);

        // How far each part is below the largest, 0 to 1.
        float red = (most - R) / spread;
        float green = (most - G) / spread;
        float blue = (most - B) / spread;

        // The largest part and the smallest say which sixth of the wheel. Red
        // over the smallest green is the last one, and Roblox puts pure red
        // there too: at its end, 1, and not at 0.
        float sixth;
        if (R == most)
            sixth = G == least ? 5f + blue : 1f - green;
        else if (G == most)
            sixth = B == least ? 1f + red : 3f - blue;
        else
            sixth = R == least ? 3f + green : 5f - red;

        return (sixth / 6f, spread / most, most);
    }

    /// <summary>
    /// Six hex digits in lower case with no # in front. Each part is brought
    /// between 0 and 1 and rounded to the nearest of 255 steps.
    /// </summary>
    public string ToHex() => string.Create(
        CultureInfo.InvariantCulture, $"{Step(R):x2}{Step(G):x2}{Step(B):x2}");

    /// <summary>The three parts with six digits each, as Roblox prints a colour: <c>1, 0.501961, 0</c>.</summary>
    public override string ToString() => $"{Printed(R)}, {Printed(G)}, {Printed(B)}";

    private static int Step(float part) =>
        float.IsNaN(part) ? 0 : (int)MathF.Round(Math.Clamp(part, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);

    // What C's %g prints, which is what Roblox prints a part with.
    private static string Printed(float part)
    {
        if (float.IsNaN(part))
            return "nan";
        if (float.IsInfinity(part))
            return part > 0f ? "inf" : "-inf";

        return part.ToString("g6", CultureInfo.InvariantCulture);
    }

    private static int HexDigit(char digit) => digit switch
    {
        >= '0' and <= '9' => digit - '0',
        >= 'a' and <= 'f' => digit - 'a' + 10,
        >= 'A' and <= 'F' => digit - 'A' + 10,
        _ => -1,
    };

    // The curve's own way back lands a rounding step off for many values,
    // white among them. So the values round it are tried until one becomes
    // the linear value again. A colour of 255 steps, as a picker and FromRgb
    // give one, is tried first.
    private static float DisplayOf(float linear)
    {
        float near = ColorSpace.LinearToSrgb(linear);
        if (!float.IsFinite(near))
            return near;

        float step = MathF.Round(near * 255f) / 255f;
        if (ColorSpace.SrgbToLinear(step) == linear)
            return step;
        if (ColorSpace.SrgbToLinear(near) == linear)
            return near;

        float above = near;
        float below = near;
        for (int i = 0; i < NearbySteps; i++)
        {
            above = MathF.BitIncrement(above);
            if (ColorSpace.SrgbToLinear(above) == linear)
                return above;

            below = MathF.BitDecrement(below);
            if (ColorSpace.SrgbToLinear(below) == linear)
                return below;
        }

        return near;
    }
}
