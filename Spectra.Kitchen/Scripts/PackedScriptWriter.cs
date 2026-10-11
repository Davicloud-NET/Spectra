using SpectraEngine.Core.Scripting;
using System;
using System.Buffers.Binary;

namespace Spectra.Kitchen.Scripts;

/// <summary>Writes a script as a pack holds it. <see cref="PackedScript"/> reads it back.</summary>
public static class PackedScriptWriter
{
    /// <summary>A packed script that holds a script's text as it is, byte for byte.</summary>
    /// <param name="source">The script's text as UTF-8, as its file holds it.</param>
    public static byte[] WriteSource(ReadOnlySpan<byte> source)
    {
        var entry = new byte[PackedScript.HeaderSize + source.Length];
        Span<byte> header = entry;

        PackedScript.Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[PackedScript.VersionOffset..], PackedScript.Version);
        header[PackedScript.PayloadKindOffset] = (byte)PackedScriptPayload.Source;
        BinaryPrimitives.WriteUInt32LittleEndian(header[PackedScript.PayloadLengthOffset..], (uint)source.Length);

        source.CopyTo(header[PackedScript.HeaderSize..]);
        return entry;
    }
}
