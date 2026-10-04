using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets.Packs;
using System;
using System.Collections.Generic;
using System.IO;

namespace Spectra.Kitchen.Cache;

/// <summary>One output a cached rule run emitted, by name and by hash.</summary>
/// <param name="Path">Normalised content-relative path the engine resolves it by.</param>
/// <param name="Kind">The pack entry kind the rule asked for.</param>
/// <param name="ContentHash">Its name in the <see cref="ContentStore"/>.</param>
/// <param name="Length">Uncompressed byte count.</param>
public readonly record struct CachedOutput(string Path, PackEntryKind Kind, UInt128 ContentHash, long Length);

/// <summary>One remembered run of a rule: its key and what it emitted.</summary>
public sealed record CookGeneration(UInt128 Key, IReadOnlyList<CachedOutput> Outputs);

/// <summary>
/// What a rule has done to one asset: the paths it last touched, and the last few
/// keys those paths produced.
/// </summary>
/// <param name="SourcePath">The asset the rule was asked to cook.</param>
/// <param name="Dependencies">The paths the latest run touched and how, misses included.</param>
/// <param name="Generations">
/// Recent runs, newest first, capped at <see cref="CookGraph.GenerationsKept"/>.
/// </param>
// Several generations so that editing a file and reverting it is a hit.
public sealed record CookGraphRecord(
    string SourcePath,
    IReadOnlyList<RuleDependency> Dependencies,
    IReadOnlyList<CookGeneration> Generations);

/// <summary>
/// One record per rule, persisted as <c>graph.bin</c>.
/// </summary>
// A file that does not parse is discarded and the cache rebuilt.
// Generations live inside the record, not in a table keyed on the cache key:
// a rule that emits without reading its own path would key two assets alike.
// Locked for the scheduler's workers. Records are immutable once built.
public sealed class CookGraph
{
    private const uint Magic = 0x52474353; // "SCGR" little-endian
    private const uint FormatVersion = 2;

    /// <summary>How many past runs of one rule are remembered.</summary>
    // Each one holds a full set of payloads in the store, which nothing sweeps yet.
    public const int GenerationsKept = 4;

    private readonly object _gate = new();
    private readonly Dictionary<string, CookGraphRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    private bool _dirty;

    /// <summary>Records held.</summary>
    public int Count { get { lock (_gate) return _records.Count; } }

    /// <summary>Whether anything changed since this was loaded.</summary>
    public bool IsDirty { get { lock (_gate) return _dirty; } }

    /// <summary>Why the graph file on disk was discarded, or null.</summary>
    public string? DiscardedReason { get; private set; }

    /// <summary>The record for <paramref name="sourcePath"/>, if there is one.</summary>
    public bool TryGet(string sourcePath, out CookGraphRecord record)
    {
        lock (_gate) return _records.TryGetValue(sourcePath, out record!);
    }

    /// <summary>Records one rule run as the newest generation.</summary>
    public void Set(
        string sourcePath,
        IReadOnlyList<RuleDependency> dependencies,
        UInt128 key,
        IReadOnlyList<CachedOutput> outputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(outputs);

        var generations = new List<CookGeneration>(GenerationsKept) { new(key, outputs) };

        lock (_gate)
        {
            if (_records.TryGetValue(sourcePath, out CookGraphRecord? existing))
            {
                for (int i = 0; i < existing.Generations.Count && generations.Count < GenerationsKept; i++)
                {
                    if (existing.Generations[i].Key == key) continue;
                    generations.Add(existing.Generations[i]);
                }
            }

            _records[sourcePath] = new CookGraphRecord(sourcePath, [.. dependencies], generations);
            _dirty = true;
        }
    }

    /// <summary>
    /// Drops every record whose source is not in <paramref name="live"/>.
    /// Does not sweep the content store.
    /// </summary>
    public void RetainOnly(IReadOnlyCollection<string> live)
    {
        ArgumentNullException.ThrowIfNull(live);

        var keep = new HashSet<string>(live, StringComparer.OrdinalIgnoreCase);
        List<string>? drop = null;

        lock (_gate)
        {
            foreach (string path in _records.Keys)
            {
                if (keep.Contains(path)) continue;

                drop ??= [];
                drop.Add(path);
            }

            if (drop is null) return;

            foreach (string path in drop) _records.Remove(path);
            _dirty = true;
        }
    }

    /// <summary>Loads the graph, or an empty one when it is absent or unreadable.</summary>
    public static CookGraph Load(string path)
    {
        var graph = new CookGraph();

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return graph;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            graph.DiscardedReason = ex.Message;
            return graph;
        }

        try
        {
            graph.Read(bytes);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or OverflowException)
        {
            graph._records.Clear();
            graph.DiscardedReason = ex.Message;
        }

        return graph;
    }

    /// <summary>Writes the graph, creating its directory.</summary>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        lock (_gate) Write(path);
    }

    private void Write(string path)
    {
        // Sorted so two cache files can be diffed.
        var keys = new List<string>(_records.Keys);
        keys.Sort(StringComparer.Ordinal);

        var bytes = new List<byte>(64 + (keys.Count * 128));
        CacheBytes.U32(bytes, Magic);
        CacheBytes.U32(bytes, FormatVersion);
        CacheBytes.U32(bytes, (uint)keys.Count);

        foreach (string key in keys)
        {
            CookGraphRecord record = _records[key];

            CacheBytes.Str(bytes, record.SourcePath);

            CacheBytes.U32(bytes, (uint)record.Dependencies.Count);
            for (int i = 0; i < record.Dependencies.Count; i++)
            {
                RuleDependency dependency = record.Dependencies[i];
                CacheBytes.Str(bytes, dependency.Path);
                bytes.Add((byte)dependency.Kind);
                CacheBytes.U128(bytes, dependency.ContentHash);
            }

            CacheBytes.U32(bytes, (uint)record.Generations.Count);
            foreach (CookGeneration generation in record.Generations)
            {
                CacheBytes.U128(bytes, generation.Key);
                CacheBytes.U32(bytes, (uint)generation.Outputs.Count);

                for (int i = 0; i < generation.Outputs.Count; i++)
                {
                    CachedOutput output = generation.Outputs[i];
                    CacheBytes.Str(bytes, output.Path);
                    bytes.Add((byte)output.Kind);
                    CacheBytes.U128(bytes, output.ContentHash);
                    CacheBytes.U64(bytes, (ulong)output.Length);
                }
            }
        }

        File.WriteAllBytes(path, [.. bytes]);
        _dirty = false;
    }

    private void Read(ReadOnlySpan<byte> bytes)
    {
        var reader = new CacheReader(bytes);
        if (reader.U32() != Magic) throw new InvalidDataException("Not a cook graph.");
        if (reader.U32() != FormatVersion) throw new InvalidDataException("Cook graph version mismatch.");

        uint recordCount = reader.U32();
        for (uint r = 0; r < recordCount; r++)
        {
            string sourcePath = reader.Str();

            uint dependencyCount = reader.U32();
            var dependencies = new RuleDependency[dependencyCount];
            for (uint i = 0; i < dependencyCount; i++)
            {
                string path = reader.Str();
                RuleDependencyKind kind = ToDependencyKind(reader.U8());
                UInt128 hash = reader.U128();
                dependencies[i] = new RuleDependency(path, kind, hash);
            }

            uint generationCount = reader.U32();
            var generations = new CookGeneration[generationCount];
            for (uint g = 0; g < generationCount; g++)
            {
                UInt128 key = reader.U128();
                uint outputCount = reader.U32();
                var outputs = new CachedOutput[outputCount];

                for (uint i = 0; i < outputCount; i++)
                {
                    string path = reader.Str();
                    var kind = (PackEntryKind)reader.U8();
                    UInt128 hash = reader.U128();
                    long length = checked((long)reader.U64());
                    outputs[i] = new CachedOutput(path, kind, hash, length);
                }

                generations[g] = new CookGeneration(key, outputs);
            }

            _records[sourcePath] = new CookGraphRecord(sourcePath, dependencies, generations);
        }
    }

    // No cast: an unknown value must not be taken for a known kind.
    private static RuleDependencyKind ToDependencyKind(byte value) => value switch
    {
        (byte)RuleDependencyKind.Read => RuleDependencyKind.Read,
        (byte)RuleDependencyKind.ProbeFound => RuleDependencyKind.ProbeFound,
        (byte)RuleDependencyKind.ProbeMissing => RuleDependencyKind.ProbeMissing,
        _ => throw new InvalidDataException(
            $"Cook graph names dependency kind {value}, which this build has no name for."),
    };
}
