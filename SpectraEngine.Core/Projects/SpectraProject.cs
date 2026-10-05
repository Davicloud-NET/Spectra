using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Serialization;
using SpectraEngine.Core.Windowing;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Projects;

/// <summary>
/// A game project: the text manifest at the root of a project folder, naming
/// the maps, the display defaults and the backends.
/// </summary>
public sealed class SpectraProject
{
    internal static readonly string[] MemberOrder =
        [ProjectFormat.FormatVersionMember, ProjectFormat.MinimumReadableMember, ProjectFormat.EngineMember,
         ProjectFormat.NameMember, ProjectFormat.IdMember, ProjectFormat.LanguageMember,
         ProjectFormat.StartupMapMember, ProjectFormat.MapsMember, ProjectFormat.PacksMember,
         ProjectFormat.DisplayMember, ProjectFormat.DefaultBackendMember,
         ProjectFormat.AllowedBackendsMember];

    public int FormatVersion { get; set; } = EngineInfo.ProjectFormatVersion;

    /// <summary>
    /// The oldest reader that can still make sense of this project. A reader
    /// refuses a document whose value here exceeds what it implements.
    /// </summary>
    public int MinimumReadableVersion { get; set; } = EngineInfo.MinimumReadableProjectVersion;

    /// <summary>Engine version that last wrote this file. Informational; never a load gate.</summary>
    public string Engine { get; set; } = EngineInfo.VersionString;

    /// <summary>The game's display name.</summary>
    public string Name { get; set; } = "Untitled";

    /// <summary>
    /// Stable identity for the project, used to namespace save data and packs.
    /// Survives a rename, which the name would not.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The language the project's own text is written in, as a
    /// <see cref="LanguageTag"/>, or null when the file names none.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// <see cref="Language"/>, or <see cref="LanguageTag.Default"/> for a
    /// project that names none. Captions in another language fall back to it.
    /// </summary>
    public string LanguageOrDefault => Language ?? LanguageTag.Default;

    /// <summary>
    /// Project-relative path of the map bundle a shipped game boots into, or
    /// null when the project has none yet.
    /// </summary>
    public string? StartupMap { get; set; }

    /// <summary>
    /// Project-relative paths of the map bundles this project contains, in
    /// order. This is what a cook bakes; <see cref="ProjectLayout.DiscoverMaps"/>
    /// finds bundles that are on disk but not listed.
    /// </summary>
    public List<string> Maps { get; } = [];

    /// <summary>
    /// Project-relative paths of the packs a boot mounts, in mount order.
    /// Later entries win. When empty, <see cref="ProjectPacks.Resolve"/> falls
    /// back to the conventional pack under <c>cooked/</c>.
    /// </summary>
    public List<string> Packs { get; } = [];

    /// <summary>Window defaults for a shipped game.</summary>
    public ProjectDisplay Display { get; set; } = new();

    /// <summary>
    /// Which backend a shipped game asks for first, or null to let the host
    /// decide.
    /// </summary>
    public GraphicsBackend? DefaultBackend { get; set; }

    /// <summary>
    /// Backends this project is allowed to run on. Empty means no restriction.
    /// </summary>
    public List<GraphicsBackend> AllowedBackends { get; } = [];

    /// <summary>Members this engine version does not recognise.</summary>
    public List<PreservedMember> Unknown { get; } = [];
}

/// <summary>Window defaults for a shipped game.</summary>
public sealed class ProjectDisplay
{
    internal static readonly string[] MemberOrder =
        [ProjectFormat.WidthMember, ProjectFormat.HeightMember,
         ProjectFormat.VsyncMember, ProjectFormat.ModeMember];

    public int Width { get; set; } = 1280;

    public int Height { get; set; } = 720;

    public bool Vsync { get; set; } = true;

    public WindowMode Mode { get; set; } = WindowMode.Windowed;

    public List<PreservedMember> Unknown { get; } = [];
}
