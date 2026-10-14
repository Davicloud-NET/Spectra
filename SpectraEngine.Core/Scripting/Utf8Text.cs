using System;
using System.Buffers;
using System.Text.Unicode;

namespace SpectraEngine.Core.Scripting;

// Text a value holds as a string or as UTF-8, compared and hashed by what it
// says, so the two forms of one text are equal.
internal static class Utf8Text
{
    private const int Chunk = 256;

    private const uint FnvStart = 2166136261;
    private const uint FnvPrime = 16777619;

    // The same number for both forms of one text: FNV-1a over its UTF-8.
    public static int HashOf(ReadOnlySpan<byte> utf8) => (int)Add(FnvStart, utf8);

    public static int HashOf(string text)
    {
        uint hash = FnvStart;
        Span<byte> room = stackalloc byte[Chunk * 3];
        ReadOnlySpan<char> rest = text;
        while (!rest.IsEmpty)
        {
            Utf8.FromUtf16(rest[..Math.Min(Chunk, rest.Length)], room, out int read, out int written, isFinalBlock: rest.Length <= Chunk);
            hash = Add(hash, room[..written]);
            rest = rest[Math.Max(read, 1)..];
        }

        return (int)hash;
    }

    // Whether the bytes are the UTF-8 of the string. Half of a surrogate pair
    // in the string is the UTF-8 of nothing.
    public static bool Says(ReadOnlySpan<byte> utf8, string text)
    {
        Span<byte> room = stackalloc byte[Chunk * 3];
        ReadOnlySpan<char> rest = text;
        while (!rest.IsEmpty)
        {
            OperationStatus status = Utf8.FromUtf16(
                rest[..Math.Min(Chunk, rest.Length)], room, out int read, out int written,
                replaceInvalidSequences: false, isFinalBlock: rest.Length <= Chunk);
            if (status == OperationStatus.InvalidData || read == 0 || !utf8.StartsWith(room[..written]))
                return false;

            utf8 = utf8[written..];
            rest = rest[read..];
        }

        return utf8.IsEmpty;
    }

    private static uint Add(uint hash, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
            hash = (hash ^ b) * FnvPrime;

        return hash;
    }
}
