using System;
using System.Buffers;
using System.IO;
using System.IO.Hashing;
using System.Threading;

namespace Spectra.Kitchen.Packs;

/// <summary>A payload's immutable identity and either owned bytes or a verified file reference.</summary>
public sealed class PackPayload
{
    private readonly byte[]? _bytes;
    public string? FilePath { get; }
    public long Length { get; }
    public UInt128 Hash { get; }
    private PackPayload(byte[]? bytes, string? path, long length, UInt128 hash)
    { _bytes = bytes; FilePath = path; Length = length; Hash = hash; }
    public static PackPayload FromBytes(byte[] bytes) => new(bytes, null, bytes.LongLength, XxHash128.HashToUInt128(bytes));
    internal static PackPayload KnownFile(string path, long length, UInt128 hash) => new(null, Path.GetFullPath(path), length, hash);
    public static PackPayload FromFile(string path, CancellationToken cancellationToken = default)
    {
        using var stream = OpenFile(path);
        var hash = new XxHash128();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            int count;
            while ((count = stream.Read(buffer)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.Append(buffer.AsSpan(0, count));
            }
            return KnownFile(path, stream.Length, hash.GetCurrentHashAsUInt128());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>Streams and verifies while copying. A replaced or damaged source cannot silently change a repeated write.</summary>
    public void CopyTo(Stream destination, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_bytes is not null)
        {
            var memoryHash = new XxHash128();
            for (int start = 0; start < _bytes.Length; start += Math.Min(128 * 1024, _bytes.Length - start))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var block = _bytes.AsSpan(start, Math.Min(128 * 1024, _bytes.Length - start));
                memoryHash.Append(block);
                destination.Write(block);
            }
            if (memoryHash.GetCurrentHashAsUInt128() != Hash)
                throw new IOException("An owned cooked payload was mutated after publication.");
            return;
        }
        using var source = OpenFile(FilePath!);
        if (source.Length != Length) throw new IOException($"Cooked payload '{FilePath}' changed length.");
        byte[] buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        var hash = new XxHash128();
        try
        {
            long copied = 0;
            int count;
            while ((count = source.Read(buffer)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.Append(buffer.AsSpan(0, count));
                destination.Write(buffer.AsSpan(0, count)); copied += count;
            }
            if (copied != Length || hash.GetCurrentHashAsUInt128() != Hash)
                throw new IOException($"Cooked payload '{FilePath}' changed or failed verification.");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>Compatibility materialization; pack assembly and cache replay use streaming instead.</summary>
    public byte[] ReadAllBytes()
    {
        if (_bytes is not null) return _bytes;
        if (Length > Array.MaxLength) throw new IOException("This payload requires streaming; it cannot fit in a byte array.");
        byte[] bytes = new byte[(int)Length];
        using var stream = new MemoryStream(bytes, writable: true);
        CopyTo(stream); return bytes;
    }

    internal static FileStream OpenFile(string path) => new(path, FileMode.Open, FileAccess.Read,
        FileShare.Read | FileShare.Delete, 128 * 1024, FileOptions.SequentialScan);
}
