using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// A reference to a material by interned id, not the
/// <see cref="Graphics.Material"/> itself. This is what brush faces and carved
/// polygons carry, so the background CSG compile touches no GPU or asset state.
/// Resolved on the render thread by <see cref="AssetManager.ResolveMaterial"/>.
/// </summary>
public readonly struct MaterialRef : IEquatable<MaterialRef>
{
    /// <summary>What a face with no material assigned carries.</summary>
    public static readonly MaterialRef Default = default;

    internal MaterialRef(int id) => Id = id;

    /// <summary>
    /// The interned id. 0 is the default; every other value comes from
    /// <see cref="MaterialRegistry.Intern"/>. Per-process: never write it to disk.
    /// </summary>
    public int Id { get; }

    /// <summary>True when this is the default material (id 0).</summary>
    public bool IsDefault => Id == 0;

    /// <inheritdoc/>
    public bool Equals(MaterialRef other) => Id == other.Id;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MaterialRef other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Id;

    /// <summary>Equality on the interned id.</summary>
    public static bool operator ==(MaterialRef left, MaterialRef right) => left.Id == right.Id;

    /// <summary>Inequality on the interned id.</summary>
    public static bool operator !=(MaterialRef left, MaterialRef right) => left.Id != right.Id;

    /// <inheritdoc/>
    public override string ToString() =>
        MaterialRegistry.TryGetPath(this, out string? path) ? path : $"material#{Id}";
}

/// <summary>
/// The process-wide intern table behind <see cref="MaterialRef"/>. Append-only:
/// an id stays valid for the life of the process. Thread-safe. Paths are keyed
/// case-insensitively with backslashes folded to forward slashes.
/// </summary>
public static class MaterialRegistry
{
    private static readonly object Sync = new();

    // Index 0 is a placeholder for the default material so id == index.
    private static readonly List<string> Paths = [string.Empty];

    private static readonly Dictionary<string, MaterialRef> ByPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of paths interned so far, excluding the default.</summary>
    public static int Count
    {
        get { lock (Sync) return Paths.Count - 1; }
    }

    /// <summary>
    /// Returns the id for a content-relative <c>.spectramat</c> path, assigning
    /// one on first use. An empty or whitespace path is the default material.
    /// </summary>
    public static MaterialRef Intern(string materialPath)
    {
        ArgumentNullException.ThrowIfNull(materialPath);
        if (string.IsNullOrWhiteSpace(materialPath))
            return MaterialRef.Default;

        string key = Normalize(materialPath);
        lock (Sync)
        {
            if (ByPath.TryGetValue(key, out MaterialRef existing))
                return existing;

            var created = new MaterialRef(Paths.Count);
            Paths.Add(key);
            ByPath[key] = created;
            return created;
        }
    }

    /// <summary>
    /// Resolves an id back to its path. False for the default material and for
    /// an id this process never handed out.
    /// </summary>
    public static bool TryGetPath(MaterialRef reference, out string path)
    {
        int id = reference.Id;
        if (id > 0)
        {
            lock (Sync)
            {
                if (id < Paths.Count)
                {
                    path = Paths[id];
                    return true;
                }
            }
        }

        path = string.Empty;
        return false;
    }

    private static string Normalize(string materialPath) =>
        materialPath.Trim().Replace('\\', '/');
}
