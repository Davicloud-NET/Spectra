using System;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Cache;

/// <summary>Temporary immutable payload storage for a cook without a persistent cache.</summary>
internal sealed class PayloadSpool : IDisposable
{
    private readonly string? _temporaryRoot;
    internal ContentStore Store { get; }
    internal PayloadSpool(ContentStore? persistent)
    {
        if (persistent is not null) { Store = persistent; return; }
        _temporaryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SpectraCook-" + Guid.NewGuid().ToString("N")));
        Store = new ContentStore(_temporaryRoot);
    }
    public void Dispose()
    {
        if (_temporaryRoot is null || !Directory.Exists(_temporaryRoot)) return;
        string prefix = _temporaryRoot + Path.DirectorySeparatorChar;
        // Only this session's generated tree is eligible; no recursive deletion
        // is issued against an arbitrary output or project directory.
        foreach (string file in Directory.EnumerateFiles(_temporaryRoot, "*", SearchOption.AllDirectories))
        {
            if (!Path.GetFullPath(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Spool path escaped its session directory.");
            File.Delete(file);
        }
        foreach (string directory in Directory.EnumerateDirectories(_temporaryRoot, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            Directory.Delete(directory);
        Directory.Delete(_temporaryRoot);
    }
}
