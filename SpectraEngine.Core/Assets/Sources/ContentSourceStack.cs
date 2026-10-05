using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace SpectraEngine.Core.Assets.Sources;

/// <summary>
/// An overlay of <see cref="IContentSource"/>s: priority ordered, first hit
/// wins. Equal priorities keep mount order. Thread-safe.
/// </summary>
// Strict mode lives here, not on AssetManager: a cook wants a miss to throw,
// the engine wants a placeholder. It applies to TryOpen only; Exists never throws.
public sealed class ContentSourceStack : IContentSource
{
    // Highest priority first. Replaced whole on every mount, so readers need no lock.
    private volatile IContentSource[] _sources = [];

    /// <summary>
    /// Creates an empty stack. <paramref name="strict"/> makes a total
    /// <see cref="TryOpen"/> miss throw instead of returning false;
    /// <paramref name="priority"/> only matters when this stack is itself
    /// mounted into another one.
    /// </summary>
    public ContentSourceStack(bool strict = false, int priority = 0)
    {
        Strict = strict;
        Priority = priority;
    }

    /// <summary>
    /// Whether a <see cref="TryOpen"/> miss in every source throws. False for
    /// the engine, true for tools such as the cook.
    /// </summary>
    public bool Strict { get; }

    /// <inheritdoc/>
    public int Priority { get; }

    /// <summary>Number of mounted sources, after flattening.</summary>
    public int Count => _sources.Length;

    /// <summary>The mounted sources, highest priority first.</summary>
    public IReadOnlyList<IContentSource> Sources => _sources;

    /// <summary>
    /// Mounts <paramref name="source"/> into the overlay. Another stack is
    /// flattened into this one rather than nested.
    /// </summary>
    public void Mount(IContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(source, this))
            throw new ArgumentException("A content source stack cannot be mounted into itself.", nameof(source));

        if (source is ContentSourceStack nested)
        {
            // Flatten: nested, its members could not interleave with ours by
            // priority, and its strictness would override this stack's.
            IContentSource[] members = nested._sources;
            for (int i = 0; i < members.Length; i++)
                Mount(members[i]);
            return;
        }

        IContentSource[] current = _sources;
        var next = new IContentSource[current.Length + 1];

        // After every source of equal or higher priority, so mount order breaks ties.
        int at = current.Length;
        for (int i = 0; i < current.Length; i++)
        {
            if (current[i].Priority < source.Priority)
            {
                at = i;
                break;
            }
        }

        Array.Copy(current, next, at);
        next[at] = source;
        Array.Copy(current, at, next, at + 1, current.Length - at);
        _sources = next;
    }

    /// <inheritdoc/>
    /// <exception cref="FileNotFoundException">
    /// No source has the content and this stack is <see cref="Strict"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// A source that makes this content when asked refused the file it is made
    /// from. Sources below it are not tried: the content is there and broken.
    /// </exception>
    public bool TryOpen(string path, [NotNullWhen(true)] out ContentBlob? blob)
    {
        IContentSource[] sources = _sources;
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i].TryOpen(path, out blob))
                return true;
        }

        if (Strict)
        {
            throw new FileNotFoundException(
                $"Content '{path}' was not found in any of the {sources.Length} mounted source(s).", path);
        }

        blob = null;
        return false;
    }

    /// <inheritdoc/>
    public bool Exists(string path)
    {
        IContentSource[] sources = _sources;
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i].Exists(path)) return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetWatchPath(string path, [NotNullWhen(true)] out string? fullPath)
    {
        IContentSource[] sources = _sources;
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i].TryGetWatchPath(path, out fullPath)) return true;
        }

        fullPath = null;
        return false;
    }

    /// <inheritdoc/>
    public void TryEnumerate(string prefix, string extension, List<string> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        IContentSource[] sources = _sources;
        if (sources.Length == 1)
        {
            sources[0].TryEnumerate(prefix, extension, results);
            return;
        }

        // A path several sources hold appears once. Only this call's additions
        // are deduplicated, not what the caller already had in the list.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < sources.Length; i++)
        {
            int before = results.Count;
            sources[i].TryEnumerate(prefix, extension, results);
            for (int j = before; j < results.Count; j++)
            {
                if (seen.Add(results[j])) continue;

                results.RemoveAt(j);
                j--;
            }
        }
    }

    /// <summary>
    /// One line naming every mounted source in resolution order, for the log.
    /// </summary>
    public string Describe()
    {
        IContentSource[] sources = _sources;
        if (sources.Length == 0) return "(no content sources mounted)";

        var builder = new StringBuilder();
        for (int i = 0; i < sources.Length; i++)
        {
            if (i > 0) builder.Append(" -> ");
            builder.Append(sources[i].ToString()).Append(" [priority ").Append(sources[i].Priority).Append(']');
        }

        if (Strict) builder.Append(" (strict)");
        return builder.ToString();
    }

    /// <inheritdoc/>
    public override string ToString() => Describe();
}
