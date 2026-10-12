using System.Text;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// Script files that are text a test handed in, by content path.
internal sealed class TextScriptSource : IScriptSource
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unreadable = new(StringComparer.Ordinal);

    public void Add(string path, string text) => _files[path] = Encoding.UTF8.GetBytes(text);

    // As a Windows editor saves a file, with a byte order mark in front.
    public void AddWithMark(string path, string text) =>
        _files[path] = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(text)];

    // A file that is there and cannot be read as a script, with the reason.
    public void AddUnreadable(string path, string why) => _unreadable[path] = why;

    public byte[]? Read(string path, out string? problem)
    {
        problem = _unreadable.GetValueOrDefault(path);
        return problem is null ? _files.GetValueOrDefault(path) : null;
    }
}
