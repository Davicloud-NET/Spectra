using System;

namespace SpectraEngine.Editing.Hosting;

/// <summary>
/// What one <see cref="SceneEditorHost.InsertModel"/> did. A model that cannot
/// be resolved still places an empty node (<see cref="Unresolved"/>), which is
/// in the scene and the history; <see cref="Refused"/> means nothing happened.
/// </summary>
/// <param name="ContentPath">The content-relative path that was asked for.</param>
/// <param name="NodeId">The node that was placed, or <see cref="Guid.Empty"/> when nothing was.</param>
/// <param name="Unresolved">Why the placed node carries no geometry, or null when it does.</param>
/// <param name="Refused">Why nothing was placed at all, or null when something was.</param>
public readonly record struct ModelInsertReport(
    string ContentPath,
    Guid NodeId,
    string NodeName,
    string? Unresolved,
    string? Refused)
{
    /// <summary>Whether a node reached the scene.</summary>
    public bool Placed => NodeId != Guid.Empty;

    /// <summary>Whether a node reached the scene carrying the model's geometry.</summary>
    public bool IsComplete => Placed && Unresolved is null;

    /// <summary>A report for a drop nothing acted on, naming why.</summary>
    public static ModelInsertReport RefusedBecause(string contentPath, string reason) =>
        new(contentPath, Guid.Empty, string.Empty, null, reason);

    /// <summary>One line for a status bar or an output log.</summary>
    public string Describe()
    {
        if (Refused is { } refused)
            return $"{ContentPath} was not placed: {refused}.";

        return Unresolved is { } unresolved
            ? $"{ContentPath} was placed as an empty node: {unresolved}."
            : $"{ContentPath} placed as '{NodeName}'.";
    }
}
