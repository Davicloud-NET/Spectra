using System;

namespace SpectraEngine.Core.Projects;

/// <summary>
/// A project manifest could not be read. Carries the byte offset that caused it.
/// </summary>
public sealed class ProjectFormatException : Exception
{
    public ProjectFormatException(string message, long byteOffset, Exception? inner = null)
        : base($"{message} (at byte {byteOffset})", inner) => ByteOffset = byteOffset;

    /// <summary>Byte offset into the document where the offending token starts.</summary>
    public long ByteOffset { get; }
}
