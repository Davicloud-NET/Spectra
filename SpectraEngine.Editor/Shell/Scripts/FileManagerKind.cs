namespace SpectraEngine.Editor.Shell.Scripts;

/// <summary>Which file manager shows a file in its folder.</summary>
public enum FileManagerKind
{
    /// <summary>None this editor knows how to ask.</summary>
    None,

    /// <summary>Windows Explorer, which can select the file.</summary>
    Explorer,

    /// <summary>Finder, which can select the file.</summary>
    Finder,

    /// <summary>Whatever opens a folder on this desktop. The file is not selected.</summary>
    FolderOpener,
}
