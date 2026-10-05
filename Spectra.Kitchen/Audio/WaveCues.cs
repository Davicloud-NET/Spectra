using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.Unicode;

namespace Spectra.Kitchen.Audio;

// Collects a WAV's cue points and the labels that name them while the decoder
// walks the chunks. The two chunks can come in either order, so they are
// joined at the end. A chunk that is cut short gives what it holds.
internal sealed class WaveCues
{
    private const int CuePointSize = 24;

    // dwSampleOffset. For plain PCM in one data chunk it is the sample frame.
    private const int CueSampleOffsetAt = 20;

    private readonly List<(uint Id, long Frame)> _points = [];
    private readonly List<(uint Id, string Text)> _labels = [];

    // The body of a 'cue ' chunk: a count, then 24-byte cue points.
    public void ReadCueChunk(ReadOnlySpan<byte> body)
    {
        if (body.Length < 4) return;

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(body);
        for (uint i = 0; i < count; i++)
        {
            long at = 4 + ((long)i * CuePointSize);
            if (at + CuePointSize > body.Length) break;

            ReadOnlySpan<byte> point = body.Slice((int)at, CuePointSize);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(point);
            uint frame = BinaryPrimitives.ReadUInt32LittleEndian(point[CueSampleOffsetAt..]);
            _points.Add((id, frame));
        }
    }

    // The body of a 'LIST' chunk. Only type 'adtl' names cue points, in 'labl'
    // sub-chunks: a cue id, then the text.
    public void ReadListChunk(ReadOnlySpan<byte> body)
    {
        if (!HasTag(body, 0, "adtl")) return;

        int at = 4;
        while (at + 8 <= body.Length)
        {
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 4)..]);
            if (size > (uint)(body.Length - at - 8)) break;

            if (size >= 4 && HasTag(body, at, "labl"))
            {
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(body[(at + 8)..]);
                _labels.Add((id, DecodeText(body.Slice(at + 12, (int)size - 4))));
            }

            // Word-aligned like every RIFF chunk.
            at += 8 + (int)size + ((int)size & 1);
        }
    }

    // One marker per cue point, in the order the cue chunk lists them.
    public SourceMarker[] ToMarkers()
    {
        var markers = new SourceMarker[_points.Count];
        for (int i = 0; i < markers.Length; i++)
            markers[i] = new SourceMarker(_points[i].Frame, LabelOf(_points[i].Id));

        return markers;
    }

    private string LabelOf(uint id)
    {
        foreach ((uint labelled, string text) in _labels)
        {
            if (labelled == id) return text;
        }

        return string.Empty;
    }

    // Text ends at the first NUL. Newer editors write UTF-8 and older ones the
    // system code page, which is read as Latin-1 here.
    private static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        if (end >= 0) bytes = bytes[..end];

        Encoding encoding = Utf8.IsValid(bytes) ? Encoding.UTF8 : Encoding.Latin1;
        return encoding.GetString(bytes).Trim();
    }

    private static bool HasTag(ReadOnlySpan<byte> bytes, int at, string tag) =>
        bytes.Length >= at + 4 &&
        bytes[at] == tag[0] && bytes[at + 1] == tag[1] &&
        bytes[at + 2] == tag[2] && bytes[at + 3] == tag[3];
}
