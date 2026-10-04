using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Maps;

namespace Spectra.Kitchen.Maps;

/// <summary>
/// The <c>SourceMapDigest</c> a compiled map stamps: one hash over a whole
/// <c>.smap</c> bundle, paths included.
/// </summary>
// Files are hashed in ordinal order of their bundle-relative path, not in
// directory walk order, which differs between filesystems.
public static class MapBundleDigest
{
    /// <summary>Hashes every file in <paramref name="bundlePath"/>, recursively.</summary>
    public static UInt128 Compute(string bundlePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);

        if (!Directory.Exists(bundlePath))
        {
            throw new DirectoryNotFoundException(
                $"'{bundlePath}' is not a map bundle directory, so it has no source digest.");
        }

        string root = Path.GetFullPath(bundlePath);
        List<(string Path, byte[] Bytes)> files = [];

        foreach (string absolute in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            files.Add((Path.GetRelativePath(root, absolute).Replace('\\', '/'), File.ReadAllBytes(absolute)));
        }

        return Compute(files);
    }

    /// <summary>Hashes a bundle already read into memory.</summary>
    /// <param name="files">
    /// Bundle-relative, forward-slash paths and their bytes, in any order.
    /// </param>
    public static UInt128 Compute(IReadOnlyList<(string Path, byte[] Bytes)> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var kept = new List<(string Path, byte[] Bytes)>(files.Count);
        foreach ((string path, byte[] bytes) in files)
        {
            if (IsSourceFile(path)) kept.Add((path, bytes));
        }

        (string Path, byte[] Bytes)[] sorted = [.. kept];
        Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Path, b.Path));

        var digest = new PackDigest.Accumulator();
        foreach ((string path, byte[] bytes) in sorted)
        {
            // Path too, or a rename that keeps the sort order goes unseen.
            digest.Append(Encoding.UTF8.GetBytes(path));
            digest.Append(bytes);
        }

        return digest.Finish();
    }

    /// <summary>
    /// Whether a bundle-relative file is part of what the bake reads.
    /// Per-user editor state is not.
    /// </summary>
    public static bool IsSourceFile(string bundleRelativePath)
    {
        ArgumentNullException.ThrowIfNull(bundleRelativePath);

        return !Path.GetFileName(bundleRelativePath.AsSpan())
            .Equals(MapFormat.UserStateFileName, StringComparison.OrdinalIgnoreCase);
    }
}
