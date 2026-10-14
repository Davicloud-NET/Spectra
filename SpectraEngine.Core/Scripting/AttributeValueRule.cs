using System;
using System.Numerics;
using System.Text;
using System.Text.Unicode;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// The rule for the value of an attribute a level is built with. It is
/// stricter than what a running script may set: a map is text, and text has
/// no way to write a number that is not finite.
/// </summary>
public static class AttributeValueRule
{
    /// <summary>Why a level cannot hold this value, as a sentence, or null when it can.</summary>
    public static string? Refusal(in MemberValue value) => value.Kind switch
    {
        MemberValueKind.Text => value.IsUtf8 ? Utf8Refusal(value.Utf8) : TextRefusal(value.Text),
        MemberValueKind.Boolean => null,
        MemberValueKind.Number => double.IsFinite(value.Number) ? null : NotFinite,
        MemberValueKind.Vector3 or MemberValueKind.Color => IsFinite(value.Vector) ? null : NotFinite,
        MemberValueKind.CFrame => IsFinite(value.Frame.Position) && IsFinite(value.Frame.Rotation) ? null : NotFinite,
        _ => "An attribute has to hold a value.",
    };

    private const string NotFinite = "An attribute's number has to be finite.";

    private const string NotText = "An attribute's text has to be text a file can hold.";

    private static string? TextRefusal(string text)
    {
        if (!TextRule.IsWellFormed(text))
            return NotText;

        int bytes = Encoding.UTF8.GetByteCount(text);
        return bytes <= AttributeLimits.MaxTextBytes ? null : TooLong(bytes);
    }

    private static string? Utf8Refusal(ReadOnlySpan<byte> utf8)
    {
        if (!Utf8.IsValid(utf8))
            return NotText;

        return utf8.Length <= AttributeLimits.MaxTextBytes ? null : TooLong(utf8.Length);
    }

    private static string TooLong(int bytes) =>
        $"An attribute's text is at most {AttributeLimits.MaxTextBytes} bytes long, and this is {bytes}.";

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
