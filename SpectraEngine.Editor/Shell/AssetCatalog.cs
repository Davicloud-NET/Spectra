using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Inspection;
using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>One file a picker can offer.</summary>
/// <param name="ContentPath">
/// The normalized content-relative path, which is the name the engine knows this
/// file by: what a material writes down, what a map records, what a pack hashes
/// its id from.
/// </param>
/// <param name="Stem">The file name without its extension, for reading.</param>
/// <param name="Folder">Its folder, content-relative, for telling two alike names apart.</param>
/// <param name="FullPath">Where it is on this machine.</param>
/// <param name="Kind">What it is.</param>
public sealed record AssetCatalogEntry(
    string ContentPath, string Stem, string Folder, string FullPath, ContentKind Kind);

/// <summary>
/// What a project has, for the pickers that assign it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A walk rather than a query of the asset manager.</b> The manager knows
/// what has been LOADED, which for a fresh session is almost nothing; a picker
/// has to offer what exists. The registry cannot answer either: it interns paths
/// and never enumerates.
/// </para>
/// <para>
/// <b>The content path comes from <see cref="ContentDragPayload"/>'s own rule</b>,
/// so a material picked here and a material dragged from the browser produce the
/// same string. A fifth spelling of asset identity is the failure this content
/// layer specialises in: everything resolves, every log line reads healthy, and
/// nothing binds.
/// </para>
/// </remarks>
public sealed class AssetCatalog(ILogger logger)
{
    private readonly List<AssetCatalogEntry> _entries = [];
    private string? _root;

    /// <summary>Everything found under the root, folders first then alphabetical.</summary>
    public IReadOnlyList<AssetCatalogEntry> Entries => _entries;

    /// <summary>Why the walk found nothing, or null.</summary>
    public string? Warning { get; private set; }

    /// <summary>The root the last walk covered, or null.</summary>
    public string? Root => _root;

    /// <summary>Re-reads the project's asset folder.</summary>
    /// <remarks>
    /// Called when a picker opens rather than watched: a few hundred file names
    /// is a millisecond, and a watcher here would be a second index beside the
    /// content browser's.
    /// </remarks>
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
            // A folder the editor cannot read is a report rather than a crash:
            // the picker is a convenience and the panel still works.
            logger.LogWarning(ex, "Could not walk {Root} for the asset picker", assetsRoot);
            Warning = "Could not read this project's Assets folder.";
        }
    }

    /// <summary>
    /// The best matches of one kind, ranked.
    /// </summary>
    /// <remarks>
    /// <b>The stem is scored first and the path second, a few points behind.</b>
    /// Somebody typing "brick" means the file called brick, not every file in a
    /// folder that happens to contain those letters; but a path match still
    /// beats no match, because folders are how people organise.
    /// </remarks>
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
        _ => ContentKind.Other,
    };
}
