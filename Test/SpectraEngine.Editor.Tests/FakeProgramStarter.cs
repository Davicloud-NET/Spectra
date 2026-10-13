using SpectraEngine.Editor.Shell.Scripts;

namespace SpectraEngine.Editor.Tests;

// Starts nothing. Keeps what it was asked to start, and fails the programs a
// test names, the way a program that is there and broken does.
internal sealed class FakeProgramStarter : IProgramStarter
{
    private readonly Dictionary<string, string> _broken = new(StringComparer.Ordinal);

    public List<ProgramStart> Asked { get; } = [];

    // Called at the moment a program would start, to look at what is there by then.
    public Action<ProgramStart>? OnAsked { get; set; }

    public void Break(string fileName, string why) => _broken[fileName] = why;

    public bool TryStart(ProgramStart start, out string failure)
    {
        Asked.Add(start);
        OnAsked?.Invoke(start);

        if (_broken.TryGetValue(start.FileName, out string? why))
        {
            failure = why;
            return false;
        }

        failure = string.Empty;
        return true;
    }
}
