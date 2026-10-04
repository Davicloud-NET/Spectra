using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Core.Projects;

/// <summary>
/// What a boot mounts: packs only for a shipped game, packs plus the loose
/// <c>Assets</c> folder above them for a developer.
/// </summary>
public enum ContentMountProfile
{
    /// <summary>Packs and nothing else. What a player's machine runs.</summary>
    Shipped,

    /// <summary>
    /// The same packs with loose files at <see cref="PackMountBand.Loose"/>, so
    /// an edited file shadows the cooked entry with no rebuild.
    /// </summary>
    Dev,
}

/// <summary>
/// A project's content stack, assembled from its packs: what an
/// <c>AssetManager</c> is handed instead of a folder.
/// </summary>
// Flattened at mount: a loose file created afterwards is not served until a
// remount. Editing an existing file still works.
public sealed class ProjectContentMount : IDisposable
{
    private readonly PackMountStack _packs;
    private bool _disposed;

    private ProjectContentMount(
        PackMountStack packs,
        ContentSourceStack content,
        IReadOnlyList<string> packPaths,
        ContentMountProfile profile,
        bool hotReloadEnabled,
        string? hotReloadDisabledReason)
    {
        _packs = packs;
        Content = content;
        PackPaths = packPaths;
        Profile = profile;
        HotReloadEnabled = hotReloadEnabled;
        HotReloadDisabledReason = hotReloadDisabledReason;
    }

    /// <summary>The stack an <c>AssetManager</c> takes.</summary>
    public ContentSourceStack Content { get; }

    /// <summary>The mounted packs, and the loose overlay when there is one.</summary>
    public PackMountStack Packs => _packs;

    /// <summary>The pack files that were mounted, in mount order.</summary>
    public IReadOnlyList<string> PackPaths { get; }

    /// <summary>Which profile this mount was assembled for.</summary>
    public ContentMountProfile Profile { get; }

    /// <summary>
    /// Whether an <c>AssetManager</c> over this stack may watch files. False for
    /// a pure-pack mount.
    /// </summary>
    public bool HotReloadEnabled { get; }

    /// <summary>
    /// Why hot reload is off, or null when it is on.
    /// </summary>
    public string? HotReloadDisabledReason { get; }

    /// <summary>
    /// Every shadowing decision the flatten made: which source won a path, and
    /// which one it took it from.
    /// </summary>
    public IReadOnlyList<MountShadowing> Shadowings => _packs.Shadowings;

    /// <summary>
    /// Mounts <paramref name="project"/>'s packs and returns the stack to run
    /// on.
    /// </summary>
    /// <exception cref="PackMountException">
    /// A pack the project boots from is missing or is refused. There is no
    /// fallback to loose files.
    /// </exception>
    public static ProjectContentMount Open(
        ILogger logger, ProjectLayout project, ContentMountProfile profile)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(project);

        IReadOnlyList<string> packPaths = ProjectPacks.Resolve(project);
        bool dev = profile == ContentMountProfile.Dev;
        var packs = new PackMountStack(logger);

        try
        {
            for (int i = 0; i < packPaths.Count; i++)
            {
                string path = packPaths[i];
                if (!File.Exists(path))
                {
                    throw new PackMountException(
                        $"The project '{project.Project.Name}' boots from '{path}', which does not exist. "
                        + $"Cook it first (scook cook \"{project.Root}\"), or run without --pack to use loose files.");
                }

                // All in the base band: manifest order is mount order, later
                // wins.
                packs.Mount(new PackSource(logger, path, PackMountBand.Base));
            }

            if (dev)
                packs.Mount(new LooseFileSource(logger, project.AssetsPath, PackMountBand.Loose));

            packs.Flatten();
        }
        catch
        {
            // Unmap what was already mapped, or the views leak and Windows
            // keeps the folder locked.
            packs.Dispose();
            throw;
        }

        var content = new ContentSourceStack();
        content.Mount(packs);

        string? reason = dev
            ? null
            : "every content source is a pack, and a pack has no file for a watcher to watch";

        logger.LogInformation(
            "Project content mounted ({Profile}, {Packs} pack(s)): {Stack}",
            dev ? "dev" : "shipped", packPaths.Count, packs.Describe());

        if (reason is null)
        {
            logger.LogInformation(
                "Hot reload ON: loose files at priority {Band} shadow the packs beneath them.",
                PackMountBand.Loose);
        }
        else
        {
            logger.LogInformation("Hot reload OFF: {Reason}.", reason);
        }

        return new ProjectContentMount(packs, content, packPaths, profile, dev, reason);
    }

    /// <summary>Unmounts every pack and its mapped view.</summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _packs.Dispose();
    }
}
