using System;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// The fixed byte geometry of a <c>.spack</c> file, stated once for the writer
/// in <c>Spectra.Kitchen</c> and the reader here.
/// </summary>
// Readers seek to the header's EntryTableOffset and DataSectionOffset, never to a
// computed position, so the header can grow without a version bump.
public static class PackFormat
{
    /// <summary>
    /// File magic, <c>"SPAK"</c>. Stored as a little-endian <see cref="uint"/>,
    /// so the first four bytes on disk read <c>S P A K</c> in a hex dump.
    /// </summary>
    public const uint Magic = 'S' | ('P' << 8) | ('A' << 16) | ((uint)'K' << 24);

    /// <summary>
    /// The extension a pack is written and mounted under, including the dot.
    /// </summary>
    public const string FileExtension = ".spack";

    /// <summary>Bytes in the header, which lives at offset 0.</summary>
    public const int HeaderSize = 64;

    /// <summary>Bytes in one entry-table record, fixed stride.</summary>
    public const int EntrySize = 48;

    /// <summary>Bytes in the trailing content digest.</summary>
    public const int DigestSize = 16;

    /// <summary>
    /// The smallest legal file: a header and the trailing digest with nothing
    /// between them.
    /// </summary>
    public const int MinimumFileSize = HeaderSize + DigestSize;

    /// <summary>
    /// Alignment every payload starts on. Mapped payloads are cast in place to
    /// <c>Vector4</c>, <c>Matrix4x4</c> and similar.
    /// </summary>
    public const int PayloadAlignment = 16;

    /// <summary>
    /// Alignment the data section starts on, so a block-level patcher diffs on
    /// 4K boundaries.
    /// </summary>
    public const int DataSectionAlignment = 4096;

    /// <summary>
    /// What <see cref="PackEntry.NameOffset"/> holds when an entry has no name
    /// table record. Zero is the first record's offset.
    /// </summary>
    public const uint NameOffsetAbsent = 0xFFFFFFFFu;

    /// <summary>
    /// Rounds <paramref name="value"/> up to the next multiple of
    /// <paramref name="alignment"/>, which must be a power of two.
    /// </summary>
    public static long AlignUp(long value, int alignment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(alignment);
        if ((alignment & (alignment - 1)) != 0)
            throw new ArgumentException($"Alignment {alignment} is not a power of two.", nameof(alignment));

        long mask = alignment - 1L;
        return (value + mask) & ~mask;
    }

    /// <summary>
    /// Throws on a big-endian machine. Pack data is cast in place, so it cannot
    /// be byte-swapped without copying.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">The machine is big-endian.</exception>
    public static void RequireLittleEndian()
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException(
                "The .spack container is little-endian only: every header, entry and payload is " +
                "reinterpreted in place, so a big-endian host would have to copy and byte-swap all of it.");
        }
    }

    /// <summary>
    /// Throws for a file too short to hold a header and a digest.
    /// </summary>
    /// <exception cref="PackMountException">The file is shorter than <see cref="MinimumFileSize"/>.</exception>
    public static void RequireMinimumFileSize(string source, long length)
    {
        if (length >= MinimumFileSize) return;

        throw new PackMountException(
            $"'{source}' is {length} bytes, too short to hold a {HeaderSize}-byte header and a " +
            $"{DigestSize}-byte content digest ({MinimumFileSize} bytes minimum).");
    }
}
