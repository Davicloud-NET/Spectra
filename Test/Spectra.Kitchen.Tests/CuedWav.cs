using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Spectra.Kitchen.Tests;

// Adds cue points and their labels to a WAV from TempProject.Wav. Written
// from the RIFF spec, not through engine code.
internal static class CuedWav
{
    // A cue point, and the label naming it when it has one. Frame is written
    // as the sample offset, and as the position too unless one is given.
    public readonly record struct Cue(uint Id, uint Frame, string? Label = null, uint? Position = null);

    // The WAV with a cue chunk and a LIST chunk of type adtl after its data.
    public static byte[] Add(byte[] wav, params Cue[] cues) =>
        AppendChunk(AppendChunk(wav, "cue ", CueBody(cues)), "LIST", LabelBody(cues));

    // The same two chunks the other way round. A reader must not care.
    public static byte[] AddLabelsFirst(byte[] wav, params Cue[] cues) =>
        AppendChunk(AppendChunk(wav, "LIST", LabelBody(cues)), "cue ", CueBody(cues));

    public static byte[] AppendChunk(byte[] wav, string id, byte[] body)
    {
        using var file = new MemoryStream();
        file.Write(wav);
        file.Write(Encoding.ASCII.GetBytes(id));

        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)body.Length);
        file.Write(size);
        file.Write(body);

        // An odd body gets a pad byte that is not counted in its size.
        if ((body.Length & 1) != 0) file.WriteByte(0);

        byte[] bytes = file.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        return bytes;
    }

    // A count, then 24 bytes a point: id, position, "data", chunk start, block
    // start, sample offset.
    public static byte[] CueBody(params Cue[] cues)
    {
        var body = new byte[4 + cues.Length * 24];
        BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)cues.Length);

        for (int i = 0; i < cues.Length; i++)
        {
            Span<byte> point = body.AsSpan(4 + i * 24, 24);
            BinaryPrimitives.WriteUInt32LittleEndian(point, cues[i].Id);
            BinaryPrimitives.WriteUInt32LittleEndian(point[4..], cues[i].Position ?? cues[i].Frame);
            Encoding.ASCII.GetBytes("data").CopyTo(point[8..]);
            BinaryPrimitives.WriteUInt32LittleEndian(point[20..], cues[i].Frame);
        }

        return body;
    }

    // "adtl", then a labl sub-chunk for every cue that has a label: its id and
    // the text, ended by a zero.
    public static byte[] LabelBody(params Cue[] cues) => LabelBody(Encoding.UTF8, cues);

    public static byte[] LabelBody(Encoding encoding, params Cue[] cues)
    {
        using var body = new MemoryStream();
        body.Write(Encoding.ASCII.GetBytes("adtl"));

        Span<byte> word = stackalloc byte[4];
        foreach (Cue cue in cues)
        {
            if (cue.Label is null) continue;

            byte[] text = encoding.GetBytes(cue.Label);
            int size = 4 + text.Length + 1;

            body.Write(Encoding.ASCII.GetBytes("labl"));
            BinaryPrimitives.WriteUInt32LittleEndian(word, (uint)size);
            body.Write(word);
            BinaryPrimitives.WriteUInt32LittleEndian(word, cue.Id);
            body.Write(word);
            body.Write(text);
            body.WriteByte(0);

            if ((size & 1) != 0) body.WriteByte(0);
        }

        return body.ToArray();
    }
}
