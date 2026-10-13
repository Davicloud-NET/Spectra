using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>A colour: red, green and blue as a screen shows them, each 0 to 1. A Color3 never changes: every method gives a new one.</summary>
// Each member is a thin call of the Core type's own.
[ScriptValue("Color3")]
internal static partial class Color3Api
{
    /// <summary>The red part, 0 to 1.</summary>
    [ScriptProperty]
    public static float R(Color3 self) => self.R;

    /// <summary>The green part, 0 to 1.</summary>
    [ScriptProperty]
    public static float G(Color3 self) => self.G;

    /// <summary>The blue part, 0 to 1.</summary>
    [ScriptProperty]
    public static float B(Color3 self) => self.B;

    /// <summary>The colour part of the way to the goal: 0 is this colour and 1 is the goal.</summary>
    [ScriptMethod]
    public static Color3 Lerp(Color3 self, Color3 goal, float alpha) => self.Lerp(goal, alpha);

    /// <summary>The hue, the saturation and the value of the colour, each 0 to 1. A grey has a hue of 0. A red with as much green as blue has a hue of 1, as in Roblox, and Color3.fromHSV makes the same red from 0 and from 1.</summary>
    [ScriptMethod]
    public static (float H, float S, float V) ToHSV(Color3 self) => self.ToHsv();

    /// <summary>The colour as six hex digits in lower case, with no # in front.</summary>
    [ScriptMethod]
    public static string ToHex(Color3 self) => self.ToHex();
}
