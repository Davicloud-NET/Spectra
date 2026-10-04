using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Serialization;
using System;
using System.Collections.Generic;
using System.IO;

namespace SpectraEngine.Core.Projects;

/// <summary>
/// A project on disk: the folder, its manifest, and where things live inside
/// it.
/// </summary>
/// <remarks>
/// <code>
/// MyGame/
///   MyGame.spectraproj    the manifest
///   Assets/               the content root
///   Maps/                 Lobby.smap/, Arena.smap/  (folders, not files)
///   Scripts/              shared script modules
///   cooked/               cook output, gitignored
/// </code>
/// Everything authored is text. Only <c>cooked/</c> is binary.
/// </remarks>
public sealed class ProjectLayout
{
    private ProjectLayout(string root, string manifestPath, SpectraProject project)
    {
        Root = root;
        ManifestPath = manifestPath;
        Project = project;
    }

    /// <summary>The project folder.</summary>
    public string Root { get; }

    /// <summary>Full path of the <c>.spectraproj</c> manifest.</summary>
    public string ManifestPath { get; }

    /// <summary>The manifest's contents.</summary>
    public SpectraProject Project { get; }

    public string AssetsPath => Path.Combine(Root, ProjectFormat.AssetsFolder);
    public string MapsPath => Path.Combine(Root, ProjectFormat.MapsFolder);
    public string ScriptsPath => Path.Combine(Root, ProjectFormat.ScriptsFolder);
    public string CookedPath => Path.Combine(Root, ProjectFormat.CookedFolder);

    /// <summary>Resolves a project-relative path to a full one.</summary>
    public string Resolve(string projectRelativePath) =>
        Path.Combine(Root, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Opens the project whose manifest is at <paramref name="manifestPath"/>,
    /// or which lives in the folder <paramref name="manifestPath"/> names.
    /// </summary>
    /// <exception cref="FileNotFoundException">No manifest was found, or the folder holds several.</exception>
    /// <exception cref="ProjectFormatException">The manifest is malformed.</exception>
    public static ProjectLayout Open(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        string resolved = Directory.Exists(manifestPath)
            ? FindManifest(manifestPath)
            : manifestPath;

        if (!File.Exists(resolved))
        {
            throw new FileNotFoundException(
                $"'{manifestPath}' is not a Spectra project: no {ProjectFormat.Extension} file.", resolved);
        }

        string root = Path.GetDirectoryName(Path.GetFullPath(resolved))
            ?? throw new FileNotFoundException($"'{resolved}' has no containing folder.", resolved);

        return new ProjectLayout(root, Path.GetFullPath(resolved), ProjectReader.Read(File.ReadAllBytes(resolved)));
    }

    /// <summary>
    /// Writes the manifest back, leaving it alone when nothing changed.
    /// </summary>
    /// <returns>True when the file was written; false when it was already byte-identical.</returns>
    public bool Save()
    {
        byte[] content = ProjectWriter.Write(Project);
        if (File.Exists(ManifestPath) && File.ReadAllBytes(ManifestPath).AsSpan().SequenceEqual(content))
            return false;

        // Temp file plus rename, so a crash cannot leave half a manifest.
        string temporary = ManifestPath + ".tmp";
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, ManifestPath, overwrite: true);
        return true;
    }

    /// <summary>
    /// Creates a project folder with the canonical layout, an empty manifest,
    /// and a <c>.gitignore</c> and <c>.gitattributes</c>. Existing files are
    /// kept.
    /// </summary>
    public static ProjectLayout Create(string root, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, ProjectFormat.AssetsFolder));
        Directory.CreateDirectory(Path.Combine(root, ProjectFormat.MapsFolder));
        Directory.CreateDirectory(Path.Combine(root, ProjectFormat.ScriptsFolder));

        WriteIfAbsent(Path.Combine(root, ".gitignore"), GitIgnore);
        WriteIfAbsent(Path.Combine(root, ".gitattributes"), GitAttributes);

        var project = new SpectraProject { Name = name, Id = Guid.NewGuid() };
        string manifestPath = Path.Combine(root, name + ProjectFormat.Extension);
        var layout = new ProjectLayout(Path.GetFullPath(root), Path.GetFullPath(manifestPath), project);
        layout.Save();
        return layout;
    }

    /// <summary>
    /// Every map bundle present under <c>Maps/</c>, as project-relative paths,
    /// sorted. May differ from the manifest's list.
    /// </summary>
    public IReadOnlyList<string> DiscoverMaps()
    {
        if (!Directory.Exists(MapsPath)) return [];

        var found = new List<string>();
        foreach (string directory in Directory.EnumerateDirectories(MapsPath))
        {
            if (!MapBundle.IsBundle(directory)) continue;
            found.Add($"{ProjectFormat.MapsFolder}/{Path.GetFileName(directory)}");
        }

        // EnumerateDirectories has no documented order.
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>Loads a map bundle named by a project-relative path.</summary>
    public MapDocument LoadMap(string projectRelativePath) => MapBundle.Load(Resolve(projectRelativePath));

    private static string FindManifest(string folder)
    {
        string[] candidates = Directory.GetFiles(folder, "*" + ProjectFormat.Extension);
        if (candidates.Length == 1) return candidates[0];

        if (candidates.Length == 0)
        {
            throw new FileNotFoundException(
                $"'{folder}' contains no {ProjectFormat.Extension} file.",
                Path.Combine(folder, "project" + ProjectFormat.Extension));
        }

        // Several manifests: refuse, don't guess.
        Array.Sort(candidates, StringComparer.Ordinal);
        throw new FileNotFoundException(
            $"'{folder}' contains {candidates.Length} project files "
            + $"({string.Join(", ", Array.ConvertAll(candidates, Path.GetFileName))}); name the one to open.",
            candidates[0]);
    }

    private static void WriteIfAbsent(string path, string content)
    {
        if (!File.Exists(path))
            File.WriteAllText(path, content);
    }

    private const string GitIgnore = """
        # Cook output. Derived from the authored files beside it; never authored.
        cooked/

        # The cook's incremental cache: a content-addressed store of cooked
        # payloads plus the dependency graph over them. Derived, per machine, and
        # keyed partly on the toolchain that wrote it, so it is worth nothing in
        # somebody else's checkout.
        .spectra-cook/

        # Per-user editor state: viewport camera, selection, window layout.
        # Losing one loses nothing but a camera position.
        *.user
        *.user.json
        """;

    private const string GitAttributes = """
        * text=auto

        # A .smap map is a FOLDER bundle of text, and the codec's promise is that
        # save/load/save is byte-identical so a hand edit stays a small diff. Under
        # `* text=auto` a Windows checkout rewrites those files to CRLF underneath
        # you and the next no-op save becomes a whole-file diff.
        #
        # Both stars are needed: attribute patterns use gitignore syntax, where a
        # separator in the middle anchors the pattern to this file's directory, so
        # `*.smap/**` would match only a bundle sitting in the project root.
        **/*.smap/** text eol=lf
        *.spectraproj text eol=lf
        """;
}
