using System;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Converts keyvalues to and from their string form. Culture-invariant, and
/// non-finite floats have no string form: formatting one throws, parsing one fails.
/// </summary>
public static class KeyvalueWire
{
    /// <summary>The wire form of a <see cref="KeyvalueType.Bool"/>: <c>"1"</c> or <c>"0"</c>.</summary>
    public static string Format(bool value) => value ? "1" : "0";

    /// <summary>The wire form of a <see cref="KeyvalueType.Int"/>.</summary>
    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The wire form of a <see cref="KeyvalueType.Flags"/> bit set.</summary>
    public static string Format(uint value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The wire form of a <see cref="KeyvalueType.Float"/> or a
    /// <see cref="KeyvalueType.Distance"/>.
    /// </summary>
    public static string Format(float value) => Finite(value).ToString(CultureInfo.InvariantCulture);

    /// <summary>The wire form of a <see cref="KeyvalueType.Vec2"/>: <c>"x y"</c>.</summary>
    public static string Format(Vector2 value) =>
        string.Create(CultureInfo.InvariantCulture, $"{Finite(value.X)} {Finite(value.Y)}");

    /// <summary>The wire form of a <see cref="KeyvalueType.Vec3"/>: <c>"x y z"</c>.</summary>
    public static string Format(Vector3 value) =>
        string.Create(CultureInfo.InvariantCulture, $"{Finite(value.X)} {Finite(value.Y)} {Finite(value.Z)}");

    /// <summary>The wire form of a <see cref="KeyvalueType.Vec4"/>: <c>"x y z w"</c>.</summary>
    public static string Format(Vector4 value) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Finite(value.X)} {Finite(value.Y)} {Finite(value.Z)} {Finite(value.W)}");

    /// <summary>
    /// The wire form of a <see cref="KeyvalueType.Color"/>: three linear floats,
    /// <c>"r g b"</c>. Same convention as a light's colour in the map format.
    /// </summary>
    public static string FormatColor(Vector3 linearColor) => Format(linearColor);

    /// <summary>
    /// The wire form of a <see cref="KeyvalueType.Angles"/>: three floats in
    /// degrees, <c>"pitch yaw roll"</c>.
    /// </summary>
    public static string FormatAngles(Vector3 degrees) => Format(degrees);

    /// <summary>
    /// The wire form of a <see cref="KeyvalueType.NodeRef"/>: a
    /// <c>SceneNode.Id</c> in the hyphenated form. "No reference" is the empty
    /// string, which this never produces.
    /// </summary>
    public static string Format(Guid nodeId) => nodeId.ToString("D");

    /// <summary>Reads a <see cref="KeyvalueType.Bool"/>.</summary>
    public static bool TryParseBool(string? text, out bool value)
    {
        ReadOnlySpan<char> token = Trim(text);
        if (token.Length == 1 && token[0] == '1')
        {
            value = true;
            return true;
        }

        value = false;
        return token.Length == 1 && token[0] == '0';
    }

    /// <summary>Reads a <see cref="KeyvalueType.Int"/>.</summary>
    public static bool TryParseInt(string? text, out int value) =>
        int.TryParse(Trim(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>Reads a <see cref="KeyvalueType.Flags"/> bit set.</summary>
    public static bool TryParseFlags(string? text, out uint value) =>
        uint.TryParse(Trim(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Reads a finite <see cref="KeyvalueType.Float"/> or
    /// <see cref="KeyvalueType.Distance"/>.
    /// </summary>
    public static bool TryParseFloat(string? text, out float value) =>
        TryParseComponent(Trim(text), out value);

    /// <summary>Reads a <see cref="KeyvalueType.Vec2"/>.</summary>
    public static bool TryParseVec2(string? text, out Vector2 value)
    {
        Span<float> parts = stackalloc float[2];
        if (!TryReadFloats(text, parts))
        {
            value = default;
            return false;
        }

        value = new Vector2(parts[0], parts[1]);
        return true;
    }

    /// <summary>Reads a <see cref="KeyvalueType.Vec3"/>.</summary>
    public static bool TryParseVec3(string? text, out Vector3 value)
    {
        Span<float> parts = stackalloc float[3];
        if (!TryReadFloats(text, parts))
        {
            value = default;
            return false;
        }

        value = new Vector3(parts[0], parts[1], parts[2]);
        return true;
    }

    /// <summary>Reads a <see cref="KeyvalueType.Vec4"/>.</summary>
    public static bool TryParseVec4(string? text, out Vector4 value)
    {
        Span<float> parts = stackalloc float[4];
        if (!TryReadFloats(text, parts))
        {
            value = default;
            return false;
        }

        value = new Vector4(parts[0], parts[1], parts[2], parts[3]);
        return true;
    }

    /// <summary>Reads a <see cref="KeyvalueType.Color"/> as linear RGB.</summary>
    public static bool TryParseColor(string? text, out Vector3 linearColor) =>
        TryParseVec3(text, out linearColor);

    /// <summary>Reads a <see cref="KeyvalueType.Angles"/> triple, in degrees.</summary>
    public static bool TryParseAngles(string? text, out Vector3 degrees) =>
        TryParseVec3(text, out degrees);

    /// <summary>
    /// Reads a <see cref="KeyvalueType.NodeRef"/>. The empty string ("no
    /// reference") fails rather than yielding <see cref="Guid.Empty"/>.
    /// </summary>
    public static bool TryParseNodeRef(string? text, out Guid nodeId) =>
        Guid.TryParseExact(Trim(text), "D", out nodeId);

    /// <summary>
    /// Whether <paramref name="text"/> is a value the declared
    /// <paramref name="type"/> can carry. Text types accept anything; an empty
    /// node reference is valid and means "no reference".
    /// </summary>
    public static bool IsWellFormed(KeyvalueType type, string? text)
    {
        if (text is null)
            return false;

        switch (type)
        {
            case KeyvalueType.Bool:
                return TryParseBool(text, out _);
            case KeyvalueType.Int:
                return TryParseInt(text, out _);
            case KeyvalueType.Float:
            case KeyvalueType.Distance:
                return TryParseFloat(text, out _);
            case KeyvalueType.Vec2:
                return TryParseVec2(text, out _);
            case KeyvalueType.Vec3:
            case KeyvalueType.Color:
            case KeyvalueType.Angles:
                return TryParseVec3(text, out _);
            case KeyvalueType.Vec4:
                return TryParseVec4(text, out _);
            case KeyvalueType.NodeRef:
                return text.Length == 0 || TryParseNodeRef(text, out _);
            case KeyvalueType.Flags:
                return TryParseFlags(text, out _);
            case KeyvalueType.String:
            case KeyvalueType.TargetName:
            case KeyvalueType.AssetModel:
            case KeyvalueType.AssetMaterial:
            case KeyvalueType.AssetTexture:
            case KeyvalueType.AssetSound:
            case KeyvalueType.Choices:
                return true;
            default:
                return false;
        }
    }

    // No Split: this runs on load and on every property commit.
    // The component count must match, so a Vec2 in a Vec3 field is refused.
    private static bool TryReadFloats(string? text, Span<float> values)
    {
        if (text is null)
            return false;

        ReadOnlySpan<char> span = text;
        int written = 0;
        int i = 0;
        while (true)
        {
            while (i < span.Length && char.IsWhiteSpace(span[i]))
                i++;
            if (i >= span.Length)
                break;

            int start = i;
            while (i < span.Length && !char.IsWhiteSpace(span[i]))
                i++;

            if (written == values.Length)
                return false;
            if (!TryParseComponent(span[start..i], out float parsed))
                return false;

            values[written++] = parsed;
        }

        return written == values.Length;
    }

    // NumberStyles.Float has no AllowThousands, so "1,5" fails instead of reading as 15.
    private static bool TryParseComponent(ReadOnlySpan<char> token, out float value)
    {
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;

        if (float.IsFinite(value))
            return true;

        value = 0f;
        return false;
    }

    private static ReadOnlySpan<char> Trim(string? text) => text is null ? default : text.AsSpan().Trim();

    private static float Finite(float value) => float.IsFinite(value)
        ? value
        : throw new ArgumentOutOfRangeException(
            nameof(value), value, "A keyvalue cannot carry a value that cannot be read back.");
}
