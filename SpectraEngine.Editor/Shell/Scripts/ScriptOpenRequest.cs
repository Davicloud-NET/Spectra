namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>One script somebody asked to open in a code editor.</summary>
/// <param name="FilePath">The file's full path on disk.</param>
/// <param name="ProjectFolder">
/// The project the file belongs to, or null. A code editor opened on it sees
/// every script of the project, which its language server needs.
/// </param>
/// <param name="Line">The line to put the caret on, counted from 1, or null for the top.</param>
public sealed record ScriptOpenRequest(string FilePath, string? ProjectFolder, int? Line = null);
