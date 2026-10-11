using System.Collections.Generic;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>One program to start, and what to hand it.</summary>
/// <param name="FileName">The program, or with <paramref name="UseShell"/> the file or folder to open.</param>
/// <param name="Arguments">The arguments, one per entry. Quoting is the starter's job.</param>
/// <param name="UseShell">Whether the system picks the program from the file's type.</param>
/// <param name="VerbatimArguments">
/// The whole argument text as the program wants it, for one that reads its
/// command line by its own rules. Used in place of <paramref name="Arguments"/>.
/// </param>
public sealed record ProgramStart(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool UseShell = false,
    string? VerbatimArguments = null);
