namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>What this computer has that could open a script.</summary>
/// <param name="VisualStudioCode">Where Visual Studio Code is, or null when it was not found.</param>
/// <param name="HasDefaultProgram">Whether the system has a program set for script files.</param>
/// <param name="FileManager">How this system shows a file in its folder.</param>
public readonly record struct ScriptEditorFacts(
    string? VisualStudioCode,
    bool HasDefaultProgram,
    FileManagerKind FileManager);
