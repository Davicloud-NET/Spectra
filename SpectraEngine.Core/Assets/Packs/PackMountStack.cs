using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SpectraEngine.Core.Assets.Packs;

/// <summary>
/// One shadowing decision: a source higher in the stack took a logical path from
/// a source lower in it.
/// </summary>
public readonly record struct MountShadowing(
    string Path,
    string Winner,
    int WinnerPriority,
    string Shadowed,
    int ShadowedPriority,
    bool HiddenByTombstone)
{
    /// <summary>The line this decision is logged as.</summary>
    public string Describe() => HiddenByTombstone
        ? $"{Path}: hidden by a tombstone in {Winner} [priority {WinnerPriority}], " +
          $"over {Shadowed} [priority {ShadowedPriority}]"
        : $"{Path}: {Winner} [priority {WinnerPriority}] shadows {Shadowed} [priority {ShadowedPriority}]";
}

/// <summary>
/// The mount stack: priority bands flattened into one dictionary at mount, so a
/// lookup is one hash rather than a probe per source. A tombstone wins its path
/// like any entry and then resolves to a miss.
/// </summary>
// Mount only invalidates; the flatten runs once before the next lookup, so a batch
// of mounts is not quadratic. The map is published whole and never mutated, which
// makes lookups thread-safe.
public sealed class PackMountStack : IContentSource, IDisposable
{
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly List<IContentSource> _mounted = [];
    private readonly List<MountShadowing> _shadowings = [];

    private volatile Dictionary<string, Resolution>? _flat;
    private bool _disposed;

    /// <summary>Creates an empty stack.</summary>
    /// <param name="priority">
    /// Where this stack sits when it is itself mounted into another overlay.
    /// </param>
    public PackMountStack(ILogger logger, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        Priority = priority;
    }

    /// <inheritdoc/>
    public int Priority { get; }

    /// <summary>The mounted sources, in the order they were mounted.</summary>
    public IReadOnlyList<IContentSource> Sources => _mounted;

    /// <summary>
    /// Every shadowing decision the flatten made, in the order it made them.
    /// Flattens the stack if it has not been flattened yet.
    /// </summary>
    public IReadOnlyList<MountShadowing> Shadowings
    {
        get
        {
            EnsureFlattened();
            lock (_gate) return _shadowings.ToArray();
        }
    }

    /// <summary>Number of logical paths the stack resolves, tombstoned ones included.</summary>
    public int PathCount
    {
        get
        {
            Dictionary<string, Resolution> flat = EnsureFlattened();
            return flat.Count;
        }
    }

    /// <summary>
    /// Mounts <paramref name="source"/> at its own <see cref="IContentSource.Priority"/>.
    /// The stack takes ownership and disposes it in <see cref="Dispose"/>.
    /// </summary>
    public void Mount(IContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(source, this))
            throw new ArgumentException("A mount stack cannot be mounted into itself.", nameof(source));

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _mounted.Add(source);
            _flat = null;
        }
    }

    /// <summary>
    /// Flattens the mounted sources into one map, recording and logging every
    /// shadowing decision. Idempotent, and called automatically by the first
    /// lookup.
    /// </summary>
    public void Flatten() => EnsureFlattened();

    /// <inheritdoc/>
    public bool TryOpen(string path, [NotNullWhen(true)] out ContentBlob? blob)
    {
        blob = null;
        if (!TryResolve(path, out Resolution resolution) || resolution.IsTombstone) return false;

        return resolution.Source.TryOpen(path, out blob);
    }

    /// <inheritdoc/>
    public bool Exists(string path) =>
        TryResolve(path, out Resolution resolution) && !resolution.IsTombstone && resolution.Source.Exists(path);

    /// <inheritdoc/>
    public bool TryGetWatchPath(string path, [NotNullWhen(true)] out string? fullPath)
    {
        if (TryResolve(path, out Resolution resolution) && !resolution.IsTombstone)
            return resolution.Source.TryGetWatchPath(path, out fullPath);

        fullPath = null;
        return false;
    }

    /// <inheritdoc/>
    // Sorted, so the listing does not depend on hash iteration order.
    public void TryEnumerate(string prefix, string extension, List<string> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        Dictionary<string, Resolution> flat = EnsureFlattened();
        string? normalizedPrefix = NormalizePrefix(prefix);

        int before = results.Count;
        foreach ((string path, Resolution resolution) in flat)
        {
            if (resolution.IsTombstone) continue;
            if (!Matches(path, normalizedPrefix, extension)) continue;

            results.Add(path);
        }

        results.Sort(before, results.Count - before, StringComparer.Ordinal);
    }

    /// <summary>
    /// One line per mounted source in resolution order, highest priority first,
    /// followed by every shadowing decision.
    /// </summary>
    public string Describe()
    {
        Dictionary<string, Resolution> flat = EnsureFlattened();

        IContentSource[] ordered;
        MountShadowing[] shadowings;
        lock (_gate)
        {
            ordered = OrderedForResolution();
            shadowings = [.. _shadowings];
        }

        var builder = new StringBuilder();
        builder.Append(ordered.Length).Append(" source(s), ").Append(flat.Count).Append(" path(s):");

        for (int i = ordered.Length - 1; i >= 0; i--)
        {
            builder.Append(Environment.NewLine).Append("  ")
                .Append(ordered[i]).Append(" [priority ").Append(ordered[i].Priority).Append(']');
        }

        for (int i = 0; i < shadowings.Length; i++)
            builder.Append(Environment.NewLine).Append("  shadowed: ").Append(shadowings[i].Describe());

        return builder.ToString();
    }

    /// <summary>Unmounts everything mounted into this stack.</summary>
    public void Dispose()
    {
        IContentSource[] mounted;
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            mounted = [.. _mounted];
            _mounted.Clear();
            _shadowings.Clear();
            _flat = new Dictionary<string, Resolution>(StringComparer.OrdinalIgnoreCase);
        }

        for (int i = 0; i < mounted.Length; i++)
            (mounted[i] as IDisposable)?.Dispose();
    }

    /// <inheritdoc/>
    public override string ToString() => $"mount stack of {_mounted.Count} source(s)";

    // Raw key first: callers normally pass normalised paths, and normalising allocates.
    private bool TryResolve(string path, out Resolution resolution)
    {
        resolution = default;
        if (string.IsNullOrEmpty(path)) return false;

        Dictionary<string, Resolution> flat = EnsureFlattened();
        if (flat.TryGetValue(path, out resolution)) return true;

        string normalized;
        try
        {
            normalized = ContentRoot.NormalizeRelativePath(path);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return flat.TryGetValue(normalized, out resolution);
    }

    private Dictionary<string, Resolution> EnsureFlattened()
    {
        Dictionary<string, Resolution>? flat = _flat;
        if (flat is not null) return flat;

        lock (_gate)
        {
            flat = _flat;
            if (flat is not null) return flat;

            flat = Build();
            _flat = flat;
            return flat;
        }
    }

    // Lowest priority first, so each higher source overwrites what it shadows.
    // Each source's paths are sorted so the shadowing list is deterministic too.
    private Dictionary<string, Resolution> Build()
    {
        _shadowings.Clear();

        var flat = new Dictionary<string, Resolution>(StringComparer.OrdinalIgnoreCase);
        IContentSource[] ordered = OrderedForResolution();

        var paths = new List<MountPath>();
        for (int i = 0; i < ordered.Length; i++)
        {
            IContentSource source = ordered[i];

            paths.Clear();
            CollectPaths(source, paths);
            paths.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));

            for (int j = 0; j < paths.Count; j++)
            {
                MountPath candidate = paths[j];
                if (!TryNormalize(candidate.Path, source, out string key)) continue;

                if (flat.TryGetValue(key, out Resolution existing))
                {
                    var shadowing = new MountShadowing(
                        key,
                        source.ToString() ?? string.Empty,
                        source.Priority,
                        existing.Source.ToString() ?? string.Empty,
                        existing.Source.Priority,
                        candidate.IsTombstone);

                    _shadowings.Add(shadowing);
                    _logger.LogInformation("Mount shadowing: {Decision}", shadowing.Describe());
                }

                flat[key] = new Resolution(source, candidate.IsTombstone);
            }
        }

        _logger.LogInformation(
            "Flattened {Sources} content source(s) into {Paths} path(s) with {Shadowed} shadowing decision(s).",
            ordered.Length, flat.Count, _shadowings.Count);

        return flat;
    }

    private IContentSource[] OrderedForResolution()
    {
        int count = _mounted.Count;
        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;

        // Ties break on mount index. Array.Sort is not stable.
        List<IContentSource> mounted = _mounted;
        Array.Sort(order, (a, b) =>
        {
            int byPriority = mounted[a].Priority.CompareTo(mounted[b].Priority);
            return byPriority != 0 ? byPriority : a.CompareTo(b);
        });

        var ordered = new IContentSource[count];
        for (int i = 0; i < count; i++) ordered[i] = mounted[order[i]];
        return ordered;
    }

    private static void CollectPaths(IContentSource source, List<MountPath> results)
    {
        if (source is IMountPathSource declaring)
        {
            declaring.EnumerateMountPaths(results);
            return;
        }

        var served = new List<string>();
        source.TryEnumerate(string.Empty, string.Empty, served);
        for (int i = 0; i < served.Count; i++)
            results.Add(new MountPath(served[i], IsTombstone: false));
    }

    private bool TryNormalize(string path, IContentSource source, out string key)
    {
        try
        {
            key = ContentRoot.NormalizeRelativePath(path);
            return true;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("{Source} offers '{Path}', which is not a content path: {Message}", source, path, ex.Message);
            key = string.Empty;
            return false;
        }
    }

    private static string? NormalizePrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return null;

        try
        {
            return ContentRoot.NormalizeRelativePath(prefix);
        }
        catch (ArgumentException)
        {
            return " ";
        }
    }

    private static bool Matches(string path, string? prefix, string extension)
    {
        if (prefix is not null &&
            !(path.Length > prefix.Length &&
              path[prefix.Length] == '/' &&
              path.AsSpan(0, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return string.IsNullOrEmpty(extension) || path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct Resolution(IContentSource Source, bool IsTombstone);
}
