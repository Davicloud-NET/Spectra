using SpectraEngine.Core.Assets.Packs;
using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Core.Projects;

/// <summary>
/// Which pack files a project boots from, in mount order. The manifest's
/// <see cref="SpectraProject.Packs"/> wins; when it is empty the pack is
/// <c>cooked/&lt;manifest name&gt;.spack</c>.
/// </summary>
// The cook writes to the same path. Named after the manifest file, not the
// display name, which may hold characters a filesystem rejects.
public static class ProjectPacks
{
    /// <summary>
    /// The pack files <paramref name="project"/> mounts, lowest priority first,
    /// as absolute paths. Existence is not checked.
    /// </summary>
    public static IReadOnlyList<string> Resolve(ProjectLayout project)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.Project.Packs.Count > 0)
        {
            var listed = new List<string>(project.Project.Packs.Count);
            foreach (string pack in project.Project.Packs)
                listed.Add(Path.GetFullPath(project.Resolve(pack)));

            return listed;
        }

        return [ConventionalPackPath(project)];
    }

    /// <summary>
    /// Where <c>scook</c> puts this project's pack when nothing overrode its
    /// output directory.
    /// </summary>
    public static string ConventionalPackPath(ProjectLayout project)
    {
        ArgumentNullException.ThrowIfNull(project);

        return Path.GetFullPath(Path.Combine(
            project.CookedPath,
            Path.GetFileNameWithoutExtension(project.ManifestPath) + PackFormat.FileExtension));
    }
}
