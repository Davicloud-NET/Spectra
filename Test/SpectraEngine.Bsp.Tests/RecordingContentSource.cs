using SpectraEngine.Core.Assets.Sources;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

// Content held in memory that records what it was asked for.
// Locked: the lookup under test is asked from several threads at once.
internal sealed class RecordingContentSource : IContentSource
{
    private readonly object _lock = new();
    private readonly Dictionary<string, byte[]> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _refusals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _opened = [];
    private readonly List<string> _probed = [];

    public int Priority => 0;

    public IReadOnlyList<string> Opened
    {
        get { lock (_lock) return _opened.ToArray(); }
    }

    public IReadOnlyList<string> Probed
    {
        get { lock (_lock) return _probed.ToArray(); }
    }

    public void Add(string path, string text) => Add(path, Encoding.UTF8.GetBytes(text));

    public void Add(string path, byte[] bytes)
    {
        lock (_lock) _entries[path] = bytes;
    }

    // The path exists, and opening it throws as a source that cooks on demand may.
    public void Refuse(string path, string reason)
    {
        lock (_lock)
        {
            _entries[path] = [];
            _refusals[path] = reason;
        }
    }

    public bool TryOpen(string path, [NotNullWhen(true)] out ContentBlob? blob)
    {
        lock (_lock)
        {
            _opened.Add(path);

            if (_refusals.TryGetValue(path, out string? reason))
                throw new InvalidDataException(reason);

            if (!_entries.TryGetValue(path, out byte[]? bytes))
            {
                blob = null;
                return false;
            }

            blob = ContentBlob.CopyOf(bytes);
            return true;
        }
    }

    public bool Exists(string path)
    {
        lock (_lock)
        {
            _probed.Add(path);
            return _entries.ContainsKey(path);
        }
    }

    public bool TryGetWatchPath(string path, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;
        return false;
    }

    public void TryEnumerate(string prefix, string extension, List<string> results)
    {
        lock (_lock)
        {
            foreach (string path in _entries.Keys)
            {
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (extension.Length > 0 && !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;

                results.Add(path);
            }
        }
    }

    public override string ToString() => "recording source";
}
