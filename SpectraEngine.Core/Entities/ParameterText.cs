using System;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// Turns a typed value into the text a wire carries, and that text back into
/// the value. The text is the one <see cref="KeyvalueWire"/> writes for a
/// keyvalue of the same type, so an entity reads what it always read.
/// </summary>
public static class ParameterText
{
    /// <summary>
    /// The text that carries <paramref name="value"/> to an input of
    /// <paramref name="parameter"/>, or from an output of it.
    /// </summary>
    /// <returns>
    /// False when the value is not one the parameter takes: a number for an
    /// input that takes none, text where a number belongs, no value where
    /// one is needed, or a number that is not finite.
    /// </returns>
    // With no type said the value is written in its own form, as a person
    // would type it on a wire.
    public static bool TryFormat(ParameterType parameter, in ParameterValue value, out string text)
    {
        text = "";

        if (value.Kind == ParameterValueKind.None)
            return !parameter.HasValue || parameter.IsOptional || parameter.ValueType == KeyvalueType.NodeRef;

        if (parameter.IsNone)
            return false;

        return parameter.HasValue
            ? TryFormatAs(parameter.ValueType, in value, out text)
            : TryFormatPlain(in value, out text);
    }

    /// <summary>
    /// The value <paramref name="text"/> is for <paramref name="parameter"/>.
    /// With no type said it is the text itself. A parameter that takes no
    /// value gives none, whatever the text.
    /// </summary>
    /// <returns>False when the type cannot read the text.</returns>
    public static bool TryParse(ParameterType parameter, string? text, out ParameterValue value)
    {
        string wire = text ?? "";

        if (!parameter.IsSaid)
        {
            value = ParameterValue.Of(wire);
            return true;
        }

        value = ParameterValue.None;
        if (parameter.IsNone || (wire.Length == 0 && parameter.IsOptional))
            return true;

        return TryParseAs(parameter.ValueType, wire, out value);
    }

    /// <summary>
    /// What becomes of the parameter a wire was authored with, when the wire
    /// leaves an output that sends <paramref name="sent"/> and arrives at an
    /// input that takes <paramref name="taken"/>.
    /// </summary>
    public static ParameterVerdict Judge(ParameterType sent, ParameterType taken, string? parameter)
    {
        string text = parameter ?? "";

        if (sent.HasValue)
            return text.Length == 0 ? ParameterVerdict.Fine : ParameterVerdict.Replaced;

        if (!taken.IsSaid)
            return ParameterVerdict.Fine;

        if (taken.IsNone)
            return text.Length == 0 ? ParameterVerdict.Fine : ParameterVerdict.NotTaken;

        if (TryParse(taken, text, out _))
            return ParameterVerdict.Fine;

        if (text.Length > 0)
            return ParameterVerdict.Unreadable;

        // An output that does not say what it sends may send the value.
        return sent.IsNone ? ParameterVerdict.Missing : ParameterVerdict.Fine;
    }

    private static bool TryFormatAs(KeyvalueType type, in ParameterValue value, out string text)
    {
        text = "";

        switch (type)
        {
            case KeyvalueType.Bool:
                if (value.Kind != ParameterValueKind.Boolean)
                    return false;

                text = KeyvalueWire.Format(value.Boolean);
                return true;

            case KeyvalueType.Int:
                return value.Kind == ParameterValueKind.Number && TryFormatWhole(value.Number, out text);

            case KeyvalueType.Float:
                return value.Kind == ParameterValueKind.Number && TryFormatSingle(value.Number, out text);

            case KeyvalueType.String:
            case KeyvalueType.TargetName:
                text = value.Text;
                return value.Kind == ParameterValueKind.Text;

            case KeyvalueType.Vec2:
                return TryFormatVector(in value, 2, out text);

            case KeyvalueType.Vec3:
            case KeyvalueType.Color:
            case KeyvalueType.Angles:
                return TryFormatVector(in value, 3, out text);

            case KeyvalueType.Vec4:
                return TryFormatVector(in value, 4, out text);

            case KeyvalueType.NodeRef:
                return TryFormatNode(in value, out text);

            default:
                return false;
        }
    }

    private static bool TryFormatPlain(in ParameterValue value, out string text)
    {
        switch (value.Kind)
        {
            case ParameterValueKind.Boolean:
                text = KeyvalueWire.Format(value.Boolean);
                return true;

            // A whole number keeps every digit that way. As a float it would
            // lose them past 16777216.
            case ParameterValueKind.Number:
                return TryFormatWhole(value.Number, out text) || TryFormatSingle(value.Number, out text);

            case ParameterValueKind.Text:
                text = value.Text;
                return true;

            case ParameterValueKind.Vector:
                return TryFormatVector(in value, value.VectorSize, out text);

            case ParameterValueKind.NodeId:
                return TryFormatNode(in value, out text);

            default:
                text = "";
                return false;
        }
    }

    private static bool TryFormatWhole(double number, out string text)
    {
        // NaN fails both comparisons.
        if (number >= int.MinValue && number <= int.MaxValue && number == Math.Truncate(number))
        {
            text = KeyvalueWire.Format((int)number);
            return true;
        }

        text = "";
        return false;
    }

    // An entity reads a float, so that is what is written. A number too big
    // for one becomes infinity here and is refused.
    private static bool TryFormatSingle(double number, out string text)
    {
        float single = (float)number;
        if (float.IsFinite(single))
        {
            text = KeyvalueWire.Format(single);
            return true;
        }

        text = "";
        return false;
    }

    private static bool TryFormatVector(in ParameterValue value, int size, out string text)
    {
        text = "";
        Vector4 v = value.Vector;
        if (value.Kind != ParameterValueKind.Vector || value.VectorSize != size || !IsFinite(v))
            return false;

        text = size switch
        {
            2 => KeyvalueWire.Format(new Vector2(v.X, v.Y)),
            3 => KeyvalueWire.Format(new Vector3(v.X, v.Y, v.Z)),
            _ => KeyvalueWire.Format(v),
        };
        return true;
    }

    // No node never gets here: it is no value, which TryFormat writes as "".
    private static bool TryFormatNode(in ParameterValue value, out string text)
    {
        bool isNode = value.Kind == ParameterValueKind.NodeId;
        text = isNode ? KeyvalueWire.Format(value.NodeId) : "";
        return isNode;
    }

    private static bool TryParseAs(KeyvalueType type, string text, out ParameterValue value)
    {
        value = ParameterValue.None;

        switch (type)
        {
            case KeyvalueType.Bool when KeyvalueWire.TryParseBool(text, out bool flag):
                value = ParameterValue.Of(flag);
                return true;

            case KeyvalueType.Int when KeyvalueWire.TryParseInt(text, out int whole):
                value = ParameterValue.Of(whole);
                return true;

            case KeyvalueType.Float when KeyvalueWire.TryParseFloat(text, out float single):
                value = ParameterValue.Of(AsWritten(text, single));
                return true;

            case KeyvalueType.String:
            case KeyvalueType.TargetName:
                value = ParameterValue.Of(text);
                return true;

            case KeyvalueType.Vec2 when KeyvalueWire.TryParseVec2(text, out Vector2 two):
                value = ParameterValue.Of(two);
                return true;

            case KeyvalueType.Vec3 or KeyvalueType.Color or KeyvalueType.Angles
                when KeyvalueWire.TryParseVec3(text, out Vector3 three):
                value = ParameterValue.Of(three);
                return true;

            case KeyvalueType.Vec4 when KeyvalueWire.TryParseVec4(text, out Vector4 four):
                value = ParameterValue.Of(four);
                return true;

            // Empty is no reference, which a keyvalue of this type allows too.
            case KeyvalueType.NodeRef when text.Length == 0:
                return true;

            case KeyvalueType.NodeRef when KeyvalueWire.TryParseNodeRef(text, out Guid nodeId):
                value = ParameterValue.Of(nodeId);
                return true;

            default:
                return false;
        }
    }

    // The number the text spells, not the float widened: "0.1" is 0.1 to
    // whoever asked, and the widened float is 0.10000000149011612.
    private static double AsWritten(string text, float single) =>
        double.TryParse(text.AsSpan().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? number
            : single;

    private static bool IsFinite(Vector4 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);
}
