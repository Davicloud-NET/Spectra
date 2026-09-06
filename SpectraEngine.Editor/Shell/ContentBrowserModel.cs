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
    /// <summary>A directory. Double-click descends.</summary>
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

    /// <summary>
    /// The decoded preview, once a background decode has landed. Null until
    /// then, and null forever for anything that is not an image.
    /// </summary>
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

    /// <summary>The file's own name without its extension.</summary>
    public string Stem => System.IO.Path.GetFileNameWithoutExtension(Name);

    /// <summary>
    /// The content-relative path, or empty for a folder.
    /// </summary>
    /// <remarks>
    /// Carried on the row rather than derived where it is needed, because the
    /// browser is the only thing that knows the root and a second derivation
    /// somewhere else is a fifth spelling of asset identity.
    /// </remarks>
    public string ContentPath { get; init; } = string.Empty;

    /// <summary>
    /// Where this file is, content-relative, shown beside a search result.
    /// </summary>
    /// <remarks>
    /// A search is flat and crosses the whole project, so two files called
    /// "brick" are one row apart with nothing to tell them apart but this.
    /// </remarks>
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

    /// <summary>
    /// The glyph for this kind, pulled from the theme dictionary.
    /// </summary>
    /// <remarks>
    /// <b>Resolved here rather than by a selector or a converter</b>, because
    /// the alternative is a DataTemplate per kind - six near-identical copies of
    /// one tile - or a value converter class per lookup, which is what this
    /// codebase already refuses elsewhere for exactly this reason. A null
    /// resource simply draws nothing, and the name beneath still says what the
    /// file is.
    /// </remarks>
    public Geometry? Icon => Resource<Geometry>(Kind switch
    {
        ContentKind.Folder => "IconOpenFolder",
        ContentKind.Texture => "IconMesh",
        ContentKind.Material => "IconBrushPart",
        ContentKind.Model => "IconMesh",
        ContentKind.Shader => "IconBrushWorld",
        _ => "IconEmpty",
    });

    /// <summary>The kind's tint, from the same palette the scene tree uses.</summary>
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

/// <summary>
/// The project's <c>Assets/</c> folder, browsed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The headline absence.</b> Nothing in the shell showed a texture, a
/// material or a model, and there was no milestone for one anywhere in the
/// roadmap either - a hole in the plan rather than only in the window. An
/// editor whose content root is real files on disk and which cannot show you
/// those files is asking you to keep a file manager open beside it.
/// </para>
/// <para>
/// <b>It reads the filesystem directly, and the shell decodes the
/// thumbnails.</b> Not through <c>AssetManager</c>: that is the render thread's,
/// its caches are keyed for rendering, and asking it for a preview would create
/// a GPU texture for a picture the user is only looking at. An Avalonia
/// <c>Bitmap</c> off a background thread costs nothing the engine can see.
/// </para>
/// <para>
/// <b>Every listing is a snapshot, and it says when it was taken.</b> There is
/// no file watcher here: one per folder is the shape the texture hot-reload
/// already uses and it is the right eventual answer, but a browser that
/// silently showed a stale folder would be worse than one with a refresh
/// button, which is what this has.
/// </para>
/// <para>UI thread, except the decode.</para>
/// </remarks>
public sealed class ContentBrowserModel : ObservableObject
{
    /// <summary>How many search results one query shows.</summary>
    /// <remarks>
    /// A cap rather than the whole project, because a texture folder is
    /// unbounded and a list nobody can reach the end of is a search box with
    /// extra scrolling. The footer says how many were hidden, because a capped
    /// list with no count looks exactly like a complete one.
    /// </remarks>
    public const int MaxSearchResults = 200;

    /// <summary>How many tiles the grid draws before it asks for the list.</summary>
    /// <remarks>
    /// <b>The grid does not virtualise and the list does.</b> Stock Avalonia has
    /// no virtualising wrap panel, so a folder of four thousand textures would
    /// realise four thousand tiles; the cap is what keeps that from happening,
    /// and the footer names the way out rather than silently truncating.
    /// </remarks>
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

    // Bumped on every navigation, so a decode that lands after the user has
    // moved on is dropped rather than writing a thumbnail into a row that is
    // no longer on screen. Same shape as the asset manager's per-asset
    // sequence ticket, and for the same reason.
    private int _generation;

    public ContentBrowserModel(ILogger logger)
    {
        _logger = logger;
        _index = new ContentIndex(logger);

        // One reader, two views: the folder listing and the search both come off
        // the index, so a rename cannot show up in one and not the other.
        _index.Changed += Relist;
        _index.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ContentIndex.IsWalking) or nameof(ContentIndex.Warning))
                Raise(nameof(ResultNote));
        };
    }

    /// <summary>The index behind both views, for the picker that shares it.</summary>
    public ContentIndex Index => _index;

    /// <summary>What is being searched for, across the whole project.</summary>
    /// <remarks>
    /// <b>A query replaces the folder view rather than filtering it.</b> Somebody
    /// typing "brick" is asking where the bricks are, not which of the files in
    /// this one folder is called brick; a filter over the current folder answers
    /// a question nobody asked and reports nothing when the file is one level up.
    /// </remarks>
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

    /// <summary>Tiles or dense rows.</summary>
    /// <remarks>
    /// <b>A grid for pictures and a list for everything else, and the choice is
    /// the user's.</b> A texture's picture IS the information, which is the one
    /// case where a tile beats a row; a folder of shaders in a grid is a wall of
    /// identical glyphs. The list also virtualises, so it is the mode for a big
    /// folder whatever is in it.
    /// </remarks>
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

    public bool IsGridView => _viewMode == ContentViewMode.Grid;
    public bool IsListView => _viewMode == ContentViewMode.List;

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

    /// <summary>
    /// Turns one browsed entry into a drag payload, or refuses it.
    /// </summary>
    /// <remarks>
    /// <b>Here rather than in the panel, because the ROOT lives here.</b> The
    /// conversion from an absolute path to the engine's own content-relative
    /// identity needs the root the browser was pointed at, and a panel that
    /// reached for it would be the second place that knows where a project's
    /// assets are.
    /// </remarks>
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

        // BEFORE the index is pointed anywhere, and the order is the whole of
        // it. This call fills the pane in the meantime, so it is never blank
        // with no message; the walk publishes its own listing when it lands. In
        // the other order the two overlap - the walk can finish while this one
        // is still adding rows - and two writers on one ObservableCollection is
        // an IndexOutOfRangeException from inside a list control rather than
        // anything that names itself.
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
    /// Navigates to the folder holding a content-relative path and selects it.
    /// </summary>
    /// <remarks>
    /// How "Reveal in Content" works, and how a picker sends somebody to the
    /// file they just assigned. It clears the query, because a reveal into a
    /// filtered list would show the file and hide its neighbours.
    /// </remarks>
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
    /// One line about the selection: what it is, where it is and how big.
    /// </summary>
    /// <remarks>
    /// <b>The content-relative path, not the absolute one.</b> That string is
    /// what a material writes down, what a map records and what the pack hashes
    /// its id from, so it is the name this file HAS as far as the engine is
    /// concerned. The absolute path is a fact about this machine.
    /// </remarks>
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
    /// textures. Empty until the read lands, and empty when it cannot.
    /// </summary>
    /// <remarks>
    /// <b>Read in the background and never by decoding.</b> A texture's
    /// dimensions are in the first bytes of its header (<see cref="ImageHeader"/>)
    /// and a material is a small text file, so this is two open-and-read calls;
    /// decoding a 4K image to learn two numbers would cost 32 MB for a line of
    /// text. A read that lands after the selection moved on is dropped by
    /// generation, the same ticket the thumbnails use.
    /// </remarks>
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

            // The parser is forward-compatible by design: an unknown key warns
            // rather than throwing, and that warning is invisible everywhere
            // else in the shell. Saying it here is the whole reason a details
            // strip is worth having for a material.
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

    /// <summary>
    /// Rebuilds the list from the index: this folder, or the whole project when
    /// there is a query.
    /// </summary>
    /// <remarks>
    /// <b>The collection is rebuilt rather than patched, and that is a
    /// deliberate difference from the scene tree.</b> The tree is patched
    /// because it holds a user's expansion state and scroll position across
    /// thousands of rows that mostly do not change; a content listing changes
    /// wholesale on every navigation and every keystroke of a query, so a diff
    /// would be a diff of two unrelated lists.
    /// </remarks>
    private void Relist()
    {
        // Every in-flight decode is now stale.
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
        // A folder is navigation rather than content, so it survives every
        // filter: hiding it would make a filtered view a dead end.
        _ when kind == ContentKind.Folder => !IsSearching,

        ContentFilter.All => true,
        ContentFilter.Textures => kind == ContentKind.Texture,
        ContentFilter.Materials => kind == ContentKind.Material,
        ContentFilter.Models => kind == ContentKind.Model,
        _ => true,
    };

    /// <summary>
    /// The best matches across the whole project, ranked.
    /// </summary>
    /// <remarks>
    /// <b>A name match outranks a path match by a fixed margin.</b> Somebody
    /// typing "brick" means the file called brick, not every file in a folder
    /// that happens to contain those letters; but a path match still beats no
    /// match, because folders are how people organise. The same rule the asset
    /// picker uses, and the same scorer, so a file found in one is found in the
    /// other.
    /// </remarks>
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
        // Only a search needs it: in a folder view every row shares the folder
        // the breadcrumb already names, and repeating it on each tile is noise.
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
            // DecodeToWidth rather than a full decode: a 4K texture decoded at
            // full size costs 32 MB of managed memory to draw at 72 pixels, and
            // a folder of them is how a content browser becomes the reason an
            // editor runs out of memory.
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
            // A file that is not really an image, a truncated download, a lock.
            // The row keeps its kind glyph, which is a correct answer.
            _logger.LogDebug(ex, "No preview for {Path}", entry.FullPath);
        }
    }

    /// <summary>The decoded width of a preview, in pixels.</summary>
    public const int ThumbnailWidth = 96;

}
