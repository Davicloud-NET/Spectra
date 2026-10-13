using System.Numerics;
using System.Text;

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
        MemberValueKind.Text => TextRefusal(value.Text),
        MemberValueKind.Boolean => null,
        MemberValueKind.Number => double.IsFinite(value.Number) ? null : NotFinite,
        MemberValueKind.Vector3 or MemberValueKind.Color => IsFinite(value.Vector) ? null : NotFinite,
        MemberValueKind.CFrame => IsFinite(value.Frame.Position) && IsFinite(value.Frame.Rotation) ? null : NotFinite,
        _ => "An attribute has to hold a value.",
    };

    private const string NotFinite = "An attribute's number has to be finite.";

    private static string? TextRefusal(string text)
    {
        if (!TextRule.IsWellFormed(text))
            return "An attribute's text has to be text a file can hold.";

        int bytes = Encoding.UTF8.GetByteCount(text);
        return bytes <= AttributeLimits.MaxTextBytes
            ? null
            : $"An attribute's text is at most {AttributeLimits.MaxTextBytes} bytes long, and this is {bytes}.";
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
