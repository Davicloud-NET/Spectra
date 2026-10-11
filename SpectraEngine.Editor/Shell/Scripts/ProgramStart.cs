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
/// <param name="NoWindow">Whether a console program starts with no console window of its own.</param>
/// <param name="ChecksExitCode">
/// Whether the program is waited on for a moment and an exit code other than
/// 0 counts as not started. For a program that only starts another.
/// </param>
public sealed record ProgramStart(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool UseShell = false,
    string? VerbatimArguments = null,
    bool NoWindow = false,
    bool ChecksExitCode = false);
