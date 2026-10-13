using System.Text;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>The ways a script makes a Color3.</summary>
[ScriptLibrary("Color3")]
internal static partial class Color3Library
{
    // A sign and six digits. A longer text is no colour.
    private const int MostHexChars = 7;

    /// <summary>Makes a colour from red, green and blue, each 0 to 1. One that is left out is 0.</summary>
    [ScriptFunction(Name = "new")]
    public static Color3 New(float red = 0f, float green = 0f, float blue = 0f) => new(red, green, blue);

    /// <summary>Makes a colour from red, green and blue, each 0 to 255. One that is left out is 0.</summary>
    [ScriptFunction(Name = "fromRGB")]
    public static Color3 FromRgb(float red = 0f, float green = 0f, float blue = 0f) => Color3.FromRgb(red, green, blue);

    /// <summary>Makes a colour from a hue, a saturation and a value, each 0 to 1. One that is left out is 0.</summary>
    [ScriptFunction(Name = "fromHSV")]
    public static Color3 FromHsv(float hue = 0f, float saturation = 0f, float value = 0f) =>
        Color3.FromHsv(hue, saturation, value);

    /// <summary>Makes a colour from six hex digits, or from three that are each doubled, with or without a # in front.</summary>
    // Written by hand: text that is no colour is the script's error.
    [ScriptFunction(Name = "fromHex", Parameters = "hex: string", Returns = "Color3")]
    private static int FromHex(LuauStack stack)
    {
        if (!stack.TryGetUtf8(1, out ReadOnlySpan<byte> hex))
            return ScriptArguments.Wrong(stack, 1, "fromHex", "string");

        // A hex digit is one byte, so a byte is taken for a character.
        Span<char> text = stackalloc char[MostHexChars];
        if (hex.Length <= text.Length)
        {
            for (int i = 0; i < hex.Length; i++)
                text[i] = (char)hex[i];

            if (Color3.TryFromHex(text[..hex.Length], out Color3 color))
                return ScriptResult.Of(ColorValue.Push(stack, color));
        }

        return stack.Fail(
            $"Color3.fromHex takes six hex digits or three, with or without a # in front, and got \"{Encoding.UTF8.GetString(hex)}\"");
    }
}
