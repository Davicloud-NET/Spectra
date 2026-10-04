using System;
using System.IO;
using System.Text;
using System.Threading;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// Locates the folder that holds the engine's on-disk content (textures, and
/// later materials/models) and normalises the relative paths used as asset
/// cache keys. Thread-safe.
/// </summary>
// In a developer build the repo's Assets folder wins over the copy beside the
// executable, so hot reload watches the files that get edited.
public static class ContentRoot
{
    /// <summary>Name of the content folder, both in the repo and beside the executable.</summary>
    public const string DirectoryName = "Assets";

    private static readonly Lazy<Resolution> Resolved =
        new(ResolveCore, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Absolute path of the content root. Resolved on first access and cached.
    /// The directory may not exist.
    /// </summary>
    public static string Path => Resolved.Value.Root;

    /// <summary>
    /// True when the content root was found inside the source tree. Asset hot
    /// reload defaults to this value.
    /// </summary>
    public static bool IsDeveloperBuild => Resolved.Value.FromSourceTree;

    /// <summary>
    /// Why the content root did not come from the source tree, or null when it
    /// did.
    /// </summary>
    public static string? NotFromSourceTreeReason => Resolved.Value.Reason;

    /// <summary>
    /// Canonical cache-key form of <paramref name="relativePath"/>: forward
    /// slashes, no leading separator, no <c>.</c> segments. Compare keys with
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path is empty, rooted, or contains a <c>..</c> segment.
    /// </exception>
    public static string NormalizeRelativePath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (relativePath.Length == 0)
            throw new ArgumentException("Asset path must not be empty.", nameof(relativePath));

        // "/Textures/x.png" means content-relative, so strip before the rooted check.
        ReadOnlySpan<char> remaining = relativePath.AsSpan().TrimStart("/\\");

        // Linux doesn't call "C:\x" rooted, but it is never a content path.
        bool hasDrive = remaining.Length >= 2 && remaining[1] == ':' && char.IsAsciiLetter(remaining[0]);
        if (hasDrive || System.IO.Path.IsPathRooted(remaining))
            throw new ArgumentException(
                $"Asset path '{relativePath}' must be relative to the content root.", nameof(relativePath));

        var builder = new StringBuilder(relativePath.Length);
        while (!remaining.IsEmpty)
        {
            int cut = remaining.IndexOfAny('/', '\\');
            ReadOnlySpan<char> part = cut < 0 ? remaining : remaining[..cut];
            remaining = cut < 0 ? default : remaining[(cut + 1)..];

            part = part.Trim();
            if (part.Length == 0 || part.SequenceEqual(".")) continue;
            if (part.SequenceEqual(".."))
                throw new ArgumentException(
                    $"Asset path '{relativePath}' must not escape the content root with '..'.",
                    nameof(relativePath));

            if (builder.Length > 0) builder.Append('/');
            builder.Append(part);
        }

        if (builder.Length == 0)
            throw new ArgumentException(
                $"Asset path '{relativePath}' has no path segments.", nameof(relativePath));

        return builder.ToString();
    }

    /// <summary>
    /// Absolute filesystem path of <paramref name="relativePath"/> under
    /// <paramref name="root"/>. The path is normalised first, so it cannot
    /// escape the root.
    /// </summary>
    public static string ResolveAbsolute(string root, string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        return System.IO.Path.GetFullPath(
            System.IO.Path.Combine(root, normalized.Replace('/', System.IO.Path.DirectorySeparatorChar)));
    }

    private static Resolution ResolveCore()
    {
        string? sourceRoot = TryFindSourceRoot();
        if (sourceRoot is not null)
        {
            string candidate = System.IO.Path.Combine(sourceRoot, DirectoryName);
            if (Directory.Exists(candidate))
                return new Resolution(candidate, FromSourceTree: true, Reason: null);
        }

        string fallback = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, DirectoryName));
        string reason = sourceRoot is null
            ? $"no .slnx or .sln above the base directory '{AppContext.BaseDirectory}' " +
              "(expected in a deployed or NativeAOT-published build)"
            : $"the source tree at '{sourceRoot}' has no '{DirectoryName}' folder";

        return new Resolution(fallback, FromSourceTree: false, reason);
    }

    // Nearest ancestor holding a solution file is the repo root.
    private static string? TryFindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private readonly record struct Resolution(string Root, bool FromSourceTree, string? Reason);
}
