using System;

namespace SpectraEngine.Core.Entities;

/// <summary>
/// A <c>.sentdef</c> image could not be read. The message says what was found
/// and what was expected.
/// </summary>
public sealed class SentDefFormatException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SentDefFormatException(string message, long byteOffset, Exception? inner = null)
        : base($"{message} (at byte {byteOffset})", inner) => ByteOffset = byteOffset;

    /// <summary>Byte offset into the image where the offending bytes start.</summary>
    public long ByteOffset { get; }
}
