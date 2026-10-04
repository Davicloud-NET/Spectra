using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;

namespace Spectra.Kitchen.Tests;

// Damages a written pack in one named way. Offsets come from HandParsedPack
// (the spec), not from PackEntry, so a reordered struct cannot move the damage.
// Most edits re-stamp the digest: otherwise the digest check catches every
// corruption first and nothing past the mount runs.
internal static class PackSurgery
{
    // Flips one payload byte, digest left alone.
    public static void CorruptFirstPayload(string packPath)
    {
        byte[] bytes = File.ReadAllBytes(packPath);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(bytes);
        List<HandParsedPack.Entry> entries = HandParsedPack.ReadEntries(bytes, header);

        int at = (int)entries[0].PayloadOffset;
        bytes[at] ^= 0xFF;

        File.WriteAllBytes(packPath, bytes);
    }

    public static void CorruptDigest(string packPath)
    {
        byte[] bytes = File.ReadAllBytes(packPath);
        BinaryPrimitives.WriteUInt128LittleEndian(
            bytes.AsSpan(bytes.Length - HandParsedPack.DigestSize), UInt128.MaxValue);

        File.WriteAllBytes(packPath, bytes);
    }

    // 0x07 is BFINAL = 1, BTYPE = 11, which RFC 1951 reserves and every decoder
    // refuses. A random flip would only usually break the stream.
    public static void MakeFirstPayloadUndecodable(string packPath)
    {
        byte[] bytes = File.ReadAllBytes(packPath);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(bytes);
        List<HandParsedPack.Entry> entries = HandParsedPack.ReadEntries(bytes, header);

        bytes[(int)entries[0].PayloadOffset] = 0x07;
        RestampDigest(bytes, header);

        File.WriteAllBytes(packPath, bytes);
    }

    // Leaves the table intact but unsorted, digest re-stamped.
    public static void SwapFirstTwoEntries(string packPath)
    {
        byte[] bytes = File.ReadAllBytes(packPath);
        HandParsedPack.Header header = HandParsedPack.ReadHeader(bytes);

        int first = (int)header.EntryTableOffset;
        int second = first + HandParsedPack.EntrySize;

        byte[] held = bytes[first..second];
        Array.Copy(bytes, second, bytes, first, HandParsedPack.EntrySize);
        Array.Copy(held, 0, bytes, second, HandParsedPack.EntrySize);

        RestampDigest(bytes, header);
        File.WriteAllBytes(packPath, bytes);
    }

    private static void RestampDigest(byte[] bytes, HandParsedPack.Header header)
    {
        ReadOnlySpan<byte> region = HandParsedPack.DigestedRegion(bytes, header);

        // XxHash128's canonical form is big-endian; the file stores it little-endian.
        Span<byte> canonical = stackalloc byte[HandParsedPack.DigestSize];
        XxHash128.Hash(region, canonical);

        BinaryPrimitives.WriteUInt128LittleEndian(
            bytes.AsSpan(bytes.Length - HandParsedPack.DigestSize),
            BinaryPrimitives.ReadUInt128BigEndian(canonical));
    }
}
