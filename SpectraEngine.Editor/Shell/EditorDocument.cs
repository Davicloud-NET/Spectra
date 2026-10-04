using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Projects;
using System.IO;

namespace SpectraEngine.Editor.Shell;

/// <summary>
/// What the shell currently has open: a project, a map bundle, and whether the
/// scene has been edited since it was last written.
/// Project and map are independent; either may be absent.
/// </summary>
// Dirty errs toward dirty: undoing back to the saved state stays dirty. Undo
// depth cannot tell that apart from a different edit at the same depth.
public sealed class EditorDocument : ObservableObject
{
    private ProjectLayout? _project;
    private string? _mapPath;
    private bool _isDirty;

    /// <summary>The open project, or null when a bundle was opened on its own.</summary>
    public ProjectLayout? Project
    {
        get => _project;
        private set
        {
            if (!Set(ref _project, value)) return;
            Raise(nameof(HasProject));
            Raise(nameof(ProjectLabel));
            Raise(nameof(Title));
        }
    }

    /// <summary>Full path of the open map bundle, or null when it has never been saved.</summary>
    public string? MapPath
    {
        get => _mapPath;
        private set
        {
            if (!Set(ref _mapPath, value)) return;
            Raise(nameof(HasMapPath));
            Raise(nameof(MapLabel));
            Raise(nameof(Title));
        }
    }

    /// <summary>Whether the scene has been edited since it was last written.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (!Set(ref _isDirty, value)) return;
            Raise(nameof(Title));
            Raise(nameof(DirtyMark));
        }
    }

    public bool HasProject => _project is not null;

    public bool HasMapPath => _mapPath is not null;

    /// <summary>The project's name, or a placeholder when none is open.</summary>
    public string ProjectLabel => _project?.Project.Name ?? "no project";

    /// <summary>The bundle's folder name without its extension, or "untitled".</summary>
    public string MapLabel => _mapPath is null
        ? "untitled"
        : Path.GetFileNameWithoutExtension(_mapPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>An asterisk while unsaved.</summary>
    public string DirtyMark => _isDirty ? "*" : string.Empty;

    /// <summary>The window title.</summary>
    public string Title => $"{MapLabel}{DirtyMark} - {ProjectLabel} - Spectra Editor";

    /// <summary>
    /// Where a save-as should start browsing: the project's Maps folder when
    /// there is one, otherwise beside whatever is already open.
    /// </summary>
    public string? SuggestedMapFolder => _project?.MapsPath
        ?? (_mapPath is null ? null : Path.GetDirectoryName(_mapPath));

    /// <summary>Records that the scene has been edited.</summary>
    public void MarkDirty() => IsDirty = true;

    /// <summary>Records a successful write of <paramref name="mapPath"/>.</summary>
    public void MarkSaved(string mapPath)
    {
        MapPath = Path.GetFullPath(mapPath);
        IsDirty = false;
    }

    /// <summary>Records that a bundle was loaded and is therefore unedited.</summary>
    public void MarkOpened(string mapPath)
    {
        MapPath = Path.GetFullPath(mapPath);
        IsDirty = false;
    }

    /// <summary>Records a scene with no file behind it yet.</summary>
    public void MarkNew()
    {
        MapPath = null;
        IsDirty = false;
    }

    /// <summary>Opens a project, leaving the map alone.</summary>
    public void SetProject(ProjectLayout? project) => Project = project;

    /// <summary>
    /// The project-relative form of the open map, or null when the map is not
    /// inside the open project. Such a map must not be written to the manifest.
    /// </summary>
    public string? MapPathWithinProject()
    {
        if (_project is null || _mapPath is null) return null;

        string relative = Path.GetRelativePath(_project.Root, _mapPath);
        if (relative.StartsWith("..", System.StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return null;

        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>The map bundle's folder extension.</summary>
    public static string BundleExtension => MapFormat.BundleExtension;
}
