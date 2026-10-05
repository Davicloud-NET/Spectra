using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Shell;

/// <summary>One file or folder the index knows about.</summary>
/// <param name="ContentPath">Normalized content-relative path, the engine's name for the file.</param>
/// <param name="Folder">Absolute path of the containing folder.</param>
/// <param name="Bytes">Size on disk, or -1 for a folder.</param>
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
/// The folder view and the project-wide search both read it.
/// </summary>
// Walks run off the UI thread and publish whole; a walk that lands after the
// root changed is dropped by its generation.
public sealed class ContentIndex : ObservableObject
{
    /// <summary>How long a burst of file events is gathered before a rebuild.</summary>
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

    /// <summary>Everything under the root.</summary>
    public IReadOnlyList<ContentIndexEntry> Entries => _entries;

    /// <summary>The folder being indexed, or null.</summary>
    public string? Root => _root;

    /// <summary>
    /// Whether changes to the files are picked up as they happen. Off, the
    /// index changes only on <see cref="Refresh"/>.
    /// </summary>
    // A test turns it off. Windows reports a folder as changed a moment after
    // files were written into it, and the walk that follows rebuilds the rows
    // a test is pressing.
    public bool WatchesFiles { get; init; } = true;

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

    /// <summary>The walk in flight, or a completed task. Awaitable form of <see cref="IsWalking"/>.</summary>
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

    private static int CompareForListing(ContentIndexEntry a, ContentIndexEntry b)
    {
        bool aFolder = a.Kind == ContentKind.Folder;
        bool bFolder = b.Kind == ContentKind.Folder;

        if (aFolder != bFolder) return aFolder ? -1 : 1;

        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Applies one filesystem change without a rewalk. Public so tests can drive it.</summary>
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
            _logger.LogWarning(ex, "Could not index {Root}", root);
            warning = "Could not read this project's Assets folder.";
        }

        // Stale walk: the root changed while it ran.
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

    // Null when the engine cannot name the path (a file outside the root).
    // The content path uses ContentDragPayload's rule so a drag gives the same string.
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
            // Vanished between the listing and the stat.
            return null;
        }
    }

    private void StartWatching()
    {
        if (!WatchesFiles || string.IsNullOrEmpty(_root) || !Directory.Exists(_root)) return;

        try
        {
            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                    NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            // Events arrive on a worker thread; each only restarts the timer on the UI thread.
            _watcher.Created += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.Error += OnWatcherError;

            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Network share, or a platform with no watcher support.
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
                // Buffer overflow drops events, so the index is stale: rewalk.
                _logger.LogWarning(e.GetException(), "Content watcher overflowed");
                Warning = "Content index rebuilt after a watcher overflow.";
                Rewalk();
            },
            DispatcherPriority.Background);
}
