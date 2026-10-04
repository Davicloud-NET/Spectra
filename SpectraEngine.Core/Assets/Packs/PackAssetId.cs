using System;
using System.IO.Hashing;
using System.Text;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// Turns a content path into the 128-bit key the entry table is sorted and
/// searched by.
/// </summary>
// The id hashes the normalized content-relative source path, case-folded. It is the same
// string the asset caches key on, so a loose file and a pack entry share one identity.
// It hashes the path, not the content: an id survives a recook.
public static class PackAssetId
{
    // Longer paths go to the heap.
    private const int StackLimit = 512;

    /// <summary>
    /// The id of <paramref name="contentPath"/>, normalising it first.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path is empty, rooted, or escapes the content root.
    /// </exception>
    public static UInt128 From(string contentPath) =>
        FromNormalized(ContentRoot.NormalizeRelativePath(contentPath));

    /// <summary>
    /// The id of a path that has already been through
    /// <see cref="ContentRoot.NormalizeRelativePath"/>. Skips the per-lookup
    /// string allocation.
    /// </summary>
    public static UInt128 FromNormalized(string normalizedPath)
    {
        ArgumentNullException.ThrowIfNull(normalizedPath);

        // UTF-8 bytes only: the id must not depend on culture or casing tables,
        // or a pack cooked on one machine is unreadable on another.
        int byteCount = Encoding.UTF8.GetByteCount(normalizedPath);
        if (byteCount <= StackLimit)
        {
            Span<byte> utf8 = stackalloc byte[StackLimit];
            int written = Encoding.UTF8.GetBytes(normalizedPath, utf8);
            Span<byte> key = utf8[..written];
            FoldAsciiCase(key);
            return XxHash128.HashToUInt128(key);
        }

        byte[] heap = Encoding.UTF8.GetBytes(normalizedPath);
        FoldAsciiCase(heap);
        return XxHash128.HashToUInt128(heap);
    }

    // The caches compare OrdinalIgnoreCase, so the id folds case too. ASCII only:
    // ToUpperInvariant depends on the host's globalization mode.
    // Changing the fold renumbers every id in every cooked pack.
    private static void FoldAsciiCase(Span<byte> utf8)
    {
        for (int i = 0; i < utf8.Length; i++)
        {
            byte b = utf8[i];
            if (b is >= (byte)'a' and <= (byte)'z')
                utf8[i] = (byte)(b - ('a' - 'A'));
        }
    }
}
