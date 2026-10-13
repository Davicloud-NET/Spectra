using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Serialization;
using System.Collections.Generic;
using System.Text.Json;

namespace SpectraEngine.Core.Maps;

// A node's 'tags' member: an array of strings on one line, in the order the
// node got them. A closed record: a tag that breaks the rule, or is there
// twice, is refused, since a set cannot hold it and the next save would
// drop it.
internal static class MapTags
{
    public static void Read(ref Utf8JsonReader reader, List<string> into, string? nodeName)
    {
        // A second member would be read into the first, and saved as one.
        if (into.Count > 0)
            throw Fail(ref reader, $"'{MapFormat.TagsMember}' is there twice", nodeName);

        reader.Read();
        if (reader.TokenType != JsonTokenType.StartArray)
            throw Fail(ref reader, $"'{MapFormat.TagsMember}' must be an array of strings", nodeName);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw Fail(ref reader, "a tag must be a string", nodeName);

            into.Add(reader.GetString() ?? string.Empty);
        }

        if (!TagSet.TryMake(into, out _, out string? refusal))
            throw Fail(ref reader, refusal, nodeName);
    }

    public static byte[] Compact(List<string> tags) => CanonicalJson.Compact(writer =>
    {
        writer.WriteStartArray();
        foreach (string tag in tags)
            writer.WriteStringValue(tag);
        writer.WriteEndArray();
    });

    private static MapFormatException Fail(ref Utf8JsonReader reader, string message, string? nodeName) =>
        new(message, nodeName, reader.TokenStartIndex);
}
