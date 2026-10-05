using SpectraEngine.Core.Assets.Sources;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Bsp.Tests;

// Content held in memory whose opens wait until the test lets them through,
// as a source that cooks a file on first use holds its caller. It remembers
// which thread asked and what it handed out.
internal sealed class GatedContentSource : IContentSource, IDisposable
{
    // Long enough for a slow machine, short enough that a caller stuck on the
    // test's own thread fails the test and does not hang the suite.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly object _lock = new();
    private readonly Dictionary<string, byte[]> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ContentBlob> _handedOut = [];
    private readonly ManualResetEventSlim _gate = new(initialState: false);
    private readonly ManualResetEventSlim _asked = new(initialState: false);
    private int _openCount;
    private int _askingThread;

    // Above the rig's folder of loose files.
    public int Priority => 10;

    public int OpenCount => Volatile.Read(ref _openCount);

    // The managed thread the last open came from, or 0 when none has.
    public int AskingThread => Volatile.Read(ref _askingThread);

    public void Add(string path, byte[] bytes)
    {
        lock (_lock) _entries[path] = bytes;
    }

    // Lets every open through, the waiting ones and the later ones.
    public void Open() => _gate.Set();

    // Whether an open has arrived, waiting for one if need be.
    public bool WaitUntilAsked() => _asked.Wait(Patience);

    // Whether every blob handed out has been disposed by whoever took it.
    public bool EverythingWasReleased()
    {
        lock (_lock) return _handedOut.TrueForAll(IsDisposed);
    }

    public bool TryOpen(string path, [NotNullWhen(true)] out ContentBlob? blob)
    {
        byte[]? bytes;
        lock (_lock)
        {
            if (!_entries.TryGetValue(path, out bytes))
            {
                blob = null;
                return false;
            }
        }

        Interlocked.Increment(ref _openCount);
        Volatile.Write(ref _askingThread, Environment.CurrentManagedThreadId);
        _asked.Set();
        _gate.Wait(Patience);

        blob = ContentBlob.CopyOf(bytes);
        lock (_lock) _handedOut.Add(blob);
        return true;
    }

    public bool Exists(string path)
    {
        lock (_lock) return _entries.ContainsKey(path);
    }

    public bool TryGetWatchPath(string path, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;
        return false;
    }

    public void TryEnumerate(string prefix, string extension, List<string> results)
    {
    }

    public void Dispose()
    {
        _gate.Set();
        _gate.Dispose();
        _asked.Dispose();
    }

    public override string ToString() => "gated source";

    private static bool IsDisposed(ContentBlob blob)
    {
        try
        {
            _ = blob.Span.Length;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}
