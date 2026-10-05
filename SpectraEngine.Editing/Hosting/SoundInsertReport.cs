using System;

namespace SpectraEngine.Editing.Hosting;

/// <summary>
/// What one <see cref="SceneEditorHost.InsertSound"/> did: a sound entity is
/// in the scene and the history, or <see cref="Refused"/> says why nothing is.
/// </summary>
/// <param name="ContentPath">The content-relative path that was asked for.</param>
/// <param name="NodeId">The node that was placed, or <see cref="Guid.Empty"/> when nothing was.</param>
/// <param name="NodeName">The placed node's name, taken from the file.</param>
/// <param name="Refused">Why nothing was placed, or null when something was.</param>
public readonly record struct SoundInsertReport(
    string ContentPath,
    Guid NodeId,
    string NodeName,
    string? Refused)
{
    /// <summary>Whether a node reached the scene.</summary>
    public bool Placed => NodeId != Guid.Empty;

    /// <summary>A report for a drop nothing acted on, naming why.</summary>
    public static SoundInsertReport RefusedBecause(string contentPath, string reason) =>
        new(contentPath, Guid.Empty, string.Empty, reason);

    /// <summary>One line for a status bar or an output log.</summary>
    public string Describe() => Refused is { } refused
        ? $"{ContentPath} was not placed: {refused}."
        : $"{ContentPath} placed as the sound '{NodeName}'.";
}
