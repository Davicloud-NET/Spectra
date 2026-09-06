using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Shell;

/// <summary>One file the index knows about.</summary>
/// <param name="FullPath">Where it is on this machine.</param>
/// <param name="ContentPath">
/// The normalized content-relative path, which is the name the engine knows it
/// by: what a material writes down, what a map records, what the pack hashes its
/// id from.
/// </param>
/// <param name="Name">The file name with its extension, for reading.</param>
/// <param name="Folder">Its folder's absolute path, for the folder view.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Bytes">Its size on disk, or -1 for a folder.</param>
/// <param name="MtimeTicks">When it last changed, for the thumbnail cache key.</param>
public sealed record ContentIndexEntry(
    string FullPath,
    string ContentPath,
    string Name,
    string Folder,
    ContentKind Kind,
    long Bytes,
    long MtimeTicks);

/// <summary>
/// Every file under a project's assets folder, walked once and kept current.
/// </summary>
/// <remarks>
/// <para>
/// <b>ONE reader, two views.</b> The folder view and the project-wide search
/// both read this. Enumerating the directory for one and indexing for the other
/// is two readers that disagree the first time somebody renames a file: the
/// folder shows the new name because it just listed, the search shows the old
/// one because nothing told it, and neither reports a problem.
/// </para>
/// <para>
/// <b>The walk is off the UI thread and the result is published whole.</b> A
/// project with a few thousand assets takes long enough to be felt, and a
/// collection mutated from a worker is a crash in a list control rather than a
/// race somebody notices. Every walk carries a generation, so one that lands
/// after the root has changed is dropped - the same ticket shape the asset
/// manager's decode queue uses.
/// </para>
/// <para>
/// <b>A watcher can overflow, and this one says so.</b> A burst larger than the
/// internal buffer raises <see cref="FileSystemWatcher.Error"/> and drops
/// events, which for an index means it is silently wrong from then on. That is
/// answered with a full rewalk and a warning, because an index nobody knows is
/// stale is worse than a refresh button.
/// </para>
/// </remarks>
public sealed class ContentIndex : ObservableObject
{
    /// <summary>How long a burst of file events is gathered before a rebuild.</summary>
    /// <remarks>
    /// A save from most editors is several events (a temp file, a rename, an
    /// attribute change), and a copy of a folder is hundreds. Rebuilding per
    /// event would rebuild a project's index a hundred times for one paste.
    /// </remarks>
    public const int CoalesceMilliseconds = 250;

    private readonly ILogger _logger;
    private readonly DispatcherTimer _coalesce;
    private readonly List<ContentIndexEntry> _entries = [];

    private FileSystemWatcher? _watcher;
    private string? _root;
    private int _generation;
    private bool _walking;
    private string? _warning;

    /// <summary>Builds an index with nothing in it.</summary>
    public ContentIndex(ILogger logger)
    {
        _logger = logger;

        _coalesce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(CoalesceMilliseconds),
        };
        _coalesce.Tick += (_, _) =>
        {
            _coalesce.Stop();
            Rewalk();
        };
    }

    /// <summary>Everything under the root. Replaced whole; never mutated in place.</summary>
    public IReadOnlyList<ContentIndexEntry> Entries => _entries;

    /// <summary>The folder being indexed, or null.</summary>
    public string? Root => _root;

    /// <summary>Whether a walk is running.</summary>
    public bool IsWalking
    {
        get => _walking;
        private set => Set(ref _walking, value);
    }

    /// <summary>How many files are indexed.</summary>
    public int Count => _entries.Count;

    /// <summary>Why the index may be wrong, or null.</summary>
    public string? Warning
    {
        get => _warning;
        private set => Set(ref _warning, value);
    }

    /// <summary>Raised on the UI thread when the entries have been replaced.</summary>
    public event Action? Changed;

    /// <summary>
    /// The walk in flight, or a completed task.
    /// </summary>
    /// <remarks>
    /// <b>Published rather than kept private, because "the index is still
    /// walking" is a real state with a real consumer.</b> The footer says
    /// "Indexing..." from <see cref="IsWalking"/>; this is the same fact in the
    /// form something can wait on, which is what a test needs to assert about a
    /// walk rather than about a seeded list.
    /// </remarks>
    public Task Walking { get; private set; } = Task.CompletedTask;

    /// <summary>Points the index at a project's assets folder, or at nothing.</summary>
    public void SetRoot(string? assetsRoot)
    {
        _root = assetsRoot;
        Warning = null;

        StopWatching();
        Rewalk();
        StartWatching();
    }

    /// <summary>Walks the root again, discarding whatever is in flight.</summary>
    public void Refresh()
    {
        Warning = null;
        Rewalk();
    }

    /// <summary>The files directly inside one folder, folders first then by name.</summary>
    public List<ContentIndexEntry> InFolder(string folderFullPath)
    {
        List<ContentIndexEntry> rows = [];

        foreach (ContentIndexEntry entry in _entries)
        {
            if (string.Equals(entry.Folder, folderFullPath, StringComparison.OrdinalIgnoreCase))
                rows.Add(entry);
        }

        rows.Sort(CompareForListing);
        return rows;
    }

    /// <summary>Folders first, then files, each alphabetically.</summary>
    /// <remarks>
    /// Not by date and not by kind: somebody looking for a file knows its name,
    /// and any other order means hunting.
    /// </remarks>
    private static int CompareForListing(ContentIndexEntry a, ContentIndexEntry b)
    {
        bool aFolder = a.Kind == ContentKind.Folder;
        bool bFolder = b.Kind == ContentKind.Folder;

        if (aFolder != bFolder) return aFolder ? -1 : 1;

        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies one filesystem change without a rewalk.
    /// </summary>
    /// <remarks>
    /// <b>Public so a test can drive it, the same reason
    /// <c>AssetManager.NotifyFileChanged</c> is.</b> A real watcher needs a real
    /// filesystem, real timing and a real dispatcher, none of which a test can
    /// depend on; what can be tested is that a create appears, a rename moves
    /// rather than duplicates, and a delete disappears.
    /// </remarks>
    public void ApplyChange(WatcherChangeTypes kind, string fullPath, string? oldFullPath)
    {
        if (_root is null) return;

        if (kind.HasFlag(WatcherChangeTypes.Renamed) && oldFullPath is not null)
            Remove(oldFullPath);

        if (kind.HasFlag(WatcherChangeTypes.Deleted))
        {
            Remove(fullPath);
            Changed?.Invoke();
            Raise(nameof(Count));
            return;
        }

        Remove(fullPath);

        if (Describe(fullPath, _root) is { } entry)
            _entries.Add(entry);

        Changed?.Invoke();
        Raise(nameof(Count));
    }

    private void Remove(string fullPath)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_entries[i].FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                _entries.RemoveAt(i);
        }
    }

    private void Rewalk()
    {
        int generation = ++_generation;
        string? root = _root;

        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            _entries.Clear();
            IsWalking = false;
            Walking = Task.CompletedTask;
            Raise(nameof(Count));
            Changed?.Invoke();
            return;
        }

        IsWalking = true;
        Walking = WalkAsync(root, generation);
    }

    private async Task WalkAsync(string root, int generation)
    {
        List<ContentIndexEntry>? walked = null;
        string? warning = null;

        try
        {
            walked = await Task.Run(() => Walk(root)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder the editor cannot read is a report rather than a crash:
            // the browser is a convenience and the project still opens.
            _logger.LogWarning(ex, "Could not index {Root}", root);
            warning = "Could not read this project's Assets folder.";
        }

        // A walk that lands after the root moved on describes a project the user
        // has already left.
        if (Volatile.Read(ref _generation) != generation) return;

        if (walked is not null)
        {
            _entries.Clear();
            _entries.AddRange(walked);
        }

        if (warning is not null) Warning = warning;

        IsWalking = false;
        Raise(nameof(Count));
        Changed?.Invoke();
    }

    private static List<ContentIndexEntry> Walk(string root)
    {
        List<ContentIndexEntry> found = [];

        foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            if (Describe(dir, root) is { } entry) found.Add(entry);
        }

        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (Describe(file, root) is { } entry) found.Add(entry);
        }

        return found;
    }

    /// <summary>
    /// Turns a path into an entry, or null when the engine cannot name it.
    /// </summary>
    /// <remarks>
    /// The content path comes from <see cref="ContentDragPayload"/>'s own rule,
    /// so an entry here and a drag from the browser produce the same string. A
    /// file outside the root cannot be named at all and is dropped rather than
    /// carried as a path nothing will resolve.
    /// </remarks>
    private static ContentIndexEntry? Describe(string fullPath, string root)
    {
        try
        {
            bool isFolder = Directory.Exists(fullPath);
            ContentKind kind = isFolder ? ContentKind.Folder : ContentClassifier.Classify(fullPath);

            string content;
            long bytes = -1;
            long ticks = 0;

            if (isFolder)
            {
                content = string.Empty;
                ticks = Directory.GetLastWriteTimeUtc(fullPath).Ticks;
            }
            else
            {
                if (!ContentDragPayload.TryCreate(root, fullPath, kind, out ContentDragPayload? payload))
                    return null;

                content = payload.ContentPath;

                var info = new FileInfo(fullPath);
                if (!info.Exists) return null;

                bytes = info.Length;
                ticks = info.LastWriteTimeUtc.Ticks;
            }

            return new ContentIndexEntry(
                fullPath,
                content,
                Path.GetFileName(fullPath),
                Path.GetDirectoryName(fullPath) ?? root,
                kind,
                bytes,
                ticks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file that vanished between the listing and the stat. Dropping it
            // is right: the watcher's delete event is already on its way.
            return null;
        }
    }

    private void StartWatching()
    {
        if (string.IsNullOrEmpty(_root) || !Directory.Exists(_root)) return;

        try
        {
            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            // Every event lands on a worker thread, so each one only asks the UI
            // thread to restart the coalescing timer: the rebuild itself runs
            // once, there, after the burst.
            _watcher.Created += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.Error += OnWatcherError;

            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A root on a network share or a platform with no watcher support.
            // The browser still works; it just needs its refresh button.
            _logger.LogWarning(ex, "No file watcher for {Root}", _root);
            Warning = "This folder is not being watched. Use Refresh after changing files.";
            _watcher = null;
        }
    }

    private void StopWatching()
    {
        _coalesce.Stop();

        if (_watcher is not { } watcher) return;

        _watcher = null;
        watcher.EnableRaisingEvents = false;
        watcher.Created -= OnFileEvent;
        watcher.Deleted -= OnFileEvent;
        watcher.Changed -= OnFileEvent;
        watcher.Renamed -= OnFileEvent;
        watcher.Error -= OnWatcherError;
        watcher.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) =>
        Dispatcher.UIThread.Post(RestartCoalesce, DispatcherPriority.Background);

    private void RestartCoalesce()
    {
        _coalesce.Stop();
        _coalesce.Start();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                // The buffer overflowed and events were dropped, so the index is
                // wrong from here and nothing else would ever say so.
                _logger.LogWarning(e.GetException(), "Content watcher overflowed");
                Warning = "Content index rebuilt after a watcher overflow.";
                Rewalk();
            },
            DispatcherPriority.Background);
}
