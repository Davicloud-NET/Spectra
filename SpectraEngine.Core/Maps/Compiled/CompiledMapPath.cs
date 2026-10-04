using System;
using System.IO;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// Where a map bundle's compiled form lives: the source path with the cooked
/// extension, so <c>Maps/Lobby.smap</c> cooks to <c>Maps/Lobby.scmap</c>.
/// Shared by the cook that writes one and the boot that looks for one.
/// </summary>
public static class CompiledMapPath
{
    /// <summary>
    /// The content path a bundle's compiled map is emitted and resolved under.
    /// </summary>
    /// <param name="bundlePath">
    /// The bundle's path relative to the project root, not to <c>Assets/</c>.
    /// </param>
    public static string For(string bundlePath)
    {
        ArgumentNullException.ThrowIfNull(bundlePath);
        return Path.ChangeExtension(bundlePath, ScmapFormat.FileExtension).Replace('\\', '/');
    }

    /// <summary>Whether <paramref name="contentPath"/> names a compiled map.</summary>
    public static bool IsCompiled(string contentPath)
    {
        ArgumentNullException.ThrowIfNull(contentPath);
        return contentPath.EndsWith(ScmapFormat.FileExtension, StringComparison.OrdinalIgnoreCase);
    }
}
