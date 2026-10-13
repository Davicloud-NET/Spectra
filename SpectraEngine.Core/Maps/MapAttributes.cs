using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Serialization;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace SpectraEngine.Core.Maps;

// A node's 'attributes' member: one object on one line, a value under each
// name, in the order they were added. A value JSON has no kind for is an
// object with one member that names it: {"vector3":[x,y,z]},
// {"color3":[r,g,b]}, {"cframe":[x,y,z,qx,qy,qz,qw]}. A closed record.
internal static class MapAttributes
{
    private const int FrameNumbers = 7;

    public static void Read(
        ref Utf8JsonReader reader, List<KeyValuePair<string, MemberValue>> into, string? nodeName)
    {
        // A second member would be read into the first, and saved as one.
        if (into.Count > 0)
            throw Fail(ref reader, $"'{MapFormat.AttributesMember}' is there twice", nodeName);

        reader.Read();
        if (reader.TokenType != JsonTokenType.StartObject)
            throw Fail(ref reader, $"'{MapFormat.AttributesMember}' must be an object", nodeName);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString() ?? string.Empty;
            reader.Read();
            into.Add(new KeyValuePair<string, MemberValue>(name, ReadValue(ref reader, name, nodeName)));
        }

        if (!AttributeSet.TryMake(into, out _, out string? refusal))
            throw Fail(ref reader, refusal, nodeName);
    }

    public static byte[] Compact(List<KeyValuePair<string, MemberValue>> attributes) =>
        CanonicalJson.Compact(writer =>
        {
            writer.WriteStartObject();
            foreach (KeyValuePair<string, MemberValue> pair in attributes)
            {
                writer.WritePropertyName(pair.Key);
                WriteValue(writer, pair.Key, pair.Value);
            }

            writer.WriteEndObject();
        });

    private static MemberValue ReadValue(ref Utf8JsonReader reader, string name, string? nodeName)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return MemberValue.Of(reader.GetString() ?? string.Empty);
            case JsonTokenType.True or JsonTokenType.False:
                return MemberValue.Of(reader.GetBoolean());
            case JsonTokenType.Number:
                return MemberValue.Of(reader.GetDouble());
            case JsonTokenType.StartObject:
                return ReadShaped(ref reader, name, nodeName);
            default:
                throw Fail(ref reader, $"attribute '{name}' must be {Shapes}", nodeName);
        }
    }

    private const string Shapes =
        "a string, true or false, a number, or an object with one member: " +
        $"'{MapFormat.Vector3Value}', '{MapFormat.Color3Value}' or '{MapFormat.CFrameValue}'";

    // The reader is on the object's start and is left on its end.
    private static MemberValue ReadShaped(ref Utf8JsonReader reader, string name, string? nodeName)
    {
        reader.Read();
        if (reader.TokenType != JsonTokenType.PropertyName)
            throw Fail(ref reader, $"attribute '{name}' must be {Shapes}", nodeName);

        string kind = reader.GetString() ?? string.Empty;
        MemberValue value;
        switch (kind)
        {
            case MapFormat.Vector3Value:
                value = MemberValue.Of(ReadThree(ref reader, name, kind, nodeName));
                break;

            case MapFormat.Color3Value:
                Vector3 parts = ReadThree(ref reader, name, kind, nodeName);
                value = MemberValue.Of(new Color3(parts.X, parts.Y, parts.Z));
                break;

            case MapFormat.CFrameValue:
                float[] f = ReadNumbers(ref reader, FrameNumbers, name, kind, nodeName);
                var turn = new Quaternion(f[3], f[4], f[5], f[6]);

                // A frame takes such a rotation for none, and a save would
                // then write one the file never had. One that is only off
                // unit length is the same rotation and is kept.
                if (!UnitLength.TryOf(turn, out _))
                {
                    throw Fail(
                        ref reader, $"attribute '{name}': the rotation in '{kind}' must be finite and have a length",
                        nodeName);
                }

                value = MemberValue.Of(new CFrame(new Vector3(f[0], f[1], f[2]), turn));
                break;

            // No fall-through to text: a kind this engine lacks would be
            // saved as something it was not.
            default:
                throw Fail(ref reader, $"attribute '{name}' must be {Shapes}, not an object with '{kind}'", nodeName);
        }

        reader.Read();
        if (reader.TokenType != JsonTokenType.EndObject)
            throw Fail(ref reader, $"attribute '{name}' must have '{kind}' and nothing else", nodeName);

        return value;
    }

    private static Vector3 ReadThree(ref Utf8JsonReader reader, string name, string kind, string? nodeName)
    {
        float[] f = ReadNumbers(ref reader, 3, name, kind, nodeName);
        return new Vector3(f[0], f[1], f[2]);
    }

    private static float[] ReadNumbers(
        ref Utf8JsonReader reader, int count, string name, string kind, string? nodeName)
    {
        string what = $"attribute '{name}': '{kind}' must be an array of {count} numbers";

        reader.Read();
        if (reader.TokenType != JsonTokenType.StartArray)
            throw Fail(ref reader, what, nodeName);

        var numbers = new float[count];
        for (int i = 0; i < count; i++)
        {
            reader.Read();
            if (reader.TokenType != JsonTokenType.Number)
                throw Fail(ref reader, what, nodeName);

            numbers[i] = reader.GetSingle();
        }

        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray)
            throw Fail(ref reader, what, nodeName);

        return numbers;
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, in MemberValue value)
    {
        // JSON has no number that is not finite. Refused here by name.
        if (AttributeValueRule.Refusal(in value) is { } whyNot)
            throw new MapFormatException($"Cannot write attribute '{name}': {whyNot}", null, 0);

        switch (value.Kind)
        {
            case MemberValueKind.Text:
                writer.WriteStringValue(value.Text);
                break;
            case MemberValueKind.Boolean:
                writer.WriteBooleanValue(value.Boolean);
                break;
            case MemberValueKind.Number:
                writer.WriteNumberValue(value.Number);
                break;
            case MemberValueKind.Vector3:
                WriteShaped(writer, MapFormat.Vector3Value, value.Vector.X, value.Vector.Y, value.Vector.Z);
                break;
            case MemberValueKind.Color:
                WriteShaped(writer, MapFormat.Color3Value, value.Color.R, value.Color.G, value.Color.B);
                break;
            case MemberValueKind.CFrame:
                Vector3 p = value.Frame.Position;
                Quaternion r = value.Frame.Rotation;
                WriteShaped(writer, MapFormat.CFrameValue, p.X, p.Y, p.Z, r.X, r.Y, r.Z, r.W);
                break;
        }
    }

    private static void WriteShaped(Utf8JsonWriter writer, string kind, params ReadOnlySpan<float> numbers)
    {
        writer.WriteStartObject();
        writer.WriteStartArray(kind);
        foreach (float number in numbers)
            writer.WriteNumberValue(number);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static MapFormatException Fail(ref Utf8JsonReader reader, string message, string? nodeName) =>
        new(message, nodeName, reader.TokenStartIndex);
}
