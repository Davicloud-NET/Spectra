using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Core.Assets.Sources;

/// <summary>
/// One place content bytes can come from: a folder of loose files, a pack, or a
/// stack of both. Implementations are read-only and thread-safe, create no GPU
/// resources, and never throw for a miss.
/// </summary>
// Paths are content-relative and normalised by the caller
// (ContentRoot.NormalizeRelativePath). Anything else must answer false.
public interface IContentSource
{
    /// <summary>
    /// Where this source sits in an overlay: higher wins. Must not change
    /// after the source is mounted.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Opens <paramref name="path"/> and hands the caller its bytes, which the
    /// caller disposes. False when this source has no such content, or has it
    /// and could not read it.
    /// </summary>
    bool TryOpen(string path, [NotNullWhen(true)] out ContentBlob? blob);

    /// <summary>
    /// Whether this source can serve <paramref name="path"/>. Never throws.
    /// </summary>
    bool Exists(string path);

    /// <summary>
    /// The absolute filesystem path a hot-reload watcher should watch for
    /// <paramref name="path"/>. False when this source is not backed by real
    /// files, such as a pack.
    /// </summary>
    bool TryGetWatchPath(string path, [NotNullWhen(true)] out string? fullPath);

    /// <summary>
    /// Appends every content-relative path this source serves under
    /// <paramref name="prefix"/> with extension <paramref name="extension"/>
    /// (including the dot; empty matches any) to <paramref name="results"/>.
    /// For tools, not per-frame use: it may allocate.
    /// </summary>
    void TryEnumerate(string prefix, string extension, List<string> results);
}
