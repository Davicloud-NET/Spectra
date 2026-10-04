using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace SpectraEngine.Core.Assets.Sources;

/// <summary>
/// Content served from a folder of loose files, such as a project's <c>Assets</c>
/// directory. The only source that can supply a hot-reload watch path.
/// </summary>
public sealed class LooseFileSource : IContentSource
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a source over <paramref name="rootPath"/>. The folder need not
    /// exist; every path is then a miss.
    /// </summary>
    public LooseFileSource(ILogger logger, string rootPath, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(rootPath);

        _logger = logger;
        RootPath = Path.GetFullPath(rootPath);
        Priority = priority;
    }

    /// <summary>Absolute path of the folder this source reads from.</summary>
    public string RootPath { get; }

    /// <inheritdoc/>
    public int Priority { get; }

    /// <inheritdoc/>
    public bool TryOpen(string path, [NotNullWhen(true)] out ContentBlob? blob)
    {
        blob = null;
        if (!TryResolve(path, out string absolute) || !File.Exists(absolute))
            return false;

        try
        {
            blob = FileContent.Read(absolute);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Deleted between the probe and the open: a plain miss, not logged.
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable is treated as a miss. The log line is the only record of the difference.
            _logger.LogWarning("Could not read content '{Path}' from {Source}: {Message}", path, this, ex.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Exists(string path) => TryResolve(path, out string absolute) && File.Exists(absolute);

    /// <inheritdoc/>
    public bool TryGetWatchPath(string path, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;
        if (!TryResolve(path, out string absolute) || !File.Exists(absolute))
            return false;

        fullPath = absolute;
        return true;
    }

    /// <inheritdoc/>
    public void TryEnumerate(string prefix, string extension, List<string> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        string directory = RootPath;
        if (!string.IsNullOrEmpty(prefix) && !TryResolve(prefix, out directory))
            return;
        if (!Directory.Exists(directory))
            return;

        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (!string.IsNullOrEmpty(extension) &&
                    !file.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(Path.GetRelativePath(RootPath, file).Replace('\\', '/'));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep what was found so far.
            _logger.LogWarning("Enumerating '{Prefix}' in {Source} stopped early: {Message}", prefix, this, ex.Message);
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"loose files @ {RootPath}";

    // ContentRoot rejects '..' and rooted paths, which keeps reads inside this folder.
    // An unresolvable path is a miss, never an exception.
    private bool TryResolve(string path, out string absolute)
    {
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                absolute = ContentRoot.ResolveAbsolute(RootPath, path);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
            }
        }

        absolute = string.Empty;
        return false;
    }
}
