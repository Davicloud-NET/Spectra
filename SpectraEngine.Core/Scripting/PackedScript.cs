using System;
using System.Buffers.Binary;
using System.IO;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// A script as a cook packs it: a header of sixteen bytes, then the payload.
/// The header says what the payload is. Today that is always the script's
/// text, and the game compiles it when the level starts.
/// </summary>
// Little-endian: the magic, a u16 version, the payload kind as one byte, a
// zero byte, the payload's length as a u32, four zero bytes, the payload.
// docs/formats-and-pipeline.md has the table.
public static class PackedScript
{
    /// <summary>The extension of a script file, loose or packed, with its dot.</summary>
    public const string FileExtension = ".luau";

    /// <summary>Bytes in the header.</summary>
    public const int HeaderSize = 16;

    /// <summary>The version this build reads and its cook writes. A reader refuses any other.</summary>
    public const ushort Version = 1;

    /// <summary>Where the header holds the version, as two bytes.</summary>
    public const int VersionOffset = 4;

    /// <summary>Where the header holds the payload kind, as one byte.</summary>
    public const int PayloadKindOffset = 6;

    /// <summary>Where the header holds the payload's length, as four bytes.</summary>
    public const int PayloadLengthOffset = 8;

    /// <summary>
    /// The first four bytes of a packed script. The first is the escape
    /// character, which no script's text starts with.
    /// </summary>
    public static ReadOnlySpan<byte> Magic => [0x1B, (byte)'S', (byte)'L', (byte)'U'];

    /// <summary>Whether a file's bytes are a packed script and not a script's text.</summary>
    public static bool IsPacked(ReadOnlySpan<byte> file) => file.StartsWith(Magic);

    /// <summary>The script's text inside a packed script, as a view of <paramref name="file"/>.</summary>
    /// <param name="file">The whole entry, header included.</param>
    /// <param name="path">The script's content path, for the message.</param>
    /// <exception cref="InvalidDataException">
    /// The entry is cut short, of another version, or holds something other
    /// than text.
    /// </exception>
    public static ReadOnlySpan<byte> SourceOf(ReadOnlySpan<byte> file, string path)
    {
        if (!IsPacked(file) || file.Length < HeaderSize)
        {
            throw new InvalidDataException(
                $"'{path}' is not a packed script: it has no header of {HeaderSize} bytes that starts as one.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(file[VersionOffset..]);
        if (version != Version)
        {
            throw new InvalidDataException(
                $"'{path}' is a packed script of version {version}, and this build reads version {Version}. " +
                "Cook the project again.");
        }

        byte payload = file[PayloadKindOffset];
        if (payload != (byte)PackedScriptPayload.Source)
        {
            throw new InvalidDataException(
                $"'{path}' is a packed script with payload kind {payload}, and this build reads only a " +
                "script's text. Cook the project again.");
        }

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(file[PayloadLengthOffset..]);
        if (length != (uint)(file.Length - HeaderSize))
        {
            throw new InvalidDataException(
                $"'{path}' is a packed script that says it holds {length} bytes of text and holds " +
                $"{file.Length - HeaderSize}.");
        }

        return file[HeaderSize..];
    }
}
