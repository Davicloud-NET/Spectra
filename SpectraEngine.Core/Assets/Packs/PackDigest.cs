using System;
using System.Buffers.Binary;
using System.IO.Hashing;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// The trailing 16-byte content digest: <c>XxHash128</c> over everything from
/// <see cref="PackHeader.EntryTableOffset"/> to end of file, with the digest bytes
/// themselves excluded. Detects corruption; it is not tamper resistance.
/// </summary>
// The header is not covered. Truncation is caught by PackHeader.TotalFileSize.
public static class PackDigest
{
    /// <summary>The digest of one contiguous region.</summary>
    public static UInt128 Compute(ReadOnlySpan<byte> region) => XxHash128.HashToUInt128(region);

    /// <summary>
    /// Writes <paramref name="digest"/> into the file's trailing
    /// <see cref="PackFormat.DigestSize"/> bytes.
    /// </summary>
    // Little-endian so the tail can be read in place as a UInt128.
    // XxHash's canonical byte form is big-endian.
    public static void Write(Span<byte> destination, UInt128 digest) =>
        BinaryPrimitives.WriteUInt128LittleEndian(destination, digest);

    /// <summary>Reads a digest previously written by <see cref="Write"/>.</summary>
    public static UInt128 Read(ReadOnlySpan<byte> source) =>
        BinaryPrimitives.ReadUInt128LittleEndian(source);

    /// <summary>
    /// Accumulates the digest of a region delivered in pieces.
    /// </summary>
    public sealed class Accumulator
    {
        private readonly XxHash128 _hash = new();

        /// <summary>Appends the next piece of the region, in order.</summary>
        public void Append(ReadOnlySpan<byte> bytes) => _hash.Append(bytes);

        /// <summary>
        /// The digest of everything appended so far. Does not reset, so it may be
        /// read and then appended to.
        /// </summary>
        public UInt128 Finish()
        {
            Span<byte> canonical = stackalloc byte[PackFormat.DigestSize];
            _hash.GetCurrentHash(canonical);

            // Big-endian read matches what HashToUInt128 returns in Compute.
            return BinaryPrimitives.ReadUInt128BigEndian(canonical);
        }
    }
}
