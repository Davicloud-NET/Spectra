using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;

namespace Spectra.Kitchen.Cache;

/// <summary>
/// Modification time plus size per input, used only to skip re-hashing a file
/// that has not changed.
/// </summary>
// Content hashes decide invalidation. This only saves computing them, so it
// can be thrown away at any time.
// Known hole: same length, same reported mtime, different bytes is served
// with the old hash. --no-cache gets around it.
// The lock covers the hash too. Hashing outside it would let two workers hash
// one file and make the counters depend on scheduling.
public sealed class StatCache
{
    private const uint Magic = 0x54415343; // "CSAT" little-endian
    private const uint FormatVersion = 1;

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private int _shortCircuits;
    private int _rehashes;
    private bool _dirty;

    /// <summary>Files whose hash was answered without reading them.</summary>
    public int ShortCircuits { get { lock (_gate) return _shortCircuits; } }

    /// <summary>Files that had to be read and hashed.</summary>
    public int Rehashes { get { lock (_gate) return _rehashes; } }

    /// <summary>Whether anything changed since this was loaded.</summary>
    public bool IsDirty { get { lock (_gate) return _dirty; } }

    /// <summary>Entries held.</summary>
    public int Count { get { lock (_gate) return _entries.Count; } }

    /// <summary>
    /// The content hash of the file at <paramref name="fullPath"/>, or false when
    /// there is nothing there.
    /// </summary>
    /// <param name="contentPath">Normalised content-relative path, which is the key.</param>
    public bool TryGetHash(string contentPath, string fullPath, out UInt128 hash)
    {
        lock (_gate)
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                // Drop the entry, or a restored file with the same size and
                // mtime would be served the old hash.
                if (_entries.Remove(contentPath)) _dirty = true;

                hash = UInt128.Zero;
                return false;
            }

            long ticks = info.LastWriteTimeUtc.Ticks;
            long length = info.Length;

            if (_entries.TryGetValue(contentPath, out Entry entry) &&
                entry.Ticks == ticks &&
                entry.Length == length)
            {
                _shortCircuits++;
                hash = entry.Hash;
                return true;
            }

            Spectra.Kitchen.Packs.PackPayload payload;
            try
            {
                payload = Spectra.Kitchen.Packs.PackPayload.FromFile(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable: no hash, so no hit. The rule runs and reports it.
                hash = UInt128.Zero;
                return false;
            }

            _rehashes++;
            hash = payload.Hash;
            _entries[contentPath] = new Entry(ticks, length, hash);
            _dirty = true;
            return true;
        }
    }

    /// <summary>Loads the stat cache, or an empty one when it is absent or unreadable.</summary>
    public static StatCache Load(string path)
    {
        var cache = new StatCache();

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return cache;
        }

        try
        {
            cache.Read(bytes);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            cache._entries.Clear();
        }

        return cache;
    }

    /// <summary>Writes the stat cache, creating its directory.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        lock (_gate)
        {
            // Sorted so two cache files can be diffed.
            var keys = new List<string>(_entries.Keys);
            keys.Sort(StringComparer.Ordinal);

            var bytes = new List<byte>(16 + (keys.Count * 48));
            CacheBytes.U32(bytes, Magic);
            CacheBytes.U32(bytes, FormatVersion);
            CacheBytes.U32(bytes, (uint)keys.Count);

            foreach (string key in keys)
            {
                Entry entry = _entries[key];
                CacheBytes.Str(bytes, key);
                CacheBytes.U64(bytes, (ulong)entry.Ticks);
                CacheBytes.U64(bytes, (ulong)entry.Length);
                CacheBytes.U128(bytes, entry.Hash);
            }

            File.WriteAllBytes(path, [.. bytes]);
            _dirty = false;
        }
    }

    private void Read(ReadOnlySpan<byte> bytes)
    {
        var reader = new CacheReader(bytes);
        if (reader.U32() != Magic) throw new InvalidDataException("Not a stat cache.");
        if (reader.U32() != FormatVersion) throw new InvalidDataException("Stat cache version mismatch.");

        uint count = reader.U32();
        for (uint i = 0; i < count; i++)
        {
            string key = reader.Str();
            long ticks = (long)reader.U64();
            long length = (long)reader.U64();
            UInt128 hash = reader.U128();
            _entries[key] = new Entry(ticks, length, hash);
        }
    }

    private readonly record struct Entry(long Ticks, long Length, UInt128 Hash);
}
