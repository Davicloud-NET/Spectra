using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>One file a picker can offer.</summary>
/// <param name="ContentPath">Normalized content-relative path, the engine's identity for the file.</param>
/// <param name="Folder">Content-relative folder.</param>
public sealed record AssetCatalogEntry(
    string ContentPath, string Stem, string Folder, string FullPath, ContentKind Kind);

/// <summary>The files a project has on disk, for the asset pickers.</summary>
// Walks the folder: the asset manager only knows what has been loaded.
// Content paths come from ContentDragPayload so a pick and a drag agree.
public sealed class AssetCatalog(ILogger logger)
{
    private readonly List<AssetCatalogEntry> _entries = [];
    private string? _root;

    /// <summary>Everything found under the root, sorted by content path.</summary>
    public IReadOnlyList<AssetCatalogEntry> Entries => _entries;

    /// <summary>Why the walk found nothing, or null.</summary>
    public string? Warning { get; private set; }

    /// <summary>The root the last walk covered, or null.</summary>
    public string? Root => _root;

    /// <summary>Re-reads the project's asset folder. Call when a picker opens.</summary>
    public void Rebuild(string? assetsRoot)
    {
        _entries.Clear();
        Warning = null;
        _root = assetsRoot;

        if (string.IsNullOrEmpty(assetsRoot) || !Directory.Exists(assetsRoot))
        {
            Warning = "No project is open.";
            return;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
            {
                ContentKind kind = ContentClassifier.Classify(file);
                if (kind is ContentKind.Folder or ContentKind.Other) continue;

                if (!ContentDragPayload.TryCreate(assetsRoot, file, kind, out ContentDragPayload? payload))
                    continue;

                string content = payload.ContentPath;
                int slash = content.LastIndexOf('/');

                _entries.Add(new AssetCatalogEntry(
                    content,
                    Path.GetFileNameWithoutExtension(file),
                    slash > 0 ? content[..slash] : string.Empty,
                    file,
                    kind));
            }

            _entries.Sort(static (a, b) => string.CompareOrdinal(a.ContentPath, b.ContentPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not walk {Root} for the asset picker", assetsRoot);
            Warning = "Could not read this project's Assets folder.";
        }
    }

    /// <summary>The best matches of one kind, ranked. A name match outranks a folder match.</summary>
    public List<AssetCatalogEntry> Search(string query, ContentKind kind, int max)
    {
        ArgumentNullException.ThrowIfNull(query);

        List<(AssetCatalogEntry Entry, int Score)> matches = [];
        foreach (AssetCatalogEntry entry in _entries)
        {
            if (entry.Kind != kind) continue;

            int score = CommandScore.Of(entry.Stem, query);
            if (score == CommandScore.NoMatch)
            {
                int path = CommandScore.Of(entry.ContentPath, query);
                if (path == CommandScore.NoMatch) continue;
                score = path - 8;
            }

            matches.Add((entry, score));
        }

        matches.Sort(static (a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : string.CompareOrdinal(a.Entry.ContentPath, b.Entry.ContentPath);
        });

        var rows = new List<AssetCatalogEntry>(Math.Min(max, matches.Count));
        for (int i = 0; i < matches.Count && rows.Count < max; i++)
            rows.Add(matches[i].Entry);

        return rows;
    }

    /// <summary>Which asset kind a property row's kind asks for.</summary>
    public static ContentKind KindFor(AssetKind kind) => kind switch
    {
        AssetKind.Material => ContentKind.Material,
        AssetKind.Texture => ContentKind.Texture,
        AssetKind.Model => ContentKind.Model,
        AssetKind.Sound => ContentKind.Sound,
        _ => ContentKind.Other,
    };
}
