using System;
using System.Globalization;

namespace SpectraEngine.Core.Assets;

// Reads the numbers on a material file's line: a plain list or a hex colour.
internal static class MaterialNumbers
{
    // Whitespace or comma separated, invariant culture.
    public static bool TryParseList(ReadOnlySpan<char> value, Span<float> destination, out int count)
    {
        destination.Clear();
        count = 0;

        ReadOnlySpan<char> remaining = value;
        while (true)
        {
            while (!remaining.IsEmpty && (char.IsWhiteSpace(remaining[0]) || remaining[0] == ','))
                remaining = remaining[1..];
            if (remaining.IsEmpty) break;

            int end = 0;
            while (end < remaining.Length && !char.IsWhiteSpace(remaining[end]) && remaining[end] != ',')
                end++;

            if (count == destination.Length) return false;
            if (!float.TryParse(remaining[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                return false;

            destination[count++] = parsed;
            remaining = remaining[end..];
        }

        return count > 0;
    }

    // RRGGBB or RRGGBBAA, without the '#'.
    public static bool TryParseHexColor(ReadOnlySpan<char> digits, Span<float> destination, out int count)
    {
        destination.Clear();
        count = 0;
        if (digits.Length is not (6 or 8)) return false;

        int components = digits.Length / 2;
        for (int i = 0; i < components; i++)
        {
            if (!byte.TryParse(digits.Slice(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                return false;
            destination[i] = b / 255f;
        }

        count = components;
        return true;
    }
}
