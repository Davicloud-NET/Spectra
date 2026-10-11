using SpectraEngine.Core.Assets;
using System;

namespace SpectraEngine.Core.Scene;

/// <summary>
/// The script a node is: the <c>.luau</c> file it names, whether it is switched
/// on, and whether it is a module. Never changes once made, so an edit is a
/// new one: <c>script with { IsEnabled = false }</c>.
/// </summary>
// The node above the one that carries this is what the script calls its parent.
public sealed record Script
{
    private readonly string _path;

    /// <summary>A script that names <paramref name="path"/>, switched on and not a module.</summary>
    /// <exception cref="ArgumentException">The path is empty, rooted or leaves the content root.</exception>
    public Script(string path)
    {
        _path = ContentRoot.NormalizeRelativePath(path);
    }

    /// <summary>
    /// The script's file, as a normalized content-relative path.
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty, rooted or leaves the content root.</exception>
    public string Path
    {
        get => _path;
        init => _path = ContentRoot.NormalizeRelativePath(value);
    }

    /// <summary>Whether the script may run. On unless switched off.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Whether this is a module. A module does not run when the level starts.
    /// It runs when another script asks for it.
    /// </summary>
    public bool IsModule { get; init; }
}
