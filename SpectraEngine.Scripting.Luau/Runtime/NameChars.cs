using System.Buffers;
using System.Text.Unicode;

namespace SpectraEngine.Scripting.Luau.Runtime;

// A name a script handed over, as the characters a node's name is compared
// with. Luau keeps a string as UTF-8 and a node keeps its name as UTF-16, so
// the name is turned once, into room on the caller's stack, and every node
// is then compared with no string made.
internal static class NameChars
{
    // More than any name a running level can give a node. A longer text
    // gets an array of its own.
    public const int Room = 256;

    // False for bytes that are no text, which no name equals.
    public static bool TryRead(ReadOnlySpan<byte> utf8, Span<char> room, out ReadOnlySpan<char> chars)
    {
        // UTF-16 never takes more characters than UTF-8 takes bytes.
        Span<char> turned = utf8.Length <= room.Length ? room : new char[utf8.Length];
        OperationStatus status = Utf8.ToUtf16(utf8, turned, out _, out int written, replaceInvalidSequences: false);

        chars = status == OperationStatus.Done ? turned[..written] : default;
        return status == OperationStatus.Done;
    }
}
