using SpectraEngine.Core.Scripting.Tooling;
using System;

namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>
/// Sets a project's folder up for a code editor: the definitions file of
/// this build's script API, and the files that point luau-lsp at it.
/// </summary>
// Reads and writes the disk, so it is called off the UI thread.
public sealed class ScriptEditorSetup
{
    private readonly Func<string>? _definitions;

    /// <summary>Creates a setup.</summary>
    /// <param name="definitions">The definitions file's text, asked for when a project is set up.</param>
    public ScriptEditorSetup(Func<string> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions;
    }

    private ScriptEditorSetup()
    {
    }

    /// <summary>A setup that writes nothing, for a launcher with no project on disk.</summary>
    public static ScriptEditorSetup None { get; } = new();

    /// <summary>A setup that writes the script API of this build.</summary>
    public static ScriptEditorSetup OfThisBuild() => new(ScriptDefinitions.Read);

    /// <summary>
    /// Writes what the project lacks before a script of it opens, so the
    /// code editor finds the files when it starts.
    /// </summary>
    public ScriptProjectReport Prepare(string projectFolder) => Run(projectFolder, ScriptProjectSetup.Prepare);

    /// <summary>
    /// Brings the definitions file of a project that has one up to this
    /// build. A project that was never set up stays as it is.
    /// </summary>
    public ScriptProjectReport Refresh(string projectFolder) => Run(projectFolder, ScriptProjectSetup.Refresh);

    private ScriptProjectReport Run(string projectFolder, Func<string, string, ScriptProjectReport> write)
    {
        if (_definitions is null || string.IsNullOrWhiteSpace(projectFolder))
            return ScriptProjectReport.Nothing;

        try
        {
            return write(projectFolder, _definitions());
        }
        catch (Exception ex)
        {
            // The boundary: a project that cannot be set up is a line in
            // Output, and its script opens all the same.
            return ScriptProjectReport.Failed(ex.Message);
        }
    }
}
