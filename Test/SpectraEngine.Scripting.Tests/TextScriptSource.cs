using System.Text;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// Script files that are text a test handed in, by content path.
internal sealed class TextScriptSource : IScriptSource
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unreadable = new(StringComparer.Ordinal);

    // A path finds its file whatever its case, as in a pack or a Windows
    // folder. Left off, case counts, as in a Linux folder.
    public bool IgnoresCase { get; set; }

    public void Add(string path, string text) => _files[path] = Encoding.UTF8.GetBytes(text);

    // As a Windows editor saves a file, with a byte order mark in front.
    public void AddWithMark(string path, string text) =>
        _files[path] = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(text)];

    // A file that is there and cannot be read as a script, with the reason.
    public void AddUnreadable(string path, string why) => _unreadable[path] = why;

    public byte[]? Read(string path, out string? problem)
    {
        if (IgnoresCase)
            path = _files.Keys.FirstOrDefault(known => known.Equals(path, StringComparison.OrdinalIgnoreCase)) ?? path;

        problem = _unreadable.GetValueOrDefault(path);
        return problem is null ? _files.GetValueOrDefault(path) : null;
    }
}
