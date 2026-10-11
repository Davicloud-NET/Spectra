namespace SpectraEngine.Core.ConsoleSystem;

/// <summary>A file and a line in it that a console line points at.</summary>
/// <param name="File">The file, as a content path.</param>
/// <param name="Line">The line in it, counted from 1, or 0 when only the file is known.</param>
/// <param name="Function">The function that was running there, or null when it has no name or is not known.</param>
public readonly record struct ConsolePlace(string File, int Line, string? Function = null)
{
    /// <summary>The file and the line as <c>Scripts/door.luau:12</c>, or the file alone.</summary>
    public string Position => Line > 0 ? $"{File}:{Line}" : File;

    /// <summary>The position, with <c>in open</c> after it when the function is known.</summary>
    public string Described => Function is null ? Position : $"{Position} in {Function}";
}
