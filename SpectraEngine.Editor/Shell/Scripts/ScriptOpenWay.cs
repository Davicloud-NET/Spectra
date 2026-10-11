namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>A way a script can be put in front of somebody.</summary>
public enum ScriptOpenWay
{
    /// <summary>In Visual Studio Code, opened on the project.</summary>
    VisualStudioCode,

    /// <summary>In the program the system has for the file's type.</summary>
    DefaultProgram,

    /// <summary>Not opened: shown in its folder.</summary>
    FileManager,
}
