namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// How one pack entry's payload is stored, as one byte in
/// <see cref="PackEntry.Codec"/>.
/// </summary>
// Compression is per entry. Solid compression would break random access and in-place mapping.
public enum PackCodec : byte
{
    /// <summary>
    /// Stored verbatim. The only codec whose payload can be read in place off a
    /// mapped view.
    /// </summary>
    None = 0,

    /// <summary>RFC 1951 deflate, via the in-box <c>DeflateStream</c>.</summary>
    Deflate = 1,

    /// <summary>
    /// Reserved, not implemented. Waits for the in-box Zstandard in .NET 11.
    /// </summary>
    Zstandard = 2,
}
