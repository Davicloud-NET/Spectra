using System;
using System.IO;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>Looks at this computer for what could open a script.</summary>
// Read again for every open: a code editor installed while this one runs is
// found without a restart.
public static class ScriptEditorSurvey
{
    /// <summary>What is here now. Reads the environment and the disk.</summary>
    public static ScriptEditorFacts Take() => new(
        VisualStudioCodeLocator.Find(
            Environment.GetEnvironmentVariable, File.Exists, OperatingSystem.IsWindows()),
        HasDefaultProgram(),
        FileManager());

    // Only Windows can be asked. Elsewhere the start is tried, and a failure
    // falls through to the file manager.
    private static bool HasDefaultProgram() =>
        !OperatingSystem.IsWindows() || FileAssociations.HasProgramFor(ContentClassifier.ScriptExtension);

    private static FileManagerKind FileManager()
    {
        if (OperatingSystem.IsWindows()) return FileManagerKind.Explorer;
        if (OperatingSystem.IsMacOS()) return FileManagerKind.Finder;
        return FileManagerKind.FolderOpener;
    }
}
