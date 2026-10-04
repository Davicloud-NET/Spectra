using System;

namespace SpectraEngine.Core.Maps;

/// <summary>
/// A map document could not be read. Names the node and the byte offset of the
/// token that caused it.
/// </summary>
public sealed class MapFormatException : Exception
{
    public MapFormatException(string message, string? nodeName, long byteOffset, Exception? inner = null)
        : base(Describe(message, nodeName, byteOffset), inner)
    {
        NodeName = nodeName;
        ByteOffset = byteOffset;
    }

    /// <summary>Name of the node being read when this failed, or null outside a node.</summary>
    public string? NodeName { get; }

    /// <summary>Byte offset into the document where the offending token starts.</summary>
    public long ByteOffset { get; }

    private static string Describe(string message, string? nodeName, long byteOffset) =>
        nodeName is null
            ? $"{message} (at byte {byteOffset})"
            : $"{message} (node '{nodeName}', at byte {byteOffset})";
}
