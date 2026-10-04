using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Shell;

/// <summary>What kind of thing one row in the content browser is.</summary>
public enum ContentKind
{
    /// <summary>A directory.</summary>
    Folder,

    /// <summary>An image the shell can decode and show.</summary>
    Texture,

    /// <summary>A <c>.spectramat</c>.</summary>
    Material,

    /// <summary>An <c>.obj</c>, <c>.gltf</c> or similar.</summary>
    Model,

    /// <summary>A <c>.spectrashade</c>.</summary>
    Shader,

    /// <summary>Anything else. Listed rather than hidden.</summary>
    Other,
}

/// <summary>One entry in the content browser.</summary>
public sealed class ContentEntry : ObservableObject
{
    private Bitmap? _thumbnail;

    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public required ContentKind Kind { get; init; }

    /// <summary>Size on disk, formatted, or empty for a folder.</summary>
    public required string SizeLabel { get; init; }

    /// <summary>The decoded preview. Null until the decode lands, and for non-images.</summary>
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (Set(ref _thumbnail, value))
                Raise(nameof(HasThumbnail));
        }
    }

    /// <summary>Whether a decoded preview is available.</summary>
    public bool HasThumbnail => _thumbnail is not null;

    private bool _isSelected;

    /// <summary>Whether this is the entry the details strip describes.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => Set(ref _isSelected, value);
    }

    /// <summary>The file name without its extension.</summary>
    public string Stem => System.IO.Path.GetFileNameWithoutExtension(Name);

    /// <summary>The content-relative path, or empty for a folder.</summary>
    public string ContentPath { get; init; } = string.Empty;

    /// <summary>The content-relative folder, shown beside a search result.</summary>
    public string FolderLabel { get; init; } = string.Empty;

    /// <summary>When this file last changed, for keying a decoded preview.</summary>
    public long MtimeTicks { get; init; }

    /// <summary>A word for this kind, for the details strip.</summary>
    public string KindLabel => Kind switch
    {
        ContentKind.Folder => "folder",
        ContentKind.Texture => "texture",
        ContentKind.Material => "material",
        ContentKind.Model => "model",
        ContentKind.Shader => "shader",
        _ => "file",
    };

    public bool IsFolder => Kind == ContentKind.Folder;

    /// <summary>The glyph for this kind, from the theme dictionary.</summary>
    public Geometry? Icon => Resource<Geometry>(Kind switch
    {
        ContentKind.Folder => "IconOpenFolder",
        ContentKind.Texture => "IconMesh",
        ContentKind.Material => "IconBrushPart",
        ContentKind.Model => "IconMesh",
        ContentKind.Shader => "IconBrushWorld",
        _ => "IconEmpty",
    });

    /// <summary>The kind's tint.</summary>
    public IBrush? KindBrush => Resource<IBrush>(Kind switch
    {
        ContentKind.Folder => "SpectraMode",
        ContentKind.Texture => "SpectraKindMesh",
        ContentKind.Material => "SpectraKindBrushPart",
        ContentKind.Model => "SpectraKindBrushWorld",
        ContentKind.Shader => "SpectraKindLight",
        _ => "SpectraTextMuted",
    });

    private static T? Resource<T>(string key) where T : class
        => Application.Current?.TryFindResource(key, out object? value) == true ? value as T : null;
}

/// <summary>The project's <c>Assets/</c> folder, browsed. UI thread only.</summary>
// Thumbnails are decoded by the shell, not AssetManager: that belongs to the
// render thread and would create a GPU texture per preview.
public sealed class ContentBrowserModel : ObservableObject
{
    /// <summary>How many search results one query shows.</summary>
    public const int MaxSearchResults = 200;

    /// <summary>How many tiles the grid draws before it asks for the list.</summary>
    // Avalonia has no virtualising wrap panel, so the grid realises every tile.
    public const int GridCap = 400;

    private readonly ILogger _logger;
    private readonly ContentIndex _index;
    private string? _root;
    private string _currentPath = string.Empty;
    private string _breadcrumb = string.Empty;
    private bool _hasContent;
    private string _emptyMessage = "No project is open.";
    private string _query = string.Empty;
    private ContentFilter _filter = ContentFilter.All;
    private ContentViewMode _viewMode = ContentViewMode.Grid;
    private string _resultNote = string.Empty;

    // Bumped on every relist so a thumbnail decode that lands late is dropped.
    private int _generation;

    public ContentBrowserModel(ILogger logger)
    {
        _logger = logger;
        _index = new ContentIndex(logger);

        _index.Changed += Relist;
        _index.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ContentIndex.IsWalking) or nameof(ContentIndex.Warning))
                Raise(nameof(ResultNote));
        };
    }

    /// <summary>The index behind the folder view and the search.</summary>
    public ContentIndex Index => _index;

    /// <summary>
    /// The search text. A non-empty query replaces the folder view with matches
    /// from the whole project.
    /// </summary>
    public string Query
    {
        get => _query;
        set
        {
            if (!Set(ref _query, value ?? string.Empty)) return;

            Raise(nameof(IsSearching));
            Relist();
        }
    }

    /// <summary>Whether the list is a search result rather than a folder.</summary>
    public bool IsSearching => _query.Length > 0;

    /// <summary>Which kinds are listed.</summary>
    public ContentFilter Filter
    {
        get => _filter;
        set
        {
            if (!Set(ref _filter, value)) return;

            Raise(nameof(IsFilterAll));
            Raise(nameof(IsFilterTextures));
            Raise(nameof(IsFilterMaterials));
            Raise(nameof(IsFilterModels));
            Relist();
        }
    }

    public bool IsFilterAll => _filter == ContentFilter.All;
    public bool IsFilterTextures => _filter == ContentFilter.Textures;
    public bool IsFilterMaterials => _filter == ContentFilter.Materials;
    public bool IsFilterModels => _filter == ContentFilter.Models;

    /// <summary>Tiles or dense rows. Only the list virtualises.</summary>
    public ContentViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (!Set(ref _viewMode, value)) return;

            Raise(nameof(IsGridView));
            Raise(nameof(IsListView));
            ViewChanged?.Invoke(_viewMode);
            Relist();
        }
    }

    public bool IsGridView => _viewMode == ContentViewMode.Grid && !_isCramped;
    public bool IsListView => _viewMode == ContentViewMode.List || _isCramped;

    private bool _isCramped;

    /// <summary>
    /// True when the panel is too short to show a whole tile. Rows are shown
    /// instead, whatever <see cref="ViewMode"/> says.
    /// </summary>
    public bool IsCramped
    {
        get => _isCramped;
        set
        {
            if (!Set(ref _isCramped, value)) return;

            Raise(nameof(IsGridView));
            Raise(nameof(IsListView));
        }
    }

    /// <summary>Raised when the user changes the view, so it can be saved.</summary>
    public event Action<ContentViewMode>? ViewChanged;

    /// <summary>
    /// What was left out, or what the index is doing. Empty when neither.
    /// </summary>
    public string ResultNote
    {
        get
        {
            if (_index.IsWalking) return "Indexing...";
            if (_index.Warning is { Length: > 0 } warning) return warning;
            return _resultNote;
        }
    }

    /// <summary>The path from the root to here, as clickable segments.</summary>
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    /// <summary>The entries in the current folder, folders first.</summary>
    public ObservableCollection<ContentEntry> Entries { get; } = [];

    /// <summary>Where the browser is, relative to the assets root.</summary>
    public string Breadcrumb
    {
        get => _breadcrumb;
        private set => Set(ref _breadcrumb, value);
    }

    /// <summary>Whether the current folder has anything in it.</summary>
    public bool HasContent
    {
        get => _hasContent;
        private set => Set(ref _hasContent, value);
    }

    /// <summary>What to say when it does not.</summary>
    public string EmptyMessage
    {
        get => _emptyMessage;
        private set => Set(ref _emptyMessage, value);
    }

    /// <summary>Whether the browser can go up a level.</summary>
    public bool CanGoUp => _root is not null &&
        !string.Equals(_currentPath, _root, StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns one browsed entry into a drag payload, or refuses it.</summary>
    public bool TryDescribe(
        ContentEntry entry, [NotNullWhen(true)] out ContentDragPayload? payload)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ContentDragPayload.TryCreate(_root, entry.FullPath, entry.Kind, out payload);
    }

    /// <summary>Points the browser at a project's assets folder, or at nothing.</summary>
    public void SetRoot(string? assetsPath)
    {
        _root = assetsPath;
        _query = string.Empty;
        Raise(nameof(Query));
        Raise(nameof(IsSearching));

        _currentPath = assetsPath ?? string.Empty;

        // Must run before _index.SetRoot. The walk relists when it lands, and
        // in the other order the two can overlap and write Entries at once.
        Relist();

        _index.SetRoot(assetsPath);
    }

    /// <summary>Navigates to a folder, clearing any query.</summary>
    public void NavigateTo(string folderFullPath)
    {
        if (string.IsNullOrEmpty(folderFullPath)) return;

        _currentPath = folderFullPath;
        _query = string.Empty;
        Raise(nameof(Query));
        Raise(nameof(IsSearching));

        Select(null);
        Relist();
    }

    /// <summary>
    /// Navigates to the folder holding a content-relative path and selects the
    /// file. Clears the query.
    /// </summary>
    public void Reveal(string contentPath)
    {
        if (_root is null || string.IsNullOrWhiteSpace(contentPath)) return;

        string full = Path.Combine(
            _root, contentPath.Replace('/', Path.DirectorySeparatorChar));

        NavigateTo(Path.GetDirectoryName(full) ?? _root);

        foreach (ContentEntry entry in Entries)
        {
            if (string.Equals(entry.FullPath, full, StringComparison.OrdinalIgnoreCase))
            {
                Select(entry);
                RevealScrolled?.Invoke(entry);
                return;
            }
        }
    }

    /// <summary>Raised when a reveal selected a row, so the panel can scroll to it.</summary>
    public event Action<ContentEntry>? RevealScrolled;

    /// <summary>Goes up one level, if there is one.</summary>
    public void GoUp()
    {
        if (!CanGoUp)
            return;

        NavigateTo(Path.GetDirectoryName(_currentPath) ?? string.Empty);
    }

    /// <summary>Walks the project again and re-lists.</summary>
    public void Refresh() => _index.Refresh();

    /// <summary>Descends into <paramref name="entry"/> if it is a folder.</summary>
    public void Open(ContentEntry entry)
    {
        if (entry.IsFolder)
            NavigateTo(entry.FullPath);
    }

    private ContentEntry? _selected;

    /// <summary>The entry the details strip describes, or null.</summary>
    public ContentEntry? Selected
    {
        get => _selected;
        private set
        {
            if (!Set(ref _selected, value)) return;

            Raise(nameof(HasSelected));
            Raise(nameof(SelectedDetails));
        }
    }

    /// <summary>Whether anything is selected.</summary>
    public bool HasSelected => _selected is not null;

    /// <summary>
    /// One line about the selection: its kind, content-relative path and size.
    /// </summary>
    public string SelectedDetails
    {
        get
        {
            if (_selected is not { } entry) return string.Empty;

            string path = TryDescribe(entry, out ContentDragPayload? payload)
                ? payload.ContentPath
                : entry.Name;

            return entry.SizeLabel.Length > 0
                ? $"{entry.KindLabel}  {path}  {entry.SizeLabel}"
                : $"{entry.KindLabel}  {path}";
        }
    }

    /// <summary>Selects one entry, or clears the selection with null.</summary>
    public void Select(ContentEntry? entry)
    {
        if (ReferenceEquals(_selected, entry)) return;

        if (_selected is { } previous) previous.IsSelected = false;
        if (entry is not null) entry.IsSelected = true;

        Selected = entry;

        SelectedExtra = string.Empty;
        if (entry is not null) _ = DescribeSelectionAsync(entry, ++_detailGeneration);
    }

    private string _selectedExtra = string.Empty;
    private int _detailGeneration;

    /// <summary>
    /// What the file itself says: a texture's size, a material's shader and
    /// textures. Empty until the background read lands, or when it fails.
    /// </summary>
    public string SelectedExtra
    {
        get => _selectedExtra;
        private set => Set(ref _selectedExtra, value);
    }

    private async Task DescribeSelectionAsync(ContentEntry entry, int generation)
    {
        string text = entry.Kind switch
        {
            ContentKind.Texture => await Task.Run(() => DescribeTexture(entry.FullPath)).ConfigureAwait(true),
            ContentKind.Material => await Task.Run(() => DescribeMaterial(entry.FullPath)).ConfigureAwait(true),
            _ => string.Empty,
        };

        if (Volatile.Read(ref _detailGeneration) != generation) return;

        SelectedExtra = text;
    }

    // Header read only. Decoding a 4K image for two numbers costs 32 MB.
    private static string DescribeTexture(string fullPath) =>
        ImageHeader.TryReadFile(fullPath, out int width, out int height)
            ? $"{width} x {height}"
            : string.Empty;

    private string DescribeMaterial(string fullPath)
    {
        try
        {
            MaterialDefinition definition = MaterialParser.ParseFile(fullPath);

            string shader = definition.ShaderName is { Length: > 0 } named
                ? named
                : MaterialParser.BuiltInShaderName;

            // Parser warnings (an unknown key, say) show nowhere else in the shell.
            if (definition.Warnings.Count > 0)
                return $"shader {shader}  ({definition.Warnings.Count} warning(s) in this file)";

            return definition.Textures.Count == 0
                ? $"shader {shader}  (no textures)"
                : $"shader {shader}  {definition.Textures.Count} texture(s)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _logger.LogDebug(ex, "Could not describe {Path}", fullPath);
            return string.Empty;
        }
    }

    // Rebuilds Entries from the index: this folder, or the whole project when
    // there is a query. Not patched like the scene tree; a listing changes wholesale.
    private void Relist()
    {
        int generation = ++_generation;

        Entries.Clear();
        Raise(nameof(CanGoUp));
        RebuildBreadcrumbs();

        if (_root is null)
        {
            Breadcrumb = string.Empty;
            HasContent = false;
            EmptyMessage = "No project is open.";
            _resultNote = string.Empty;
            Raise(nameof(ResultNote));
            return;
        }

        Breadcrumb = _currentPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase)
            ? "Assets" + _currentPath[_root.Length..].Replace(Path.DirectorySeparatorChar, '/')
            : _currentPath;

        List<ContentIndexEntry> rows = IsSearching ? Search() : _index.InFolder(_currentPath);

        int shown = 0;
        int cap = ViewMode == ContentViewMode.Grid ? GridCap : int.MaxValue;

        foreach (ContentIndexEntry row in rows)
        {
            if (!Admits(row.Kind)) continue;
            if (shown >= cap) break;

            var entry = new ContentEntry
            {
                Name = row.Name,
                FullPath = row.FullPath,
                Kind = row.Kind,
                SizeLabel = FormatBytes(row.Bytes),
                ContentPath = row.ContentPath,
                FolderLabel = FolderLabelFor(row),
                MtimeTicks = row.MtimeTicks,
            };

            Entries.Add(entry);
            shown++;

            if (row.Kind == ContentKind.Texture)
                _ = LoadThumbnailAsync(entry, generation);
        }

        HasContent = Entries.Count > 0;

        EmptyMessage = IsSearching
            ? "Nothing in this project matches."
            : _index.Count == 0 ? "This project has no assets yet." : "This folder is empty.";

        int hidden = CountAdmitted(rows) - shown;
        _resultNote = hidden <= 0
            ? string.Empty
            : ViewMode == ContentViewMode.Grid && !IsSearching
                ? $"Showing {shown} of {shown + hidden}. Switch to the list to see them all."
                : $"Showing {shown} of {shown + hidden}. Keep typing to narrow it.";

        Raise(nameof(ResultNote));
    }

    private int CountAdmitted(List<ContentIndexEntry> rows)
    {
        int count = 0;
        foreach (ContentIndexEntry row in rows)
        {
            if (Admits(row.Kind)) count++;
        }

        return count;
    }

    private bool Admits(ContentKind kind) => _filter switch
    {
        // Folders survive every filter, or a filtered view is a dead end.
        _ when kind == ContentKind.Folder => !IsSearching,

        ContentFilter.All => true,
        ContentFilter.Textures => kind == ContentKind.Texture,
        ContentFilter.Materials => kind == ContentKind.Material,
        ContentFilter.Models => kind == ContentKind.Model,
        _ => true,
    };

    // A name match outranks a path match. Same rule as AssetCatalog.Search.
    private List<ContentIndexEntry> Search()
    {
        List<(ContentIndexEntry Row, int Score)> matches = [];

        foreach (ContentIndexEntry row in _index.Entries)
        {
            if (row.Kind == ContentKind.Folder) continue;

            int score = CommandScore.Of(row.Name, _query);
            if (score == CommandScore.NoMatch)
            {
                int path = CommandScore.Of(row.ContentPath, _query);
                if (path == CommandScore.NoMatch) continue;
                score = path - 8;
            }

            matches.Add((row, score));
        }

        matches.Sort(static (a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            return byScore != 0
                ? byScore
                : string.CompareOrdinal(a.Row.ContentPath, b.Row.ContentPath);
        });

        List<ContentIndexEntry> rows = [];
        foreach ((ContentIndexEntry row, _) in matches)
        {
            if (rows.Count >= MaxSearchResults) break;
            rows.Add(row);
        }

        return rows;
    }

    private string FolderLabelFor(ContentIndexEntry row)
    {
        // Only for a search; in a folder view the breadcrumb already says it.
        if (!IsSearching || row.ContentPath.Length == 0) return string.Empty;

        int slash = row.ContentPath.LastIndexOf('/');
        return slash > 0 ? row.ContentPath[..slash] : "Assets";
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();

        if (_root is null) return;

        Breadcrumbs.Add(new BreadcrumbSegment("Assets", _root));

        if (!_currentPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) return;

        string rest = _currentPath[_root.Length..].Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (rest.Length == 0) return;

        string walked = _root;
        foreach (string part in rest.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            walked = Path.Combine(walked, part);
            Breadcrumbs.Add(new BreadcrumbSegment(part, walked));
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 0 => string.Empty,
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };

    private async Task LoadThumbnailAsync(ContentEntry entry, int generation)
    {
        try
        {
            // DecodeToWidth: a full decode of a 4K texture is 32 MB per tile.
            Bitmap bitmap = await Task.Run(() =>
            {
                using FileStream stream = File.OpenRead(entry.FullPath);
                return Bitmap.DecodeToWidth(stream, ThumbnailWidth);
            }).ConfigureAwait(true);

            if (Volatile.Read(ref _generation) != generation)
            {
                bitmap.Dispose();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => entry.Thumbnail = bitmap);
        }
        catch (Exception ex)
        {
            // Not an image, truncated or locked. The row keeps its kind glyph.
            _logger.LogDebug(ex, "No preview for {Path}", entry.FullPath);
        }
    }

    /// <summary>The decoded width of a preview, in pixels.</summary>
    public const int ThumbnailWidth = 96;

}
