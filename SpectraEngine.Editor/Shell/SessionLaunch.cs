using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Projects;

namespace SpectraEngine.Editor.Shell;

/// <summary>What a new engine session is started to show.</summary>
/// <param name="Project">The open project, or null for a level opened on its own.</param>
/// <param name="ContentRoot">The asset root the session reads, or null for the engine's default.</param>
/// <param name="OpenMapPath">A level to open from disk, or null to start on a baseplate.</param>
/// <param name="Restore">
/// A level taken from a session that died. It is shown instead of
/// <paramref name="OpenMapPath"/>, and the open document keeps its path.
/// </param>
/// <param name="RestoreLoss">What <paramref name="Restore"/> could not hold, as a sentence, or null.</param>
public sealed record SessionLaunch(
    ProjectLayout? Project,
    string? ContentRoot,
    string? OpenMapPath,
    MapDocument? Restore = null,
    string? RestoreLoss = null);
