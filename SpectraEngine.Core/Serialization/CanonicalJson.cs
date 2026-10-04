using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SpectraEngine.Core.Serialization;

/// <summary>
/// JSON settings and helpers shared by every authored text document the engine
/// writes (maps, projects). Output is byte-stable across platforms.
/// </summary>
public static class CanonicalJson
{
    /// <summary>Spaces per indent level.</summary>
    public const int IndentSize = 2;

    // NewLine defaults to Environment.NewLine (CRLF on Windows). The default
    // encoder escapes + < > & and all non-ASCII to \uXXXX.
    public static JsonWriterOptions WriterOptions => new()
    {
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = IndentSize,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = false,
    };

    /// <summary>
    /// Reader settings. Comments and trailing commas are refused, since the
    /// next save would drop them.
    /// </summary>
    public static JsonReaderOptions ReaderOptions => new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>Renders a document to canonical UTF-8 bytes, with no BOM and a trailing newline.</summary>
    public static byte[] Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            write(writer);

        buffer.Write("\n"u8);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Renders one record on a single line.</summary>
    // Through a writer, not string concatenation, so escaping and number
    // formatting match the indented path.
    public static byte[] Compact(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>(128);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            write(writer);
        }
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Writes an array of already-compacted records, one per line.</summary>
    // One record per line keeps an edit a one-line diff. WriteRawValue does not
    // indent raw content, so the layout is built by hand.
    public static void WriteRecordArray(Utf8JsonWriter writer, string member, List<byte[]> records)
    {
        writer.WritePropertyName(member);

        if (records.Count == 0)
        {
            writer.WriteRawValue("[]"u8, skipInputValidation: true);
            return;
        }

        int depth = writer.CurrentDepth;
        string outer = new(' ', depth * IndentSize);
        string inner = new(' ', (depth + 1) * IndentSize);

        var text = new StringBuilder("[\n");
        for (int i = 0; i < records.Count; i++)
        {
            text.Append(inner).Append(Encoding.UTF8.GetString(records[i]));
            text.Append(i == records.Count - 1 ? "\n" : ",\n");
        }
        text.Append(outer).Append(']');

        writer.WriteRawValue(text.ToString());
    }

    /// <summary>
    /// Emits every preserved member anchored to <paramref name="anchor"/>, the
    /// index of the canonical member just written (-1 before the first).
    /// </summary>
    public static void Flush(Utf8JsonWriter writer, List<PreservedMember> unknown, int anchor)
    {
        for (int i = 0; i < unknown.Count; i++)
        {
            if (unknown[i].Anchor != anchor) continue;
            writer.WritePropertyName(unknown[i].Name);
            writer.WriteRawValue(unknown[i].Raw);
        }
    }

    /// <summary>
    /// Captures the current member's raw value bytes. The reader must be on the
    /// property name, and <paramref name="utf8"/> must be the whole document as
    /// one contiguous span.
    /// </summary>
    // TokenStartIndex and BytesConsumed are relative to the reader's own input,
    // so a chunked read would slice the wrong bytes.
    public static byte[] CaptureValue(ref Utf8JsonReader reader, ReadOnlySpan<byte> utf8)
    {
        reader.Read();
        long start = reader.TokenStartIndex;
        reader.Skip();
        return utf8[(int)start..(int)reader.BytesConsumed].ToArray();
    }

    /// <summary>Strips a UTF-8 byte order mark, if one is present.</summary>
    public static ReadOnlySpan<byte> StripBom(ReadOnlySpan<byte> utf8) =>
        utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF ? utf8[3..] : utf8;
}
